using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._RD.Weight.Components;
using Content.Shared._RD.Weight.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Imperial.Medieval.Ships.Oar;
using Content.Shared.Imperial.Medieval.Skills;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Imperial.Medieval.Ships.Oar;

public sealed class OarSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedSkillsSystem _skills = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly RDWeightSystem _rdWeight = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OarComponent, OnOarDoAfterEvent>(OnOarDoAfter);
    }

    private void OnOarDoAfter(EntityUid uid, OarComponent component, ref OnOarDoAfterEvent args)
    {
        var item = _hands.GetActiveItem(args.User);
        if (args.Cancelled || args.Handled || item == null)
            return;

        if (!TryComp<OarComponent>(item, out var oar))
            return;

        Push(oar.Direction, oar.Power, args.User);
        args.Handled = true;
        args.Repeat = true;
    }

    private void Push(Angle direction, float power, EntityUid player)
    {
        power += power * (10 - _skills.GetSkillLevel(player, "Strength")) * 0.1f;

        var boat = _transform.GetParentUid(player);
        if (TryComp<ShuttleComponent>(boat, out var shuttle) && !shuttle.Enabled)
            return;

        var weight = _rdWeight.GetTotal(boat);
        if (weight == 0)
            weight = 10;

        var entities = _lookup.GetEntitiesIntersecting(boat);
        if (entities.Count > 1000)
            return;

        foreach (var entity in entities)
        {
            if (HasComp<RDWeightComponent>(entity))
                weight += _rdWeight.GetTotal(entity);
        }

        var normalizedAngle = (float) direction.Theta % (2 * MathF.PI);
        if (normalizedAngle < 0)
            normalizedAngle += 2 * MathF.PI;

        var directionVec = new Vector2(MathF.Cos(normalizedAngle), MathF.Sin(normalizedAngle));
        if (TryComp<TransformComponent>(player, out var playerTransform))
            directionVec = playerTransform.LocalRotation.RotateVec(directionVec);

        var impulse = directionVec * (power / weight);
        if (!TryComp<PhysicsComponent>(boat, out var body))
            return;

        _physics.WakeBody(boat);
        _physics.ApplyLinearImpulse(boat, impulse, body: body);
    }
}
