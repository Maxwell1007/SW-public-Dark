using Robust.Shared.GameStates;

namespace Content.Shared.Imperial.Medieval.Myrmex;

/// <summary>
///     imperial medieval - temporary damage-reduction marker from eating root stew.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MyrmexTempShieldComponent : Component
{
    [DataField]
    public float Multiplier = 1f;
}
