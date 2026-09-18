using System.Linq;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Containers;

namespace Content.Server.Imperial.Medieval.Alchemy;

public sealed class AlchemyCoolingBathSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AlchemyCoolingBathComponent, GetVerbsEvent<ActivationVerb>>(OnVerbs);
        SubscribeLocalEvent<AlchemyCoolingBathComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<AlchemyCoolingBathComponent, ContainerIsInsertingAttemptEvent>(OnInsert);
        SubscribeLocalEvent<AlchemyCoolingBathComponent, ContainerIsRemovingAttemptEvent>(OnRemove);
    }

    private void OnVerbs(EntityUid uid, AlchemyCoolingBathComponent comp, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || comp.IsProcessing ||
            !TryComp<StorageComponent>(uid, out var storage) || storage.Container.ContainedEntities.Count == 0)
            return;

        foreach (var duration in comp.Durations.Where(duration => duration > 0))
        {
            args.Verbs.Add(new ActivationVerb
            {
                Text = Loc.GetString("alchemy-cooling-bath-timer", ("seconds", duration)),
                Act = () => Start(uid, comp, duration),
            });
        }
    }

    private void Start(EntityUid uid, AlchemyCoolingBathComponent comp, int duration)
    {
        if (comp.IsProcessing || !TryComp<StorageComponent>(uid, out var storage) ||
            storage.Container.ContainedEntities.Count == 0)
            return;
        comp.RemainingTime = duration;
        comp.IsProcessing = true;
    }

    private void OnExamine(EntityUid uid, AlchemyCoolingBathComponent comp, ExaminedEvent args)
    {
        args.PushText(comp.IsProcessing
            ? Loc.GetString("alchemy-cooling-bath-running", ("seconds", (int) Math.Ceiling(comp.RemainingTime)))
            : Loc.GetString("alchemy-cooling-bath-instructions"));
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
        while (query.MoveNext(out _, out var comp, out var storage))
        {
            if (!comp.IsProcessing)
                continue;
            comp.RemainingTime -= frameTime;
            if (comp.RemainingTime > 0)
                continue;

            comp.IsProcessing = false;
            comp.RemainingTime = 0;
            foreach (var item in storage.Container.ContainedEntities.ToArray())
            {
                if (TerminatingOrDeleted(item))
                    continue;
                var temperature = EnsureComp<TemperatureComponent>(item);
                _temperature.ForceChangeTemperature(item, Math.Min(temperature.CurrentTemperature, comp.Temperature), temperature);
                if (!TryComp<SolutionContainerManagerComponent>(item, out var solutions))
                    continue;
                foreach (var (_, solution) in _solutions.EnumerateSolutions((item, solutions)))
                    _solutions.SetTemperature(solution, Math.Min(solution.Comp.Solution.Temperature, comp.Temperature));
            }
        }
    }
}
