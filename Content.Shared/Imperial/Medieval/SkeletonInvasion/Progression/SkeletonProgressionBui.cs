using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.SkeletonInvasion;

[Serializable, NetSerializable]
public enum SkeletonProgressionUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class SkeletonProgressionBuiState(
    int points,
    List<ProtoId<SkeletonUpgradePrototype>> availableUpgrades,
    List<ProtoId<SkeletonUpgradePrototype>> purchasedUpgrades,
    int sharedPoints,
    List<ProtoId<SkeletonUpgradePrototype>> availableSharedUpgrades,
    List<ProtoId<SkeletonUpgradePrototype>> purchasedSharedUpgrades,
    bool canBuy) : BoundUserInterfaceState
{
    public readonly int Points = points;
    public readonly List<ProtoId<SkeletonUpgradePrototype>> AvailableUpgrades = availableUpgrades;
    public readonly List<ProtoId<SkeletonUpgradePrototype>> PurchasedUpgrades = purchasedUpgrades;
    public readonly int SharedPoints = sharedPoints;
    public readonly List<ProtoId<SkeletonUpgradePrototype>> AvailableSharedUpgrades = availableSharedUpgrades;
    public readonly List<ProtoId<SkeletonUpgradePrototype>> PurchasedSharedUpgrades = purchasedSharedUpgrades;
    public readonly bool CanBuy = canBuy;
}

[Serializable, NetSerializable]
public sealed class SkeletonBuyUpgradeMessage(ProtoId<SkeletonUpgradePrototype> upgrade) : BoundUserInterfaceMessage
{
    public readonly ProtoId<SkeletonUpgradePrototype> Upgrade = upgrade;
}
