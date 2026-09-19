using Content.Shared.NPC;
using Robust.Shared.Map;

namespace Content.Server.NPC.Pathfinding;

// Imperial Medieval npc-obstacle-handling Start
/// <summary>
/// Identifies a node by position, so it still matches after a chunk rebuild.
/// </summary>
public readonly record struct PathNodeRef(EntityUid GraphUid, Vector2i ChunkOrigin, byte TileIndex)
{
    public static PathNodeRef From(PathPoly poly) => new(poly.GraphUid, poly.ChunkOrigin, poly.TileIndex);

    public bool Matches(PathPoly poly) =>
        poly.GraphUid == GraphUid && poly.ChunkOrigin == ChunkOrigin && poly.TileIndex == TileIndex;
}
// Imperial Medieval npc-obstacle-handling End

public sealed class PathPoly : IEquatable<PathPoly>
{
    [ViewVariables]
    public readonly EntityUid GraphUid;

    [ViewVariables]
    public readonly Vector2i ChunkOrigin;

    [ViewVariables]
    public readonly byte TileIndex;

    [ViewVariables]
    public readonly Box2 Box;

    [ViewVariables]
    public PathfindingData Data;

    [ViewVariables]
    public readonly HashSet<PathPoly> Neighbors;

    public PathPoly(EntityUid graphUid, Vector2i chunkOrigin, byte tileIndex, Box2 vertices, PathfindingData data, HashSet<PathPoly> neighbors)
    {
        GraphUid = graphUid;
        ChunkOrigin = chunkOrigin;
        TileIndex = tileIndex;
        Box = vertices;
        Data = data;
        Neighbors = neighbors;
    }

    public bool IsValid()
    {
        return (Data.Flags & PathfindingBreadcrumbFlag.Invalid) == 0x0;
    }

    [ViewVariables]
    public EntityCoordinates Coordinates => new(GraphUid, Box.Center);

    // Explicitly don't check neighbors.

    public bool IsEquivalent(PathPoly other)
    {
        return GraphUid.Equals(other.GraphUid) &&
               ChunkOrigin.Equals(other.ChunkOrigin) &&
               TileIndex == other.TileIndex &&
               Data.IsEquivalent(other.Data) &&
               Box.Equals(other.Box);
    }

    public bool Equals(PathPoly? other)
    {
        return other != null &&
               GraphUid.Equals(other.GraphUid) &&
               ChunkOrigin.Equals(other.ChunkOrigin) &&
               TileIndex == other.TileIndex &&
               Data.Equals(other.Data) &&
               Box.Equals(other.Box);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is PathPoly other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GraphUid, ChunkOrigin, TileIndex, Box);
    }
}
