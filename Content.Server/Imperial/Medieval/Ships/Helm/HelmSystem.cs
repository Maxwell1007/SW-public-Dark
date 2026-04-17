using System;
using Content.Server.Shuttles.Components;
using Content.Shared.Imperial.Medieval.Administration.Ships;
using Content.Shared.Imperial.Medieval.Ships.Helm;
using Content.Shared.Maps;
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
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private TimeSpan _nextCheckTime;

    public override void Update(float frameTime)
    {
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
            RotateShip(boat, helm, CheckForce(boat));
        }
    }

    public void RotateShip(EntityUid boat, EntityUid helm, float helmSpeed)
    {
        if (TryComp<ShuttleComponent>(boat, out var shuttle) && !shuttle.Enabled)
            return;

        _physics.WakeBody(boat);

        var helmAngle = _transform.GetWorldRotation(helm);
        EntityUid? steeringOar = null;
        var entities = _lookup.GetEntitiesIntersecting(boat);
        foreach (var entity in entities)
        {
            if (!HasComp<SteeringOarComponent>(entity))
                continue;

            steeringOar = entity;
            break;
        }

        if (steeringOar == null)
            return;

        var steeringOarAngle = _transform.GetWorldRotation(steeringOar.Value);
        while (steeringOarAngle > 2)
            steeringOarAngle -= 2;

        while (helmAngle > 2)
            helmAngle -= 2;

        var diff = (float) steeringOarAngle * 180 - (float) helmAngle * 180;
        diff *= 0.001f;

        if (!TryComp<MapGridComponent>(boat, out var boatMapComp))
            return;

        var count = 0;
        var tiles = _map.GetAllTilesEnumerator(boat, boatMapComp);
        while (tiles.MoveNext(out _))
        {
            count++;
        }

        diff *= -count;

        if (!TryComp<TransformComponent>(helm, out var helmTransform))
            return;

        _transform.SetLocalRotation(helm, helmTransform.LocalRotation - (diff / 18));
        diff *= 0.01f;

        if (helmSpeed > 0)
        {
            _physics.ApplyAngularImpulse(boat, diff);
            return;
        }

        if (helmSpeed < 0)
            _physics.ApplyAngularImpulse(boat, -diff);
    }

    public float CheckForce(EntityUid boat)
    {
        var velocity = _physics.GetMapLinearVelocity(boat);
        return Math.Abs(velocity.X) + Math.Abs(velocity.Y);
    }
}
