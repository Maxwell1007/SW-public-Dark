using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.BoneCradle;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class BoneCradleTeleportComponent : Component
{
    [DataField]
    public EntProtoId ActionPrototype = "ActionBoneCradleTeleport";

    [DataField]
    public TimeSpan GracePeriod = TimeSpan.FromMinutes(6);

    [DataField]
    public LocId NoSpaceMessage = "bone-cradle-teleport-no-space";

    [DataField]
    public TimeSpan ChannelDuration = TimeSpan.FromSeconds(10);

    [DataField]
    public float MinimumRadius = 1.5f;

    [DataField]
    public float MaximumRadius = 3f;

    [DataField]
    public int PositionAttempts = 20;

    [DataField]
    public float Clearance = 0.45f;

    [DataField]
    public TimeSpan RefreshInterval = TimeSpan.FromSeconds(0.5);

    [DataField, AutoPausedField]
    public TimeSpan GraceEndsAt;

    [DataField, AutoPausedField]
    public TimeSpan NextRefresh;

    [ViewVariables]
    public bool Active;

    [ViewVariables]
    public HashSet<NetUserId> TeleportedUsers = new();
}
