using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Imperial.Medieval.Alchemy;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server.Imperial.Medieval.Alchemy;

public sealed partial class AlchemySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    private void InitializeApparatus()
    {
        SubscribeLocalEvent<AlchemyApparatusComponent, ActivateInWorldEvent>(OnApparatusActivate);
        SubscribeLocalEvent<AlchemyApparatusComponent, GetVerbsEvent<ActivationVerb>>(OnApparatusVerbs);
        SubscribeLocalEvent<AlchemyApparatusComponent, ExaminedEvent>(OnApparatusExamine);
        SubscribeLocalEvent<AlchemyApparatusComponent, ContainerIsInsertingAttemptEvent>(OnApparatusInsert);
        SubscribeLocalEvent<AlchemyApparatusComponent, ContainerIsRemovingAttemptEvent>(OnApparatusRemove);
    }

    private void OnApparatusInsert(EntityUid uid, AlchemyApparatusComponent comp, ContainerIsInsertingAttemptEvent args)
    {
        if (comp.Running)
            args.Cancel();
    }

    private void OnApparatusRemove(EntityUid uid, AlchemyApparatusComponent comp, ContainerIsRemovingAttemptEvent args)
    {
        if (comp.Running)
            args.Cancel();
    }

    private void OnApparatusExamine(EntityUid uid, AlchemyApparatusComponent comp, ExaminedEvent args)
    {
        args.PushText(Loc.GetString(comp.Running ? "alchemy-apparatus-running" : "alchemy-apparatus-instructions"));
    }

    private void OnApparatusActivate(EntityUid uid, AlchemyApparatusComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        StartApparatus(uid, comp, args.User);
    }

    private void OnApparatusVerbs(EntityUid uid, AlchemyApparatusComponent comp, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || comp.Running)
            return;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("alchemy-apparatus-start"),
            Act = () => StartApparatus(uid, comp, args.User),
        });
    }

    private void StartApparatus(EntityUid uid, AlchemyApparatusComponent comp, EntityUid user)
    {
        if (comp.Running || !TryComp<StorageComponent>(uid, out var storage))
            return;
        if (_itemSlots.GetItemOrNull(uid, comp.OutputSlot) is not { } receiver ||
            !_solutions.TryGetRefillableSolution(receiver, out _, out _))
        {
            _popup.PopupEntity(Loc.GetString("alchemy-apparatus-no-receiver"), uid, user);
            return;
        }
        if (storage.Container.ContainedEntities.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("alchemy-apparatus-empty"), uid, user);
            return;
        }
        EnsureRound();
        comp.Items = storage.Container.ContainedEntities.ToList();
        comp.Sources.Clear();
        foreach (var item in comp.Items)
        {
            if (_solutions.TryGetDrainableSolution(item, out var source, out _) && source != null)
                comp.Sources[source.Value.Owner] = EnsureComp<AlchemySolutionComponent>(source.Value.Owner).Revision;
        }
        comp.Running = true;
        _itemSlots.SetLock(uid, comp.OutputSlot, true);
        var operation = _prototypes.Index<AlchemyOperationPrototype>(comp.Operation);
        _popup.PopupEntity(Loc.GetString("alchemy-apparatus-running"), uid, user);
        Timer.Spawn(TimeSpan.FromSeconds(operation.Duration), () => FinishApparatus(uid, comp, receiver, user));
    }

    private void FinishApparatus(EntityUid uid, AlchemyApparatusComponent comp, EntityUid receiver, EntityUid user)
    {
        if (TerminatingOrDeleted(uid) || !comp.Running)
            return;
        comp.Running = false;
        _itemSlots.SetLock(uid, comp.OutputSlot, false);
        try
        {
            if (TerminatingOrDeleted(receiver) || _itemSlots.GetItemOrNull(uid, comp.OutputSlot) != receiver ||
                !_solutions.TryGetRefillableSolution(receiver, out var output, out _) || output == null ||
                !TryComp<StorageComponent>(uid, out var storage) ||
                !comp.Items.ToHashSet().SetEquals(storage.Container.ContainedEntities) ||
                comp.Items.Any(item => TerminatingOrDeleted(item)) ||
                comp.Sources.Any(p => !TryComp<AlchemySolutionComponent>(p.Key, out var tracker) || tracker.Revision != p.Value))
            {
                if (!TerminatingOrDeleted(user))
                    _popup.PopupEntity(Loc.GetString("alchemy-mixture-changed"), uid, user);
                return;
            }
            if (!_solutions.TryGetSolution(uid, comp.Solution, out var input, out var mixture) || input == null || mixture == null)
                return;
            var rawItems = new List<EntityUid>();
            var sources = new List<Entity<SolutionComponent>>();
            foreach (var item in comp.Items)
            {
                if (_solutions.TryGetDrainableSolution(item, out var source, out var liquid) && source != null && liquid != null)
                    sources.Add(source.Value);
                else
                    rawItems.Add(item);
            }
            if (!comp.Sources.Keys.ToHashSet().SetEquals(sources.Select(source => source.Owner)))
            {
                if (!TerminatingOrDeleted(user))
                    _popup.PopupEntity(Loc.GetString("alchemy-mixture-changed"), uid, user);
                return;
            }
            foreach (var source in sources)
                mixture.AddSolution(_solutions.SplitSolution(source, source.Comp.Solution.Volume), _prototypes);
            var operation = _prototypes.Index<AlchemyOperationPrototype>(comp.Operation);
            if (operation.Temperature is { } temperature)
                mixture.Temperature = operation.Heating ? Math.Max(mixture.Temperature, temperature) : Math.Min(mixture.Temperature, temperature);
            CompleteOperation(input.Value, comp.Operation, TerminatingOrDeleted(user) ? null : user, rawItems);
            var vessel = EnsureComp<AlchemyVesselComponent>(receiver);
            vessel.Solution = output.Value.Comp.Solution.Name ?? vessel.Solution;
            vessel.Processing = true;
            try
            {
                _solutions.ForceAddSolution(output.Value, _solutions.SplitSolution(input.Value, mixture.Volume));
            }
            finally
            {
                vessel.Hot = output.Value.Comp.Solution.Temperature >= _prototypes.Index(HeatOperation).Temperature;
                vessel.Processing = false;
            }
            if (!TerminatingOrDeleted(user))
                _popup.PopupEntity(Loc.GetString("alchemy-operation-complete", ("operation", Loc.GetString(operation.Name))), uid, user);
        }
        finally
        {
            comp.Items.Clear();
            comp.Sources.Clear();
        }
    }
}
