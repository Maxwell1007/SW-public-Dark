using System.Linq;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Imperial.Medieval.Alchemy;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.Alchemy;

public sealed partial class AlchemyCoolingBathSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        InitializeUi();
        SubscribeLocalEvent<AlchemyCoolingBathComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<AlchemyCoolingBathComponent, ContainerIsInsertingAttemptEvent>(OnInsert);
        SubscribeLocalEvent<AlchemyCoolingBathComponent, ContainerIsRemovingAttemptEvent>(OnRemove);
    }

    private void Start(EntityUid uid, AlchemyCoolingBathComponent comp, int duration)
    {
        if (comp.IsProcessing || duration <= 0 || !comp.Durations.Contains(duration) ||
            !TryComp<StorageComponent>(uid, out var storage) ||
            storage.Container.ContainedEntities.Count == 0)
            return;
        comp.RemainingTime = duration;
        comp.IsProcessing = true;
        _appearance.SetData(uid, AlchemyCoolingBathVisuals.IsProcessing, true);
        UpdateUi(uid, comp);
    }

    private void OnExamine(EntityUid uid, AlchemyCoolingBathComponent comp, ExaminedEvent args)
    {
        if (comp.IsProcessing)
            args.PushText(Loc.GetString("alchemy-cooling-bath-running", ("seconds", (int) Math.Ceiling(comp.RemainingTime))));
    }

    private void OnInsert(EntityUid uid, AlchemyCoolingBathComponent comp, ContainerIsInsertingAttemptEvent args)
    {
        if (comp.IsProcessing)
            args.Cancel();
    }

    private void OnRemove(EntityUid uid, AlchemyCoolingBathComponent comp, ContainerIsRemovingAttemptEvent args)
    {
        if (comp.IsProcessing)
            args.Cancel();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<AlchemyCoolingBathComponent, StorageComponent>();
        while (query.MoveNext(out var uid, out var comp, out var storage))
        {
            if (!comp.IsProcessing)
                continue;
            var elapsed = Math.Min(frameTime, comp.RemainingTime);
            CoolContents(comp, storage, elapsed);
            comp.RemainingTime = Math.Max(0, comp.RemainingTime - elapsed);
            if (comp.RemainingTime <= 0)
            {
                comp.IsProcessing = false;
                _appearance.SetData(uid, AlchemyCoolingBathVisuals.IsProcessing, false);
                UpdateUi(uid, comp);
            }
        }
    }

    private void CoolContents(AlchemyCoolingBathComponent comp, StorageComponent storage, float elapsed)
    {
        var energy = comp.CoolingPower * elapsed;
        if (energy <= 0)
            return;
        foreach (var item in storage.Container.ContainedEntities.ToArray())
        {
            if (TerminatingOrDeleted(item))
                continue;
            if (TryComp<TemperatureComponent>(item, out var temperature) &&
                temperature.CurrentTemperature > comp.MinimumTemperature)
            {
                var available = (temperature.CurrentTemperature - comp.MinimumTemperature) *
                                _temperature.GetHeatCapacity(item, temperature);
                _temperature.ChangeHeat(item, -Math.Min(energy, available), temperature: temperature);
            }
            if (!TryComp<SolutionContainerManagerComponent>(item, out var solutions))
                continue;
            foreach (var (_, solution) in _solutions.EnumerateSolutions((item, solutions)))
            {
                var mixture = solution.Comp.Solution;
                if (mixture.Volume <= 0 || mixture.Temperature <= comp.MinimumTemperature)
                    continue;
                var available = (mixture.Temperature - comp.MinimumTemperature) * mixture.GetHeatCapacity(_prototypes);
                _solutions.AddThermalEnergy(solution, -Math.Min(energy, available));
            }
        }
    }
}
