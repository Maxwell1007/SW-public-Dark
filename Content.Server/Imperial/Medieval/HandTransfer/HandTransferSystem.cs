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
        SubscribeLocalEvent<HandTransferRequestComponent, AcceptHandTransferAlertEvent>(OnAcceptAlert);
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

        if (!CanCreateRequest(offerer, itemUid, receiver))
            return;

        if (TryComp<HandTransferRequestComponent>(receiver, out var existing) &&
            IsRequestCurrent(receiver, existing))
        {
            _popup.PopupEntity(
                Loc.GetString("hand-transfer-recipient-busy", ("target", Identity.Entity(receiver, EntityManager))),
                receiver,
                offerer,
                PopupType.SmallCaution);
            return;
        }

        var request = existing ?? EnsureComp<HandTransferRequestComponent>(receiver);
        request.Offerer = offerer;
        request.Item = itemUid;
        Dirty(receiver, request);

        EnsureComp<AlertsComponent>(receiver);
        _alerts.ShowAlert(receiver, TransferAlert);

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
               Exists(request.Offerer) &&
               IsTransferable(request.Item) &&
               _hands.GetActiveItem(request.Offerer) == request.Item &&
               _interaction.InRangeUnobstructed(request.Offerer, receiver);
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

        if (deferred)
            RemCompDeferred<HandTransferRequestComponent>(receiver);
        else
            RemComp<HandTransferRequestComponent>(receiver);
    }
}
