using Content.Shared.Trigger.Components.Effects;

namespace Content.Server.Imperial.Medieval.Magic.Triggers.ExplodeOnTrigger;

/// <summary>
/// Explodes a spell projectile while attributing the explosion to its shooter.
/// </summary>
[RegisterComponent, Access(typeof(MedievalExplodeOnTriggerSystem))]
public sealed partial class MedievalExplodeOnTriggerComponent : BaseXOnTriggerComponent;
