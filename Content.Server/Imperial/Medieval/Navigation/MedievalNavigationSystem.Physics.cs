using System.Numerics;
using Content.Shared.Climbing.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server.Imperial.Medieval.Navigation;

public sealed partial class MedievalNavigationSystem
{
    private const int ClimbMask = (int) (CollisionGroup.TableLayer | CollisionGroup.LowImpassable);
    private EntityUid _queryUid;
    private EntityUid? _queryTarget;
    private int _queryMask;
    private int _queryLayer;
    private BodyType _queryBodyType;
    private PhysShapeCircle _queryShape = new(0.2f);

    private bool RefreshBody(EntityUid uid, MedievalNavigationComponent component)
    {
        if (!TryComp<FixturesComponent>(uid, out var fixtures) ||
            !TryComp<PhysicsComponent>(uid, out var body) || !body.CanCollide)
            return false;

        var radius = 0f;
        var mask = 0;
        var layer = 0;
        TryComp<ClimbingComponent>(uid, out var climbing);
        foreach (var (name, fixture) in fixtures.Fixtures)
        {
            if (!fixture.Hard)
                continue;

            mask |= fixture.CollisionMask;
            if (climbing != null && climbing.DisabledFixtureMasks.TryGetValue(name, out var disabled))
                mask |= disabled;
            layer |= fixture.CollisionLayer;
            if (fixture.Shape is PhysShapeCircle circle)
            {
                var center = circle.Position;
                radius = Math.Max(radius, center.Length() + circle.Radius);
                continue;
            }

            for (var child = 0; child < fixture.Shape.ChildCount; child++)
            {
                var bounds = fixture.Shape.ComputeAABB(PhysicsTransform.Empty, child);
                var extent = Vector2.Max(Vector2.Abs(bounds.BottomLeft), Vector2.Abs(bounds.TopRight));
                radius = Math.Max(radius, extent.Length());
            }
        }

        radius += Math.Clamp(component.Clearance, 0f, 0.1f);
        if (radius <= 0.01f)
            return false;

        if (MathF.Abs(radius - component.Radius) > 0.01f ||
            mask != component.CollisionMask || layer != component.CollisionLayer)
        {
            component.Radius = radius;
            component.Shape = new PhysShapeCircle(radius);
            component.CollisionMask = mask;
            component.CollisionLayer = layer;
            component.Search = null;
            component.Path.Clear();
            component.PathIndex = 0;
            component.NextSearch = TimeSpan.Zero;
        }

        return true;
    }

    private void PrepareQuery(EntityUid uid, MedievalNavigationComponent component, bool climbing = false)
    {
        _queryUid = uid;
        _queryTarget = component.Target;
        _queryMask = climbing ? component.CollisionMask & ~ClimbMask : component.CollisionMask;
        _queryLayer = component.CollisionLayer;
        _queryBodyType = Comp<PhysicsComponent>(uid).BodyType;
        _queryShape = component.Shape;
    }

    private bool IgnoreBody(EntityUid uid)
    {
        if (uid == _queryUid || uid == _queryTarget || !TryComp<PhysicsComponent>(uid, out var body) || !body.CanCollide)
            return true;

        if (_queryBodyType == BodyType.KinematicController && body.BodyType == BodyType.KinematicController)
            return true;

        return false;
    }

    private float OnShapeHit(FixtureProxy proxy, Vector2 point, Vector2 normal, float fraction, ref RayResult result)
    {
        if (IgnoreBody(proxy.Entity) || !proxy.Fixture.Hard ||
            (proxy.Fixture.CollisionLayer & _queryMask) == 0 && (proxy.Fixture.CollisionMask & _queryLayer) == 0)
            return -1f;

        if (!result.Hit || fraction < result.Results[0].Fraction)
        {
            result.Results.Clear();
            result.Results.Add(new RayHit(proxy.Entity, normal, fraction) { Point = point });
        }

        return fraction;
    }

    private MapCoordinates MapPosition(MedievalNavigationComponent component, Vector2 point)
    {
        return _transform.ToMapCoordinates(new EntityCoordinates(component.Reference, point));
    }

