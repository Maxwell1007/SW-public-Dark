using Content.Shared.Imperial.Medieval.SkeletonInvasion;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

[RegisterComponent]
public sealed partial class SkeletonProgressionRoundComponent : Component
{
    [DataField]
    public EntProtoId MindRole = "MindRoleMorbiusSkeleton";

    [DataField]
    public EntProtoId RecruitSpawnerPrototype = "MedievalMorbiusRecruitSpawner";

    [DataField]
    public List<ProtoId<SkeletonUpgradePrototype>> AvailableSharedUpgrades = new()
    {
        "MorbiusRecruitFollower",
        "MorbiusMindConnection",
    };

    [ViewVariables]
    public int SharedPoints;

    [ViewVariables]
    public HashSet<ProtoId<SkeletonUpgradePrototype>> PurchasedSharedUpgrades = new();

    [ViewVariables]
    public Dictionary<NetUserId, SkeletonProgressionAccount> Accounts = new();

    [ViewVariables]
    public HashSet<EntityUid> PendingMinds = new();

    [ViewVariables]
    public Queue<int> PendingRecruitPoints = new();

    [ViewVariables]
    public bool Resetting;
}
