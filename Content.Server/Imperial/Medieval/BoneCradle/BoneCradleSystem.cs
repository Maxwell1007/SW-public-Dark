using Content.Server.Cult.Components;
using Content.Server.GameTicking.Rules;
using Content.Server.Imperial.Medieval.SkeletonInvasion;
using Content.Shared.Chat;
using Content.Shared.Imperial.Medieval.Calendar;
using Robust.Server.Spawners;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Server.Imperial.Medieval.BoneCradle;

public sealed class BoneCradleSystem : GameRuleSystem<BoneCradleRuleComponent>
{
    [Dependency] private readonly SkeletonProgressionSystem _progression = default!;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(TimedDespawnSystem));

        SubscribeLocalEvent<CalendarEventAvailabilityEvent>(OnEventAvailability);
        SubscribeLocalEvent<CalendarEventStartedEvent>(OnCalendarEventStarted);
        SubscribeLocalEvent<BoneCradleComponent, ComponentStartup>(OnCradleStartup);
        SubscribeLocalEvent<BoneCradleComponent, TimedDespawnEvent>(OnCradleExpired);
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

            if (FindCradle() is { } existing)
            {
                Log.Error($"Cannot spawn a bone cradle for calendar event {args.EventId}: {ToPrettyString(existing)} already exists.");
                return;
            }

            var markers = GetSpawnMarkers();
            if (markers.Count == 0)
            {
                Log.Error($"Cannot spawn a bone cradle for calendar event {args.EventId}: no available spawn markers found.");
                return;
            }

            var marker = RobustRandom.Pick(markers);
            var transform = Transform(marker);
            var message = Loc.GetString(component.SpawnAnnouncement,
                ("location", Loc.GetString(GetMarkerLocation(transform, component))));
            ChatManager.ChatMessageToAll(ChatChannel.Radio, message, message, EntityUid.Invalid, false, true,
                colorOverride: component.AnnouncementColor);
            Spawn(component.CradlePrototype, transform.Coordinates);
            return;
        }
    }

    private void OnCradleStartup(Entity<BoneCradleComponent> ent, ref ComponentStartup args)
    {
        if (FindCradle(ent.Owner) is { } existing)
        {
            Log.Error($"Cannot initialize a second bone cradle {ToPrettyString(ent.Owner)}: {ToPrettyString(existing)} already exists.");
            QueueDel(ent.Owner);
            return;
        }

        if (ent.Comp.GracePeriod < TimeSpan.Zero || ent.Comp.RewardInterval <= TimeSpan.Zero || ent.Comp.PointsPerReward <= 0 ||
            !TryComp<TimedDespawnComponent>(ent.Owner, out var despawn))
        {
            Log.Error($"Invalid bone cradle reward configuration on {ToPrettyString(ent.Owner)}.");
            return;
        }

        ent.Comp.NextReward = Timing.CurTime + ent.Comp.GracePeriod + ent.Comp.RewardInterval;
        ent.Comp.RewardEnd = Timing.CurTime + TimeSpan.FromSeconds(despawn.Lifetime);
        ent.Comp.RewardsEnabled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BoneCradleComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.RewardsEnabled || TerminatingOrDeleted(uid))
                continue;

            AwardPoints(component, Timing.CurTime);
        }
    }

    private void OnCradleExpired(Entity<BoneCradleComponent> ent, ref TimedDespawnEvent args)
    {
        if (!ent.Comp.RewardsEnabled)
            return;

        AwardPoints(ent.Comp, ent.Comp.RewardEnd);
        ent.Comp.RewardsEnabled = false;
    }

    private void AwardPoints(BoneCradleComponent component, TimeSpan through)
    {
        while (component.NextReward <= through && component.NextReward <= component.RewardEnd)
        {
            _progression.AddPointsToAll(component.PointsPerReward);
            component.NextReward += component.RewardInterval;
        }
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

    private List<EntityUid> GetSpawnMarkers()
    {
        var markers = new List<EntityUid>();
        var query = EntityManager.AllEntityQueryEnumerator<BoneCradleSpawnMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
                transform.MapUid is not { } map || TerminatingOrDeleted(map) || EntityManager.IsQueuedForDeletion(map))
            {
                continue;
            }

            markers.Add(uid);
        }

        return markers;
    }

    private LocId GetMarkerLocation(TransformComponent transform, BoneCradleRuleComponent rule)
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
