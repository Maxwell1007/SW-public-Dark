using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules;

[RegisterComponent]
public sealed partial class SkeletonInvasionRuleComponent : Component
{
    [DataField(required: true)]
    public ResPath Arena;

    [DataField]
    public EntProtoId SkeletonPrototype = "MedievalMobSkeletonNecroFighter";

    [DataField]
    public TimeSpan DefeatRoundEndDelay = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan VictoryRoundEndDelay = TimeSpan.FromMinutes(25);

    [DataField]
    public TimeSpan BossfightRoundExtension = TimeSpan.FromMinutes(10);

    [ViewVariables]
    public EntityUid? Boss;

    [ViewVariables]
    public TimeSpan? EndTime;

    [ViewVariables]
    public SkeletonInvasionRoundResult Result;
}
