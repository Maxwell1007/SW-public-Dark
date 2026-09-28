using Robust.Shared.Map;

namespace Content.Server.NPC.Components;

/// <summary>
/// Where a target was after it broke line of sight. Updated by <see cref="Systems.NPCTargetMemorySystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NPCTargetMemoryComponent : Component
{
    /// <summary>
    /// How long we keep tracking the target's real position after losing sight of it.
    /// </summary>
    [DataField]
    public TimeSpan TrackDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the frozen position is worth investigating once tracking runs out.
    /// </summary>
    [DataField]
    public TimeSpan MemoryDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How close we have to get before the spot counts as checked.
    /// </summary>
    [DataField]
    public float ArrivalRange = 1.5f;

    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public TimeSpan LastSeen;

    /// <summary>
    /// Null once the memory is spent.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates? LastKnownCoordinates;
}
