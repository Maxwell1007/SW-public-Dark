using System;
using System.Linq;
using System.Numerics;
using Content.Server.Imperial.Medieval.Navigation;
using NUnit.Framework;

namespace Content.Tests.Server.Imperial.Medieval;

[TestFixture]
public sealed class LocalNavigationPathfinderTest
{
    private readonly record struct Obstacle(float Left, float Bottom, float Right, float Top);

    private static bool Intersects(Vector2 start, Vector2 end, Obstacle obstacle, float radius)
    {
        var min = new Vector2(obstacle.Left - radius, obstacle.Bottom - radius);
        var max = new Vector2(obstacle.Right + radius, obstacle.Top + radius);
        var direction = end - start;
        var enter = 0f;
        var leave = 1f;
        for (var axis = 0; axis < 2; axis++)
        {
            var origin = axis == 0 ? start.X : start.Y;
            var delta = axis == 0 ? direction.X : direction.Y;
            var lower = axis == 0 ? min.X : min.Y;
            var upper = axis == 0 ? max.X : max.Y;
            if (Math.Abs(delta) < 0.00001f)
            {
                if (origin < lower || origin > upper)
                    return false;
                continue;
            }

            var first = (lower - origin) / delta;
            var second = (upper - origin) / delta;
            enter = Math.Max(enter, Math.Min(first, second));
            leave = Math.Min(leave, Math.Max(first, second));
            if (enter > leave)
                return false;
        }

        return true;
    }

    private static LocalNavigationSearch Find(Vector2 start, Vector2 goal, float bodyRadius,
        Obstacle[] obstacles, float searchRadius = 16, int limit = 2048)
    {
        var search = LocalNavigationPathfinder.Create(start, goal, 0.5f, 6f, searchRadius, limit);
        for (var step = 0; step < 100000 && search.Status == LocalNavigationSearchStatus.Searching; step++)
            LocalNavigationPathfinder.Step(search, (from, to) =>
                obstacles.Any(obstacle => Intersects(from, to, obstacle, bodyRadius)) ? null : new LocalNavigationEdge(to));
        Assert.That(search.Status, Is.Not.EqualTo(LocalNavigationSearchStatus.Searching));
        var previous = start;
        foreach (var edge in search.Result)
        {
            Assert.That(obstacles.Any(obstacle => Intersects(previous, edge.End, obstacle, bodyRadius)), Is.False);
            previous = edge.End;
        }

        return search;
    }

