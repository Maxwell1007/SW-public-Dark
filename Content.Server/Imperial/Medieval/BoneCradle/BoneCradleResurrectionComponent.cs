using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.BoneCradle;

[RegisterComponent]
public sealed partial class BoneCradleResurrectionComponent : Component
{
    [DataField]
    public EntProtoId SkeletonPrototype = "MedievalMobSkeletonNecroFighter";

    [DataField]
    public LocId Notification = "bone-cradle-resurrection-notification";

    [DataField]
    public SoundSpecifier NotificationSound = new SoundPathSpecifier("/Audio/Imperial/Medieval/Effects/teleport.ogg");
}
