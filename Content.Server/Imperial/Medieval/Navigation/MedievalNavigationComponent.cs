using System.Numerics;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Server.Imperial.Medieval.Navigation;

[RegisterComponent]
public sealed partial class MedievalNavigationComponent : Component
{
    [DataField]
    public float SampleSpacing = 0.5f;

    [DataField]
    public float VisionRange = 6f;

    [DataField]
    public float SearchRadius = 24f;

    [DataField]
    public int MaxSearchNodes = 2048;

    [DataField]
    public int MaxRetrySearchNodes = 16384;

    [DataField]
    public float Clearance = 0.03f;

    [DataField]
    public float MaxClimbDistance = 3f;

    [DataField]
    public float WaypointTolerance = 0.12f;

    [DataField]
    public float RepathDistance = 0.5f;

    [DataField]
    public float RepathInterval = 0.5f;

    [DataField]
    public float SearchTimeout = 8f;

    [DataField]
    public int MaxPathJoinChecks = 8;

    [DataField]
    public float RecoveryDistance = 1f;

    [DataField]
    public float ClimbRetryDelay = 1f;

    [DataField]
    public float BlockedClimbRetryDelay = 4f;

    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public LocalNavigationSearch? Search;

    public Func<Vector2, Vector2, LocalNavigationEdge?>? Probe;
    public Func<Vector2, bool>? CanFinish;

    [ViewVariables]
    public readonly List<LocalNavigationEdge> Path = new();

    [ViewVariables]
    public int PathIndex;

    [ViewVariables]
    public int Failures;

    [ViewVariables]
    public string LastFailure = string.Empty;

    public EntityUid Reference;
    public MapId Map;
    public Vector2 Goal;
    public float StopDistance;
    public float Radius;
    public PolygonShape SweepShape = new(0.2f);
    public PolygonShape EscapeSweepShape = new(0.2f);
    public int CollisionMask;
    public int CollisionLayer;
    public bool Queued;
    public TimeSpan NextSearch;
    public TimeSpan SearchStarted;
    public TimeSpan NextRepath;
    public int SearchBudgetFailures;
    public bool RefineSearch;
    public float ProgressDistance;
    public Vector2? RecoveryTarget;
    public Vector2? SteeringTarget;
    public TimeSpan SteeringExpires;
    public Vector2 ProgressPosition;
    public TimeSpan LastProgress;
    public EntityUid? ClimbObstacle;
    public Vector2 ClimbExit;
    public Vector2 ClimbEntry;
    public DoAfterId? ClimbDoAfter;
    public TimeSpan ClimbDeadline;
    public bool ClimbStarted;
    public bool Retreating;
    public readonly List<(Vector2 From, Vector2 To, TimeSpan Until)> FailedSegments = new();
    public readonly Dictionary<EntityUid, TimeSpan> FailedClimbs = new();
    public readonly HashSet<EntityUid> Overlaps = new();
}

public readonly record struct LocalNavigationEdge(Vector2 End, EntityUid? Climb = null, float ExtraCost = 0f,
    Vector2? Entry = null);

public sealed class LocalNavigationNode
{
    public Vector2 Position;
    public float Cost;
    public int Parent = -1;
    public LocalNavigationEdge Edge;
    public bool Closed;
}

public sealed class LocalNavigationSearch
{
    public Vector2 Start;
    public Vector2 Goal;
    public float Spacing;
    public float VisionRange;
    public float Radius;
    public float StopDistance;
    public int Limit;
    public int Expanding = -1;
    public int Direction;
    public bool LimitReached;
    public LocalNavigationSearchStatus Status;
    public readonly List<LocalNavigationNode> Nodes = new();
    public readonly Dictionary<Vector2i, int> Samples = new();
    public readonly PriorityQueue<int, float> Frontier = new();
    public readonly List<LocalNavigationEdge> Result = new();
}

public enum LocalNavigationSearchStatus : byte
{
    Searching,
    Found,
    NoPath,
    BudgetExceeded,
}
