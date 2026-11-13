using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Imperial.Medieval.SkeletonInvasion;

[Prototype("skeletonUpgrade")]
public sealed partial class SkeletonUpgradePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField(required: true)]
    public SpriteSpecifier Icon = default!;

    [DataField(required: true)]
    public int Cost;

    [DataField(required: true)]
    public Vector2 Position;

    [DataField]
    public List<ProtoId<SkeletonUpgradePrototype>> Required = new();

    [DataField]
    public float SpeedModifier = 1f;

    [DataField]
    public float StaminaDamageModifier = 1f;

    [DataField]
    public float DashStaminaModifier = 1f;
}
