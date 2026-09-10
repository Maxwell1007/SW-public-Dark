using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.Imperial.Medieval.Magic;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Imperial.Medieval.Magic.FireWave;

public sealed class MedievalFireWaveSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedievalFireWaveComponent, MedievalAfterSpawnEntityBySpellEvent>(OnSpawn);
    }

    private void OnSpawn(EntityUid uid, MedievalFireWaveComponent component, MedievalAfterSpawnEntityBySpellEvent args)
    {
        var ent = new Entity<MedievalFireWaveComponent>(uid, component);
        if (args.TargetCoordinates is not { } target || !TryComp(args.Performer, out TransformComponent? casterTransform))
        {
            QueueDel(ent);
            return;
        }

        var origin = _transform.GetMapCoordinates(args.Performer, casterTransform);
        if (origin.MapId != target.MapId)
        {
            QueueDel(ent);
            return;
        }

        var direction = target.Position - origin.Position;
        ent.Comp.Caster = args.Performer;
        ent.Comp.Direction = direction.LengthSquared() > 0
            ? Vector2.Normalize(direction)
            : _transform.GetWorldRotation(casterTransform).ToWorldVec();
        ent.Comp.ChecksPerformed = 0;
        _transform.SetParent(ent, args.Performer);
        _ = RunWaveAsync(ent);
    }

    private async Task RunWaveAsync(Entity<MedievalFireWaveComponent> ent)
    {
        try
        {
            while (!TerminatingOrDeleted(ent) &&
                   TryComp<MedievalFireWaveComponent>(ent, out var wave) && wave == ent.Comp &&
                   wave.ChecksPerformed < wave.CheckCount)
            {
                if (wave.Caster is not { } caster || TerminatingOrDeleted(caster))
                    return;

                if (!MetaData(ent).EntityPaused && !MetaData(caster).EntityPaused)
                    PerformCheck(ent, caster, _transform.GetMapCoordinates(caster));

                await Timer.Delay(wave.CheckInterval);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Fire wave failed: {e}");
        }
        finally
        {
            if (!TerminatingOrDeleted(ent))
                QueueDel(ent);
        }
    }

    private void UpdateVisual(Entity<MedievalFireWaveComponent> ent, MapCoordinates origin)
    {
        var radius = ent.Comp.InitialRadius + Math.Max(0, ent.Comp.ChecksPerformed - 1) * ent.Comp.RadiusIncrement;
        var position = origin.Position + ent.Comp.Direction * radius;
        var localPosition = Vector2.Transform(position, _transform.GetInvWorldMatrix(Transform(ent).ParentUid));
        _transform.SetLocalPosition(ent, localPosition);
        _transform.SetWorldRotation(ent, ent.Comp.Direction.ToWorldAngle());
    }

    private void PerformCheck(Entity<MedievalFireWaveComponent> ent, EntityUid caster, MapCoordinates origin)
    {
        var wave = ent.Comp;
        var radius = wave.InitialRadius + wave.ChecksPerformed * wave.RadiusIncrement;
        var minimumDot = Math.Cos(wave.SectorAngle.Theta / 2);
        var targets = _lookup.GetEntitiesInRange<DamageableComponent>(origin, radius, LookupFlags.Uncontained);

        foreach (var target in targets)
        {
            if (target.Owner == caster || TerminatingOrDeleted(target))
                continue;

            var offset = _transform.GetWorldPosition(target) - origin.Position;
            var distanceSquared = offset.LengthSquared();
            if (distanceSquared > radius * radius ||
                distanceSquared > 0 && Vector2.Dot(Vector2.Normalize(offset), wave.Direction) < minimumDot)
                continue;

            if (!_interaction.InRangeUnobstructed(caster, target.Owner, radius + 0.1f, overlapCheck: false))
                continue;

            _damageable.TryChangeDamage(target, wave.Damage, origin: caster);
            if (!TerminatingOrDeleted(target) && TryComp<FlammableComponent>(target, out var flammable))
            {
                _flammable.AdjustFireStacks(target, wave.FireStacks, flammable);
                _flammable.Ignite(target, caster, flammable);
            }
        }

        wave.ChecksPerformed++;
        UpdateVisual(ent, origin);
    }
}
