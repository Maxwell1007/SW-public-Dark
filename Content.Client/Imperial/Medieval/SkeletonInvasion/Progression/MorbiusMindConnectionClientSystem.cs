using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared.Imperial.Medieval.SkeletonInvasion;
using Content.Shared.Mind;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.Imperial.Medieval.SkeletonInvasion.Progression;

public sealed class MorbiusMindConnectionClientSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    private readonly Dictionary<ChatBox, Action<GUIBoundKeyEventArgs>> _handlers = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var controller = _ui.GetUIController<ChatUIController>();
        foreach (var chatBox in controller.Chats)
        {
            if (!_handlers.ContainsKey(chatBox))
                Register(chatBox);

            if (TryGetChat(out var chat) && TryGetMessage(chatBox.ChatInput.Input.Text, chat, out _))
            {
                chatBox.ChatInput.ChannelSelector.Text = Loc.GetString("morbius-mind-chat-channel-name");
                chatBox.ChatInput.ChannelSelector.Modulate = chat.ChatColor;
            }
        }

        foreach (var chatBox in _handlers.Keys.Where(chatBox => !controller.Chats.Contains(chatBox)).ToList())
            Unregister(chatBox);
    }

    public override void Shutdown()
    {
        foreach (var chatBox in _handlers.Keys.ToList())
            Unregister(chatBox);

        base.Shutdown();
    }

    private void Register(ChatBox chatBox)
    {
        Action<GUIBoundKeyEventArgs> submit = args => OnSubmit(chatBox, args);
        chatBox.ChatInput.Input.OnKeyBindDown += submit;
        _handlers.Add(chatBox, submit);
    }

    private void Unregister(ChatBox chatBox)
    {
        if (_handlers.Remove(chatBox, out var submit))
            chatBox.ChatInput.Input.OnKeyBindDown -= submit;
    }

    private bool TryGetChat(out MorbiusMindChatComponent chat)
    {
        chat = default!;
        if (_player.LocalUser is not { } user || !_mind.TryGetMind(user, out var mind) ||
            !TryComp<MorbiusMindChatComponent>(mind, out var component))
            return false;

        chat = component;
        return true;
    }

    private void OnSubmit(ChatBox chatBox, GUIBoundKeyEventArgs args)
    {
        var input = chatBox.ChatInput.Input.Text;
        if (args.Function != EngineKeyFunctions.TextSubmit || !TryGetChat(out var chat) ||
            !TryGetMessage(input, chat, out var message))
            return;

        chatBox.ChatInput.Input.Clear();
        chatBox.ChatInput.Input.ReleaseKeyboardFocus();
        args.Handle();
        if (string.IsNullOrWhiteSpace(message))
            return;

        var controller = _ui.GetUIController<ChatUIController>();
        input = input.Trim();
        if (input.Length > controller.MaxMessageLength)
        {
            chatBox.AddLine(Loc.GetString("chat-manager-max-message-length",
                ("maxMessageLength", controller.MaxMessageLength)), Color.Orange);
            return;
        }

        RaiseNetworkEvent(new MorbiusMindChatMessageEvent(input));
    }

    private static bool TryGetMessage(string text, MorbiusMindChatComponent chat, out string message)
    {
        text = text.TrimStart();
        var prefix = text.StartsWith(chat.ChatPrefix, StringComparison.OrdinalIgnoreCase)
            ? chat.ChatPrefix
            : text.StartsWith(chat.AlternateChatPrefix, StringComparison.OrdinalIgnoreCase)
                ? chat.AlternateChatPrefix
                : null;
        message = prefix == null ? string.Empty : text[prefix.Length..].TrimStart();
        return prefix != null;
    }
}
