using System.Numerics;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Events;
using Content.Shared.DoAfter;
using Content.Shared.Imperial.Medieval.MobRiding;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;

namespace Content.Server.Imperial.Medieval.Navigation;

public sealed partial class MedievalNavigationSystem
{
    private void FollowPath(EntityUid uid, MedievalNavigationComponent component, Vector2 position)
    {
        while (component.PathIndex < component.Path.Count &&
               Vector2.DistanceSquared(position, component.Path[component.PathIndex].End) < 0.0144f)
        {
            component.PathIndex++;
            component.ProgressPosition = position;
            component.LastProgress = _timing.CurTime;
        }

        if (component.PathIndex >= component.Path.Count)
        {
            Halt(uid, component);
            return;
        }

        var edge = component.Path[component.PathIndex];
        if (edge.Climb is { } obstacle)
        {
            StartClimb(uid, component, obstacle, edge.End);
            return;
        }

        if (!Trace(uid, component, position, edge.End, out _))
        {
            RememberBlockedSegment(component, position, edge.End);
            Fail(uid, component, "Route obstructed");
            return;
        }

        var remaining = Vector2.Distance(position, edge.End);
        if (remaining < Vector2.Distance(component.ProgressPosition, edge.End) - 0.05f)
        {
            component.ProgressPosition = position;
            component.LastProgress = _timing.CurTime;
        }
        else if (_timing.CurTime - component.LastProgress > TimeSpan.FromSeconds(Math.Max(1.5f, 1f / Math.Max(0.1f, MoveSpeed(uid)))))
        {
            RememberBlockedSegment(component, position, edge.End);
            Fail(uid, component, "No route progress");
            return;
        }

        var destination = edge.End;
        if (component.PathIndex == component.Path.Count - 1 && remaining > component.StopDistance)
            destination -= Vector2.Normalize(destination - position) * Math.Max(0f, component.StopDistance - 0.15f);
        else if (component.PathIndex == component.Path.Count - 1 && remaining <= component.StopDistance)
        {
            Halt(uid, component);
            return;
        }

        Steer(uid, component, destination);
    }

    private void RememberBlockedSegment(MedievalNavigationComponent component, Vector2 from, Vector2 to)
    {
        component.FailedSegments.RemoveAll(segment => segment.Until <= _timing.CurTime);
        if (component.FailedSegments.Count >= 16)
            component.FailedSegments.RemoveAt(0);
        component.FailedSegments.Add((from, to, _timing.CurTime + TimeSpan.FromSeconds(4)));
    }

    private void Steer(EntityUid uid, MedievalNavigationComponent component, Vector2 destination)
    {
        component.SteeringTarget = destination;
        component.SteeringExpires = _timing.CurTime + TimeSpan.FromSeconds(0.4);
        if (!TryComp<InputMoverComponent>(uid, out var mover))
            return;

        var direction = MapPosition(component, destination).Position - _transform.GetMapCoordinates(uid).Position;
        direction = (-_mover.GetParentGridAngle(mover)).RotateVec(direction);
        SetInput(uid, mover, direction);
    }

    private void Halt(EntityUid uid, MedievalNavigationComponent component)
    {
        component.SteeringTarget = null;
        if (TryComp<InputMoverComponent>(uid, out var mover))
            SetInput(uid, mover, Vector2.Zero);
    }

    private void SetInput(EntityUid uid, InputMoverComponent mover, Vector2 direction)
    {
        _mover.SetVelocityDirection((uid, mover), Direction.East, 0, direction.X > 0.01f);
        _mover.SetVelocityDirection((uid, mover), Direction.West, 0, direction.X < -0.01f);
        _mover.SetVelocityDirection((uid, mover), Direction.North, 0, direction.Y > 0.01f);
        _mover.SetVelocityDirection((uid, mover), Direction.South, 0, direction.Y < -0.01f);
    }

    private void OnWishDirection(EntityUid uid, MedievalNavigationComponent component, ref WishDirOverrideEvent args)
    {
        if (component.Target == null)
            return;

        if (component.SteeringTarget is not { } destination || _timing.CurTime > component.SteeringExpires ||
            TerminatingOrDeleted(component.Reference) || Transform(uid).MapID != component.Map ||
            TryComp<MobStateComponent>(uid, out var mob) && mob.CurrentState != MobState.Alive)
        {
            args.WishDir = Vector2.Zero;
            return;
        }

        var offset = MapPosition(component, destination).Position - _transform.GetMapCoordinates(uid).Position;
        var distance = offset.Length();
        args.WishDir = distance < 0.03f ? Vector2.Zero : offset / distance * Math.Min(args.WishDir.Length(), distance * 6f);
    }

