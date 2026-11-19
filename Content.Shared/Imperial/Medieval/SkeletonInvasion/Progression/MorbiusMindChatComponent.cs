using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.SkeletonInvasion;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MorbiusMindChatComponent : Component
{
    [DataField, AutoNetworkedField]
    public string ChatPrefix = ":mb";

    [DataField, AutoNetworkedField]
    public string AlternateChatPrefix = ":мб";

    [DataField, AutoNetworkedField]
    public Color ChatColor = Color.LimeGreen;
}

[Serializable, NetSerializable]
public sealed class MorbiusMindChatMessageEvent(string message) : EntityEventArgs
{
    public string Message = message;
}
