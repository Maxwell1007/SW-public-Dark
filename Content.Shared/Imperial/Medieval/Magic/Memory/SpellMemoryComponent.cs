namespace Content.Shared.Imperial.Medieval.Magic.Memory;

[RegisterComponent]
public sealed partial class SpellMemoryComponent : Component
{
    [DataField]
    public int Cost;

    [DataField]
    public bool Forgotten;

    [DataField]
    public EntityUid? SpellOwner;
}
