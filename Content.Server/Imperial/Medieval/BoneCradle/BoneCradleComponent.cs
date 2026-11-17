using Content.Shared.Imperial.Medieval.Factions.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.BoneCradle;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class BoneCradleComponent : Component
{
    [DataField]
    public ProtoId<MedievalFactionPrototype> FollowerFaction = "BoneCradleMorbius";

    [DataField]
    public ProtoId<MedievalFactionPrototype> OpponentFaction = "BoneCradleOpponents";

    [DataField]
    public TimeSpan GracePeriod = TimeSpan.FromMinutes(6);

    [DataField]
    public int PointsPerReward = 1;

    [DataField]
    public TimeSpan RewardInterval = TimeSpan.FromMinutes(1);

    [DataField, AutoPausedField]
    public TimeSpan NextReward;

    [DataField, AutoPausedField]
    public TimeSpan RewardEnd;

    [ViewVariables]
    public bool RewardsEnabled;
}
