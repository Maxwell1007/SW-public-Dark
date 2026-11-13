using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Imperial.Medieval.SkeletonInvasion;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SkeletonProgressionComponent : Component
{
    [DataField]
    public EntProtoId MenuAction = "ActionMorbiusSkeletonProgression";

    [DataField]
    public int StartingPoints;

    [DataField]
    public List<ProtoId<SkeletonUpgradePrototype>> AvailableUpgrades = new();

    [DataField, AutoNetworkedField]
    public List<ProtoId<SkeletonUpgradePrototype>> Upgrades = new();

    [ViewVariables]
    public EntityUid? MenuActionEntity;

    [ViewVariables]
    public EntityUid? Mind;
}
