using System.Numerics;
using Content.Shared.Movement.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server.Imperial.Medieval.Guard;

public sealed partial class MedievalGuardSystem
{
    private void MoveTowards(EntityUid uid, MedievalGuardComponent component, EntityUid target, float stopDistance)
    {
        if (!TryComp<InputMoverComponent>(uid, out var mover) || !mover.CanMove)
        {
            StopMoving(uid);
            return;
        }

        var origin = _transform.GetMapCoordinates(uid);
        var destination = _transform.GetMapCoordinates(target);
        var offset = destination.Position - origin.Position;
        var distance = offset.Length();
        if (distance <= stopDistance)
        {
            StopMoving(uid);
            return;
        }

        var speed = TryComp<MovementSpeedModifierComponent>(uid, out var movement)
            ? movement.CurrentSprintSpeed
            : 3f;
        var step = MathF.Max(0.3f, speed * (float) component.ThinkInterval.TotalSeconds);
        var desired = offset / distance;
        var rotation = _mover.GetParentGridAngle(mover);
        var localDesired = (-rotation).RotateVec(desired);
        var heading = (int) MathF.Round(MathF.Atan2(localDesired.Y, localDesired.X) / (MathF.PI / 4));
        for (var i = 0; i < 5; i++)
        {
            var turn = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
            var localDirection = Angle.FromDegrees((heading + turn) * 45).ToVec();
            var direction = rotation.RotateVec(localDirection);
            if (Vector2.Dot(direction, desired) < -0.1f ||
                !CanMoveTowards(uid, component, origin, direction, step))
                continue;

            SetMovement(uid, mover, localDirection);
            return;
        }

        StopMoving(uid);
    }

    private bool CanMoveTowards(EntityUid uid, MedievalGuardComponent component, MapCoordinates origin,
        Vector2 direction, float distance)
    {
        if (!TryComp<PhysicsComponent>(uid, out var body))
            return false;

        var samples = Math.Max(1, (int) MathF.Ceiling(distance / component.ObstacleRadius));
        for (var i = 1; i <= samples; i++)
        {
            component.Obstacles.Clear();
            _lookup.GetEntitiesInRange(origin.MapId, origin.Position + direction * (distance * i / samples),
                component.ObstacleRadius, component.Obstacles, LookupFlags.Dynamic | LookupFlags.Static);
            foreach (var obstacle in component.Obstacles)
            {
                if (obstacle == uid || !TryComp<PhysicsComponent>(obstacle, out var physics) ||
                    !physics.CanCollide || !physics.Hard)
                    continue;

                if ((body.CollisionMask & physics.CollisionLayer) != 0 ||
                    (body.CollisionLayer & physics.CollisionMask) != 0)
                    return false;
            }
        }

        return true;
    }

    private void StopMoving(EntityUid uid)
    {
        if (TryComp<InputMoverComponent>(uid, out var mover))
            SetMovement(uid, mover, Vector2.Zero);
    }

    private void SetMovement(EntityUid uid, InputMoverComponent mover, Vector2 direction)
    {
        _mover.SetVelocityDirection((uid, mover), Direction.East, 0, direction.X > 0.1f);
        _mover.SetVelocityDirection((uid, mover), Direction.West, 0, direction.X < -0.1f);
        _mover.SetVelocityDirection((uid, mover), Direction.North, 0, direction.Y > 0.1f);
        _mover.SetVelocityDirection((uid, mover), Direction.South, 0, direction.Y < -0.1f);
    }
}
