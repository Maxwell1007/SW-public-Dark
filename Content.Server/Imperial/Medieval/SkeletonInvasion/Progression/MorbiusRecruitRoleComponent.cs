using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

[RegisterComponent]
public sealed partial class MorbiusRecruitRoleComponent : Component
{
    [DataField]
    public EntProtoId SkeletonPrototype = "MedievalMobSkeletonNecroFighter";

    [DataField]
    public int Points;

    [ViewVariables]
    public bool Claimed;
}
