using System.Numerics;
using Content.Shared.Damage;

namespace Content.Server.Imperial.Medieval.Magic.FireWave;

[RegisterComponent]
public sealed partial class MedievalFireWaveComponent : Component
{
    [DataField]
    public int CheckCount = 10;

    [DataField]
    public float InitialRadius = 0.5f;

    [DataField]
    public float RadiusIncrement = 0.4f;

    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(0.1);

    [DataField]
    public Angle SectorAngle = Angle.FromDegrees(90);

    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public float FireStacks = 0.05f;

    [DataField]
    public EntityUid? Caster;

    [DataField]
    public Vector2 Direction;

    [DataField]
    public int ChecksPerformed;
}
