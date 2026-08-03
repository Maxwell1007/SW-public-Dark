using System;
using System.Linq;
using System.Numerics;
using Content.Server.MagicBarrier.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Robust.Shared.Map;

namespace Content.Server.MagicBarrier;

public sealed class MagicBarrierRiftSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicBarrierRiftComponent, AfterInteractUsingEvent>(OnRiftUse);
        SubscribeLocalEvent<MagicBarrierRiftComponent, EntityTerminatingEvent>(OnRiftTerminating);
        SubscribeLocalEvent<RiftGuardianComponent, MobStateChangedEvent>(OnGuardianStateChanged);
        SubscribeLocalEvent<RiftGuardianComponent, EntityTerminatingEvent>(OnGuardianTerminating);
        SubscribeLocalEvent<RiftGuardianComponent, ComponentShutdown>(OnGuardianShutdown);
    }

    private void OnRiftUse(EntityUid uid, MagicBarrierRiftComponent component, AfterInteractUsingEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach)
            return;

        if (!TryComp<RiftKeyComponent>(args.Used, out var keyComponent)
            || !string.Equals(keyComponent.Element, component.Element, StringComparison.OrdinalIgnoreCase))
            return;

        if (component.GuardiansSpawned || component.State != MagicBarrierRiftState.Ready)
            return;

        component.GuardiansSpawned = true;
        component.Guardians.Clear();
        component.RemainingGuardians = 0;
        component.DestroyedLegitimately = false;
        component.State = MagicBarrierRiftState.Active;

        QueueDel(args.Used);

        for (var i = 0; i < component.GuardianEntities.Count; i++)
        {
            SpawnGuardian(uid, component, i);
        }
        args.Handled = true;
    }

    private void SpawnGuardian(EntityUid rift, MagicBarrierRiftComponent component, int index)
    {
        if (index < 0 || index >= component.GuardianEntities.Count)
            return;

        var offset = index < component.GuardianOffsets.Count
            ? component.GuardianOffsets[index]
            : Vector2.Zero;
        var coords = Transform(rift).Coordinates.Offset(offset);
        var guardian = Spawn(component.GuardianEntities[index], coords);
        var guardianComponent = EnsureComp<RiftGuardianComponent>(guardian);
        guardianComponent.Rift = rift;
        guardianComponent.Defeated = false;
        component.Guardians.Add(guardian);
        component.RemainingGuardians++;
    }

    private void OnGuardianStateChanged(EntityUid uid, RiftGuardianComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || component.Defeated)
            return;

        if (!TryComp<MagicBarrierRiftComponent>(component.Rift, out var riftComponent))
            return;

        if (riftComponent.State != MagicBarrierRiftState.Active ||
            !riftComponent.Guardians.Contains(uid) ||
            riftComponent.RemainingGuardians <= 0)
            return;

        component.Defeated = true;
        riftComponent.RemainingGuardians--;
        if (riftComponent.RemainingGuardians != 0)
            return;

        var coords = Transform(component.Rift).Coordinates;
        Spawn("MedievalSkeletDespawnEffect", coords);
        riftComponent.State = MagicBarrierRiftState.Completed;
        riftComponent.DestroyedLegitimately = true;
        QueueDel(component.Rift);
    }

    private void OnGuardianTerminating(EntityUid uid, RiftGuardianComponent component, ref EntityTerminatingEvent args)
    {
        HandleGuardianRemoval(uid, component);
    }

    private void OnGuardianShutdown(EntityUid uid, RiftGuardianComponent component, ComponentShutdown args)
    {
        if (MetaData(uid).EntityLifeStage >= EntityLifeStage.Terminating)
            return;

        HandleGuardianRemoval(uid, component);
    }

    private void HandleGuardianRemoval(EntityUid uid, RiftGuardianComponent component)
    {
        if (!TryComp<MagicBarrierRiftComponent>(component.Rift, out var riftComponent) ||
            riftComponent.State != MagicBarrierRiftState.Active ||
            !riftComponent.Guardians.Contains(uid))
            return;

        if (component.Defeated)
        {
            riftComponent.Guardians.Remove(uid);
            return;
        }

        if (TerminatingOrDeleted(component.Rift) || EntityManager.IsQueuedForDeletion(component.Rift))
            return;

        ResetRift(riftComponent);
    }

    private void ResetRift(MagicBarrierRiftComponent component)
    {
        if (component.State != MagicBarrierRiftState.Active)
            return;

        component.State = MagicBarrierRiftState.Resetting;
        DeleteGuardians(component);
        component.DestroyedLegitimately = false;
        component.State = MagicBarrierRiftState.Ready;
    }

    private void OnRiftTerminating(EntityUid uid, MagicBarrierRiftComponent component, ref EntityTerminatingEvent args)
    {
        DeleteGuardians(component);
    }

    private void DeleteGuardians(MagicBarrierRiftComponent component)
    {
        var guardians = component.Guardians.ToArray();
        component.Guardians.Clear();
        component.RemainingGuardians = 0;
        component.GuardiansSpawned = false;

        foreach (var guardian in guardians)
        {
            if (TerminatingOrDeleted(guardian) || EntityManager.IsQueuedForDeletion(guardian))
                continue;

            QueueDel(guardian);
        }
    }
}
