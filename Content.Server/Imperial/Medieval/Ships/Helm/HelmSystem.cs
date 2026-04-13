using System;
using Content.Shared._RD.Weight.Systems;
using Content.Shared.Imperial.Medieval.Administration.Ships;
using Content.Shared.Imperial.Medieval.Ships.Helm;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Imperial.Medieval.Ships.Helm;

public sealed class HelmSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly RDWeightSystem _rdWeight = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    private TimeSpan _nextCheckTime;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        if (curTime <= _nextCheckTime)
            return;

        _nextCheckTime = curTime + TimeSpan.FromSeconds(_cfg.GetCVar(ShipsCCVars.WindDelay));
        if (!_cfg.GetCVar(ShipsCCVars.WindEnabled))
            return;

        foreach (var helmComponent in EntityManager.EntityQuery<HelmComponent>())
        {
            var helm = helmComponent.Owner;
            var boat = _transform.GetParentUid(helm);
            if (!HasComp<MapGridComponent>(boat))
                continue;

            RotateShip(boat, helm, helmComponent);
        }
    }

    private void RotateShip(EntityUid boat, EntityUid helm, HelmComponent helmComponent)
    {
        var steeringOars = CountSteeringOars(boat);
        if (steeringOars <= 0)
            return;

        var steeringInput = GetSteeringInput(boat, helm, helmComponent);
        if (MathF.Abs(steeringInput) < 0.001f)
            return;

        var weight = MathF.Max(helmComponent.MinShipWeight, _rdWeight.GetTotal(boat));
        var motionFactor = MathF.Max(helmComponent.MinMotionFactor, _physics.GetMapLinearVelocity(boat).Length());
        var angularImpulse = steeringInput * motionFactor * steeringOars * helmComponent.TurnImpulseScalar / weight;

        _physics.WakeBody(boat);
        _physics.ApplyAngularImpulse(boat, angularImpulse);
    }

    private int CountSteeringOars(EntityUid boat)
    {
        var count = 0;
        // We assume ship grids never overlap each other, so intersecting entities belong to this ship.
        foreach (var entity in _lookup.GetEntitiesIntersecting(boat))
        {
            if (HasComp<SteeringOarComponent>(entity))
                count++;
        }

        return count;
    }

    private float GetSteeringInput(EntityUid boat, EntityUid helm, HelmComponent helmComponent)
    {
        var boatAngle = _transform.GetWorldRotation(boat);
        var helmAngle = _transform.GetWorldRotation(helm);
        var diffDegrees = (float) Angle.ShortestDistance(boatAngle, helmAngle).Degrees;
        var maxTurnAngle = MathF.Max(1f, helmComponent.SteeringAngleForMaxTurn);
        return Math.Clamp(diffDegrees / maxTurnAngle, -1f, 1f);
    }
}
