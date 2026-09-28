using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Examine;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

/// <summary>
/// Keeps <see cref="NPCTargetMemoryComponent"/> up to date.
/// </summary>
public sealed class NPCTargetMemorySystem : EntitySystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private const float UpdateInterval = 0.25f;

    private float _accumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;

        if (_accumulator < UpdateInterval)
            return;

        _accumulator -= UpdateInterval;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<NPCTargetMemoryComponent, HTNComponent>();

        while (query.MoveNext(out var uid, out var memory, out var htn))
        {
            Update(uid, memory, htn, now);
        }
    }

    private void Update(EntityUid uid, NPCTargetMemoryComponent memory, HTNComponent htn, TimeSpan now)
    {
        var blackboard = htn.Blackboard;

        if (blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager) &&
            Exists(target) &&
            _mobState.IsAlive(target))
        {
            var radius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);

            // Nothing clears the key once a pursuit ends, so a stale one can't revive a spent memory.
            if (_examine.InRangeUnOccluded(uid, target, radius, null))
            {
                memory.Target = target;
                memory.LastSeen = now;
            }

            if (memory.Target == target && now - memory.LastSeen <= memory.TrackDuration)
            {
                memory.LastKnownCoordinates = Transform(target).Coordinates;
                return;
            }
        }

        UpdateMemory(uid, memory, now);
    }

    private void UpdateMemory(EntityUid uid, NPCTargetMemoryComponent memory, TimeSpan now)
    {
        if (memory.Target == null)
            return;

        if (now - memory.LastSeen > memory.TrackDuration + memory.MemoryDuration)
        {
            Forget(memory);
            return;
        }

        if (memory.LastKnownCoordinates is { } last &&
            last.IsValid(EntityManager) &&
            _transform.InRange(Transform(uid).Coordinates, last, memory.ArrivalRange))
        {
            Forget(memory);
        }
    }

    /// <summary>
    /// Expiry is handled in <see cref="UpdateMemory"/>, so this stays a plain read for planning threads.
    /// </summary>
    public bool TryGetLastKnown(Entity<NPCTargetMemoryComponent> ent, out EntityCoordinates coordinates)
    {
        coordinates = default;

        if (ent.Comp.LastKnownCoordinates is not { } last || !last.IsValid(EntityManager))
            return false;

        coordinates = last;
        return true;
    }

    public void Forget(NPCTargetMemoryComponent memory)
    {
        memory.Target = null;
        memory.LastKnownCoordinates = null;
    }
}
