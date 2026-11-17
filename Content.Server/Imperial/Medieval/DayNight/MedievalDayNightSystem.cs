using Content.Server.GameTicking;
using Content.Shared.DayTime;
using Content.Shared.GameTicking;
using Content.Shared.Imperial.Medieval.DayNight;
using Content.Shared.Imperial.Medieval.Ships.Sea;
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server.Imperial.Medieval.DayNight;

public sealed class MedievalDayNightSystem : SharedMedievalDayNightLightingSystem
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public int CurrentPhase => GetClock()?.PhaseIndex ?? 0;
    public bool IsInitialized => GetClock() is { ClockStarted: true };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundStart(RoundStartedEvent args)
    {
        ClearClock();
        var uid = Spawn("MedievalDayNightClock", MapCoordinates.Nullspace);
        var clock = Comp<MedievalDayNightClockComponent>(uid);
        if (clock.DayDuration <= TimeSpan.Zero || clock.NightDuration <= TimeSpan.Zero ||
            clock.LightingUpdateInterval <= TimeSpan.Zero)
        {
            Log.Error("Cannot initialize archipelago time: durations must be positive.");
            QueueDel(uid);
            return;
        }

        clock.ClockStarted = true;
        SetPhase(clock, 0, _timing.CurTime);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        ClearClock();
    }

    private void ClearClock()
    {
        var query = EntityManager.AllEntityQueryEnumerator<MedievalDayNightClockComponent>();
        while (query.MoveNext(out var uid, out _))
            QueueDel(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound || GetClock() is not { ClockStarted: true } clock)
            return;

        while (clock.NextTransition <= _timing.CurTime && clock.PhaseIndex < int.MaxValue - 1)
            SetPhase(clock, clock.PhaseIndex + 1, clock.NextTransition);

        if (clock.NextLightingUpdate <= _timing.CurTime)
            UpdateLighting(clock);
    }

    public bool TrySkipPhase()
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || GetClock() is not { ClockStarted: true } clock ||
            clock.PhaseIndex >= int.MaxValue - 1)
            return false;

        SetPhase(clock, clock.PhaseIndex + 1, _timing.CurTime);
        return true;
    }

    public bool TryAdvanceDays(int days)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || GetClock() is not { ClockStarted: true } clock || days < 1)
            return false;

        var next = ((long) clock.PhaseIndex / 2 + days) * 2;
        if (next >= int.MaxValue)
            return false;

        SetPhase(clock, (int) next, _timing.CurTime);
        return true;
    }

    private void SetPhase(MedievalDayNightClockComponent clock, int phase, TimeSpan startedAt)
    {
        var previous = clock.PhaseIndex;
        clock.PhaseIndex = phase;
        clock.PhaseStartedAt = startedAt;
        clock.NextTransition = startedAt + (phase % 2 == 0 ? clock.DayDuration : clock.NightDuration);
        UpdateLighting(clock);
        RaiseLocalEvent(new MedievalDayNightChangedEvent(previous, phase));
    }

    private void UpdateLighting(MedievalDayNightClockComponent clock)
    {
        clock.NextLightingUpdate = _timing.CurTime + clock.LightingUpdateInterval;
        var maps = EntityManager.AllEntityQueryEnumerator<MapLightComponent>();
        while (maps.MoveNext(out var uid, out var mapLight))
        {
            if (!HasComp<MapComponent>(uid) || TerminatingOrDeleted(uid))
                continue;

            if (!TryComp<MedievalDayNightLightingComponent>(uid, out var lighting))
            {
                if (!HasComp<DayTimeComponent>(uid) && !HasComp<SeaComponent>(uid) &&
                    !(HasComp<LightCycleComponent>(uid) && _ticker.IsGameRuleActive("TDM")))
                    continue;

                lighting = AddComp<MedievalDayNightLightingComponent>(uid);
                lighting.DayColor = TryComp<LightCycleComponent>(uid, out var cycle)
                    ? cycle.OriginalColor
                    : mapLight.AmbientLightColor;
            }

            if (!lighting.LightingConfigured)
            {
                lighting.LightingConfigured = true;
                Dirty(uid, lighting);
            }

            RemComp<LightCycleComponent>(uid);
            var duration = clock.PhaseIndex % 2 == 0 ? clock.DayDuration : clock.NightDuration;
            if (lighting.PhaseStartedAt != clock.PhaseStartedAt || lighting.PhaseDuration != duration ||
                lighting.IsNight != (clock.PhaseIndex % 2 != 0) || lighting.ColorMultiplier != clock.ColorMultiplier)
            {
                lighting.PhaseStartedAt = clock.PhaseStartedAt;
                lighting.PhaseDuration = duration;
                lighting.IsNight = clock.PhaseIndex % 2 != 0;
                lighting.ColorMultiplier = clock.ColorMultiplier;
                Dirty(uid, lighting);
            }

            var color = GetColor(lighting, _timing.CurTime);
            if (mapLight.AmbientLightColor != color)
            {
                mapLight.AmbientLightColor = color;
                Dirty(uid, mapLight);
            }
        }
    }

    public void SetColorMultiplier(Color color)
    {
        if (GetClock() is { ClockStarted: true } clock)
        {
            clock.ColorMultiplier = color;
            UpdateLighting(clock);
        }
    }

    private MedievalDayNightClockComponent? GetClock()
    {
        var query = EntityManager.AllEntityQueryEnumerator<MedievalDayNightClockComponent>();
        while (query.MoveNext(out var uid, out var clock))
        {
            if (!TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid))
                return clock;
        }

        return null;
    }
}
