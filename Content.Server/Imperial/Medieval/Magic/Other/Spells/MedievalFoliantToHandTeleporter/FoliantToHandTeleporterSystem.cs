using Content.Server.Imperial.Medieval.Magic.BindStoreOnEquip;
using Content.Server.Imperial.Medieval.Magic.MedievalSpawnInFreeSlot;
using Content.Shared.Imperial.Medieval.Magic;

namespace Content.Server.Imperial.Medieval.Magic.MedievalFoliantToHandTeleporter;

public sealed partial class FoliantToHandTeleporterSystem : EntitySystem
{
    [Dependency] private readonly MedievalSpawnInFreeSlotSystem _placementSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FoliantToHandTeleporterComponent, MedievalAfterSpawnEntityBySpellEvent>(FindFoliant);
    }

    private void FindFoliant(EntityUid uid, FoliantToHandTeleporterComponent component, MedievalAfterSpawnEntityBySpellEvent args)
    {
        EntityUid playerUid = args.Performer;
        var query = EntityQueryEnumerator<BindStoreOnEquipComponent>();

        while (query.MoveNext(out var folliantUID, out var bindComp))
        {
            if (bindComp.BindedEntity == playerUid)
            {
                _placementSystem.TryPlaceInFreeSlot(playerUid, folliantUID);
                break;
            }
        }
    }
}
