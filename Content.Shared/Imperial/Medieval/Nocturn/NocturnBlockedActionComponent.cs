namespace Content.Shared.Nocturn.Components;

[RegisterComponent]
public sealed partial class NocturnBlockedActionComponent : Component
{
    [DataField]
    public TimeSpan BlockedCooldown = TimeSpan.FromSeconds(5);

    [DataField]
    public LocId BlockedMessage = "medieval-ancient-nocturne-action-blocked";

    [DataField]
    public bool CheckOnAttempt = true;
}
