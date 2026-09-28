
using Content.Server.Imperial.Medieval.Citizen;
using Content.Shared.Inventory;
using Content.Shared.Imperial.LockDoor.Components;
using Content.Shared.GameTicking;
using Content.Server.Station.Systems;

namespace Content.Shared.Imperial.Medieval.Citizen;

public sealed class CitizenSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly UniversalKeyServerSystem _universalKey = default!;
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly MetaDataSystem _metaDataSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CitizenComponent, PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(Entity<CitizenComponent> ent, ref PlayerSpawnCompleteEvent args)
    {
        // Первый проход: ищем дом с доступными местами
        var query = EntityQueryEnumerator<CitizenSpawnerComponent>();
        while (query.MoveNext(out var spawnerUid, out var spawnerComp))
        {
            if (ent.Comp.IsRich != spawnerComp.IsRich)
                continue;

            if (spawnerComp.Members.Count >= spawnerComp.MaxMembers)
                continue;

            SpawnInHouse(ent, (spawnerUid, spawnerComp));
            return;
        }

        // Второй проход: если все дома заполнены, селим в любой подходящий по статусу
        CitizenSpawnerComponent? bestSpawnerComp = null;
        EntityUid? bestSpawnerUid = null;
        var minMembers = int.MaxValue;

        var fallbackQuery = EntityQueryEnumerator<CitizenSpawnerComponent>();
        while (fallbackQuery.MoveNext(out var spawnerUid, out var spawnerComp))
        {
            if (ent.Comp.IsRich != spawnerComp.IsRich)
                continue;

            if (spawnerComp.Members.Count < minMembers)
            {
                minMembers = spawnerComp.Members.Count;
                bestSpawnerComp = spawnerComp;
                bestSpawnerUid = spawnerUid;
            }
        }

        if (bestSpawnerUid != null && bestSpawnerComp != null)
        {
            SpawnInHouse(ent, (bestSpawnerUid.Value, bestSpawnerComp));
            return;
        }
    }

    private void SpawnInHouse(Entity<CitizenComponent> citizenEnt, Entity<CitizenSpawnerComponent> spawnerEnt)
    {
        spawnerEnt.Comp.Members.Add(citizenEnt);

        _transformSystem.SetCoordinates(citizenEnt, Transform(spawnerEnt).Coordinates);

        citizenEnt.Comp.KeyAccess = spawnerEnt.Comp.KeyAccess;

        if (!_inventorySystem.TryGetSlotEntity(citizenEnt, "id", out var keyUid) ||
            keyUid is not { } keyUID)
            return;

        if (!TryComp<KeyComponent>(keyUid, out var keyComponent))
            return;

        keyComponent.Accesses.Add(citizenEnt.Comp.KeyAccess);

        _universalKey.SetupKey(keyUID);


        var newDesc = $"{Description(keyUID)} {citizenEnt.Comp.KeyAccess}";
        _metaDataSystem.SetEntityDescription(keyUID, newDesc);

        if (AreAllSpawnersOccupied(citizenEnt.Comp.IsRich))
            LockJob(citizenEnt);
    }

    private void LockJob(Entity<CitizenComponent> citizenEnt)
    {
        foreach (var station in _station.GetStationsSet())
            _stationJobs.TrySetJobSlot(station, citizenEnt.Comp.JobId, 0);
    }

    /// <summary>
    /// Проверяет, заполнены ли все спавнеры домов указанной категории.
    /// Возвращает true, если свободных мест нет (или если спавнеры вовсе отсутствуют).
    /// </summary>
    public bool AreAllSpawnersOccupied(bool isRich = false)
    {
        var query = EntityQueryEnumerator<CitizenSpawnerComponent>();

        while (query.MoveNext(out _, out var spawnerComp))
        {
            if (spawnerComp.IsRich != isRich)
                continue;

            spawnerComp.Members.RemoveAll(Deleted);

            // Если найден хотя бы один дом со свободным местом — возвращаем false
            if (spawnerComp.Members.Count < spawnerComp.MaxMembers)
                return false;
        }

        // Возвращает true, если все спавнеры переполнены либо если для данной категории не найдено ни одного спавнера
        return true;
    }
}