    private Vector2 Position(EntityUid uid, MedievalNavigationComponent component)
    {
        return _transform.ToCoordinates(component.Reference, _transform.GetMapCoordinates(uid)).Position;
    }

    private bool IsFree(MedievalNavigationComponent component, Vector2 point, float radius)
    {
        _physicsQueries++;
        var map = MapPosition(component, point);
        component.Overlaps.Clear();
        _lookup.GetEntitiesInRange(map.MapId, map.Position, Math.Max(0.01f, radius), component.Overlaps,
            LookupFlags.Dynamic | LookupFlags.Static);
        foreach (var obstacle in component.Overlaps)
        {
            if (IgnoreBody(obstacle) || !TryComp<PhysicsComponent>(obstacle, out var body) || !body.Hard)
                continue;

            if ((body.CollisionLayer & _queryMask) != 0 || (body.CollisionMask & _queryLayer) != 0)
                return false;
        }

        return true;
    }

    private bool Trace(EntityUid uid, MedievalNavigationComponent component, Vector2 from, Vector2 to,
        out EntityUid? obstacle, bool climbing = false)
    {
        PrepareQuery(uid, component, climbing);
        obstacle = null;
        var start = MapPosition(component, from);
        var end = MapPosition(component, to);
        _physicsQueries++;
        var hit = _rays.CastShape(start.MapId, _queryShape, new PhysicsTransform(start.Position, Angle.Zero),
            end.Position - start.Position, new QueryFilter { MaskBits = uint.MaxValue, LayerBits = uint.MaxValue }, OnShapeHit);
        if (hit.Hit)
        {
            obstacle = hit.Results[0].Entity;
            return false;
        }

        return IsFree(component, from, component.Radius - component.Clearance - 0.01f) &&
               IsFree(component, to, component.Radius);
    }

    private LocalNavigationEdge? Probe(EntityUid uid, MedievalNavigationComponent component, Vector2 from, Vector2 to)
    {
        foreach (var segment in component.FailedSegments)
        {
            if (segment.Until > _timing.CurTime && Vector2.DistanceSquared(segment.From, from) < 0.25f &&
                Vector2.DistanceSquared(segment.To, to) < 0.25f)
                return null;
        }

        if (Trace(uid, component, from, to, out var obstacle))
            return new LocalNavigationEdge(to);

        if (obstacle is not { } climbable || component.Search is not { } search ||
            Vector2.DistanceSquared(from, to) < 0.0001f ||
            !TryComp<ClimbingComponent>(uid, out var climbing) || !climbing.CanClimb ||
            !TryComp<ClimbableComponent>(climbable, out var climb) || !climb.Vaultable ||
            component.FailedClimbs.TryGetValue(climbable, out var retry) && retry > _timing.CurTime)
            return null;

        var obstaclePosition = Position(climbable, component);
        if (Vector2.DistanceSquared(from, obstaclePosition) > MathF.Pow(Math.Min(climb.Range, component.MaxClimbDistance), 2))
            return null;

        var direction = Vector2.Normalize(to - from);
        var bounds = _physics.GetHardAABB(climbable);
        var referenceRotation = _transform.GetWorldRotation(component.Reference);
        var worldDirection = referenceRotation.RotateVec(direction);
        var worldStart = MapPosition(component, from).Position;
        var extent = Vector2.Dot(bounds.Center - worldStart, worldDirection) +
                     Math.Abs(worldDirection.X) * bounds.Width / 2 + Math.Abs(worldDirection.Y) * bounds.Height / 2;
        var exit = LocalNavigationPathfinder.Snap(search, from + direction * (extent + component.Radius + search.Spacing));
        if (Vector2.DistanceSquared(from, exit) > component.MaxClimbDistance * component.MaxClimbDistance ||
            !Trace(uid, component, from, exit, out _, true))
            return null;

        PrepareQuery(uid, component);
        if (!IsFree(component, exit, component.Radius))
            return null;

        return new LocalNavigationEdge(exit, climbable, climb.ClimbDelay * MoveSpeed(uid));
    }
}
