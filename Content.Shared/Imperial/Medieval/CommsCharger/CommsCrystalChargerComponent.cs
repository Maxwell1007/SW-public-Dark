namespace Content.Shared.Imperial.Medieval.CommsCharger;

[RegisterComponent]
public sealed partial class CommsCrystalChargerComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(2);
}
