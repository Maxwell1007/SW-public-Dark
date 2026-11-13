using Content.Shared.Damage.Events;
using Content.Shared.Imperial.Dash;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Imperial.Medieval.SkeletonInvasion;

public sealed class SharedSkeletonProgressionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SkeletonProgressionComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<SkeletonProgressionComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<SkeletonProgressionComponent, BeforeStaminaDamageEvent>(OnStaminaDamage);
        SubscribeLocalEvent<SkeletonProgressionComponent, CheckDashStaminaCostModifiersEvent>(OnDashStamina);
    }

    private void OnState(Entity<SkeletonProgressionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnSpeed(Entity<SkeletonProgressionComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        foreach (var upgrade in ent.Comp.Upgrades)
        {
            if (_prototypes.TryIndex(upgrade, out var prototype))
                args.ModifySpeed(prototype.SpeedModifier);
        }
    }

    private void OnStaminaDamage(Entity<SkeletonProgressionComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        foreach (var upgrade in ent.Comp.Upgrades)
        {
            if (_prototypes.TryIndex(upgrade, out var prototype))
                args.Value *= prototype.StaminaDamageModifier;
        }
    }

    private void OnDashStamina(Entity<SkeletonProgressionComponent> ent, ref CheckDashStaminaCostModifiersEvent args)
    {
        foreach (var upgrade in ent.Comp.Upgrades)
        {
            if (_prototypes.TryIndex(upgrade, out var prototype))
                args.Modifier *= prototype.DashStaminaModifier;
        }
    }
}
