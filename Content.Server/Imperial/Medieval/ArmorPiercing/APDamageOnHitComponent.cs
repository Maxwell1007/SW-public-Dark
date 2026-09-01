using Content.Shared.Damage;
namespace Content.Server.Imperial.Medieval.APDamage;

[RegisterComponent]
public sealed partial class APDamageOnHitComponent : Component
{
    [DataField("damage", required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier Damage = default!;
}

