using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.ImperialStore;

[Serializable, NetSerializable]
public sealed class ImperialStoreToggleSpellMemoryMessage(string listingId) : BoundUserInterfaceMessage
{
    public readonly string ListingId = listingId;
}
