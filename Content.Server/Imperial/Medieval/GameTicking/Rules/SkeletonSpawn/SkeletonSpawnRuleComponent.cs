using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules.SkeletonSpawn;

[RegisterComponent]
public sealed partial class SkeletonSpawnRuleComponent : Component
{
    [DataField(required: true)]
    public EntProtoId SkeletonPrototype;

    [DataField(required: true)]
    public EntProtoId MindRole;

    [DataField(required: true)]
    public ProtoId<AntagPrototype> AntagPrototype;

    [ViewVariables]
    public EntityUid? Performer;
}
