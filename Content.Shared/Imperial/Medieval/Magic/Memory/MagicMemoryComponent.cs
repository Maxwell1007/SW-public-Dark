using Robust.Shared.GameStates;

namespace Content.Shared.Imperial.Medieval.Magic.Memory;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicMemoryComponent : Component
{
    [DataField, AutoNetworkedField]
    public int MaxMemory;

    [DataField, AutoNetworkedField]
    public int CurrentMemory;
}
