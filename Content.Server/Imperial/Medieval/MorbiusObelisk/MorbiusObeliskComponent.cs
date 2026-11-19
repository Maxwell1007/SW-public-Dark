using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.MorbiusObelisk;

[RegisterComponent]
public sealed partial class MorbiusObeliskComponent : Component
{
    [DataField]
    public EntProtoId RewardPrototype = "MedievalSpellBookRecodeNecro";

    [ViewVariables]
    public bool RewardSpawned;
}
