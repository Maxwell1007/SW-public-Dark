using Stopwatch = System.Diagnostics.Stopwatch;
using System.Numerics;
using System.Threading;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Events;
using Content.Shared.Climbing.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Imperial.Medieval.MobRiding;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server.Imperial.Medieval.Navigation;

public sealed partial class MedievalNavigationSystem : EntitySystem
{
    private const int WorkIntervalMilliseconds = 20;
    private const int ConcurrentSearchLimit = 16;
    private const int StepsPerBatch = 96;
    private const double BatchBudgetMilliseconds = 1;

    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;

    private readonly Queue<Entity<MedievalNavigationComponent>> _requests = new();
    private readonly HashSet<EntityUid> _activeSearches = new();
    private readonly Stopwatch _workTime = new();
    private CancellationTokenSource _cancellation = new();
    private bool _scheduled;
    private int _physicsQueries;

    [ViewVariables] public int PendingSearches => _requests.Count;
    [ViewVariables] public int ActiveSearches => _activeSearches.Count;
    [ViewVariables] public double LastBatchMilliseconds { get; private set; }
    [ViewVariables] public int LastBatchSteps { get; private set; }
    [ViewVariables] public int LastBatchPhysicsQueries { get; private set; }
    [ViewVariables] public long CompletedSearches { get; private set; }
    [ViewVariables] public long FailedSearches { get; private set; }

    public override void Initialize()
    {
        SubscribeLocalEvent<MedievalNavigationComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MedievalNavigationComponent, WishDirOverrideEvent>(OnWishDirection);
        SubscribeLocalEvent<MedievalNavigationComponent, StartClimbEvent>(OnClimbStarted);
        SubscribeLocalEvent<MedievalNavigationComponent, SelfBeforeClimbEvent>(OnBeforeClimb);
    }

