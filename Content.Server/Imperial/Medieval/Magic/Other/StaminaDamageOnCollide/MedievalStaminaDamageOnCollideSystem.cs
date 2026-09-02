using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;

namespace Content.Server.Imperial.Medieval.Magic.Other.StaminaDamageOnCollide;

public sealed class MedievalStaminaDamageOnCollideSystem : EntitySystem
{
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MedievalStaminaDamageOnCollideComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnProjectileHit(Entity<MedievalStaminaDamageOnCollideComponent> ent, ref ProjectileHitEvent args)
    {
        if (!HasComp<StaminaComponent>(args.Target))
            return;

        var ev = new BeforeStaminaDamageOnTriggerEvent();
        RaiseLocalEvent(ent.Owner, ref ev);
        if (ev.Cancelled)
            return;

        _stamina.TakeStaminaDamage(
            args.Target,
            ent.Comp.Damage,
            source: args.Shooter ?? ent.Owner,
            sound: ent.Comp.Sound);
    }
}
