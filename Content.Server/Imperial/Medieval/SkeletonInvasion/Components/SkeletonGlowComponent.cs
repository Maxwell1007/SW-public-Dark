using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

[RegisterComponent]
public sealed partial class SkeletonGlowComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [ViewVariables]
    public EntityUid? Light;
}
