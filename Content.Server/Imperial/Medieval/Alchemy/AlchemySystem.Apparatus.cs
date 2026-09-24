using System.Linq;
using System.Threading.Tasks;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
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
        SubscribeLocalEvent<AlchemyApparatusComponent, ComponentShutdown>(OnApparatusShutdown);
        SubscribeLocalEvent<AlchemyApparatusComponent, EntRemovedFromContainerMessage>(OnApparatusContentsRemoved);
        SubscribeLocalEvent<AlchemyApparatusComponent, SolutionContainerChangedEvent>(OnApparatusSolutionChanged);
    }

    private void OnApparatusShutdown(EntityUid uid, AlchemyApparatusComponent comp, ComponentShutdown args)
    {
        StopApparatus(uid, comp);
    }

    private void OnApparatusContentsRemoved(EntityUid uid, AlchemyApparatusComponent comp, EntRemovedFromContainerMessage args)
    {
        if (comp.IsProcessing &&
            (args.Entity == comp.Input || args.Entity == comp.Receiver || comp.Items.Contains(args.Entity)))
            StopApparatus(uid, comp);
    }

    private void OnApparatusSolutionChanged(EntityUid uid, AlchemyApparatusComponent comp, ref SolutionContainerChangedEvent args)
    {
        if (!comp.IsProcessing || args.SolutionId != comp.Solution)
            return;

        var previousVolume = comp.InputVolume;
        comp.InputVolume = args.Solution.Volume;
        if (args.Solution.Volume < previousVolume &&
            (!TryComp<AlchemyVesselComponent>(uid, out var vessel) || !vessel.Processing))
            StopApparatus(uid, comp);
    }

    private async Task RunApparatus(EntityUid uid, AlchemyApparatusComponent comp, uint generation, float duration)
    {
        try
        {
            await Robust.Shared.Timing.Timer.Delay(TimeSpan.FromSeconds(duration));

            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
                !TryComp<AlchemyApparatusComponent>(uid, out var current) || current != comp ||
                !comp.IsProcessing || comp.ProcessingGeneration != generation)
                return;

            if (!_solutions.TryGetSolution(uid, comp.Solution, out var input, out _) || input == null ||
                input.Value.Owner != comp.Input || TerminatingOrDeleted(input.Value.Owner) ||
                EntityManager.IsQueuedForDeletion(input.Value.Owner) ||
                !HasComp<AlchemyVesselComponent>(uid) || !TryComp<StorageComponent>(uid, out var storage) ||
                comp.Items.Any(item => TerminatingOrDeleted(item) || EntityManager.IsQueuedForDeletion(item) ||
                    !storage.Container.Contains(item)))
                return;

            FinishApparatus(uid, comp, input.Value);
        }
        catch (Exception exception)
        {
            Log.Error($"Alchemy apparatus {uid} failed: {exception}");
        }
        finally
        {
            if (comp.ProcessingGeneration == generation)
                StopApparatus(uid, comp);
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
        if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
            comp.IsProcessing || !TryComp<StorageComponent>(uid, out var storage))
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
        comp.Input = input.Value.Owner;
        comp.InputVolume = mixture.Volume;
        comp.IsProcessing = true;
        var generation = ++comp.ProcessingGeneration;
        _itemSlots.SetLock(uid, comp.OutputSlot, true);
        _popup.PopupEntity(Loc.GetString(operation.RunningMessage), uid, user);
        _ = RunApparatus(uid, comp, generation, operation.Duration);
    }

    private void FinishApparatus(EntityUid uid, AlchemyApparatusComponent comp, Entity<SolutionComponent> input)
    {
        var user = comp.User is { } actor && !TerminatingOrDeleted(actor) ? comp.User : null;
        try
        {
            if (comp.Receiver is not { } receiver || TerminatingOrDeleted(receiver) || EntityManager.IsQueuedForDeletion(receiver) ||
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

            if (!comp.IsProcessing || TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
                TerminatingOrDeleted(receiver) || EntityManager.IsQueuedForDeletion(receiver) ||
                TerminatingOrDeleted(input.Owner) || EntityManager.IsQueuedForDeletion(input.Owner) ||
                TerminatingOrDeleted(output.Value.Owner) || EntityManager.IsQueuedForDeletion(output.Value.Owner) ||
                _itemSlots.GetItemOrNull(uid, comp.OutputSlot) != receiver)
                return;

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
        comp.ProcessingGeneration++;
        if (!TerminatingOrDeleted(uid) && TryComp<ItemSlotsComponent>(uid, out var slots))
            _itemSlots.SetLock(uid, comp.OutputSlot, false, slots);
        comp.Items.Clear();
        comp.User = null;
        comp.Receiver = null;
        comp.Input = null;
        comp.InputVolume = default;
    }
}
