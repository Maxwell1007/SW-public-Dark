using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared.Actions;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Imperial.Medieval.Guard;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server.Imperial.Medieval.Guard;

public sealed partial class MedievalGuardSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MedievalGuardComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MedievalGuardComponent, ComponentShutdown>(OnGuardShutdown);
        SubscribeLocalEvent<MedievalGuardComponent, MobStateChangedEvent>(OnGuardStateChanged);
        SubscribeLocalEvent<MedievalGuardOwnerComponent, ComponentShutdown>(OnOwnerShutdown);
        SubscribeLocalEvent<MedievalGuardOwnerComponent, MobStateChangedEvent>(OnOwnerStateChanged);
        SubscribeLocalEvent<MedievalGuardOwnerComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<MedievalGuardOwnerComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MedievalGuardOwnerComponent, MedievalGuardAttackActionEvent>(OnAttackAction);
        SubscribeLocalEvent<MedievalGuardOwnerComponent, MedievalGuardReleaseActionEvent>(OnReleaseAction);
    }

    private void OnMapInit(EntityUid uid, MedievalGuardComponent component, MapInitEvent args)
    {
        if (component.GuardedEntity is { } owner)
            BindGuard(uid, owner, component);
    }

    public bool BindGuard(EntityUid uid, EntityUid owner, MedievalGuardComponent? component = null)
    {
        if (!Resolve(uid, ref component) || uid == owner || TerminatingOrDeleted(owner) ||
            !IsAlive(uid) || !TryComp<MobStateComponent>(owner, out var mob) || mob.CurrentState == MobState.Dead)
            return false;

        UnbindGuard(uid, component);
        component.GuardedEntity = owner;
        var controller = EnsureComp<MedievalGuardOwnerComponent>(owner);
        controller.Guards.Add(uid);
        _actions.AddAction(owner, ref controller.AttackActionEntity, controller.AttackAction);
        _actions.AddAction(owner, ref controller.ReleaseActionEntity, controller.ReleaseAction);
        component.ThinkCancellation = new CancellationTokenSource();
        Timer.SpawnRepeating(component.ThinkInterval, () => Think(uid, component), component.ThinkCancellation.Token);
        Think(uid, component);
        return true;
    }

    public void UnbindGuard(EntityUid uid, MedievalGuardComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.ThinkCancellation?.Cancel();
        component.ThinkCancellation?.Dispose();
        component.ThinkCancellation = null;
        var owner = component.GuardedEntity;
        component.GuardedEntity = null;
        component.AttackTarget = null;
        SetState(uid, component, MedievalGuardState.Idle);
        StopMoving(uid);

        if (!TryComp<MedievalGuardOwnerComponent>(owner, out var controller))
            return;

        controller.Guards.Remove(uid);
        if (controller.Guards.Count == 0 && !controller.Deleted)
            RemComp<MedievalGuardOwnerComponent>(owner.Value);
    }

    private void OnGuardShutdown(EntityUid uid, MedievalGuardComponent component, ComponentShutdown args)
    {
        UnbindGuard(uid, component);
    }

    private void OnOwnerShutdown(EntityUid uid, MedievalGuardOwnerComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.AttackActionEntity);
        _actions.RemoveAction(uid, component.ReleaseActionEntity);
        QueueDel(component.AttackActionEntity);
        QueueDel(component.ReleaseActionEntity);
        var guards = component.Guards.ToArray();
        component.Guards.Clear();
        foreach (var guard in guards)
        {
            if (!TryComp<MedievalGuardComponent>(guard, out var guardComp))
                continue;

            guardComp.GuardedEntity = null;
            UnbindGuard(guard, guardComp);
        }
    }

    private void OnOwnerStateChanged(EntityUid uid, MedievalGuardOwnerComponent component, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            RemComp<MedievalGuardOwnerComponent>(uid);
    }

    private void OnGuardStateChanged(EntityUid uid, MedievalGuardComponent component, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            UnbindGuard(uid, component);
        else
            Think(uid, component);
    }

    private void OnAttackAction(EntityUid uid, MedievalGuardOwnerComponent component, MedievalGuardAttackActionEvent args)
    {
        if (!args.Handled && OrderAttack(uid, component, args.Target))
            args.Handled = true;
    }

    private void OnReleaseAction(EntityUid uid, MedievalGuardOwnerComponent component, MedievalGuardReleaseActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        foreach (var guard in component.Guards.ToArray())
        {
            if (!TryComp<MedievalGuardComponent>(guard, out var guardComp))
                continue;

            guardComp.AttackTarget = null;
            Think(guard, guardComp);
        }
    }

    private void OnAttacked(EntityUid uid, MedievalGuardOwnerComponent component, AttackedEvent args)
    {
        OrderAttack(uid, component, args.User);
    }

    private void OnDamageChanged(EntityUid uid, MedievalGuardOwnerComponent component, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } attacker)
            return;

        if (TryComp<ProjectileComponent>(attacker, out var projectile) && projectile.Shooter is { } shooter)
            attacker = shooter;

        OrderAttack(uid, component, attacker);
    }

    private bool OrderAttack(EntityUid owner, MedievalGuardOwnerComponent component, EntityUid target)
    {
        if (component.Guards.Count == 0 || target == owner || component.Guards.Contains(target) || !IsAlive(target))
            return false;

        var ordered = false;
        foreach (var guard in component.Guards.ToArray())
        {
            if (!TryComp<MedievalGuardComponent>(guard, out var guardComp) || !IsAlive(guard) ||
                !InRange(guard, owner, guardComp.MaxDistance) || !InRange(guard, target, guardComp.MaxDistance))
                continue;

            guardComp.AttackTarget = target;
            Think(guard, guardComp);
            ordered = true;
        }

        return ordered;
    }

    private void Think(EntityUid uid, MedievalGuardComponent component)
    {
        if (component.Deleted || TerminatingOrDeleted(uid) || Paused(uid))
            return;

        if (component.GuardedEntity is not { } owner || TerminatingOrDeleted(owner) ||
            !TryComp<MobStateComponent>(owner, out var ownerState) || ownerState.CurrentState == MobState.Dead)
        {
            UnbindGuard(uid, component);
            return;
        }

        if (!IsAlive(uid) || !InRange(uid, owner, component.MaxDistance) ||
            _containers.IsEntityOrParentInContainer(uid) || _containers.IsEntityOrParentInContainer(owner))
        {
            component.AttackTarget = null;
            SetState(uid, component, MedievalGuardState.Idle);
            StopMoving(uid);
            return;
        }

        if (component.AttackTarget is { } target &&
            (!IsAlive(target) || !InRange(uid, target, component.MaxDistance) ||
             _containers.IsEntityOrParentInContainer(target)))
            component.AttackTarget = null;

        var state = component.AttackTarget != null
            ? MedievalGuardState.Attack
            : InRange(uid, owner, component.FollowDistance) ? MedievalGuardState.Idle : MedievalGuardState.Follow;
        SetState(uid, component, state);
        var behavior = new MedievalGuardBehaviorEvent(state, component.AttackTarget ?? owner);
        RaiseLocalEvent(uid, ref behavior);
        if (behavior.Handled)
            return;

        switch (state)
        {
            case MedievalGuardState.Attack:
                Attack(uid, component, component.AttackTarget!.Value);
                break;
            case MedievalGuardState.Follow:
                MoveTowards(uid, component, owner, component.FollowDistance);
                break;
            default:
                StopMoving(uid);
                break;
        }
    }

    private void Attack(EntityUid uid, MedievalGuardComponent component, EntityUid target)
    {
        if (!_melee.TryGetWeapon(uid, out var weaponUid, out var weapon))
        {
            component.AttackTarget = null;
            Think(uid, component);
            return;
        }

        if (!InRange(uid, target, weapon.Range) ||
            !_interaction.InRangeUnobstructed(uid, Transform(target).Coordinates, weapon.Range))
        {
            MoveTowards(uid, component, target, weapon.Range * 0.8f);
            return;
        }

        StopMoving(uid);
        if (weapon.NextAttack <= _timing.CurTime)
            _melee.AttemptLightAttack(uid, weaponUid, weapon, target);
    }

    private void SetState(EntityUid uid, MedievalGuardComponent component, MedievalGuardState state)
    {
        if (component.State == state)
            return;

        component.State = state;
        if (TryComp<CombatModeComponent>(uid, out var combat))
            _combat.SetInCombatMode(uid, state == MedievalGuardState.Attack, combat);
    }

    private bool IsAlive(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && TryComp<MobStateComponent>(uid, out var mob) &&
               mob.CurrentState == MobState.Alive;
    }

    private bool InRange(EntityUid uid, EntityUid target, float range)
    {
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(target))
            return false;

        var origin = _transform.GetMapCoordinates(uid);
        var destination = _transform.GetMapCoordinates(target);
        return origin.MapId != MapId.Nullspace && origin.MapId == destination.MapId &&
               Vector2.DistanceSquared(origin.Position, destination.Position) <= range * range;
    }
}
