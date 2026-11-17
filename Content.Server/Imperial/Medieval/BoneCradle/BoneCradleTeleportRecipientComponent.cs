using Content.Shared.DoAfter;
using Robust.Shared.Network;

namespace Content.Server.Imperial.Medieval.BoneCradle;

[RegisterComponent]
public sealed partial class BoneCradleTeleportRecipientComponent : Component
{
    [ViewVariables]
    public EntityUid? Cradle;

    [ViewVariables]
    public EntityUid? Action;

    [ViewVariables]
    public NetUserId UserId;

    [ViewVariables]
    public DoAfterId? DoAfter;
}
