using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.HandTransfer;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HandTransferRequestComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Offerer;

    [AutoNetworkedField]
    public EntityUid Item;
}

[Serializable, NetSerializable]
public sealed class HandTransferOfferEvent(NetEntity item, NetEntity target) : EntityEventArgs
{
    public readonly NetEntity Item = item;
    public readonly NetEntity Target = target;
}

[Serializable, NetSerializable]
public sealed class HandTransferAcceptEvent : EntityEventArgs;

public sealed partial class AcceptHandTransferAlertEvent : BaseAlertEvent;
