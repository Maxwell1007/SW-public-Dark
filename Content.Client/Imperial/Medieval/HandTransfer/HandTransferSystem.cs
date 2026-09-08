using Content.Client.UserInterface.Systems.Actions;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Imperial.Medieval.HandTransfer;
using Content.Shared.Input;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client.Imperial.Medieval.HandTransfer;

public sealed class HandTransferSystem : EntitySystem
{
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    private HandTransferOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new HandTransferOverlay(_input, _player, EntityManager);
        _overlays.AddOverlay(_overlay);

        SubscribeLocalEvent<HandTransferRequestComponent, ComponentStartup>(OnRequestStarted);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.MedievalHandTransfer,
                InputCmdHandler.FromDelegate(OnTransferPressed, handle: true, outsidePrediction: true))
            .BindBefore(EngineKeyFunctions.Use,
                new PointerInputCmdHandler(OnTargetSelected, outsidePrediction: true),
                typeof(ActionUIController))
            .BindBefore(EngineKeyFunctions.UIRightClick,
                new PointerInputCmdHandler(OnTargetingCancelled, outsidePrediction: true),
                typeof(ActionUIController))
            .Register<HandTransferSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<HandTransferSystem>();
        _overlays.RemoveOverlay(_overlay);

        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_player.LocalEntity is not { } user ||
            !TryComp<HandTransferTargetingComponent>(user, out var targeting))
        {
            return;
        }

        if (IsInCombatMode(user) ||
            _hands.GetActiveItem(user) != targeting.Item ||
            !CanTransfer(targeting.Item))
        {
            StopTargeting(user);
        }
    }

    private void OnRequestStarted(Entity<HandTransferRequestComponent> ent, ref ComponentStartup args)
    {
        if (_player.LocalEntity == ent.Owner)
            StopTargeting(ent.Owner);
    }

    private void OnTransferPressed(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { } user || !TryComp<HandsComponent>(user, out var hands))
            return;

        if (HasComp<HandTransferRequestComponent>(user))
        {
            StopTargeting(user);
            RaiseNetworkEvent(new HandTransferAcceptEvent());
            return;
        }

        if (IsInCombatMode(user))
        {
            StopTargeting(user);
            return;
        }

        var item = _hands.GetActiveItem((user, hands));
        if (item is not { } held || !CanTransfer(held))
        {
            StopTargeting(user);
            return;
        }

        var targeting = EnsureComp<HandTransferTargetingComponent>(user);
        targeting.Item = held;
    }

    private bool OnTargetSelected(ICommonSession? session, EntityCoordinates coordinates, EntityUid target)
    {
        if (session?.AttachedEntity is not { } user ||
            !TryComp<HandTransferTargetingComponent>(user, out var targeting))
        {
            return false;
        }

        if (IsInCombatMode(user) ||
            _hands.GetActiveItem(user) != targeting.Item ||
            !CanTransfer(targeting.Item))
        {
            StopTargeting(user);
            return false;
        }

        if (target == user || !Exists(target) || !HasComp<HandsComponent>(target))
            return true;

        RaiseNetworkEvent(new HandTransferOfferEvent(GetNetEntity(targeting.Item), GetNetEntity(target)));
        StopTargeting(user);
        return true;
    }

    private bool OnTargetingCancelled(ICommonSession? session, EntityCoordinates coordinates, EntityUid target)
    {
        if (session?.AttachedEntity is not { } user || !HasComp<HandTransferTargetingComponent>(user))
            return false;

        StopTargeting(user);
        return true;
    }

    private bool IsInCombatMode(EntityUid user)
    {
        return TryComp<CombatModeComponent>(user, out var combatMode) && combatMode.IsInCombatMode;
    }

    private bool CanTransfer(EntityUid item)
    {
        return Exists(item) &&
               HasComp<ItemComponent>(item) &&
               !HasComp<VirtualItemComponent>(item) &&
               !HasComp<MobStateComponent>(item);
    }

    private void StopTargeting(EntityUid user)
    {
        RemComp<HandTransferTargetingComponent>(user);
    }
}