    private void StartClimb(EntityUid uid, MedievalNavigationComponent component, EntityUid obstacle, Vector2 exit)
    {
        var position = Position(uid, component);
        if (TerminatingOrDeleted(obstacle))
        {
            component.Path[component.PathIndex] = new LocalNavigationEdge(exit);
            FollowPath(uid, component, position);
            return;
        }

        Halt(uid, component);
        if (!TryComp<ClimbableComponent>(obstacle, out var climbable) ||
            !_climb.CanVault(climbable, uid, obstacle, out _) ||
            !Trace(uid, component, position, exit, out _, true) ||
            !Trace(uid, component, position, Position(obstacle, component), out _, true))
        {
            RejectClimb(uid, component, obstacle);
            return;
        }

        PrepareQuery(uid, component);
        if (!IsFree(component, exit, component.Radius))
        {
            RejectClimb(uid, component, obstacle);
            return;
        }

        component.ClimbObstacle = obstacle;
        component.ClimbEntry = position;
        component.ClimbExit = exit;
        component.ClimbStarted = false;
        component.Retreating = false;
        component.ClimbDeadline = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(15, climbable.ClimbDelay * 4 + 5));
        if (!_climb.TryClimb(uid, uid, obstacle, out component.ClimbDoAfter, climbable))
            RejectClimb(uid, component, obstacle);
        else if (TryComp<ClimbingComponent>(uid, out var climbing) && climbing.IsClimbing)
            component.ClimbStarted = true;
    }

    private void OnClimbStarted(EntityUid uid, MedievalNavigationComponent component, ref StartClimbEvent args)
    {
        if (component.ClimbObstacle == args.Climbable)
        {
            component.ClimbStarted = true;
            component.ClimbDoAfter = null;
        }
    }

    private void OnBeforeClimb(EntityUid uid, MedievalNavigationComponent component, SelfBeforeClimbEvent args)
    {
        if (component.ClimbObstacle != args.BeingClimbedOn.Owner)
            return;

        var position = Position(uid, component);
        if (!Trace(uid, component, position, component.ClimbExit, out _, true) ||
            !Trace(uid, component, position, Position(args.BeingClimbedOn.Owner, component), out _, true))
        {
            args.Cancel();
            return;
        }

        PrepareQuery(uid, component);
        if (!IsFree(component, component.ClimbExit, component.Radius))
            args.Cancel();
    }

    private void ContinueClimb(EntityUid uid, MedievalNavigationComponent component)
    {
        var obstacle = component.ClimbObstacle!.Value;
        if (TerminatingOrDeleted(obstacle) || !TryComp<ClimbingComponent>(uid, out var climbing) ||
            _timing.CurTime > component.ClimbDeadline)
        {
            RejectClimb(uid, component, obstacle);
            return;
        }

        if (!component.ClimbStarted)
        {
            if (_doAfter.GetStatus(component.ClimbDoAfter) != DoAfterStatus.Running)
                RejectClimb(uid, component, obstacle);
            return;
        }

        if (climbing.NextTransition != null)
            return;

        var position = Position(uid, component);
        if (Vector2.DistanceSquared(position, component.ClimbExit) < 0.0144f)
        {
            PrepareQuery(uid, component);
            if (!IsFree(component, position, component.Radius))
            {
                RejectClimb(uid, component, obstacle);
                return;
            }

            component.ClimbObstacle = null;
            component.ClimbStarted = false;
            if (component.Retreating)
            {
                component.Retreating = false;
                RejectClimb(uid, component, obstacle);
                return;
            }
            component.PathIndex++;
            component.LastProgress = _timing.CurTime;
            component.ProgressPosition = position;
            Halt(uid, component);
            return;
        }

        if (!Trace(uid, component, position, component.ClimbExit, out _, climbing.IsClimbing))
        {
            RejectClimb(uid, component, obstacle);
            return;
        }

        Steer(uid, component, component.ClimbExit);
    }

    private void RejectClimb(EntityUid uid, MedievalNavigationComponent component, EntityUid obstacle)
    {
        if (component.ClimbStarted && !component.Retreating &&
            TryComp<ClimbingComponent>(uid, out var climbing) && climbing.IsClimbing && climbing.NextTransition == null &&
            Trace(uid, component, Position(uid, component), component.ClimbEntry, out _, true))
        {
            PrepareQuery(uid, component);
            if (IsFree(component, component.ClimbEntry, component.Radius))
            {
                component.Retreating = true;
                component.ClimbExit = component.ClimbEntry;
                component.ClimbDeadline = _timing.CurTime + TimeSpan.FromSeconds(5);
                Steer(uid, component, component.ClimbEntry);
                return;
            }
        }

        _doAfter.Cancel(component.ClimbDoAfter);
        component.ClimbDoAfter = null;
        component.ClimbObstacle = null;
        component.ClimbStarted = false;
        component.Retreating = false;
        if (component.FailedClimbs.Count >= 32)
            component.FailedClimbs.Clear();
        component.FailedClimbs[obstacle] = _timing.CurTime + TimeSpan.FromSeconds(10);
        Fail(uid, component, "Climb rejected or interrupted");
    }
}
