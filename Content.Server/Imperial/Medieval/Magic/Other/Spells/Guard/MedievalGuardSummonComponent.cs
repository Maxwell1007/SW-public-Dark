namespace Content.Server.Imperial.Medieval.Magic.Guard;

[RegisterComponent]
public sealed partial class MedievalGuardSummonComponent : Component
{
    [DataField]
    public float MinSpawnRadius = 1f;

    [DataField]
    public float MaxSpawnRadius = 2f;

    [DataField]
    public float ClearanceRadius = 0.25f;

    public readonly HashSet<EntityUid> Obstacles = new();
}
