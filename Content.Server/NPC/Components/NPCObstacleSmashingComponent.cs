using Content.Shared.FixedPoint;
using Content.Shared.Whitelist;

namespace Content.Server.NPC.Components;

/// <summary>
/// What an NPC with <see cref="Pathfinding.PathFlags.Smashing"/> may break to clear its way.
/// </summary>
[RegisterComponent]
public sealed partial class NPCObstacleSmashingComponent : Component
{
    /// <summary>
    /// Only clear obstacles while pursuing something, not while idle wandering.
    /// </summary>
    [DataField]
    public bool RequireTarget = true;

    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// On top of the global <see cref="Imperial.Medieval.NPC.NPCUnsmashableComponent"/>.
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// Per-hit damage after the target's resistances. Below this we'd wedge ourselves against it.
    /// </summary>
    [DataField]
    public FixedPoint2 MinEffectiveDamage = FixedPoint2.New(1);

    /// <summary>
    /// Don't start on anything we'd still be hitting after this long.
    /// </summary>
    [DataField]
    public float MaxSmashSeconds = 60f;
}
