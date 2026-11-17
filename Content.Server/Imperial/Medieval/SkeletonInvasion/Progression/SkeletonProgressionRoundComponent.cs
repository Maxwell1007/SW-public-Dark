using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

[RegisterComponent]
public sealed partial class SkeletonProgressionRoundComponent : Component
{
    [DataField]
    public EntProtoId MindRole = "MindRoleMorbiusSkeleton";

    [ViewVariables]
    public Dictionary<NetUserId, SkeletonProgressionAccount> Accounts = new();

    [ViewVariables]
    public HashSet<EntityUid> PendingMinds = new();

    [ViewVariables]
    public bool Resetting;
}
