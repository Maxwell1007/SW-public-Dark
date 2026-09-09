using System.Linq;
using Content.Server.Imperial.Medieval.Magic.BindStoreOnEquip;
using Content.Server.Imperial.Medieval.Magic.Memory;
using Content.Shared.Actions.Components;
using Content.Shared.Imperial.ImperialStore;
using Content.Shared.Imperial.Medieval.Magic.Memory;

namespace Content.Server.Imperial.ImperialStore;

public sealed partial class ImperialStoreSystem
{
    [Dependency] private readonly MagicMemorySystem _memory = default!;

    private bool IsMemoryStore(EntityUid uid)
    {
        return HasComp<BindStoreOnEquipComponent>(uid);
    }

    private bool CanBuySpell(EntityUid buyer, ImperialListingData listing)
    {
        if (listing.ProductAction is { } prototype)
            return _memory.CanLearn(buyer, _memory.GetCost(prototype));

        if (listing.ProductActionEntity is not { } actionUid)
            return true;

        if (!TryComp<ActionUpgradeComponent>(actionUid, out var upgrade) ||
            !TryComp<ActionComponent>(actionUid, out var action))
        {
            return false;
        }

        if (action.AttachedEntity != buyer &&
            (!TryComp<SpellMemoryComponent>(actionUid, out var spell) || spell.SpellOwner != buyer || !spell.Forgotten))
        {
            return false;
        }

        var level = upgrade.Level + listing.ActionLevelUp;
        if (!_actionUpgrade.TryGetUpgradePrototype(actionUid, level, out var upgraded))
            return false;

        var cost = upgraded is { } upgradedPrototype
            ? _memory.GetCost(upgradedPrototype)
            : TryComp<SpellMemoryComponent>(actionUid, out var current) ? current.Cost : 0;
        return _memory.CanLearn(buyer, cost, actionUid);
    }

    private void UpdateMemoryListings(EntityUid buyer, EntityUid uid, ImperialStoreComponent store)
    {
        if (!IsMemoryStore(uid))
            return;

        var available = store.LastAvailableListings.Select(listing => listing.ID).ToHashSet();
        foreach (var listing in store.Listings)
        {
            listing.CanManageMemory = false;
            listing.Forgotten = false;
            listing.MemoryBlocked = false;
            listing.PurchaseBlocked = !available.Contains(listing.ID) || !CanBuySpell(buyer, listing);

            if (listing.PurchaseAmount == 0 || listing.PurchasedActionEntity is not { } actionUid ||
                !TryComp<SpellMemoryComponent>(actionUid, out var spell) || spell.SpellOwner != buyer ||
                !ListingHasCategory(listing, store.Categories))
            {
                continue;
            }

            listing.CanManageMemory = true;
            listing.Forgotten = spell.Forgotten;
            listing.MemoryBlocked = spell.Forgotten && !_memory.CanLearn(buyer, spell.Cost);
            listing.PurchaseBlocked = true;
            if (available.Add(listing.ID))
                store.LastAvailableListings.Add(listing);
        }
    }

    private void OnToggleSpellMemory(EntityUid uid, ImperialStoreComponent store, ImperialStoreToggleSpellMemoryMessage args)
    {
        if (!IsMemoryStore(uid) || store.AccountOwner != args.Actor)
            return;

        var listing = store.Listings.FirstOrDefault(x => x.ID == args.ListingId);
        if (listing == null || listing.PurchaseAmount == 0 || !ListingHasCategory(listing, store.Categories) ||
            listing.PurchasedActionEntity is not { } actionUid ||
            !TryComp<SpellMemoryComponent>(actionUid, out var spell) || spell.SpellOwner != args.Actor)
        {
            return;
        }

        if (spell.Forgotten)
        {
            if (!_memory.TryRemember(args.Actor, actionUid))
                _popup.PopupEntity(Loc.GetString("magic-memory-not-enough"), args.Actor, args.Actor);
        }
        else
        {
            _memory.TryForget(args.Actor, actionUid);
        }

        UpdateUserInterface(args.Actor, uid, store);
    }
}
