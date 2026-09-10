using Content.Server.Imperial.Medieval.Guard;
using Content.Shared.Imperial.Medieval.Magic;
using Content.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server.Imperial.Medieval.Magic.Guard;

public sealed class MedievalGuardSpellSystem : EntitySystem
{
    [Dependency] private readonly MedievalGuardSystem _guards = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MedievalGuardComponent, MedievalAfterSpawnEntityBySpellEvent>(OnSpawned);
        SubscribeLocalEvent<MedievalGuardSummonComponent, MedievalBeforeSpawnEntityBySpellEvent>(OnBeforeSpawn);
    }

    private void OnBeforeSpawn(EntityUid uid, MedievalGuardSummonComponent component,
        ref MedievalBeforeSpawnEntityBySpellEvent args)
    {
        if (args.Cancelled)
            return;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var coordinates = args.Coordinates.Offset(_random.NextVector2(component.MinSpawnRadius, component.MaxSpawnRadius));
            var mapCoordinates = _transform.ToMapCoordinates(coordinates);
            component.Obstacles.Clear();
            _lookup.GetEntitiesInRange(mapCoordinates.MapId, mapCoordinates.Position, component.ClearanceRadius,
                component.Obstacles, LookupFlags.Dynamic | LookupFlags.Static);
            var blocked = false;
            foreach (var obstacle in component.Obstacles)
            {
                if (!TryComp<PhysicsComponent>(obstacle, out var physics) || !physics.CanCollide || !physics.Hard ||
                    (physics.CollisionLayer & (int) CollisionGroup.MobMask) == 0)
                    continue;

                blocked = true;
                break;
            }

            if (blocked)
                continue;

            args.Coordinates = coordinates;
            return;
        }

        args.Cancelled = true;
    }

    private void OnSpawned(EntityUid uid, MedievalGuardComponent component, MedievalAfterSpawnEntityBySpellEvent args)
    {
        _guards.BindGuard(uid, args.Performer, component);
    }
}
