using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Imperial.Medieval.Ships.Anchor;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Imperial.Medieval.Ships.Anchor;

public sealed class ServerMedievalAnchorSystem : EntitySystem
{
    [Dependency] private readonly ShuttleSystem _shuttleSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MedievalAnchorComponent, UseAnchorEvent>(OnUseAnchor);
    }

    private void OnUseAnchor(EntityUid uid, MedievalAnchorComponent component, UseAnchorEvent args)
    {
        if (args.Target == null || args.Cancelled)
            return;

        var anchor = component.Owner;
        var anchorDown = component.Enabled;
        var anchorTransform = Transform(anchor);
        var grid = anchorTransform.GridUid;

        ShuttleComponent? shuttleComponent = null;
        if (!grid.HasValue || !anchorTransform.Anchored || !Resolve(grid.Value, ref shuttleComponent))
            return;

        if (!anchorDown)
        {
            _shuttleSystem.Disable(grid.Value);

            if (TryComp<PhysicsComponent>(grid.Value, out var body))
            {
                _physics.SetLinearVelocity(grid.Value, Vector2.Zero, body: body);
                _physics.SetAngularVelocity(grid.Value, 0f, body: body);
            }
        }
        else
        {
            _shuttleSystem.Enable(grid.Value);
        }

        shuttleComponent.Enabled = anchorDown;

        var nextAnchorPrototype = anchorDown ? "MedievalAnchorUp" : "MedievalAnchorDown";
        Spawn(nextAnchorPrototype, anchorTransform.Coordinates);
        Del(anchor);
    }
}
