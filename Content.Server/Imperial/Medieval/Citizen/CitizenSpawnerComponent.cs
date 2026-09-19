using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Server.Imperial.Medieval.Citizen;

[RegisterComponent]
public sealed partial class CitizenSpawnerComponent : Component
{
    /// <summary>
    /// Значение Access у двери или ключа.
    /// </summary>
    [DataField] public string KeyAccess = "";

    [DataField] public int MaxMembers = 1;

    [DataField] public List<EntityUid?> Members = new();

    [DataField] public bool IsRich = false;
}
