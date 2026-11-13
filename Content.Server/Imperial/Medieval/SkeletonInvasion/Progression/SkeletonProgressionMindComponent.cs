using Content.Shared.Imperial.Medieval.SkeletonInvasion;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

[RegisterComponent]
public sealed partial class SkeletonProgressionMindComponent : Component
{
    [DataField]
    public int Points;

    [DataField]
    public Dictionary<ProtoId<SkeletonUpgradePrototype>, int> PurchasedUpgrades = new();

    [ViewVariables]
    public EntityUid? Body;
}
