using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.Guard;

[RegisterComponent]
public sealed partial class MedievalGuardOwnerComponent : Component
{
    [DataField]
    public EntProtoId AttackAction = "MedievalActionGuardAttack";

    [DataField]
    public EntProtoId ReleaseAction = "MedievalActionGuardRelease";

    [ViewVariables]
    public EntityUid? AttackActionEntity;

    [ViewVariables]
    public EntityUid? ReleaseActionEntity;

    [ViewVariables]
    public readonly HashSet<EntityUid> Guards = new();
}
