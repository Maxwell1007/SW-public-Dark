namespace Content.Server.Imperial.Medieval.Magic.BindStoreOnEquip;

[RegisterComponent]
public sealed partial class BindStoreOnEquipComponent : Component
{
    [DataField]
    public bool MorbiusFollowersOnly;

    [ViewVariables]
    public EntityUid? OwnerUid;
}
