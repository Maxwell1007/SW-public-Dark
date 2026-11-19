using System.Globalization;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Imperial.Medieval.SkeletonInvasion;
using Content.Shared.Mind;
using Content.Shared.Players.RateLimiting;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

public sealed class MorbiusMindConnectionSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly SkeletonProgressionSystem _progression = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<MorbiusMindChatMessageEvent>(OnMessage);
    }

    private void OnMessage(MorbiusMindChatMessageEvent args, EntitySessionEventArgs eventArgs)
    {
        var player = eventArgs.SenderSession;
        if (!_progression.IsAntagonist(player.UserId) ||
            !_progression.HasSharedUpgrade(SkeletonSharedUpgradeEffect.MindConnection) ||
            player.AttachedEntity is not { Valid: true } source ||
            !_mind.TryGetMind(player.UserId, out var mind, out var mindComponent) ||
            !TryComp<MorbiusMindChatComponent>(mind, out var chat))
            return;

        var message = args.Message.TrimStart();
        var prefix = message.StartsWith(chat.ChatPrefix, StringComparison.OrdinalIgnoreCase)
            ? chat.ChatPrefix
            : message.StartsWith(chat.AlternateChatPrefix, StringComparison.OrdinalIgnoreCase)
                ? chat.AlternateChatPrefix
                : null;
        if (prefix == null)
            return;

        message = message[prefix.Length..].TrimStart();
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (_chat.HandleRateLimit(player) != RateLimitStatus.Allowed ||
            _chat.MessageCharacterLimit(player, args.Message))
            return;

        var culture = CultureInfo.CurrentCulture;
        var capitalizeTheWordI = (!culture.IsNeutralCulture && culture.Parent.Name == "en") ||
            (culture.IsNeutralCulture && culture.Name == "en");
        message = _chatSystem.SanitizeInGameICMessage(source, message, out _,
            punctuate: _configuration.GetCVar(CCVars.ChatPunctuation), capitalizeTheWordI: capitalizeTheWordI);
        if (string.IsNullOrWhiteSpace(message))
            return;

        var recipients = Filter.Empty();
        foreach (var session in _players.Sessions)
        {
            if (_progression.IsAntagonist(session.UserId))
                recipients.AddPlayer(session);
        }

        var channel = Loc.GetString("morbius-mind-chat-channel-name");
        var name = mindComponent.CharacterName ?? Name(source);
        var wrappedMessage = Loc.GetString("morbius-mind-chat-wrap-message",
            ("channel", $"\\[{channel}\\]"),
            ("name", FormattedMessage.EscapeText(name)),
            ("message", FormattedMessage.EscapeText(message)));
        wrappedMessage = $"[color={chat.ChatColor.ToHex()}]{wrappedMessage}[/color]";
        _chat.ChatMessageToManyFiltered(recipients, ChatChannel.Radio, message, wrappedMessage, source,
            false, false, chat.ChatColor);
    }
}
