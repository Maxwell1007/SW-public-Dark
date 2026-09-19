using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Perfume;

[DataDefinition, Serializable, NetSerializable]
public partial struct PerfumeScentInstance
{
    [DataField(required: true)]
    public LocId LocKey;

    [DataField]
    public TimeSpan Duration;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class PerfumeTargetComponent : Component
{
    [DataField]
    public Dictionary<string, PerfumeScentInstance> Scents = new();
}