using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Alchemy;

[RegisterComponent]
public sealed partial class AlchemyIngredientComponent : Component
{
    [DataField(required: true)] public string Profile = default!;
}

[RegisterComponent]
public sealed partial class AlchemyVesselComponent : Component
{
    [DataField] public string Solution = "beaker";
    [DataField] public float NigredoTemperature = 350;
    [DataField] public bool Processing;
    [DataField] public bool Hot;
}

[RegisterComponent]
public sealed partial class AlchemyToolComponent : Component
{
    [DataField(required: true)] public string Operation = default!;
}

[RegisterComponent]
public sealed partial class AlchemySolutionComponent : Component
{
    public int Revision;
}

[Serializable, NetSerializable]
public sealed partial class AlchemyDoAfterEvent : DoAfterEvent
{
    [DataField] public NetEntity Solution;
    [DataField] public int Revision;
    [DataField] public string Operation = string.Empty;
    public override DoAfterEvent Clone() => (AlchemyDoAfterEvent) MemberwiseClone();
}

[RegisterComponent]
public sealed partial class AlchemyApparatusComponent : Component
{
    [DataField] public string Operation = "Distill";
    [DataField] public string OutputSlot = "alchemy_output";
    [DataField] public string Solution = "alchemy_input";
    public bool Running;
    public Dictionary<EntityUid, int> Sources = new();
    public List<EntityUid> Items = new();
}

[RegisterComponent]
public sealed partial class AlchemyItemHistoryComponent : Component
{
    [DataField] public List<string> Operations = new();
}
