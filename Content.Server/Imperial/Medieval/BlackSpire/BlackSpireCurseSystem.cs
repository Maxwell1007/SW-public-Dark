using System.Threading;
using System.Threading.Tasks;
using Content.Server.Imperial.Medieval.NeedSleep;
using Content.Shared.Imperial.Medieval.BlackSpire;
using Content.Shared.Nutrition.EntitySystems;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server.Imperial.Medieval.BlackSpire;

public sealed class BlackSpireCurseSystem : EntitySystem
{
    [Dependency] private readonly HungerSystem _hunger = default!;

    private readonly Dictionary<EntityUid, CancellationTokenSource> _timers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BlackSpireCurseComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BlackSpireCurseComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<BlackSpireCurseComponent, GetSleepLevelModifiersEvent>(OnSleepLevel);
    }

    public override void Shutdown()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Cancel();
            timer.Dispose();
        }

        _timers.Clear();
        base.Shutdown();
    }

    public void Apply(EntityUid uid, float multiplier, TimeSpan duration)
    {
        var component = EnsureComp<BlackSpireCurseComponent>(uid);
        component.Multiplier = multiplier;
        component.Duration = duration;
        component.Active = true;
        Dirty(uid, component);
        _hunger.RefreshDecayRate(uid);
        StartTimer(uid, component);
    }

    private void OnStartup(Entity<BlackSpireCurseComponent> ent, ref ComponentStartup args)
    {
        _hunger.RefreshDecayRate(ent.Owner);
        StartTimer(ent.Owner, ent.Comp);
    }

    private void OnShutdown(Entity<BlackSpireCurseComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Active = false;
        if (!TerminatingOrDeleted(ent.Owner))
            _hunger.RefreshDecayRate(ent.Owner);

        StopTimer(ent.Owner);
    }

    private void OnSleepLevel(Entity<BlackSpireCurseComponent> ent, ref GetSleepLevelModifiersEvent args)
    {
        if (ent.Comp.Active && !args.Sleeping)
            args.Modifier *= ent.Comp.Multiplier;
    }

    private void StartTimer(EntityUid uid, BlackSpireCurseComponent component)
    {
        StopTimer(uid);
        var cancellation = new CancellationTokenSource();
        _timers.Add(uid, cancellation);
        _ = ExpireAsync(uid, component, cancellation.Token);
    }

    private void StopTimer(EntityUid uid)
    {
        if (!_timers.Remove(uid, out var cancellation))
            return;

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task ExpireAsync(EntityUid uid, BlackSpireCurseComponent component, CancellationToken token)
    {
        try
        {
            await Timer.Delay(component.Duration, token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (!TerminatingOrDeleted(uid) && TryComp<BlackSpireCurseComponent>(uid, out var current) && current == component)
                RemComp<BlackSpireCurseComponent>(uid);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }
}
