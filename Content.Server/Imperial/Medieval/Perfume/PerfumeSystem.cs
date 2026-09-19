using System.Diagnostics.CodeAnalysis;
using Content.Server.BadSmell;
using Content.Server.BadSmell.Components;
using Content.Server.Fluids.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Imperial.Medieval.Perfume;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Fluids.EntitySystems;

public sealed class PerfumeSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainers = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ReactiveSystem _reactive = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly List<string> _expiredScents = new();
    private TimeSpan _lastUpdateTime = TimeSpan.Zero;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PerfumeComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<PerfumeComponent, AfterInteractEvent>(OnAfterInteract, before: new[] { typeof(SpraySystem) });
        SubscribeLocalEvent<PerfumeComponent, ExaminedEvent>(OnPerfumeExamined);
        SubscribeLocalEvent<PerfumeTargetComponent, BadSmellBeforeExamineEvent>(OnBeforeSmellExamine);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        if (_lastUpdateTime == TimeSpan.Zero)
        {
            _lastUpdateTime = curTime;
            return;
        }

        var delta = curTime - _lastUpdateTime;
        if (delta < UpdateInterval)
            return;

        _lastUpdateTime = curTime;
        UpdateAllScents(delta);
    }

    public void UpdateAllScents(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero)
            return;

        var query = EntityQueryEnumerator<PerfumeTargetComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            UpdateTargetScents(uid, comp, delta);
        }
    }

    public void UpdateTargetScents(EntityUid uid, PerfumeTargetComponent comp, TimeSpan delta)
    {
        if (comp.Scents.Count == 0)
        {
            RemCompDeferred<PerfumeTargetComponent>(uid);
            return;
        }

        _expiredScents.Clear();

        foreach (var (scentId, scent) in comp.Scents)
        {
            var remaining = scent.Duration - delta;
            if (remaining <= TimeSpan.Zero)
            {
                _expiredScents.Add(scentId);
            }
            else
            {
                comp.Scents[scentId] = new PerfumeScentInstance
                {
                    LocKey = scent.LocKey,
                    Duration = remaining
                };
            }
        }

        if (_expiredScents.Count > 0)
        {
            foreach (var id in _expiredScents)
            {
                comp.Scents.Remove(id);
            }

            Dirty(uid, comp);

            if (comp.Scents.Count == 0)
            {
                RemCompDeferred<PerfumeTargetComponent>(uid);
            }
        }
    }

    private void OnPerfumeExamined(EntityUid uid, PerfumeComponent comp, ExaminedEvent args)
    {
        if (TryComp<BadSmellFeelComponent>(args.Examiner, out var feel) && !feel.DescEnabled)
        {
            args.PushMarkup(Loc.GetString("bad-smell-cannot-smell"));
            return;
        }

        if (!_solutionContainers.TryGetSolution(uid, SprayComponent.SolutionName, out var soln, out var solution))
            return;

        if (solution.Volume <= 0)
            return;

        var scents = new List<string>();

        foreach (var reagentQuantity in solution.Contents)
        {
            if (!_proto.TryIndex<ReagentPrototype>(reagentQuantity.Reagent.Prototype, out var reagent))
                continue;

            if (!TryGetPerfumeEffect(reagent, out var perfumeEffect))
                continue;


            var equivalentDuration = reagentQuantity.Quantity.Float() * perfumeEffect.DurationPerUnit;
            scents.Add(FormatScent(reagent.SubstanceColor, perfumeEffect.LocKey, equivalentDuration));
        }

        if (scents.Count > 0)
        {
            var line = string.Join(", ", scents);
            args.PushMarkup(Loc.GetString("bad-smell-scents-line", ("scents", line)));
        }
    }

    private void OnBeforeSmellExamine(EntityUid uid, PerfumeTargetComponent comp, BadSmellBeforeExamineEvent args)
    {
        var curTime = _timing.CurTime;
        if (_lastUpdateTime != TimeSpan.Zero)
        {
            var delta = curTime - _lastUpdateTime;
            if (delta > TimeSpan.Zero)
            {
                _lastUpdateTime = curTime;
                UpdateAllScents(delta);
            }
        }
        else
        {
            _lastUpdateTime = curTime;
        }

        if (comp.Scents.Count == 0)
            return;

        foreach (var (scentId, scent) in comp.Scents)
        {
            if (scent.Duration <= TimeSpan.Zero)
                continue;

            var color = Color.White;
            if (_proto.TryIndex<ReagentPrototype>(scentId, out var reagent))
                color = reagent.SubstanceColor;

            args.Scents.Add(FormatScent(color, scent.LocKey, scent.Duration.TotalSeconds));
        }
    }

    private string FormatScent(Color color, LocId locKey, double duration)
    {
        var locScent = Loc.GetString(locKey);

        string formatted;
        if (duration < 60)
            formatted = Loc.GetString("perfume-scent-weak", ("scent", locScent));
        else if (duration > 300)
            formatted = Loc.GetString("perfume-scent-strong", ("scent", locScent));
        else
            formatted = Loc.GetString("perfume-scent-normal", ("scent", locScent));

        var hex = color.ToHex();
        if (!hex.StartsWith('#'))
            hex = $"#{hex}";

        return $"[color={hex}]{formatted}[/color]";
    }

    private bool TryGetPerfumeEffect(ReagentPrototype reagent, [NotNullWhen(true)] out PerfumeReagentEffect? perfumeEffect)
    {
        perfumeEffect = null;
        if (reagent.ReactiveEffects == null)
            return false;

        foreach (var (_, entry) in reagent.ReactiveEffects)
        {
            foreach (var effect in entry.Effects)
            {
                if (effect is PerfumeReagentEffect pEffect)
                {
                    perfumeEffect = pEffect;
                    return true;
                }
            }
        }

        return false;
    }

    private void OnUseInHand(EntityUid uid, PerfumeComponent comp, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (TrySpraySelf(uid, comp, args.User))
            args.Handled = true;
    }

    private void OnAfterInteract(EntityUid uid, PerfumeComponent comp, AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null)
            return;

        if (args.Target == args.User)
        {
            if (TrySpraySelf(uid, comp, args.User))
                args.Handled = true;
        }
    }

    public bool TrySpraySelf(EntityUid uid, PerfumeComponent comp, EntityUid user, SprayComponent? spray = null)
    {
        if (TryComp<UseDelayComponent>(uid, out var useDelay) && _useDelay.IsDelayed((uid, useDelay)))
            return false;

        if (!Resolve(uid, ref spray))
            return false;

        if (!_solutionContainers.TryGetSolution(uid, SprayComponent.SolutionName, out var soln, out var solution))
            return false;

        if (solution.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("perfume-bottle-empty"), user, user);
            return false;
        }

        var amount = spray.TransferAmount;
        var split = _solutionContainers.SplitSolution(soln.Value, amount);

        _reactive.DoEntityReaction(user, split, ReactionMethod.Touch);

        _audio.PlayPvs(spray.SpraySound, uid);
        if (useDelay != null)
            _useDelay.TryResetDelay((uid, useDelay));

        Spawn(spray.SprayedPrototype, Transform(user).Coordinates);

        _popup.PopupEntity(Loc.GetString("perfume-bottle-spray-self"), user, user);
        return true;
    }
}