using System.Linq;
using System.Numerics;
using Content.Server.Imperial.Medieval.SkeletonInvasion;
using Content.Server.Popups;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.Imperial.Medieval.BoneCradle;
using Content.Shared.Imperial.Medieval.CapturePoint;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Imperial.Medieval.BoneCradle;

public sealed class BoneCradleTeleportSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SkeletonProgressionSystem _progression = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoneCradleTeleportComponent, MapInitEvent>(OnCradleMapInit);
        SubscribeLocalEvent<BoneCradleTeleportComponent, ComponentShutdown>(OnCradleShutdown);
        SubscribeLocalEvent<BoneCradleTeleportRecipientComponent, ComponentShutdown>(OnRecipientShutdown);
        SubscribeLocalEvent<BoneCradleTeleportRecipientComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<BoneCradleTeleportRecipientComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<BoneCradleTeleportRecipientComponent, BoneCradleTeleportActionEvent>(OnAction);
        SubscribeLocalEvent<BoneCradleTeleportRecipientComponent, BoneCradleTeleportDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<BoneCradleTeleportRecipientComponent, DoAfterAttemptEvent<BoneCradleTeleportDoAfterEvent>>(OnAttempt);
        SubscribeLocalEvent<CapturePointResultEvent>(OnCaptureResult);
    }

    private void OnCradleMapInit(Entity<BoneCradleTeleportComponent> ent, ref MapInitEvent args)
    {
        if (TerminatingOrDeleted(ent.Owner) || EntityManager.IsQueuedForDeletion(ent.Owner) ||
            !TryComp<BoneCradleComponent>(ent.Owner, out var cradle))
            return;

        var query = EntityManager.AllEntityQueryEnumerator<BoneCradleTeleportComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (uid != ent.Owner)
                Deactivate((uid, component));
        }

        ent.Comp.GraceEndsAt = _timing.CurTime + cradle.GracePeriod;
        ent.Comp.Active = cradle.GracePeriod > TimeSpan.Zero;
        if (ent.Comp.Active)
            RefreshRecipients(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BoneCradleTeleportComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.Active)
                continue;

            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
                _timing.CurTime >= component.GraceEndsAt)
            {
                Deactivate((uid, component));
                continue;
            }

            if (_timing.CurTime >= component.NextRefresh)
                RefreshRecipients((uid, component));
        }
    }

    private void RefreshRecipients(Entity<BoneCradleTeleportComponent> cradle)
    {
        cradle.Comp.NextRefresh = _timing.CurTime + cradle.Comp.RefreshInterval;

        var recipients = EntityManager.AllEntityQueryEnumerator<BoneCradleTeleportRecipientComponent>();
        while (recipients.MoveNext(out var uid, out var component))
        {
            if (component.Cradle == cradle.Owner &&
                (!IsLivingFollower(uid, out var user) || user != component.UserId || cradle.Comp.TeleportedUsers.Contains(user)))
                ClearAction((uid, component));
        }

        var followers = EntityManager.AllEntityQueryEnumerator<MindContainerComponent, MobStateComponent>();
        while (followers.MoveNext(out var uid, out _, out _))
        {
            if (!IsLivingFollower(uid, out var user) || cradle.Comp.TeleportedUsers.Contains(user))
                continue;

            var recipient = EnsureComp<BoneCradleTeleportRecipientComponent>(uid);
            if (recipient.Cradle == cradle.Owner && recipient.UserId == user &&
                recipient.Action is { } action && !TerminatingOrDeleted(action) && !EntityManager.IsQueuedForDeletion(action))
                continue;

            ClearAction((uid, recipient));
            recipient.Cradle = cradle.Owner;
            recipient.UserId = user;
            if (!_actions.AddAction(uid, ref recipient.Action, cradle.Comp.ActionPrototype))
                ClearAction((uid, recipient));
        }
    }

    private bool IsLivingFollower(EntityUid uid, out NetUserId user)
    {
        user = default;
        if (TerminatingOrDeleted(uid) || !HasComp<MobStateComponent>(uid) || _mobState.IsDead(uid) ||
            HasComp<GhostComponent>(uid) || !_progression.IsAntagonist(uid) ||
            !_mind.TryGetMind(uid, out _, out var mind) ||
            (mind.UserId ?? mind.OriginalOwnerUserId) is not { } owner)
            return false;

        user = owner;
        return true;
    }

    private bool CanTeleport(Entity<BoneCradleTeleportRecipientComponent> ent, out Entity<BoneCradleTeleportComponent> cradle)
    {
        cradle = default;
        if (ent.Comp.Cradle is not { } target || TerminatingOrDeleted(target) || EntityManager.IsQueuedForDeletion(target) ||
            !TryComp<BoneCradleTeleportComponent>(target, out var component) || !component.Active ||
            _timing.CurTime >= component.GraceEndsAt || !_mobState.IsAlive(ent.Owner) ||
            !IsLivingFollower(ent.Owner, out var user) || user != ent.Comp.UserId || component.TeleportedUsers.Contains(user))
            return false;

        cradle = (target, component);
        return true;
    }

    private void OnAction(Entity<BoneCradleTeleportRecipientComponent> ent, ref BoneCradleTeleportActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || args.Action.Owner != ent.Comp.Action ||
            ent.Comp.DoAfter != null || !CanTeleport(ent, out var cradle))
            return;

        EnsureComp<DoAfterComponent>(ent.Owner);
        var doAfter = new DoAfterArgs(EntityManager, ent.Owner, cradle.Comp.ChannelDuration,
            new BoneCradleTeleportDoAfterEvent(GetNetEntity(cradle.Owner), GetNetEntity(args.Action.Owner)), ent.Owner)
        {
            BreakOnMove = true,
            MovementThreshold = 0f,
            BreakOnDamage = true,
            DamageThreshold = 0,
            NeedHand = false,
            RequireCanInteract = false,
            DuplicateCondition = DuplicateConditions.SameEvent,
            BlockDuplicate = true,
            CancelDuplicate = false,
            AttemptFrequency = AttemptFrequency.EveryTick,
        };

        if (!_doAfter.TryStartDoAfter(doAfter, out var id))
            return;

        ent.Comp.DoAfter = id;
        args.Handled = true;
    }

    private void OnAttempt(Entity<BoneCradleTeleportRecipientComponent> ent,
        ref DoAfterAttemptEvent<BoneCradleTeleportDoAfterEvent> args)
    {
        if (!MatchesAction(ent, args.Event) || !CanTeleport(ent, out _))
            args.Cancel();
    }

    private void OnDoAfter(Entity<BoneCradleTeleportRecipientComponent> ent, ref BoneCradleTeleportDoAfterEvent args)
    {
        if (args.Handled || ent.Comp.DoAfter != args.DoAfter.Id)
            return;

        ent.Comp.DoAfter = null;
        args.Handled = true;
        if (args.Cancelled || !MatchesAction(ent, args) || !CanTeleport(ent, out var cradle))
            return;

        if (!TryFindDestination(cradle, ent.Owner, out var destination))
        {
            _popup.PopupEntity(Loc.GetString("bone-cradle-teleport-no-space"), ent.Owner, ent.Owner);
            return;
        }

        _transform.SetCoordinates(ent.Owner, _transform.ToCoordinates(destination));
        _transform.AttachToGridOrMap(ent.Owner);
        cradle.Comp.TeleportedUsers.Add(ent.Comp.UserId);
        ClearAction(ent);
    }

    private bool MatchesAction(Entity<BoneCradleTeleportRecipientComponent> ent, BoneCradleTeleportDoAfterEvent args)
    {
        return ent.Comp.Cradle is { } cradle && !TerminatingOrDeleted(cradle) &&
            ent.Comp.Action is { } action && !TerminatingOrDeleted(action) &&
            GetNetEntity(cradle) == args.Cradle && GetNetEntity(action) == args.Action;
    }

    private bool TryFindDestination(Entity<BoneCradleTeleportComponent> cradle, EntityUid user, out MapCoordinates destination)
    {
        destination = default;
        var origin = _transform.GetMapCoordinates(cradle.Owner);
        if (origin.MapId == MapId.Nullspace)
            return false;

        for (var i = 0; i < cradle.Comp.PositionAttempts; i++)
        {
            var angle = _random.NextFloat() * MathF.Tau;
            var radius = cradle.Comp.MinimumRadius + _random.NextFloat() * (cradle.Comp.MaximumRadius - cradle.Comp.MinimumRadius);
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var candidate = new MapCoordinates(origin.Position + offset, origin.MapId);
            if (!CanArriveAt(cradle.Owner, user, origin, candidate, cradle.Comp.Clearance))
                continue;

            destination = candidate;
            return true;
        }

        return false;
    }

    private bool CanArriveAt(EntityUid cradle, EntityUid user, MapCoordinates origin, MapCoordinates destination, float clearance)
    {
        var direction = destination.Position - origin.Position;
        if (direction.LengthSquared() <= 0f)
            return false;

        var ray = new CollisionRay(origin.Position, Vector2.Normalize(direction), (int) CollisionGroup.MobMask);
        if (_physics.IntersectRay(origin.MapId, ray, direction.Length(), cradle).Any())
            return false;

        var bounds = new Box2(destination.Position - new Vector2(clearance), destination.Position + new Vector2(clearance));
        foreach (var nearby in _lookup.GetEntitiesIntersecting(destination.MapId, bounds, LookupFlags.Dynamic | LookupFlags.Static))
        {
            if (nearby == cradle || nearby == user || !TryComp<PhysicsComponent>(nearby, out var physics) || !physics.CanCollide ||
                !TryComp<FixturesComponent>(nearby, out var fixtures))
                continue;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.MobMask) != 0)
                    return false;
            }
        }

        return true;
    }

    private void OnCaptureResult(CapturePointResultEvent args)
    {
        var uid = GetEntity(args.Point);
        if (TryComp<BoneCradleComponent>(uid, out var cradle) && args.WinnerFaction == cradle.OpponentFaction &&
            TryComp<BoneCradleTeleportComponent>(uid, out var teleport))
            Deactivate((uid, teleport));
    }

    private void OnCradleShutdown(Entity<BoneCradleTeleportComponent> ent, ref ComponentShutdown args)
    {
        Deactivate(ent);
    }

    private void Deactivate(Entity<BoneCradleTeleportComponent> cradle)
    {
        cradle.Comp.Active = false;
        var query = EntityManager.AllEntityQueryEnumerator<BoneCradleTeleportRecipientComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Cradle == cradle.Owner)
                ClearAction((uid, component));
        }
    }

    private void OnRecipientShutdown(Entity<BoneCradleTeleportRecipientComponent> ent, ref ComponentShutdown args)
    {
        ClearAction(ent);
    }

    private void OnMindRemoved(Entity<BoneCradleTeleportRecipientComponent> ent, ref MindRemovedMessage args)
    {
        ClearAction(ent);
    }

    private void OnMobStateChanged(Entity<BoneCradleTeleportRecipientComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            ClearAction(ent);
    }

    private void ClearAction(Entity<BoneCradleTeleportRecipientComponent> ent)
    {
        var action = ent.Comp.Action;
        var pending = ent.Comp.DoAfter;
        ent.Comp.Cradle = null;
        ent.Comp.Action = null;
        ent.Comp.DoAfter = null;
        _doAfter.Cancel(pending);
        _actions.RemoveAction(action);
    }
}
