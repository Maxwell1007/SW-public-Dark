using Content.Shared.Imperial.Medieval.SkeletonInvasion;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

[DataDefinition]
public sealed partial class SkeletonProgressionAccount
{
    [DataField]
    public int Points;

    [DataField]
    public Dictionary<ProtoId<SkeletonUpgradePrototype>, int> PurchasedUpgrades = new();

    [DataField]
    public bool IsAntagonist;

    [DataField]
    public bool StartingPointsGranted;

    [DataField]
    public EntityUid? Body;

    [DataField]
    public EntityUid? Skull;

    [DataField]
    public EntityUid? Mind;
}
