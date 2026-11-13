using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Server.Imperial.DayTime;
using Content.Server.Imperial.Medieval.Calendar;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.Imperial.Medieval.Calendar;

public sealed class CalendarSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public IReadOnlyList<ProtoId<CalendarEventPrototype>> DayDeck =>
        GetState()?.DayDeck ?? (IReadOnlyList<ProtoId<CalendarEventPrototype>>) Array.Empty<ProtoId<CalendarEventPrototype>>();

    public IReadOnlyList<ProtoId<CalendarEventPrototype>> NightDeck =>
        GetState()?.NightDeck ?? (IReadOnlyList<ProtoId<CalendarEventPrototype>>) Array.Empty<ProtoId<CalendarEventPrototype>>();

    public int CurrentCycle => GetState()?.CurrentCycle ?? 0;
    public int CurrentDayNumber => CurrentCycle + 1;
    public CalendarDayType CurrentDayType => GetState()?.CurrentDayType ?? CalendarDayType.Day;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DayCycleStageChangedEvent>(OnDayCycleChanged);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<CalendarEventStartedEvent>(OnCalendarEventStarted);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_ticker.RunLevel != GameRunLevel.InRound ||
            GetState() is not { Initialized: true, UsesExternalClock: false } state)
        {
            return;
        }

        while (state.NextTransition <= _timing.CurTime)
        {
            var nextType = state.CurrentDayType == CalendarDayType.Day
                ? CalendarDayType.Night
                : CalendarDayType.Day;
            ChangeDayType(state, nextType, state.NextTransition);
        }
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
                    {
                        Spawn(entProto, MapCoordinates.Nullspace);
                    }
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

        if (markerToSpawns.Count == 0)
            return;

        var query = EntityQueryEnumerator<CalendarSpawnMarkerComponent, TransformComponent>();
        while (query.MoveNext(out _, out var marker, out var xform))
        {
            if (!markerToSpawns.TryGetValue(marker.MarkerId, out var spawns))
                continue;

            foreach (var (proto, amount) in spawns)
            {
                for (var i = 0; i < amount; i++)
                {
                    Spawn(proto, xform.Coordinates);
                }
            }
        }
    }

    private void OnRoundStart(RoundStartedEvent args)
    {
        ClearState();

        var uid = Spawn("MedievalCalendar", MapCoordinates.Nullspace);
        var state = Comp<CalendarStateComponent>(uid);
        if (state.CycleDuration <= TimeSpan.Zero || state.DayDuration <= TimeSpan.Zero ||
            state.DayDuration >= state.CycleDuration || state.DaysCount < 1 ||
            state.DayStageNumber == state.NightStageNumber)
        {
            Log.Error("Cannot initialize calendar: invalid cycle duration, day duration, schedule size or stage numbers.");
            QueueDel(uid);
            return;
        }

        GenerateDeck(state.DayDeck, "Day", "DefaultCalendarEvent", state.DaysCount);
        GenerateDeck(state.NightDeck, "Night", "DefaultCalendarNightEvent", state.DaysCount);
        RaiseLocalEvent(new CalendarDecksGeneratedEvent());

        state.Initialized = true;
        state.NextTransition = _timing.CurTime + state.DayDuration;
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
        {
            QueueDel(uid);
        }
    }

    public void SetOverrideEvent(int dayNumber, ProtoId<CalendarEventPrototype> eventId, bool isNight = false)
    {
        if (GetState() is not { } state)
            return;

        var deck = isNight ? state.NightDeck : state.DayDeck;
        var index = dayNumber - 1;
        if (index < 0 || index >= deck.Count || !_prototype.HasIndex(eventId))
        {
            Log.Error($"Cannot assign calendar event {eventId} to day {dayNumber}: invalid day or event prototype.");
            return;
        }

        deck[index] = eventId;
        if (state.Initialized)
            RaiseLocalEvent(new CalendarUpdatedEvent());
    }

    private void GenerateDeck(List<ProtoId<CalendarEventPrototype>> deck, string filterTag, string fallbackEventId, int daysCount)
    {
        deck.Clear();

        var pool = new List<CalendarEventPrototype>();
        var playerCount = _playerManager.PlayerCount;

        foreach (var proto in _prototype.EnumeratePrototypes<CalendarEventPrototype>())
        {
            if (proto.Tags.Contains(filterTag) &&
                proto.Weight > 0f &&
                playerCount >= proto.MinPlayers &&
                playerCount <= proto.MaxPlayers)
            {
                pool.Add(proto);
            }
        }

        if (pool.Count == 0)
        {
            for (var day = 1; day <= daysCount; day++)
            {
                deck.Add(fallbackEventId);
            }
            return;
        }

        var counts = new Dictionary<string, int>(pool.Count);
        var lastOccurrence = new Dictionary<string, int>(pool.Count);

        for (var day = 1; day <= daysCount; day++)
        {
            var candidates = pool.FindAll(p =>
                p.MinDay <= day && p.GetWeight(day) > 0f &&
                counts.GetValueOrDefault(p.ID) < p.MaxOccurrences &&
                (day - lastOccurrence.GetValueOrDefault(p.ID, -999)) >= p.MinOffset
            );

            if (candidates.Count == 0)
            {
                candidates = pool.FindAll(p =>
                    p.MinDay <= day && p.GetWeight(day) > 0f &&
                    p.MaxOccurrences >= daysCount &&
                    (day - lastOccurrence.GetValueOrDefault(p.ID, -999)) >= p.MinOffset
                );
            }

            if (candidates.Count == 0)
            {
                deck.Add(fallbackEventId);
                continue;
            }

            var totalWeight = 0f;
            for (var i = 0; i < candidates.Count; i++)
            {
                totalWeight += candidates[i].GetWeight(day);
            }

            var roll = _random.NextFloat() * totalWeight;
            var acc = 0f;
            var selected = candidates[0];

            for (var i = 0; i < candidates.Count; i++)
            {
                acc += candidates[i].GetWeight(day);
                if (roll <= acc)
                {
                    selected = candidates[i];
                    break;
                }
            }

            deck.Add(selected.ID);
            counts[selected.ID] = counts.GetValueOrDefault(selected.ID) + 1;
            lastOccurrence[selected.ID] = day;
        }
    }

    private void OnDayCycleChanged(ref DayCycleStageChangedEvent args)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || GetState() is not { Initialized: true } state)
            return;

        if (!state.UsesExternalClock)
        {
            state.UsesExternalClock = true;
            Log.Info("Calendar now follows external DayTime stage events.");
        }

        TriggerNextDayStageNotification(args.NextStage);
    }

    public void TriggerNextDayStageNotification(int stageNumber)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || GetState() is not { Initialized: true } state)
            return;

        var isDay = state.DayStageNumber < state.NightStageNumber
            ? stageNumber >= state.DayStageNumber && stageNumber < state.NightStageNumber
            : stageNumber >= state.DayStageNumber || stageNumber < state.NightStageNumber;
        ChangeDayType(state, isDay ? CalendarDayType.Day : CalendarDayType.Night, _timing.CurTime);
    }

    private void ChangeDayType(CalendarStateComponent state, CalendarDayType dayType, TimeSpan transitionTime)
    {
        if (state.CurrentDayType == dayType)
            return;

        if (dayType == CalendarDayType.Day)
            state.CurrentCycle++;

        state.CurrentDayType = dayType;
        state.NextTransition = transitionTime + (dayType == CalendarDayType.Day
            ? state.DayDuration
            : state.CycleDuration - state.DayDuration);
        TriggerDayStageNotification();
    }

    public bool TryAdvanceDays(int days)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || days < 1 ||
            GetState() is not { Initialized: true } state ||
            days > int.MaxValue - state.CurrentCycle)
        {
            return false;
        }

        state.CurrentCycle += days;
        state.CurrentDayType = CalendarDayType.Day;
        state.NextTransition = _timing.CurTime + state.DayDuration;
        TriggerDayStageNotification();
        return true;
    }

    public void TriggerDayStageNotification(ProtoId<CalendarEventPrototype>? protoId = null)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || GetState() is not { Initialized: true } state)
            return;

        var deck = state.CurrentDayType == CalendarDayType.Day ? state.DayDeck : state.NightDeck;
        var id = protoId ?? deck[state.CurrentCycle % deck.Count];

        if (!_prototype.TryIndex(id, out var proto))
            return;

        RaiseLocalEvent(new CalendarUpdatedEvent());
        RaiseNetworkEvent(new CalendarBroadcastNotificationEvent(id, state.CurrentCycle + 1, state.CurrentDayType), Filter.Broadcast());
        RaiseLocalEvent(new CalendarEventStartedEvent(state.CurrentCycle + 1, id, proto, state.CurrentDayType));
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

        var entSys = IoCManager.Resolve<IEntitySystemManager>();
        var daySystem = entSys.GetEntitySystem<CalendarSystem>();

        if (!daySystem.TryAdvanceDays(dayCount))
        {
            shell.WriteError(Loc.GetString("cmd-trigger-next-day-cycle-not-running"));
            return;
        }
        shell.WriteLine(Loc.GetString("cmd-trigger-next-day-cycle-success", ("days", dayCount)));
    }
}
