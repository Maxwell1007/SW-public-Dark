using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Events;
using Content.Server.Imperial.Medieval.GameTicking.Rules;
using Content.Server.MagicBarrier.Components;
using Content.Server.RoundEnd;
using Content.Server.Voting;
using Content.Server.Voting.Managers;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Random;

namespace Content.Server.GameTicking.Systems;

public sealed partial class AutoRoundExtendSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;
    [Dependency] private readonly IVoteManager _voteManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private bool _enabled = true;
    private bool _isEnded;
    private bool _leadEventTriggered;
    private TimeSpan _targetDuration;

    private TimeSpan _initialDuration;
    private TimeSpan _maxDuration;
    private TimeSpan _voteLeadTime;
    private TimeSpan _extensionTime;

    private IVoteHandle? _currentVote;

    public bool IsEnabled => _enabled;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);

        _cfg.OnValueChanged(CCVars.AutoRoundInitialDuration, v => _initialDuration = TimeSpan.FromMinutes(v), true);
        _cfg.OnValueChanged(CCVars.AutoRoundMaxDuration, v => _maxDuration = TimeSpan.FromMinutes(v), true);
        _cfg.OnValueChanged(CCVars.AutoRoundExtensionTime, v => _extensionTime = TimeSpan.FromMinutes(v), true);
        _cfg.OnValueChanged(CCVars.AutoRoundVoteLeadTime, v => _voteLeadTime = TimeSpan.FromMinutes(v), true);
    }

    private void OnRoundStart(RoundStartingEvent ev)
    {
        ResetState();
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        ResetState();
    }

    private void ResetState()
    {
        _enabled = true;
        _isEnded = false;
        _leadEventTriggered = false;
        _targetDuration = _initialDuration;

        CancelActiveVote();
    }

    public void SetEnabled(bool value)
    {
        _enabled = value;

        if (!value)
            CancelActiveVote();
    }

    private void CancelActiveVote()
    {
        if (_currentVote != null)
        {
            _currentVote.Cancel();
            _currentVote = null;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled || _isEnded || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        var currentDuration = _ticker.RoundDuration();

        if (currentDuration >= _targetDuration)
        {
            _isEnded = true;
            _roundEndSystem.EndRound();
            return;
        }

        if (!_leadEventTriggered && currentDuration >= _targetDuration - _voteLeadTime)
        {
            _leadEventTriggered = true;

            if (_targetDuration < _maxDuration)
                StartExtensionVote();
            else
                ArmyAttack();
        }
    }

    private void StartExtensionVote()
    {
        CancelActiveVote();

        var options = new VoteOptions
        {
            InitiatorText = Loc.GetString("ui-vote-extend-round-initiator"),
            Title = Loc.GetString("ui-vote-extend-round-title", ("minutes", (int)_extensionTime.TotalMinutes)),
            Options =
            {
                (Loc.GetString("ui-vote-extend-yes"), "yes"),
                (Loc.GetString("ui-vote-extend-no"), "no")
            },
            Duration = TimeSpan.FromMinutes(3),
            DisplayVotes = true
        };

        _currentVote = _voteManager.CreateVote(options);

        _currentVote.OnFinished += (_, _) =>
        {
            if (_currentVote == null)
                return;

            var yes = _currentVote.VotesPerOption["yes"];
            var no = _currentVote.VotesPerOption["no"];

            if (yes > no)
            {
                _targetDuration = _targetDuration + _extensionTime > _maxDuration
                    ? _maxDuration
                    : _targetDuration + _extensionTime;
                _leadEventTriggered = false;
                _chatManager.DispatchServerAnnouncement(Loc.GetString("ui-vote-extend-success"));
            }
            else
            {
                _chatManager.DispatchServerAnnouncement(Loc.GetString("ui-vote-extend-fail"));
            }

            _currentVote = null;
        };

        _chatManager.DispatchServerAnnouncement(Loc.GetString("ui-vote-extend-announcement"));
    }

    private void ArmyAttack()
    {
        var skeletonRules = EntityQueryEnumerator<SkeletonInvasionRuleComponent>();
        if (skeletonRules.MoveNext(out _, out _))
            return;

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("auto-round-end-army-attack"),
            playSound: true,
            colorOverride: Color.FromHex("#9403fc"),
            sender: Loc.GetString("auto-round-end-army-sender"));

        var cursespawners = EntityManager.AllEntities<MagicBarrierCurseSpawnComponent>();
        Spawn("MedievalSpawnNecroSenderPreset", Transform(_random.Pick(cursespawners).Owner).Coordinates);
        for (var i = 0; i < 40; i++)
        {
            Spawn("MedievalSpawnNecroFighterPreset", Transform(_random.Pick(cursespawners).Owner).Coordinates);
        }
    }

    public void ForceExtendRound(TimeSpan extension, bool resetLeadEvent = false)
    {
        if (!_enabled || _isEnded)
            return;

        CancelActiveVote();

        _targetDuration += extension;

        if (_targetDuration > _maxDuration)
            _maxDuration = _targetDuration;

        if (resetLeadEvent)
            _leadEventTriggered = false;
    }
}

[AdminCommand(AdminFlags.Round)]
public sealed class ToggleAutoRoundEndCommand : IConsoleCommand
{
    public string Command => "toggleautoroundend";
    public string Description => Loc.GetString("cmd-toggleautoroundend-desc");
    public string Help => Loc.GetString("cmd-toggleautoroundend-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var sysManager = IoCManager.Resolve<IEntitySystemManager>();
        if (!sysManager.TryGetEntitySystem<AutoRoundExtendSystem>(out var system))
        {
            shell.WriteError(Loc.GetString("cmd-toggleautoroundend-system-not-found"));
            return;
        }

        if (args.Length == 0)
        {
            system.SetEnabled(!system.IsEnabled);
        }
        else if (bool.TryParse(args[0], out var enabled))
        {
            system.SetEnabled(enabled);
        }
        else
        {
            shell.WriteError(Loc.GetString("cmd-toggleautoroundend-invalid-arg"));
            return;
        }

        shell.WriteLine(Loc.GetString(system.IsEnabled
            ? "cmd-toggleautoroundend-enabled"
            : "cmd-toggleautoroundend-disabled"));
    }
}

