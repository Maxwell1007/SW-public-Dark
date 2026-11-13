using Content.Server.Cult.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared.Imperial.Medieval.Calendar;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Imperial.Medieval.BoneCradle;

public sealed class BoneCradleSystem : GameRuleSystem<BoneCradleRuleComponent>
{
    [Dependency] private readonly CalendarSystem _calendar = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CalendarDecksGeneratedEvent>(OnCalendarDecksGenerated);
        SubscribeLocalEvent<CalendarEventStartedEvent>(OnCalendarEventStarted);
        SubscribeLocalEvent<BoneCradleComponent, ComponentStartup>(OnCradleStartup);
        SubscribeLocalEvent<BoneCradleSpawnMarkerComponent, MapInitEvent>(OnMarkerMapInit);
    }

    private void OnCalendarDecksGenerated(CalendarDecksGeneratedEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var component, out _))
        {
            if (component.FirstDay < 1 || component.DayInterval < 1)
            {
                Log.Error($"Invalid bone cradle schedule on {ToPrettyString(uid)}: first day and interval must be positive.");
                continue;
            }

            var events = new List<ProtoId<CalendarEventPrototype>>();
            foreach (var (eventId, sector) in component.Events)
            {
                if (GetSpawnMarkers(sector).Count == 0)
                {
                    Log.Warning($"No bone cradle spawn markers found in {sector}; calendar event {eventId} will not be scheduled.");
                    continue;
                }

                events.Add(eventId);
            }

            if (events.Count == 0)
                continue;

            RobustRandom.Shuffle(events);
            var daysCount = component.IsNight ? _calendar.NightDeck.Count : _calendar.DayDeck.Count;
            var eventIndex = 0;
            for (var day = component.FirstDay; day <= daysCount; day += component.DayInterval)
            {
                _calendar.SetOverrideEvent(day, events[eventIndex], component.IsNight);
                eventIndex = (eventIndex + 1) % events.Count;
            }
        }
    }

    private void OnCalendarEventStarted(CalendarEventStartedEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out var component, out _))
        {
            if (!component.Events.TryGetValue(args.EventId, out var sector))
                continue;

            if (FindCradle() is { } existing)
            {
                Log.Error($"Cannot spawn a bone cradle for calendar event {args.EventId}: {ToPrettyString(existing)} already exists.");
                return;
            }

            var markers = GetSpawnMarkers(sector);
            if (markers.Count == 0)
            {
                Log.Error($"Cannot spawn a bone cradle for calendar event {args.EventId}: no spawn markers found in {sector}.");
                return;
            }

            var marker = RobustRandom.Pick(markers);
            Spawn(component.CradlePrototype, Transform(marker).Coordinates);
            return;
        }
    }

    private void OnCradleStartup(Entity<BoneCradleComponent> ent, ref ComponentStartup args)
    {
        if (FindCradle(ent.Owner) is not { } existing)
            return;

        Log.Error($"Cannot initialize a second bone cradle {ToPrettyString(ent.Owner)}: {ToPrettyString(existing)} already exists.");
        QueueDel(ent.Owner);
    }

    private void OnMarkerMapInit(Entity<BoneCradleSpawnMarkerComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<CultMapBlockerComponent>(Transform(ent.Owner).ParentUid, out var blocker) &&
            !string.IsNullOrWhiteSpace(blocker.Sector))
        {
            return;
        }

        Log.Warning($"Bone cradle spawn marker {ToPrettyString(ent.Owner)} must be placed on a grid with a CultMapBlocker sector.");
    }

    private EntityUid? FindCradle(EntityUid? ignored = null)
    {
        var query = EntityManager.AllEntityQueryEnumerator<BoneCradleComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (uid != ignored && component.Running && !TerminatingOrDeleted(uid))
                return uid;
        }

        return null;
    }

    private List<EntityUid> GetSpawnMarkers(string sector)
    {
        var markers = new List<EntityUid>();
        var query = EntityQueryEnumerator<BoneCradleSpawnMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (TerminatingOrDeleted(uid) || transform.MapUid == null ||
                !TryComp<CultMapBlockerComponent>(transform.ParentUid, out var blocker) ||
                !string.Equals(blocker.Sector, sector, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            markers.Add(uid);
        }

        return markers;
    }
}
