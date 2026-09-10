using Stopwatch = System.Diagnostics.Stopwatch;
using System.Numerics;
using System.Threading;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Events;
using Content.Shared.Climbing.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Imperial.Medieval.MobRiding;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
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
    [Dependency] private readonly RayCastSystem _rays = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

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
        if (component.Path.Count > 0 && component.PathIndex == component.Path.Count - 1)
            component.Goal = goal;
        if (Vector2.DistanceSquared(goal, component.Goal) > MathF.Pow(Math.Max(2f, component.VisionRange), 2))
        {
            component.Goal = goal;
            component.Search = null;
            component.Path.Clear();
            component.PathIndex = 0;
            component.NextSearch = TimeSpan.Zero;
            component.Failures = 0;
        }

        if (component.PathIndex < component.Path.Count)
        {
            if (component.Path[^1].Climb == null)
                component.Path[^1] = new LocalNavigationEdge(goal);
            FollowPath(uid, component, position);
            return;
        }

        Halt(uid, component);
        if (component.Search != null || _timing.CurTime < component.NextSearch)
            return;

        component.Goal = goal;
        var spacing = component.Failures >= 2 ? component.SampleSpacing / 2 : component.SampleSpacing;
        var radius = Math.Min(64f, Math.Max(component.SearchRadius, Vector2.Distance(position, goal) + 4f) + component.Failures * 4f);
        component.Search = LocalNavigationPathfinder.Create(position, goal, spacing,
            component.VisionRange, radius, component.MaxSearchNodes);
        component.SearchStarted = _timing.CurTime;
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

            if (_timing.CurTime - component.SearchStarted > TimeSpan.FromSeconds(8))
            {
                Fail(uid, component, "Search timeout");
                continue;
            }

            LocalNavigationPathfinder.Step(search, component.Probe!);
            steps++;
            if (search.Status == LocalNavigationSearchStatus.Searching)
            {
                component.Queued = true;
                _requests.Enqueue(entity);
            }
            else if (search.Status == LocalNavigationSearchStatus.Found)
            {
                CompletedSearches++;
                component.Path.Clear();
                component.Path.AddRange(search.Result);
                component.PathIndex = 0;
                component.Search = null;
                _activeSearches.Remove(uid);
                component.LastProgress = _timing.CurTime;
                component.ProgressPosition = Position(uid, component);
            }
            else
            {
                Fail(uid, component, search.Status.ToString());
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

    private void Fail(EntityUid uid, MedievalNavigationComponent component, string reason)
    {
        if (component.Search != null)
            FailedSearches++;
        component.Search = null;
        _activeSearches.Remove(uid);
        component.Path.Clear();
        component.PathIndex = 0;
        component.Failures = Math.Min(component.Failures + 1, 4);
        component.LastFailure = reason;
        component.NextSearch = _timing.CurTime + TimeSpan.FromSeconds(Math.Min(4, 0.5 * (1 << component.Failures)));
        Halt(uid, component);
    }

    private float MoveSpeed(EntityUid uid)
    {
        return TryComp<MovementSpeedModifierComponent>(uid, out var movement)
            ? movement.CurrentSprintSpeed
            : MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
    }
}
