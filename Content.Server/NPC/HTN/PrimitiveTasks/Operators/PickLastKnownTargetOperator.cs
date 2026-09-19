using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Puts the last place we saw our target into the coordinates key.
/// </summary>
public sealed partial class PickLastKnownTargetOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField("keyCoordinates")]
    public string KeyCoordinates = "TargetCoordinates";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<NPCTargetMemoryComponent>(owner, out var memory) ||
            !_entManager.System<NPCTargetMemorySystem>().TryGetLastKnown((owner, memory), out var coordinates))
        {
            return (false, null);
        }

        return (true, new Dictionary<string, object>
        {
            { KeyCoordinates, coordinates },
        });
    }
}
