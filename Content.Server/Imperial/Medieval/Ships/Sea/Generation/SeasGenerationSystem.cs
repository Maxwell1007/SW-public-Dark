using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Imperial.Medieval.Ships.Sea.Init;
using Content.Server.MagicBarrier.Components;
using Content.Shared.Imperial.Medieval.Ships.Sea;
using Content.Shared.Parallax;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Imperial.Medieval.Ships.Sea.Generation;

public sealed class SeasGenerationSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SeaMatrixInitSystem _seaMatrix = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private const int MapMin = -75;
    private const int MapMax = 75;

    // РСЃРїРѕР»СЊР·СѓРµРј РїСЂРѕС‚РѕС‚РёРїС‹ РІРјРµСЃС‚Рѕ СЃС‚СЂРѕРє
    private static readonly (string PrototypeId, int Count)[] IslandConfig = {
        ("PirateIslands", 1),   // 1 Р±РѕР»СЊС€РѕР№
        ("FrendlyIslands", 2),   // 2 СЃСЂРµРґРЅРёС…
        ("VolcanicIsland", 10)    // 10 РјРµР»РєРёС…
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<MagicBarrierComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SeasGenerationEvent>(OnSeasGeneration);
    }

    private void OnInit(EntityUid uid, MagicBarrierComponent component, ComponentInit args)
    {
        if (component.SeaMatrix == null)
            component.SeaMatrix = new SeaMatrix(new List<(int x, int y)>
            {
                (2, 2), (2, 3), (2, 4),
                (3, 2), (3, 3), (3, 4),
                (4, 2), (4, 3), (4, 4),
            });

        if (component.SeaInitalazed) return;

        var seaMatrix = component.SeaMatrix;

        // РЎРѕР·РґР°РµРј 25 РєР°СЂС‚ РјРѕСЂСЏ (5x5)
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                if (!seaMatrix.NeedsGeneration(x, y))
                    continue;

                var mapUid = _map.CreateMap();
                _metaData.SetEntityName(mapUid, $"РњРѕСЂРµ {x} {y}");
                var parallax = AddComp<ParallaxComponent>(mapUid);
                parallax.Parallax = "OceanMedieval";
                var mapId = _transform.GetMapId(mapUid);
                AddComp<SeaComponent>(mapUid);
                seaMatrix.SetSeaId(x, y, mapId);
                seaMatrix.SetGenerated(x, y, false);
            }
        }

        // вњ… Р“Р•РќР•Р РР РЈР•Рњ РћРЎРўР РћР’Рђ РЎ РРЎРџРћР›Р¬Р—РћР’РђРќРР•Рњ IPrototypeManager
        GenerateIslandsOnSeaMaps(seaMatrix);

        component.SeaInitalazed = true;
    }

    /// <summary>
    /// Р“РµРЅРµСЂРёСЂСѓРµС‚ РѕСЃС‚СЂРѕРІР°, РёСЃРїРѕР»СЊР·СѓСЏ IPrototypeManager Рё РєРѕРЅС„РёРіСѓСЂР°С†РёСЋ РїРѕ ID.
    /// Р’СЃРµ РѕСЃС‚СЂРѕРІР° СЂР°Р·РјРµС‰Р°СЋС‚СЃСЏ РІ РѕР±С‰РµРј РїСЂРѕСЃС‚СЂР°РЅСЃС‚РІРµ [-75, 75], Р±РµР· РїРµСЂРµСЃРµС‡РµРЅРёР№.
    /// </summary>
    private void GenerateIslandsOnSeaMaps(SeaMatrix seaMatrix)
    {
        var generatedObjects = new List<EntityUid>();
        var occupiedTiles = new HashSet<(int X, int Y)>();

        // РЎРѕР±РёСЂР°РµРј РІСЃРµ MapId РєР°СЂС‚ РјРѕСЂСЏ
        var seaMapIds = new List<MapId>();
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                var cell = seaMatrix.GetCell(x, y);
                if (!cell.NeedGenerate && !(cell.SeaId == new MapId(-1)))
                    seaMapIds.Add(cell.SeaId);
            }
        }

        if (seaMapIds.Count == 0)
        {
            Logger.Warning("No sea maps found to generate islands on!");
            return;
        }

        // РџСЂРѕС…РѕРґРёРј РїРѕ РєРѕРЅС„РёРіСѓСЂР°С†РёРё РѕСЃС‚СЂРѕРІРѕРІ
        foreach (var (prototypeId, count) in IslandConfig)
        {
            // РџСЂРѕРІРµСЂСЏРµРј, СЃСѓС‰РµСЃС‚РІСѓРµС‚ Р»Рё РїСЂРѕС‚РѕС‚РёРї
            if (!_prototypeManager.TryIndex<IslandPrototype>(prototypeId, out var prototype) || prototype.Path == null)
            {
                Logger.Warning($"Island prototype '{prototypeId}' not found! Skipping.");
                continue;
            }

            for (int i = 0; i < count; i++)
            {
                int attempts = 0;
                const int maxAttempts = 100;

                while (++attempts <= maxAttempts)
                {
                    // Р’С‹Р±РёСЂР°РµРј СЃР»СѓС‡Р°Р№РЅСѓСЋ РєР°СЂС‚Сѓ РјРѕСЂСЏ
                    var targetMapId = seaMapIds[_random.Next(seaMapIds.Count)];

                    // РЎР»СѓС‡Р°Р№РЅР°СЏ РїРѕР·РёС†РёСЏ РЅР° РєР°СЂС‚Рµ
                    int x = _random.Next(MapMin, MapMax - prototype.Size + 1);
                    int y = _random.Next(MapMin, MapMax - prototype.Size + 1);

                    // РџСЂРѕРІРµСЂСЏРµРј РїРµСЂРµСЃРµС‡РµРЅРёСЏ
                    bool overlaps = false;
                    var newTiles = new List<(int X, int Y)>();

                    for (int dx = 0; dx < prototype.Size; dx++)
                    {
                        for (int dy = 0; dy < prototype.Size; dy++)
                        {
                            var tile = (x + dx, y + dy);
                            if (occupiedTiles.Contains(tile))
                            {
                                overlaps = true;
                                break;
                            }
                            newTiles.Add(tile);
                        }
                        if (overlaps)
                            break;
                    }

                    if (!overlaps)
                    {
                        _mapLoader.TryLoadGrid(targetMapId, new ResPath(prototype.Path), out var newObj, offset: new Vector2(x,y));
                        if (newObj.HasValue)
                        {
                            generatedObjects.Add(newObj.Value);
                            foreach (var tile in newTiles)
                                occupiedTiles.Add(tile);
                            break; // РЈСЃРїРµС€РЅРѕ
                        }
                    }
                }

                if (attempts > maxAttempts)
                {
                    Logger.Warning($"Failed to generate {prototypeId} after {maxAttempts} attempts.");
                }
            }
        }

        Logger.Info($"Successfully generated {generatedObjects.Count} islands across {seaMapIds.Count} sea maps.");
    }

    public sealed class SeasGenerationEvent
    {
        public MapId MapId { get; set; }
        public int Count { get; set; }
        public string Prototype { get; set; } = "Reef";
    }

    private void OnSeasGeneration(SeasGenerationEvent ev)
    {
        // РћСЃС‚Р°РІР»РµРЅРѕ РґР»СЏ Р±СѓРґСѓС‰РµРіРѕ СЂР°СЃС€РёСЂРµРЅРёСЏ
    }
}

