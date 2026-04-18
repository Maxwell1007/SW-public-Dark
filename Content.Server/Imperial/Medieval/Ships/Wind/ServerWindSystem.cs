using System;
using Content.Server.Shuttles.Components;
using Content.Shared.Imperial.Medieval.Administration.Ships;
using Content.Shared.Imperial.Medieval.Ships.Sea;
using Robust.Shared.Configuration;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Imperial.Medieval.Ships.Wind;

/// <summary>
/// This handles...
/// </summary>
public sealed class ServerWindSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private TimeSpan _nextCheckTime;

    public override void Initialize()
    {
        _cfg.OnValueChanged(ShipsCCVars.StormLevel, OnStormLevelChanged, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;

        if (curTime > _nextCheckTime)
        {
            _nextCheckTime = curTime + TimeSpan.FromSeconds(_cfg.GetCVar(ShipsCCVars.WindChangeTime));
            RandomiseVind();
        }
    }

    private void RandomiseVind()
    {
        var windForce = _cfg.GetCVar(ShipsCCVars.StormLevel);
        var countShips = FindShips();

        if (windForce <= 0 + countShips)
            windForce += _random.Next(0, 2);
        else if (windForce >= 2 + countShips || countShips >= 10)
            windForce -= _random.Next(0, 2);
        else
            windForce += _random.Next(-1, 2);
        _cfg.SetCVar(ShipsCCVars.WindPower, windForce);

        var windAngle = _cfg.GetCVar(ShipsCCVars.WindRotation);
        windAngle += _random.Next(-1, 1) * 5;

        if (Math.Abs(windAngle) > 360)
            windAngle -= 360;
        else if (windAngle < 0)
            windAngle += 360;

        _cfg.SetCVar(ShipsCCVars.WindRotation, windAngle);
    }

    private int FindShips()
    {
        var count = 0;
        foreach (var seaComp in EntityManager.EntityQuery<SeaComponent>())
        {
            if (seaComp.Disabled)
                continue;

            var mapId = _transform.GetMapId(seaComp.Owner);
            var ships = new HashSet<Entity<ShuttleComponent>>();
            _lookup.GetEntitiesOnMap(mapId, ships);
            count += ships.Count;
        }

        return count;
    }

    private void OnStormLevelChanged(float stormLevel)
    {
        _cfg.SetCVar(ShipsCCVars.WindPower, MathF.Max(0f, stormLevel));
    }
}
