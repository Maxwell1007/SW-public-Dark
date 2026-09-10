using System.Numerics;

namespace Content.Server.Imperial.Medieval.Navigation;

public static class LocalNavigationPathfinder
{
    private static readonly Vector2[] Directions =
    {
        new(1, 0), new(1, 1), new(0, 1), new(-1, 1),
        new(-1, 0), new(-1, -1), new(0, -1), new(1, -1),
    };

    public static LocalNavigationSearch Create(Vector2 start, Vector2 goal, float spacing,
        float visionRange, float radius, int limit)
    {
        var search = new LocalNavigationSearch
        {
            Start = start,
            Goal = goal,
            Spacing = Math.Clamp(spacing, 0.2f, 1f),
            VisionRange = Math.Clamp(visionRange, 1f, 12f),
            Radius = Math.Clamp(radius, 2f, 64f),
            Limit = Math.Clamp(limit, 16, 4096),
        };
        search.Nodes.Add(new LocalNavigationNode { Position = start });
        search.Samples.Add(Vector2i.Zero, 0);
        search.Frontier.Enqueue(0, Vector2.Distance(start, goal));
        return search;
    }

    public static Vector2 Snap(LocalNavigationSearch search, Vector2 point)
    {
        var key = Key(search, point);
        return search.Start + new Vector2(key.X, key.Y) * search.Spacing;
    }

    private static Vector2i Key(LocalNavigationSearch search, Vector2 point)
    {
        var relative = (point - search.Start) / search.Spacing;
        return new Vector2i((int) MathF.Round(relative.X), (int) MathF.Round(relative.Y));
    }

    public static void Step(LocalNavigationSearch search, Func<Vector2, Vector2, LocalNavigationEdge?> probe)
    {
        if (search.Status != LocalNavigationSearchStatus.Searching)
            return;

        if (search.Expanding < 0)
        {
            if (!search.Frontier.TryDequeue(out var index, out _))
            {
                search.Status = search.LimitReached ? LocalNavigationSearchStatus.BudgetExceeded : LocalNavigationSearchStatus.NoPath;
                return;
            }

            if (search.Nodes[index].Closed)
                return;

            search.Expanding = index;
            search.Direction = -1;
            search.Nodes[index].Closed = true;
        }

        var parent = search.Expanding;
        var node = search.Nodes[parent];
        var direct = search.Direction == -1;
        var destination = direct
            ? search.Goal
            : node.Position + Directions[search.Direction] * search.Spacing;
        if (direct && Vector2.DistanceSquared(node.Position, search.Goal) > search.VisionRange * search.VisionRange)
            destination = Snap(search, node.Position + Vector2.Normalize(search.Goal - node.Position) * search.VisionRange);
        else if (direct)
            destination = search.Goal;

        search.Direction++;
        if (search.Direction == Directions.Length)
            search.Expanding = -1;

        if (Vector2.DistanceSquared(destination, search.Start) > search.Radius * search.Radius)
            return;

        var key = Key(search, destination);
        if (!direct && search.Samples.TryGetValue(key, out var visited) && search.Nodes[visited].Closed)
            return;

        var edge = probe(node.Position, destination);
        if (edge == null)
            return;

        if (edge.Value.Climb == null && Vector2.DistanceSquared(edge.Value.End, search.Goal) < 0.0001f)
        {
            search.Result.Add(edge.Value);
            for (var current = parent; search.Nodes[current].Parent >= 0; current = search.Nodes[current].Parent)
                search.Result.Add(search.Nodes[current].Edge);
            search.Result.Reverse();
            Simplify(search);
            search.Status = LocalNavigationSearchStatus.Found;
            return;
        }

        if (Vector2.DistanceSquared(edge.Value.End, search.Start) > search.Radius * search.Radius)
            return;

        key = Key(search, edge.Value.End);
        var cost = node.Cost + Vector2.Distance(node.Position, edge.Value.End) + Math.Max(0f, edge.Value.ExtraCost);
        if (search.Samples.TryGetValue(key, out var existing))
        {
            var neighbor = search.Nodes[existing];
            if (cost >= neighbor.Cost || Vector2.DistanceSquared(neighbor.Position, edge.Value.End) > 0.0001f)
                return;

            neighbor.Cost = cost;
            neighbor.Parent = parent;
            neighbor.Edge = edge.Value;
            neighbor.Closed = false;
            search.Frontier.Enqueue(existing, cost + Vector2.Distance(neighbor.Position, search.Goal));
            return;
        }

        if (search.Nodes.Count >= search.Limit)
        {
            search.LimitReached = true;
            return;
        }

        var next = search.Nodes.Count;
        search.Nodes.Add(new LocalNavigationNode
        {
            Position = edge.Value.End,
            Cost = cost,
            Parent = parent,
            Edge = edge.Value,
        });
        search.Samples.Add(key, next);
        search.Frontier.Enqueue(next, cost + Vector2.Distance(edge.Value.End, search.Goal));
    }

    private static void Simplify(LocalNavigationSearch search)
    {
        var start = search.Start;
        for (var i = 0; i + 1 < search.Result.Count;)
        {
            var current = search.Result[i];
            var next = search.Result[i + 1];
            var first = current.End - start;
            var second = next.End - current.End;
            if (current.Climb == null && next.Climb == null &&
                MathF.Abs(first.X * second.Y - first.Y * second.X) < 0.0001f &&
                Vector2.Dot(first, second) > 0 &&
                Vector2.DistanceSquared(start, next.End) <= search.VisionRange * search.VisionRange)
            {
                search.Result.RemoveAt(i);
                continue;
            }

            start = current.End;
            i++;
        }
    }
}
