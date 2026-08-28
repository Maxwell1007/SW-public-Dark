using System.Linq;
using System.Collections.Generic;
using Content.Server.Imperial.Medieval.Magic.BindStoreOnEquip;
using Content.Server.Imperial.Medieval.Magic.MedievalSpawnInFreeSlot;
using Content.Shared.Actions;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Imperial.Medieval.Magic;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Imperial.Medieval.Magic;

[TestFixture]
[NonParallelizable]
public sealed class SummonFoliantTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestSummonFoliantPouch
  components:
  - type: ContainerContainer
    containers:
      pouch: !type:Container

- type: entity
  id: TestSummonFoliantProjectile
  components:
  - type: FoliantToHandTeleporter

- type: entity
  id: TestSummonFoliantNestedContainer
  components:
  - type: Item
    size: Tiny
  - type: ContainerContainer
    containers:
      nested: !type:Container
";

    [Test]
    public async Task SummonDoesNotDisplaceHeldItems()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false
        });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        var entMan = server.ResolveDependency<IEntityManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var handsSystem = server.System<SharedHandsSystem>();
        var containerSystem = server.System<SharedContainerSystem>();
        var inventorySystem = server.System<InventorySystem>();
        var placementSystem = server.System<MedievalSpawnInFreeSlotSystem>();
        var storageSystem = server.System<SharedStorageSystem>();

        await server.WaitAssertion(() =>
        {
            var player = playerMan.Sessions.First().AttachedEntity!.Value;
            var hands = entMan.GetComponent<HandsComponent>(player);
            var heldItems = new List<EntityUid>();
            var heldPrototypes = new[] { "MedievalCommsCrystallColl" };

            Assert.That(hands.Count, Is.GreaterThan(heldPrototypes.Length));
            foreach (var prototype in heldPrototypes)
            {
                var held = entMan.SpawnEntity(prototype, map.GridCoords);
                Assert.That(handsSystem.TryPickupAnyHand(player, held), Is.True);
                heldItems.Add(held);
            }

            var pouch = entMan.SpawnEntity("TestSummonFoliantPouch", map.GridCoords);
            var foliant = entMan.SpawnEntity("MedievalSpellBookBase", map.GridCoords);
            var bind = entMan.GetComponent<BindStoreOnEquipComponent>(foliant);
#pragma warning disable RA0002
            bind.BindedEntity = player;
#pragma warning restore RA0002
            Assert.That(containerSystem.TryGetContainer(pouch, "pouch", out var container), Is.True);
            Assert.That(containerSystem.Insert(foliant, container), Is.True);

            var projectile = entMan.SpawnEntity("TestSummonFoliantProjectile", map.GridCoords);
            var spellEvent = new MedievalAfterSpawnEntityBySpellEvent
            {
                Performer = player,
                SpawnedEntity = projectile
            };
            entMan.EventBus.RaiseLocalEvent(projectile, spellEvent);

            Assert.That(handsSystem.IsHolding(player, heldItems[0]), Is.True);
            Assert.That(handsSystem.IsHolding(player, foliant), Is.True);
            Assert.That(container.Contains(foliant), Is.False);

            entMan.EventBus.RaiseLocalEvent(projectile, spellEvent);
            Assert.That(handsSystem.IsHolding(player, heldItems[0]), Is.True);
            Assert.That(handsSystem.IsHolding(player, foliant), Is.True);

            // An item nested inside carried storage is already safely carried and must not move.
            var carriedStorage = inventorySystem.GetHandOrInventoryEntities(player)
                .First(carried => entMan.HasComponent<StorageComponent>(carried));
            var nestedContainer = entMan.SpawnEntity("TestSummonFoliantNestedContainer", map.GridCoords);
            Assert.That(storageSystem.Insert(carriedStorage, nestedContainer, out _, playSound: false), Is.True);
            Assert.That(containerSystem.TryGetContainer(nestedContainer, "nested", out var nested), Is.True);
            var nestedItem = entMan.SpawnEntity("Crowbar", map.GridCoords);
            Assert.That(containerSystem.Insert(nestedItem, nested), Is.True);

            Assert.That(placementSystem.TryPlaceInFreeSlot(player, nestedItem), Is.True);
            Assert.That(containerSystem.TryGetContainingContainer(nestedItem, out var unchangedNested), Is.True);
            Assert.That(unchangedNested.Owner, Is.EqualTo(nestedContainer));

            // A non-item cannot be picked up or inserted into carried storage. Failure must leave it untouched.
            Assert.That(handsSystem.TryDrop(player, foliant), Is.True);
            var rejectedContainerOwner = entMan.SpawnEntity("TestSummonFoliantPouch", map.GridCoords);
            var rejectedItem = entMan.SpawnEntity("TestSummonFoliantPouch", map.GridCoords);
            Assert.That(containerSystem.TryGetContainer(rejectedContainerOwner, "pouch", out var rejectedContainer), Is.True);
            Assert.That(containerSystem.Insert(rejectedItem, rejectedContainer), Is.True);

            Assert.That(placementSystem.TryPlaceInFreeSlot(player, rejectedItem), Is.False);
            Assert.That(rejectedContainer.Contains(rejectedItem), Is.True);
            Assert.That(entMan.EntityExists(rejectedItem), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SummonThroughActionKeepsWizardItemsInPlace()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false
        });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        var entMan = server.ResolveDependency<IEntityManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var actionsSystem = server.System<SharedActionsSystem>();
        var handsSystem = server.System<SharedHandsSystem>();
        var inventorySystem = server.System<InventorySystem>();
        var storageSystem = server.System<SharedStorageSystem>();
        var containerSystem = server.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var player = playerMan.Sessions.First().AttachedEntity!.Value;
            var hands = entMan.GetComponent<HandsComponent>(player);

            foreach (var held in handsSystem.EnumerateHeld((player, hands)).ToList())
                Assert.That(handsSystem.TryDrop(player, held), Is.True);

            Assert.That(handsSystem.CountFreeHands((player, hands)), Is.EqualTo(hands.Count));

            var key = entMan.SpawnEntity("MedievalKeyWizard", map.GridCoords);
            Assert.That(handsSystem.TryPickupAnyHand(player, key), Is.True);
            Assert.That(handsSystem.CountFreeHands((player, hands)), Is.EqualTo(hands.Count - 1));

            var carriedStorage = inventorySystem.GetHandOrInventoryEntities(player)
                .First(entity => entMan.HasComponent<StorageComponent>(entity));
            var crystal = entMan.SpawnEntity("MedievalCommsCrystallColl", map.GridCoords);
            var foliant = entMan.SpawnEntity("MedievalSpellBookBase", map.GridCoords);
            var bind = entMan.GetComponent<BindStoreOnEquipComponent>(foliant);
#pragma warning disable RA0002
            bind.BindedEntity = player;
#pragma warning restore RA0002

            Assert.That(storageSystem.Insert(carriedStorage, crystal, out _), Is.True);
            Assert.That(storageSystem.Insert(carriedStorage, foliant, out _), Is.True);
            Assert.That(containerSystem.TryGetContainingContainer(foliant, out var originalContainer), Is.True);

            var actionUid = actionsSystem.AddAction(player, "MedievalActionSummonFoliantBeginner");
            Assert.That(actionUid, Is.Not.Null);
            var action = actionsSystem.GetAction(actionUid);
            Assert.That(action, Is.Not.Null);

            actionsSystem.PerformAction(player, action.Value);

            Assert.That(action.Value.Comp.Cooldown, Is.Not.Null);
            Assert.That(handsSystem.IsHolding(player, key), Is.True);
            Assert.That(containerSystem.TryGetContainingContainer(crystal, out var crystalContainer), Is.True);
            Assert.That(containerSystem.TryGetContainingContainer(foliant, out var resultingContainer), Is.True);
            Assert.That(crystalContainer.Owner, Is.EqualTo(carriedStorage));
            Assert.That(resultingContainer.Owner, Is.EqualTo(originalContainer.Owner));
        });

        await pair.CleanReturnAsync();
    }
}
