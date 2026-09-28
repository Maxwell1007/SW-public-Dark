using Robust.Shared.GameStates;

namespace Content.Shared.Imperial.Medieval.Myrmex;

/// <summary>
///     imperial medieval - temporary speed-burst marker from eating mushroom stew.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MyrmexSpeedBurstComponent : Component
{
    [DataField]
    public float Multiplier = 1f;
}
