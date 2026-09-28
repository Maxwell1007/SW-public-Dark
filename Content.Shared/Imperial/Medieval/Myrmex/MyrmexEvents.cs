using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Myrmex;

[Serializable, NetSerializable]
public sealed partial class StewFeedDoAfterEvent : DoAfterEvent
{
    public StewFeedDoAfterEvent() {}
    public override DoAfterEvent Clone() => this;
}

[Serializable]
public sealed partial class LarvaFeedEvent : EntityEventArgs
{
    public EntityPrototype Eaten;

    public LarvaFeedEvent(EntityPrototype eaten)
    {
        Eaten = eaten;
    }
}

//action events

public sealed partial class ActionMyrmexQueenLayEggEvent : InstantActionEvent;

public sealed partial class ActionMyrmexShootEvent : WorldTargetActionEvent
{
    [DataField(required: true)]
    public EntProtoId ProjectileProto;

    [DataField(required: true)]
    public float Speed;
}

public sealed partial class ActionMyrmexBoostEvent : InstantActionEvent
{
    [DataField(required: true)]
    public TimeSpan Duration;

    [DataField(required: true)]
    public float Multiplier;
}

public sealed partial class ActionMyrmexToggleArmorEvent : InstantActionEvent;

public sealed partial class ActionMyrmexToggleStunEvent : InstantActionEvent;

public sealed partial class ActionMyrmexSpawnEvent : InstantActionEvent
{
    [DataField]
    public TimeSpan DoAfterDuration;

    [DataField(required: true)]
    public EntProtoId Proto;
}

[Serializable, NetSerializable]
public sealed partial class ActionMyrmexSpawnDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public EntProtoId Proto;

    // imperial medieval - the action entity, so cooldown can be started on DoAfter completion
    // instead of immediately when the DoAfter starts (see OnSpawn/OnSpawnDoAfter)
    // NetEntity, not EntityUid - this event is networked and raw EntityUid isn't serializable here. 
    [DataField(required: true)]
    public NetEntity ActionUid; 
    
    public override DoAfterEvent Clone() => this;
}

public sealed partial class ActionMyrmexToggleStealthEvent : InstantActionEvent;

public sealed partial class ActionMyrmexHealEvent : EntityTargetActionEvent
{
    [DataField(required: true)]
    public DamageSpecifier HealedDamage;

    // imperial medieval - how much bleeding severity this heal staunches. 0 = no change.
    [DataField]
    public float BleedReduction;
}