    [Test]
    public void OpenSpaceUsesLongSightSegments()
    {
        var search = Find(Vector2.Zero, new Vector2(12, 0), 0.2f, Array.Empty<Obstacle>());
        Assert.That(search.Status, Is.EqualTo(LocalNavigationSearchStatus.Found));
        Assert.That(search.Result.Count, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public void FindsDetourAroundThinFence()
    {
        var search = Find(Vector2.Zero, new Vector2(6, 0), 0.2f,
            new[] { new Obstacle(2.95f, -4, 3.05f, 4) });
        Assert.That(search.Status, Is.EqualTo(LocalNavigationSearchStatus.Found));
        Assert.That(search.Result.Any(edge => Math.Abs(edge.End.Y) > 4.2f), Is.True);
    }

    [Test]
    public void EscapesUShapedTrapByMovingAwayFromGoal()
    {
        var search = Find(Vector2.Zero, new Vector2(6, 0), 0.2f, new[]
        {
            new Obstacle(2, -2, 2.1f, 2),
            new Obstacle(-2, -2.1f, 2.1f, -2),
            new Obstacle(-2, 2, 2.1f, 2.1f),
        });
        Assert.That(search.Status, Is.EqualTo(LocalNavigationSearchStatus.Found));
        Assert.That(search.Result.Any(edge => edge.End.X < -2.2f), Is.True);
    }

    [Test]
    public void BodySizeDeterminesWhetherGapIsUsable()
    {
        var walls = new[]
        {
            new Obstacle(2, -20, 2.1f, -0.4f),
            new Obstacle(2, 0.4f, 2.1f, 20),
        };
        var small = Find(Vector2.Zero, new Vector2(4, 0), 0.2f, walls, 5);
        var large = Find(Vector2.Zero, new Vector2(4, 0), 0.5f, walls, 5);
        Assert.That(small.Status, Is.EqualTo(LocalNavigationSearchStatus.Found));
        Assert.That(large.Status, Is.EqualTo(LocalNavigationSearchStatus.NoPath));
    }

    [Test]
    public void EnclosedTargetTerminatesWithoutRepeatedExpansion()
    {
        var search = Find(Vector2.Zero, new Vector2(4, 0), 0.2f, new[]
        {
            new Obstacle(3, -1, 3.1f, 1), new Obstacle(5, -1, 5.1f, 1),
            new Obstacle(3, 1, 5.1f, 1.1f), new Obstacle(3, -1.1f, 5.1f, -1),
        }, 6);
        Assert.That(search.Status, Is.EqualTo(LocalNavigationSearchStatus.NoPath));
    }

    [Test]
    public void EachStepPerformsAtMostOneProbe()
    {
        var search = LocalNavigationPathfinder.Create(Vector2.Zero, new Vector2(5, 0), 0.5f, 6, 8, 64);
        for (var i = 0; i < 100 && search.Status == LocalNavigationSearchStatus.Searching; i++)
        {
            var calls = 0;
            LocalNavigationPathfinder.Step(search, (_, _) => { calls++; return null; });
            Assert.That(calls, Is.LessThanOrEqualTo(1));
        }
        Assert.That(search.Status, Is.EqualTo(LocalNavigationSearchStatus.NoPath));
    }

    [Test]
    public void NodeLimitIsReportedSeparatelyFromNoPath()
    {
        var search = Find(Vector2.Zero, new Vector2(6, 0), 0.2f,
            new[] { new Obstacle(2, -20, 2.1f, 20) }, 16, 16);
        Assert.That(search.Status, Is.EqualTo(LocalNavigationSearchStatus.BudgetExceeded));
        Assert.That(search.Nodes.Count, Is.LessThanOrEqualTo(16));
    }

    [Test]
    public void NewSearchObservesChangedGeometry()
    {
        var start = Vector2.Zero;
        var goal = new Vector2(6, 0);
        var original = Find(start, goal, 0.2f, Array.Empty<Obstacle>());
        var blocked = Find(start, goal, 0.2f, new[] { new Obstacle(2, -2, 2.1f, 2) });
        var reopened = Find(start, goal, 0.2f, Array.Empty<Obstacle>());
        Assert.That(blocked.Status, Is.EqualTo(LocalNavigationSearchStatus.Found));
        Assert.That(blocked.Result.Count, Is.GreaterThan(original.Result.Count));
        Assert.That(reopened.Result.Count, Is.EqualTo(original.Result.Count));
    }

    [Test]
    public void ReconstructedRouteRetainsClimbTransition()
    {
        var search = LocalNavigationPathfinder.Create(Vector2.Zero, new Vector2(4, 0), 0.5f, 6, 8, 128);
        var obstacle = new Robust.Shared.GameObjects.EntityUid(123);
        for (var step = 0; step < 1000 && search.Status == LocalNavigationSearchStatus.Searching; step++)
        {
            LocalNavigationPathfinder.Step(search, (from, to) =>
            {
                if (from.X < 2 && to.X >= 2)
                    return new LocalNavigationEdge(new Vector2(2.5f, 0), obstacle, 2f);
                return new LocalNavigationEdge(to);
            });
        }
        Assert.That(search.Status, Is.EqualTo(LocalNavigationSearchStatus.Found));
        Assert.That(search.Result.Count(edge => edge.Climb == obstacle), Is.EqualTo(1));
    }
}
