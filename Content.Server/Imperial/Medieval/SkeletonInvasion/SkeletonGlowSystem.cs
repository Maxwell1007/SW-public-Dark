using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

public sealed class SkeletonGlowSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SkeletonGlowComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SkeletonGlowComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<SkeletonGlowComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Light = Spawn(ent.Comp.Prototype, new EntityCoordinates(ent.Owner, Vector2.Zero));
    }

    private void OnShutdown(Entity<SkeletonGlowComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Light is { } light && !TerminatingOrDeleted(light))
            QueueDel(light);
    }
}
