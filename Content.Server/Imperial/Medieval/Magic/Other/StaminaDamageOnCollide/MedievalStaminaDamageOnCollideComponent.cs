using Robust.Shared.Audio;

namespace Content.Server.Imperial.Medieval.Magic.Other.StaminaDamageOnCollide;

/// <summary>
/// Applies stamina damage from a spell projectile and attributes it to the projectile's shooter.
/// </summary>
[RegisterComponent, Access(typeof(MedievalStaminaDamageOnCollideSystem))]
public sealed partial class MedievalStaminaDamageOnCollideComponent : Component
{
    [DataField]
    public float Damage = 55f;

    [DataField]
    public SoundSpecifier? Sound;
}
