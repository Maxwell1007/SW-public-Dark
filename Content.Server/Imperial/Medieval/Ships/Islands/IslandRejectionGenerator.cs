using System.Numerics;
using Robust.Shared.Utility;

namespace Content.Server.Imperial.Medieval.Ships.Islands;

public sealed class IslandRing
{
    public readonly float Inner;
    public readonly float Outer;
    public IslandRing(float inner, float outer) { Inner = inner; Outer = outer; }
}

public readonly struct IslandPlacement
{
    public readonly Vector2 Pos;
    public readonly ResPath Path;
    public readonly float Radius;
    public IslandPlacement(Vector2 pos, ResPath path, float radius) { Pos = pos; Path = path; Radius = radius; }
}

public sealed class IslandSpatialGrid
{
    private readonly float _cell;
    private float _maxR;
    private readonly Dictionary<long, List<IslandPlacement>> _cells = new();

    public IslandSpatialGrid(float cellSize) { _cell = MathF.Max(1f, cellSize); }

    private long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    private (int, int) CellOf(Vector2 p) =>
        ((int)MathF.Floor(p.X / _cell), (int)MathF.Floor(p.Y / _cell));

    public void Add(IslandPlacement isle)
    {
        _maxR = MathF.Max(_maxR, isle.Radius);
        var (cx, cy) = CellOf(isle.Pos);
        var k = Key(cx, cy);
        if (!_cells.TryGetValue(k, out var list)) { list = new(); _cells[k] = list; }
        list.Add(isle);
    }

    public bool Conflicts(Vector2 p, float radius, float gap)
    {
        var range = (int)MathF.Ceiling((radius + _maxR + gap) / _cell);
        var (cx, cy) = CellOf(p);
        for (var dx = -range; dx <= range; dx++)
        for (var dy = -range; dy <= range; dy++)
            if (_cells.TryGetValue(Key(cx + dx, cy + dy), out var list))
                foreach (var other in list)
                {
                    var min = radius + other.Radius + gap;
                    if (Vector2.DistanceSquared(p, other.Pos) < min * min)
                        return true;
                }
        return false;
    }
}

public sealed class IslandRejectionGenerator
{
    private readonly float _gap;
    private readonly int _maxPlacementAttempts;

    public IslandRejectionGenerator(float gap, int maxPlacementAttempts = 30)
    {
        _gap = gap;
        _maxPlacementAttempts = Math.Max(1, maxPlacementAttempts);
    }

    public List<IslandPlacement> Generate(
        IslandRing ring,
        List<(ResPath Path, float Radius)> islands,
        int targetCount,
        IslandSpatialGrid grid,
        Random rng)
    {
        var result = new List<IslandPlacement>();
        if (islands.Count == 0 || targetCount <= 0)
            return result;

        var selected = Shuffle(islands, rng);
        if (selected.Count > targetCount)
            selected.RemoveRange(targetCount, selected.Count - targetCount);

        selected.Sort((left, right) => right.Radius.CompareTo(left.Radius));

        foreach (var (path, radius) in selected)
        {
            for (var attempt = 0; attempt < _maxPlacementAttempts; attempt++)
            {
                var position = RandomInRing(ring, rng);
                if (grid.Conflicts(position, radius, _gap))
                    continue;

                var placement = new IslandPlacement(position, path, radius);
                result.Add(placement);
                grid.Add(placement);
                break;
            }
        }

        return result;
    }

    private static Vector2 RandomInRing(IslandRing ring, Random rng)
    {
        var u = rng.NextSingle();
        var r = MathF.Sqrt(ring.Inner * ring.Inner + u * (ring.Outer * ring.Outer - ring.Inner * ring.Inner));
        var a = rng.NextSingle() * MathF.Tau;
        return new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
    }

    private static List<T> Shuffle<T>(List<T> source, Random rng)
    {
        var list = new List<T>(source);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
