using System;
using System.Numerics;
using Content.Shared.Imperial.Medieval.Additions;
using Content.Shared.Imperial.Medieval.Administration.Ships;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Content.Shared.Tiles;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Server.Imperial.Medieval.Ships.Wave;

public sealed class WaveSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly PhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    private readonly Random _random = new();

    public (string, ushort)[] Stages =
    {
        ("woodbroken", (ushort) 1),
        ("woodbroken2", (ushort) 2),
        ("woodbroken3", (ushort) 3)
    };

    public override void Initialize()
    {
        ResolveStages();
        SubscribeLocalEvent<WaveComponent, StartCollideEvent>(OnCollide);
    }

    private void ResolveStages()
    {
        if (Stages.Length == 0)
            return;

        for (var i = 0; i < Stages.Length; i++)
        {
            var stage = Stages[i];
            if (!_tileDefinitionManager.TryGetDefinition(stage.Item1, out var tileDefinition))
                continue;

            Stages[i] = (stage.Item1, tileDefinition.TileId);
        }
    }

    private void OnCollide(EntityUid uid, WaveComponent component, ref StartCollideEvent args)
    {
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(args.OtherEntity))
            return;

        if (component.HitList.Contains(args.OtherEntity))
            return;

        if (_cfg.GetCVar(ShipsCCVars.WaveMinToBreakLevel) > _cfg.GetCVar(ShipsCCVars.StormLevel))
        {
            _entityManager.DeleteEntity(args.OurEntity);
            return;
        }

        var collisionPos = _transform.GetMapCoordinates(args.OurEntity);
        var gridEntity = args.OtherEntity;
        if (!_entityManager.TryGetComponent<MapGridComponent>(gridEntity, out var mapGridComp))
            return;

        var grid = new Entity<MapGridComponent>(gridEntity, mapGridComp);
        var centerTilePos = _map.MapToGrid(grid, collisionPos);
        var radiusTiles = _cfg.GetCVar(ShipsCCVars.WaveRadiusTiles) + _cfg.GetCVar(ShipsCCVars.StormLevel);
        var antiradius = (int) radiusTiles * -1;

        var nearbyTiles = new List<Vector2i>();

        for (var dx = antiradius; dx <= radiusTiles; dx++)
        {
            for (var dy = antiradius; dy <= radiusTiles; dy++)
            {
                var tilePos = centerTilePos + new EntityCoordinates(gridEntity, new Vector2(dx, dy));
                var tile = _map.GetTileRef(grid, tilePos);

                if (tile.Tile.IsEmpty)
                    continue;

                var stop = false;
                foreach (var wall in _lookup.GetEntitiesInTile(tile, flags: LookupFlags.Static | LookupFlags.Approximate))
                {
                    if (!_tags.HasTag(wall, "Wall"))
                        continue;

                    stop = true;
                    break;
                }

                if (stop)
                    continue;

                var distance = Vector2.Distance(centerTilePos.Position, tilePos.Position);
                if (distance <= radiusTiles)
                    nearbyTiles.Add(((int) tilePos.X, (int) tilePos.Y));
            }
        }

        _random.Shuffle(nearbyTiles);

        var maxBreakCount = Math.Max(0, _cfg.GetCVar(ShipsCCVars.WaveMaxBreakCount));
        var tilesToReplace = Math.Min(_random.Next(0, maxBreakCount + 1), nearbyTiles.Count);
        for (var i = 0; i < tilesToReplace; i++)
        {
            var tilePos = nearbyTiles[i];
            if (!_map.TryGetTile(grid, tilePos, out var tile) || tile.IsEmpty)
                continue;

            var stageLast = Stages.Length - 1;
            if (tile.TypeId == Stages[stageLast].Item2 || tile.IsEmpty)
                continue;

            var index = 0;
            foreach (var stage in Stages)
            {
                if (stage.Item2 == tile.TypeId)
                    break;

                index++;
            }

            if (index == stageLast + 1)
                index = 0;

            _map.SetTile(grid.Owner, grid, tilePos, new Tile(Stages[index + 1].Item2, 0, 0));
        }

        if (!TerminatingOrDeleted(args.OtherEntity))
            component.HitList.Add(args.OtherEntity);

        if (component.DeleteOnCollide)
            _entityManager.DeleteEntity(args.OurEntity);
    }

    public void SpawnWave(EntityCoordinates coords, MapId mapId, Vector2 force = new(), bool deleteOnCollide = true, float lifetime = 60)
    {
        if (!_map.TryGetMap(mapId, out _))
            return;

        var wave = Spawn("WaveLarge", coords);
        var waveComponent = EnsureComp<WaveComponent>(wave);
        waveComponent.DeleteOnCollide = deleteOnCollide;

        _physics.WakeBody(wave);
        _physics.ApplyLinearImpulse(wave, force);

        RemComp<TimedDespawnComponent>(wave);
        RemComp<MedievalTimedDespawnComponent>(wave);
        if (lifetime <= 0)
            return;

        var despawnComponent = EnsureComp<MedievalTimedDespawnComponent>(wave);
        despawnComponent.Lifetime = lifetime;
        despawnComponent.OriginalLifeTime = lifetime;
    }
}
