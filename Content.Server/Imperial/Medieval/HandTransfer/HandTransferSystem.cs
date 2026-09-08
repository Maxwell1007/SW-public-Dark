using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Imperial.Medieval.HandTransfer;
using Content.Shared.Interaction;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.HandTransfer;

public sealed class HandTransferSystem : EntitySystem
{
    private static readonly ProtoId<AlertPrototype> TransferAlert = "MedievalHandTransfer";

    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<HandTransferOfferEvent>(OnOffer);
        SubscribeAllEvent<HandTransferAcceptEvent>(OnAccept);
        SubscribeAllEvent<HandTransferCancelEvent>(OnCancel);
        SubscribeLocalEvent<HandTransferRequestComponent, AcceptHandTransferAlertEvent>(OnAcceptAlert);
        SubscribeLocalEvent<HandTransferOutgoingComponent, CancelHandTransferAlertEvent>(OnCancelAlert);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<HandTransferRequestComponent>();
        while (query.MoveNext(out var receiver, out var request))
        {
            if (!IsRequestCurrent(receiver, request))
                ClearRequest(receiver, deferred: true);
        }

        var outgoingQuery = EntityQueryEnumerator<HandTransferOutgoingComponent>();
        while (outgoingQuery.MoveNext(out var offerer, out var outgoing))
        {
            if (!IsOutgoingCurrent(offerer, outgoing))
                ClearOutgoing(offerer, outgoing, deferred: true);
        }
    }

    private void OnOffer(HandTransferOfferEvent message, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } offerer ||
            !TryGetEntity(message.Item, out var item) ||
            !TryGetEntity(message.Target, out var target) ||
            item is not { } itemUid ||
            target is not { } receiver)
        {
            return;
        }

        if (TryComp<HandTransferOutgoingComponent>(offerer, out var existingOutgoing))
        {
            if (IsOutgoingCurrent(offerer, existingOutgoing))
                return;

            ClearOutgoing(offerer, existingOutgoing);
        }

        if (!CanCreateRequest(offerer, itemUid, receiver))
            return;

        HandTransferRequestComponent? existing = null;
        if (TryComp(receiver, out existing))
        {
            if (IsRequestCurrent(receiver, existing))
            {
                _popup.PopupEntity(
                    Loc.GetString("hand-transfer-recipient-busy", ("target", Identity.Entity(receiver, EntityManager))),
                    receiver,
                    offerer,
                    PopupType.SmallCaution);
                return;
            }

            ClearRequest(receiver);
            existing = null;
        }

        var request = existing ?? EnsureComp<HandTransferRequestComponent>(receiver);
        request.Offerer = offerer;
        request.Item = itemUid;
        Dirty(receiver, request);

        var outgoing = EnsureComp<HandTransferOutgoingComponent>(offerer);
        outgoing.Receiver = receiver;
        outgoing.Item = itemUid;
        Dirty(offerer, outgoing);

        EnsureComp<AlertsComponent>(receiver);
        _alerts.ShowAlert(receiver, TransferAlert);
        EnsureComp<AlertsComponent>(offerer);
        _alerts.ShowAlert(offerer, outgoing.Alert);

        _popup.PopupEntity(
            Loc.GetString(
                "hand-transfer-offer-sender",
                ("item", Identity.Entity(itemUid, EntityManager)),
                ("target", Identity.Entity(receiver, EntityManager))),
            receiver,
            offerer,
            PopupType.Small);

        _popup.PopupEntity(
            Loc.GetString(
                "hand-transfer-offer-recipient",
                ("item", Identity.Entity(itemUid, EntityManager)),
                ("offerer", Identity.Entity(offerer, EntityManager))),
            offerer,
            receiver,
            PopupType.Small);
    }

    private void OnAccept(HandTransferAcceptEvent message, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } receiver)
            TryAccept(receiver);
    }

    private void OnCancel(HandTransferCancelEvent message, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } offerer &&
            TryComp<HandTransferOutgoingComponent>(offerer, out var outgoing))
        {
            ClearOutgoing(offerer, outgoing);
        }
    }

    private void OnCancelAlert(
        Entity<HandTransferOutgoingComponent> ent,
        ref CancelHandTransferAlertEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ClearOutgoing(ent.Owner, ent.Comp);
    }

    private void OnAcceptAlert(
        Entity<HandTransferRequestComponent> ent,
        ref AcceptHandTransferAlertEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryAccept(ent.Owner, ent.Comp);
    }

    private void TryAccept(EntityUid receiver, HandTransferRequestComponent? request = null)
    {
        if (!Resolve(receiver, ref request, false))
            return;

        var offerer = request.Offerer;
        var item = request.Item;

        if (!CanCompleteTransfer(receiver, request) ||
            !_hands.TryPickupAnyHand(receiver, item, animateUser: true))
        {
            _popup.PopupEntity(
                Loc.GetString("hand-transfer-failed"),
                receiver,
                receiver,
                PopupType.SmallCaution);
            ClearRequest(receiver);
            return;
        }

        ClearRequest(receiver);

        _popup.PopupEntity(
            Loc.GetString(
                "hand-transfer-complete-sender",
                ("item", Identity.Entity(item, EntityManager)),
                ("target", Identity.Entity(receiver, EntityManager))),
            receiver,
            offerer,
            PopupType.Small);

        _popup.PopupEntity(
            Loc.GetString(
                "hand-transfer-complete-recipient",
                ("item", Identity.Entity(item, EntityManager)),
                ("offerer", Identity.Entity(offerer, EntityManager))),
            offerer,
            receiver,
            PopupType.Small);
    }

    private bool CanCreateRequest(EntityUid offerer, EntityUid item, EntityUid receiver)
    {
        if (offerer == receiver ||
            !HasComp<HandsComponent>(receiver) ||
            IsInCombatMode(offerer) ||
            !IsTransferable(item) ||
            _hands.GetActiveItem(offerer) != item ||
            !_interaction.InRangeUnobstructed(offerer, receiver) ||
            !_actionBlocker.CanInteract(offerer, receiver) ||
            !_hands.CanPickupAnyHand(receiver, item))
        {
            return false;
        }

        return true;
    }

    private bool CanCompleteTransfer(EntityUid receiver, HandTransferRequestComponent request)
    {
        return IsRequestCurrent(receiver, request) &&
               _actionBlocker.CanInteract(request.Offerer, receiver) &&
               _actionBlocker.CanInteract(receiver, request.Offerer) &&
               _hands.CanPickupAnyHand(receiver, request.Item);
    }

    private bool IsRequestCurrent(EntityUid receiver, HandTransferRequestComponent request)
    {
        return Exists(receiver) &&
               HasComp<HandsComponent>(receiver) &&
               TryComp<HandsComponent>(request.Offerer, out var offererHands) &&
               IsTransferable(request.Item) &&
               _hands.IsHolding((request.Offerer, offererHands), request.Item) &&
               _interaction.InRangeUnobstructed(request.Offerer, receiver);
    }

    private bool IsOutgoingCurrent(EntityUid offerer, HandTransferOutgoingComponent outgoing)
    {
        return TryComp<HandTransferRequestComponent>(outgoing.Receiver, out var request) &&
               request.Offerer == offerer &&
               request.Item == outgoing.Item &&
               IsRequestCurrent(outgoing.Receiver, request);
    }

    private bool IsTransferable(EntityUid item)
    {
        return Exists(item) &&
               HasComp<ItemComponent>(item) &&
               !HasComp<VirtualItemComponent>(item) &&
               !HasComp<MobStateComponent>(item);
    }

    private bool IsInCombatMode(EntityUid user)
    {
        return TryComp<CombatModeComponent>(user, out var combatMode) && combatMode.IsInCombatMode;
    }

    private void ClearRequest(EntityUid receiver, bool deferred = false)
    {
        _alerts.ClearAlert(receiver, TransferAlert);

        if (TryComp<HandTransferRequestComponent>(receiver, out var request) &&
            TryComp<HandTransferOutgoingComponent>(request.Offerer, out var outgoing) &&
            outgoing.Receiver == receiver &&
            outgoing.Item == request.Item)
        {
            _alerts.ClearAlert(request.Offerer, outgoing.Alert);

            if (deferred)
                RemCompDeferred<HandTransferOutgoingComponent>(request.Offerer);
            else
                RemComp<HandTransferOutgoingComponent>(request.Offerer);
        }

        if (deferred)
            RemCompDeferred<HandTransferRequestComponent>(receiver);
        else
            RemComp<HandTransferRequestComponent>(receiver);
    }

    private void ClearOutgoing(
        EntityUid offerer,
        HandTransferOutgoingComponent outgoing,
        bool deferred = false)
    {
        _alerts.ClearAlert(offerer, outgoing.Alert);

        if (TryComp<HandTransferRequestComponent>(outgoing.Receiver, out var request) &&
            request.Offerer == offerer &&
            request.Item == outgoing.Item)
        {
            _alerts.ClearAlert(outgoing.Receiver, TransferAlert);

            if (deferred)
                RemCompDeferred<HandTransferRequestComponent>(outgoing.Receiver);
            else
                RemComp<HandTransferRequestComponent>(outgoing.Receiver);
        }

        if (deferred)
            RemCompDeferred<HandTransferOutgoingComponent>(offerer);
        else
            RemComp<HandTransferOutgoingComponent>(offerer);
    }
}
