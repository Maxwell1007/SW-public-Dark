using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.BoneCradle;

public sealed partial class BoneCradleTeleportActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class BoneCradleTeleportDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetEntity Cradle;

    [DataField]
    public NetEntity Action;

    private BoneCradleTeleportDoAfterEvent()
    {
    }

    public BoneCradleTeleportDoAfterEvent(NetEntity cradle, NetEntity action)
    {
        Cradle = cradle;
        Action = action;
    }

    public override DoAfterEvent Clone()
    {
        return new BoneCradleTeleportDoAfterEvent(Cradle, Action);
    }
}
