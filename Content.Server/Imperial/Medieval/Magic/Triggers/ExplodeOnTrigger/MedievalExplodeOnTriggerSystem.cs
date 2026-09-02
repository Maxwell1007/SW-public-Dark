using Content.Shared.Explosion.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Trigger;

namespace Content.Server.Imperial.Medieval.Magic.Triggers.ExplodeOnTrigger;

public sealed class MedievalExplodeOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly SharedExplosionSystem _explosion = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MedievalExplodeOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<MedievalExplodeOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (args.Key != null && !ent.Comp.KeysIn.Contains(args.Key))
            return;

        var target = ent.Comp.TargetUser ? args.User : ent.Owner;
        if (target == null)
            return;

        var user = args.User;
        if (TryComp<ProjectileComponent>(ent.Owner, out var projectile) && projectile.Shooter != null)
            user = projectile.Shooter;

        _explosion.TriggerExplosive(target.Value, user: user);
        args.Handled = true;
    }
}
