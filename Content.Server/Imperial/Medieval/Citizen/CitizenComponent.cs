using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Server.Imperial.Medieval.Citizen;

[RegisterComponent]
public sealed partial class CitizenComponent : Component
{
    [DataField] public string KeyAccess;

    [DataField] public bool IsRich;
    [DataField] public string JobId = "MedievalCitizenRich";
}
