using Content.Server.Cult.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared.Chat;
using Content.Shared.Imperial.Medieval.Calendar;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Server.Imperial.Medieval.MorbiusObelisk;

public sealed class MorbiusObeliskSystem : GameRuleSystem<MorbiusObeliskRuleComponent>
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CalendarEventAvailabilityEvent>(OnEventAvailability);
        SubscribeLocalEvent<CalendarEventStartedEvent>(OnCalendarEventStarted);
        SubscribeLocalEvent<MorbiusObeliskComponent, TimedDespawnEvent>(OnObeliskExpired);
    }

    private void OnEventAvailability(CalendarEventAvailabilityEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out var component, out _))
        {
            if (args.Prototype.ID != component.CalendarEvent.Id)
                continue;

            args.Available &= GetSpawnMarkers().Count > 0;
            return;
        }
    }

    private void OnCalendarEventStarted(CalendarEventStartedEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out var component, out _))
        {
            if (args.EventId != component.CalendarEvent)
                continue;

            var markers = GetSpawnMarkers();
            if (markers.Count == 0)
            {
                Log.Error($"Cannot spawn a Morbius obelisk for calendar event {args.EventId}: no available spawn markers found.");
                return;
            }

            var marker = RobustRandom.Pick(markers);
            var transform = Transform(marker);
            var message = Loc.GetString(component.SpawnAnnouncement,
                ("location", Loc.GetString(GetMarkerLocation(transform, component))));
            ChatManager.ChatMessageToAll(ChatChannel.Radio, message, message, EntityUid.Invalid, false, true,
                colorOverride: component.AnnouncementColor);
            Spawn(component.ObeliskPrototype, transform.Coordinates);
            return;
        }
    }

    private void OnObeliskExpired(Entity<MorbiusObeliskComponent> ent, ref TimedDespawnEvent args)
    {
        if (TerminatingOrDeleted(ent.Owner) || EntityManager.IsQueuedForDeletion(ent.Owner) || ent.Comp.RewardSpawned)
            return;

        ent.Comp.RewardSpawned = true;
        Spawn(ent.Comp.RewardPrototype, Transform(ent.Owner).Coordinates);
    }

    private List<EntityUid> GetSpawnMarkers()
    {
        var markers = new List<EntityUid>();
        var query = EntityManager.AllEntityQueryEnumerator<MorbiusObeliskSpawnMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
                transform.MapUid is not { } map || TerminatingOrDeleted(map) || EntityManager.IsQueuedForDeletion(map))
                continue;

            markers.Add(uid);
        }

        return markers;
    }

    private LocId GetMarkerLocation(TransformComponent transform, MorbiusObeliskRuleComponent rule)
    {
        if (!TryComp<CultMapBlockerComponent>(transform.ParentUid, out var blocker) &&
            (transform.MapUid is not { } map || !TryComp(map, out blocker)))
            return rule.UnknownLocation;

        foreach (var (sector, location) in rule.SectorLocations)
        {
            if (!string.IsNullOrWhiteSpace(sector) &&
                string.Equals(blocker.Sector, sector, StringComparison.OrdinalIgnoreCase))
                return location;
        }

        return rule.UnknownLocation;
    }
}
