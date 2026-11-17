using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Server.Imperial.Medieval.Calendar;
using Content.Server.Imperial.Medieval.DayNight;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.Imperial.Medieval.Calendar;

public sealed partial class CalendarSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly MedievalDayNightSystem _dayNight = default!;

    public IReadOnlyList<ProtoId<CalendarEventPrototype>> DayDeck =>
        GetState()?.DayDeck ?? (IReadOnlyList<ProtoId<CalendarEventPrototype>>) Array.Empty<ProtoId<CalendarEventPrototype>>();

    public IReadOnlyList<ProtoId<CalendarEventPrototype>> NightDeck =>
        GetState()?.NightDeck ?? (IReadOnlyList<ProtoId<CalendarEventPrototype>>) Array.Empty<ProtoId<CalendarEventPrototype>>();

    public int CurrentCycle => _dayNight.CurrentPhase / 2;
    public int CurrentDayNumber => CurrentCycle + 1;
    public int FirstDeckDay => (GetState()?.DeckStartPhase ?? 0) / 2 + 1;
    public CalendarDayType CurrentDayType => _dayNight.CurrentPhase % 2 == 0 ? CalendarDayType.Day : CalendarDayType.Night;
    public bool IsInitialized => GetState() is { ScheduleReady: true };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStart, after: [typeof(MedievalDayNightSystem)]);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<MedievalDayNightChangedEvent>(OnTimeChanged);
        SubscribeLocalEvent<GameRuleStartedEvent>(OnRuleStarted);
        SubscribeLocalEvent<GameRuleEndedEvent>(OnRuleEnded);
        SubscribeLocalEvent<CalendarEventStartedEvent>(OnCalendarEventStarted);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel == GameRunLevel.InRound && GetState() is { ScheduleReady: true, RebuildRequested: true } state)
            GenerateSchedule(state, true);
    }

    private void OnRoundStart(RoundStartedEvent args)
    {
        ClearState();
        if (!_dayNight.IsInitialized)
            return;

        var uid = Spawn("MedievalCalendar", MapCoordinates.Nullspace);
        var state = Comp<CalendarStateComponent>(uid);
        if (state.PhasesCount < 2 || state.PhasesCount % 2 != 0 || state.GenerationSearchLimit < 1)
        {
            Log.Error("Cannot initialize calendar: schedule size must be positive and even, and search limit must be positive.");
            QueueDel(uid);
            return;
        }

        GenerateSchedule(state, false);
        state.ScheduleReady = true;
        TriggerDayStageNotification();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        ClearState();
    }

    private void ClearState()
    {
        var query = EntityManager.AllEntityQueryEnumerator<CalendarStateComponent>();
        while (query.MoveNext(out var uid, out _))
            QueueDel(uid);
    }

    private void OnRuleStarted(ref GameRuleStartedEvent args)
    {
        OnRuleAvailabilityChanged(args.RuleId);
    }

    private void OnRuleEnded(ref GameRuleEndedEvent args)
    {
        OnRuleAvailabilityChanged(args.RuleId);
    }

    private void OnRuleAvailabilityChanged(string ruleId)
    {
        if (GetState() is not { ScheduleReady: true } state)
            return;

        foreach (var prototype in _prototype.EnumeratePrototypes<CalendarEventPrototype>())
        {
            foreach (var rule in prototype.RequiredGameRules)
            {
                if (rule.Id != ruleId)
                    continue;

                state.RebuildRequested = true;
                return;
            }
        }
    }

    private void OnTimeChanged(MedievalDayNightChangedEvent args)
    {
        if (GetState() is not { ScheduleReady: true } state)
            return;

        if (args.PhaseIndex < state.DeckStartPhase || args.PhaseIndex - state.DeckStartPhase >= state.Deck.Count)
            GenerateSchedule(state, false);
        else if (state.RebuildRequested)
            GenerateSchedule(state, true);

        TriggerDayStageNotification();
    }

    public void SetOverrideEvent(int dayNumber, ProtoId<CalendarEventPrototype> eventId, bool isNight = false)
    {
        if (GetState() is not { ScheduleReady: true } state)
            return;

        var index = (long) (dayNumber - 1) * 2 + (isNight ? 1 : 0);
        if (index < 0 || index >= state.Deck.Count || !_prototype.HasIndex(eventId))
        {
            Log.Error($"Cannot assign calendar event {eventId} to day {dayNumber}: invalid day or event prototype.");
            return;
        }

        if (state.DeckStartPhase + index <= _dayNight.CurrentPhase)
            return;

        state.Deck[(int) index] = eventId;
        state.DisplayDeck[(int) index] = eventId;
        UpdateDeckViews(state);
        RaiseLocalEvent(new CalendarUpdatedEvent());
    }

    public bool TrySkipPhase() => _dayNight.TrySkipPhase();

    public bool TryAdvanceDays(int days) => _dayNight.TryAdvanceDays(days);

    public void TriggerDayStageNotification(ProtoId<CalendarEventPrototype>? protoId = null)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || GetState() is not { ScheduleReady: true } state)
            return;

        var index = _dayNight.CurrentPhase - state.DeckStartPhase;
        if (index < 0 || index >= state.Deck.Count)
            return;

        var id = protoId ?? state.DisplayDeck[index];
        if (!_prototype.TryIndex(id, out var proto))
            return;

        state.LastOccurrence[state.Deck[index].Id] = _dayNight.CurrentPhase;
        RaiseLocalEvent(new CalendarUpdatedEvent());
        RaiseNetworkEvent(new CalendarBroadcastNotificationEvent(id, CurrentDayNumber, CurrentDayType), Filter.Broadcast());
        RaiseLocalEvent(new CalendarEventStartedEvent(CurrentDayNumber, id, proto, CurrentDayType));
    }

    private void OnCalendarEventStarted(CalendarEventStartedEvent args)
    {
        if (args.Prototype.Spawns == null || args.Prototype.Spawns.Count == 0)
            return;

        var markerToSpawns = new Dictionary<string, List<(EntProtoId Proto, int Amount)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entProto, entries) in args.Prototype.Spawns)
        {
            foreach (var entry in entries)
            {
                if (entry.Amount <= 0)
                    continue;

                if (string.IsNullOrWhiteSpace(entry.Marker) || entry.Marker.Equals("Global", StringComparison.OrdinalIgnoreCase))
                {
                    for (var i = 0; i < entry.Amount; i++)
                        Spawn(entProto, MapCoordinates.Nullspace);
                    continue;
                }

                if (!markerToSpawns.TryGetValue(entry.Marker, out var list))
                {
                    list = new List<(EntProtoId, int)>();
                    markerToSpawns[entry.Marker] = list;
                }

                list.Add((entProto, entry.Amount));
            }
        }

        var query = EntityQueryEnumerator<CalendarSpawnMarkerComponent, TransformComponent>();
        while (query.MoveNext(out _, out var marker, out var xform))
        {
            if (!markerToSpawns.TryGetValue(marker.MarkerId, out var spawns))
                continue;

            foreach (var (proto, amount) in spawns)
            {
                for (var i = 0; i < amount; i++)
                    Spawn(proto, xform.Coordinates);
            }
        }
    }

    private CalendarStateComponent? GetState()
    {
        var query = EntityManager.AllEntityQueryEnumerator<CalendarStateComponent>();
        while (query.MoveNext(out var uid, out var state))
        {
            if (!TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid))
                return state;
        }

        return null;
    }
}

[AdminCommand(AdminFlags.VarEdit)]
public sealed class TriggerNextDayCycleCommand : IConsoleCommand
{
    public string Command => "triggernextdaycycle";
    public string Description => Loc.GetString("cmd-trigger-next-day-cycle-desc");
    public string Help => Loc.GetString("cmd-trigger-next-day-cycle-help", ("command", Command));

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-need-exactly-one-argument"));
            shell.WriteLine(Help);
            return;
        }

        if (!int.TryParse(args[0], out var dayCount) || dayCount < 1)
        {
            shell.WriteError(Loc.GetString("cmd-trigger-next-day-cycle-invalid-days"));
            return;
        }

        if (!IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<MedievalDayNightSystem>().TryAdvanceDays(dayCount))
            shell.WriteError(Loc.GetString("cmd-trigger-next-day-cycle-not-running"));
    }
}
