using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Imperial.Medieval.Alchemy;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Containers;

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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<AlchemyApparatusComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsProcessing)
                continue;
            if (!_solutions.TryGetSolution(uid, comp.Solution, out var input, out var mixture) || input == null || mixture == null)
            {
                StopApparatus(uid, comp);
                continue;
            }

            comp.RemainingTime -= frameTime;
            if (comp.RemainingTime > 0)
                continue;

            FinishApparatus(uid, comp, input.Value);
        }
    }

    private void OnApparatusInsert(EntityUid uid, AlchemyApparatusComponent comp, ContainerIsInsertingAttemptEvent args)
    {
        if (comp.IsProcessing)
            args.Cancel();
    }

    private void OnApparatusRemove(EntityUid uid, AlchemyApparatusComponent comp, ContainerIsRemovingAttemptEvent args)
    {
        if (comp.IsProcessing)
            args.Cancel();
    }

    private void OnApparatusExamine(EntityUid uid, AlchemyApparatusComponent comp, ExaminedEvent args)
    {
        if (comp.IsProcessing)
            args.PushText(Loc.GetString(_prototypes.Index<AlchemyOperationPrototype>(comp.Operation).RunningMessage));
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
        if (!args.CanAccess || !args.CanInteract || comp.IsProcessing)
            return;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString(_prototypes.Index<AlchemyOperationPrototype>(comp.Operation).StartMessage),
            Act = () => StartApparatus(uid, comp, args.User),
        });
    }

    private void StartApparatus(EntityUid uid, AlchemyApparatusComponent comp, EntityUid user)
    {
        if (comp.IsProcessing || !TryComp<StorageComponent>(uid, out var storage))
            return;
        var operation = _prototypes.Index<AlchemyOperationPrototype>(comp.Operation);
        if (operation.Temperature != null || !TryGetRoundState(out _))
            return;
        if (_itemSlots.GetItemOrNull(uid, comp.OutputSlot) is not { } receiver ||
            !_solutions.TryGetRefillableSolution(receiver, out _, out _))
        {
            _popup.PopupEntity(Loc.GetString("alchemy-apparatus-no-receiver"), uid, user);
            return;
        }
        if (!_solutions.TryGetSolution(uid, comp.Solution, out var input, out var mixture) || input == null || mixture == null)
            return;
        if (storage.Container.ContainedEntities.Count == 0 && mixture.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString(operation.EmptyMessage), uid, user);
            return;
        }

        var vessel = EnsureComp<AlchemyVesselComponent>(uid);
        vessel.Solution = comp.Solution;
        vessel.Processing = true;
        comp.Items.Clear();
        try
        {
            foreach (var item in storage.Container.ContainedEntities.ToArray())
            {
                if (_solutions.TryGetDrainableSolution(item, out var source, out var liquid) && source != null && liquid != null)
                    mixture.AddSolution(_solutions.SplitSolution(source.Value, liquid.Volume), _prototypes);
                else
                {
                    comp.Items.Add(item);
                    EnsureComp<AlchemyItemHistoryComponent>(item);
                }
            }
            UpdateTemperatureState(vessel, mixture);
            _solutions.UpdateChemicals(input.Value, false);
        }
        finally
        {
            vessel.Processing = false;
        }

        comp.User = user;
        comp.Receiver = receiver;
        comp.RemainingTime = operation.Duration;
        comp.IsProcessing = true;
        _itemSlots.SetLock(uid, comp.OutputSlot, true);
        _popup.PopupEntity(Loc.GetString(operation.RunningMessage), uid, user);
    }

    private void FinishApparatus(EntityUid uid, AlchemyApparatusComponent comp, Entity<SolutionComponent> input)
    {
        var user = comp.User is { } actor && !TerminatingOrDeleted(actor) ? comp.User : null;
        try
        {
            if (comp.Receiver is not { } receiver || TerminatingOrDeleted(receiver) ||
                _itemSlots.GetItemOrNull(uid, comp.OutputSlot) != receiver ||
                !_solutions.TryGetRefillableSolution(receiver, out var output, out _) || output == null ||
                !TryGetRoundState(out var state))
                return;

            var items = comp.Items.Where(item => !TerminatingOrDeleted(item) && !EntityManager.IsQueuedForDeletion(item)).ToList();
            var operation = _prototypes.Index<AlchemyOperationPrototype>(comp.Operation);
            var inputVessel = Comp<AlchemyVesselComponent>(uid);
            inputVessel.Processing = true;
            try
            {
                CompleteOperation(input, state, comp.Operation, user, items);
            }
            finally
            {
                inputVessel.Processing = false;
            }

            var vessel = EnsureComp<AlchemyVesselComponent>(receiver);
            vessel.Solution = output.Value.Comp.Solution.Name ?? vessel.Solution;
            vessel.Processing = true;
            try
            {
                _solutions.ForceAddSolution(output.Value, _solutions.SplitSolution(input, input.Comp.Solution.Volume));
            }
            finally
            {
                UpdateTemperatureState(vessel, output.Value.Comp.Solution);
                vessel.Processing = false;
            }
            if (user is { } recipient)
                _popup.PopupEntity(Loc.GetString(operation.CompletionMessage), uid, recipient);
        }
        finally
        {
            StopApparatus(uid, comp);
        }
    }

    private void StopApparatus(EntityUid uid, AlchemyApparatusComponent comp)
    {
        comp.IsProcessing = false;
        _itemSlots.SetLock(uid, comp.OutputSlot, false);
        comp.Items.Clear();
        comp.User = null;
        comp.Receiver = null;
        comp.RemainingTime = 0;
    }
}