    public override void Shutdown()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        _requests.Clear();
        _activeSearches.Clear();
        base.Shutdown();
    }

    private void OnShutdown(EntityUid uid, MedievalNavigationComponent component, ComponentShutdown args)
    {
        Stop(uid, component);
    }

    public void Stop(EntityUid uid, MedievalNavigationComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        component.Target = null;
        _activeSearches.Remove(uid);
        component.Search = null;
        component.Path.Clear();
        component.PathIndex = 0;
        component.Failures = 0;
        component.SearchBudgetFailures = 0;
        component.RefineSearch = false;
        component.ProgressDistance = 0f;
        component.RecoveryTarget = null;
        component.NextRepath = TimeSpan.Zero;
        component.NextSearch = TimeSpan.Zero;
        _doAfter.Cancel(component.ClimbDoAfter);
        component.ClimbDoAfter = null;
        component.ClimbObstacle = null;
        component.ClimbStarted = false;
        component.Retreating = false;
        component.FailedClimbs.Clear();
        component.FailedSegments.Clear();
        Halt(uid, component);
    }

    public void Navigate(EntityUid uid, EntityUid target, float stopDistance)
    {
        var component = EnsureComp<MedievalNavigationComponent>(uid);
        component.Probe ??= (from, to) => Probe(uid, component, from, to);
        component.CanFinish ??= point => component.Search is { } search &&
            CanReachGoal(uid, component, point, search.Goal, search.StopDistance);
        if (!TryComp<InputMoverComponent>(uid, out var mover) || TerminatingOrDeleted(target) ||
            _containers.IsEntityOrParentInContainer(uid) || !RefreshBody(uid, component))
        {
            Stop(uid, component);
            return;
        }

        var xform = Transform(uid);
        if (xform.MapID != Transform(target).MapID)
        {
            Stop(uid, component);
            return;
        }

        var reference = xform.GridUid ?? xform.MapUid;
        if (reference is not { } anchor)
        {
            Stop(uid, component);
            return;
        }

        if (component.Target != target || component.Reference != anchor || component.Map != xform.MapID ||
            MathF.Abs(component.StopDistance - stopDistance) > 0.01f)
        {
            Stop(uid, component);
            component.Target = target;
            component.Reference = anchor;
            component.Map = xform.MapID;
            component.StopDistance = stopDistance;
            component.Goal = Position(target, component);
            component.LastProgress = _timing.CurTime;
        }

        if (component.ClimbObstacle != null)
        {
            ContinueClimb(uid, component);
            return;
        }

        if (!mover.CanMove)
        {
            component.LastProgress = _timing.CurTime;
            Halt(uid, component);
            return;
        }

        var position = Position(uid, component);
        var goal = Position(target, component);
        if (CanReachGoal(uid, component, position, goal, component.StopDistance))
        {
            _activeSearches.Remove(uid);
            component.Search = null;
            component.Path.Clear();
            component.PathIndex = 0;
            component.Failures = 0;
            component.SearchBudgetFailures = 0;
            component.RefineSearch = false;
            Halt(uid, component);
            return;
        }

        var hasPath = component.PathIndex < component.Path.Count;
        var goalMoved = Vector2.DistanceSquared(goal, component.Goal) > component.RepathDistance * component.RepathDistance;
        if (hasPath && goalMoved && component.Path[^1].Climb == null)
        {
            var from = component.PathIndex == component.Path.Count - 1
                ? position
                : component.Path[^2].End;
            var destination = ApproachGoal(from, goal, component.StopDistance);
            if (Trace(uid, component, from, destination, out _) &&
                CanReachGoal(uid, component, destination, goal, component.StopDistance))
            {
                component.Path[^1] = new LocalNavigationEdge(destination);
                component.Goal = goal;
                goalMoved = false;
            }
        }

        if ((!hasPath || goalMoved) && component.Search == null &&
            _timing.CurTime >= component.NextSearch && _timing.CurTime >= component.NextRepath)
            RequestPath(uid, component, position, goal);

        if (hasPath)
            FollowPath(uid, component, position);
        else
            Halt(uid, component);
    }

    private static Vector2 ApproachGoal(Vector2 from, Vector2 goal, float stopDistance)
    {
        var offset = goal - from;
        var distance = offset.Length();
        return distance <= stopDistance ? from : goal - offset / distance * Math.Max(0f, stopDistance - 0.05f);
    }

    private void RequestPath(EntityUid uid, MedievalNavigationComponent component, Vector2 position, Vector2 goal)
    {
        var radius = Math.Min(64f, Math.Max(component.SearchRadius, Vector2.Distance(position, goal) + 4f) + component.SearchBudgetFailures * 4f);
        var spacing = Math.Clamp(component.SampleSpacing, 0.2f, 1f);
        var refinedSpacing = component.RefineSearch ? Math.Max(0.2f, spacing / 2) : spacing;
        var density = spacing * spacing / (refinedSpacing * refinedSpacing);
        var limit = (int) Math.Clamp(component.MaxSearchNodes * (double) density * (1 + component.SearchBudgetFailures),
            16, Math.Clamp(component.MaxRetrySearchNodes, 16, 16384));
        component.Search = LocalNavigationPathfinder.Create(position, goal, refinedSpacing,
            component.VisionRange, radius, limit, Math.Max(0f, component.StopDistance - 0.05f));
        component.SearchStarted = _timing.CurTime;
        component.NextRepath = _timing.CurTime + TimeSpan.FromSeconds(component.RepathInterval);
        Enqueue(uid, component);
    }

    private void Enqueue(EntityUid uid, MedievalNavigationComponent component)
    {
        if (!component.Queued)
        {
            component.Queued = true;
            _requests.Enqueue((uid, component));
        }

        if (_scheduled)
            return;

        _scheduled = true;
        Timer.Spawn(WorkIntervalMilliseconds, ProcessRequests, _cancellation.Token);
    }

    private void ProcessRequests()
    {
        _scheduled = false;
        _workTime.Restart();
        _physicsQueries = 0;
        var steps = 0;
        var inspected = 0;
        while (_requests.TryDequeue(out var entity))
        {
            var (uid, component) = entity;
            component.Queued = false;
            if (inspected++ >= 128 || _workTime.Elapsed.TotalMilliseconds >= BatchBudgetMilliseconds)
            {
                component.Queued = true;
                _requests.Enqueue(entity);
                break;
            }

            if (component.Deleted || TerminatingOrDeleted(uid) || component.Search is not { } search)
            {
                _activeSearches.Remove(uid);
                continue;
            }

            if (Paused(uid))
            {
                component.Search = null;
                _activeSearches.Remove(uid);
                continue;
            }

            if (component.Target is not { } target || TerminatingOrDeleted(target) ||
                TerminatingOrDeleted(component.Reference) || Transform(uid).MapID != component.Map ||
                Transform(target).MapID != component.Map ||
                _containers.IsEntityOrParentInContainer(uid))
            {
                Stop(uid, component);
                continue;
            }

            if (!_activeSearches.Contains(uid))
            {
                if (_activeSearches.Count >= ConcurrentSearchLimit)
                {
                    component.Queued = true;
                    _requests.Enqueue(entity);
                    continue;
                }

                _activeSearches.Add(uid);
                component.SearchStarted = _timing.CurTime;
            }

            if (_timing.CurTime - component.SearchStarted > TimeSpan.FromSeconds(component.SearchTimeout))
            {
                Fail(uid, component, "Search timeout", true, true);
                continue;
            }

            LocalNavigationPathfinder.Step(search, component.Probe!, component.CanFinish);
            steps++;
            if (search.Status == LocalNavigationSearchStatus.Searching)
            {
                component.Queued = true;
                _requests.Enqueue(entity);
            }
            else if (search.Status == LocalNavigationSearchStatus.Found)
            {
                CompletedSearches++;
                component.Search = null;
                _activeSearches.Remove(uid);
                AdoptPath(uid, component, search);
            }
            else
            {
                if (search.Status == LocalNavigationSearchStatus.NoPath)
                    component.RefineSearch = true;
                Fail(uid, component, search.Status.ToString(), true,
                    search.Status == LocalNavigationSearchStatus.BudgetExceeded);
            }

            if (steps >= StepsPerBatch || _physicsQueries >= 128 || _workTime.Elapsed.TotalMilliseconds >= BatchBudgetMilliseconds)
                break;
        }

        LastBatchSteps = steps;
        LastBatchPhysicsQueries = _physicsQueries;
        LastBatchMilliseconds = _workTime.Elapsed.TotalMilliseconds;
        if (_requests.Count == 0)
            return;

        _scheduled = true;
        Timer.Spawn(WorkIntervalMilliseconds, ProcessRequests, _cancellation.Token);
    }

    private void AdoptPath(EntityUid uid, MedievalNavigationComponent component, LocalNavigationSearch search)
    {
        var position = Position(uid, component);
        var first = -1;
        var nearest = 0;
        var nearestDistance = float.MaxValue;
        var last = search.Result.Count - 1;
        for (var i = 0; i < search.Result.Count; i++)
        {
            var edge = search.Result[i];
            var distance = Vector2.DistanceSquared(position, edge.Entry ?? edge.End);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = i;
            }
            if (edge.Climb != null)
            {
                last = i;
                break;
            }
        }

        var checks = Math.Max(1, component.MaxPathJoinChecks);
        var start = Math.Max(0, nearest - checks / 2);
        var end = Math.Min(last, start + checks - 1);
        for (var i = end; i >= start; i--)
        {
            var edge = search.Result[i];
            var destination = edge.Entry ?? edge.End;
            if (Trace(uid, component, position, destination, out _))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            component.LastFailure = "Route start moved or obstructed";
            component.NextSearch = _timing.CurTime;
            return;
        }

        component.Path.Clear();
        component.Path.AddRange(search.Result);
        component.PathIndex = first;
        component.Goal = search.Goal;
        component.RecoveryTarget = null;
        component.LastProgress = _timing.CurTime;
        component.ProgressPosition = position;
    }

    private void Fail(EntityUid uid, MedievalNavigationComponent component, string reason,
        bool preservePath = false, bool budgetExceeded = false)
    {
        if (preservePath)
        {
            if (component.Search != null)
                FailedSearches++;
            component.Search = null;
            _activeSearches.Remove(uid);
        }
        else
        {
            component.Path.Clear();
            component.PathIndex = 0;
            component.RecoveryTarget = null;
            Halt(uid, component);
        }
        component.Failures = Math.Min(component.Failures + 1, 4);
        if (budgetExceeded)
            component.SearchBudgetFailures = Math.Min(component.SearchBudgetFailures + 1, 4);
        component.LastFailure = reason;
        var delay = preservePath ? 0.5f * component.Failures : 0.25f * (component.Failures - 1);
        component.NextSearch = _timing.CurTime + TimeSpan.FromSeconds(delay);
        if (!preservePath)
            component.NextRepath = component.NextSearch;
    }

    private float MoveSpeed(EntityUid uid)
    {
        return TryComp<MovementSpeedModifierComponent>(uid, out var movement)
            ? movement.CurrentSprintSpeed
            : MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
    }
}
