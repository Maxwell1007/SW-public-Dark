using System.Collections.Generic;
using System.Linq;
using Content.Server.Actions;
using Content.Server.Imperial.ImperialStore;
using Content.Server.Imperial.Medieval.Magic.BindStoreOnEquip;
using Content.Server.Imperial.Medieval.Magic.Memory;
using Content.Server.Imperial.Medieval.Skills;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Imperial.ImperialStore;
using Content.Shared.Imperial.Medieval.Magic.Memory;
using Content.Shared.Imperial.Medieval.Skills;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Imperial.Medieval;

[TestFixture]
public sealed class MagicMemoryTest
{
    private static readonly EntProtoId Fireball = "MedievalActionFireballBeginner";
    private static readonly EntProtoId Arrow = "MedievalActionMagicArrowBeginner";
    private static readonly EntProtoId Hypnosis = "MedievalAncientNocturneHypnosisAction";

    [Test]
    public async Task PlayerReceivesMemoryAndIntelligenceBonus()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var players = server.ResolveDependency<IPlayerManager>();

        await server.WaitAssertion(() =>
        {
            var player = entities.SpawnEntity(null, map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), player);
            var memory = entities.GetComponent<MagicMemoryComponent>(player);
            Assert.That(memory.MaxMemory, Is.Zero);
            Assert.That(memory.CurrentMemory, Is.Zero);

            var skills = entities.AddComponent<SkillsComponent>(player);
            var system = server.System<MagicMemorySystem>();
            skills.Levels[SharedSkillsSystem.IntelligenceId] = 19;
            system.Refresh(player);
            Assert.That(memory.MaxMemory, Is.EqualTo(19));
            skills.Levels[SharedSkillsSystem.IntelligenceId] = 20;
            var changed = new SkillLevelChangedEvent(SharedSkillsSystem.IntelligenceId, 20, 19);
            entities.EventBus.RaiseLocalEvent(player, ref changed);
            Assert.That(memory.MaxMemory, Is.EqualTo(24));
            players.SetAttachedEntity(players.Sessions.Single(), null);
            entities.DeleteEntity(player);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ForgetRememberAndRegrantPreserveMemoryLimit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var player = entities.CreateEntityUninitialized(null, map.GridCoords);
            entities.AddComponent<SkillsComponent>(player).Levels[SharedSkillsSystem.IntelligenceId] = 1;
            entities.InitializeAndStartEntity(player, map.MapId);
            var memory = entities.GetComponent<MagicMemoryComponent>(player);
            var system = server.System<MagicMemorySystem>();
            var actions = server.System<ActionsSystem>();
            var containers = server.System<ActionContainerSystem>();
            var fireball = actions.AddAction(player, Fireball)!.Value;
            Assert.That(memory.CurrentMemory, Is.EqualTo(1));
            Assert.That(system.TryForget(player, fireball), Is.True);
            Assert.That(system.TryForget(player, fireball), Is.False);
            Assert.That(memory.CurrentMemory, Is.Zero);
            Assert.That(actions.GetActions(player).Any(a => a.Owner == fireball), Is.False);

            actions.GrantContainedActions(player, player);
            Assert.That(actions.GetActions(player).Any(a => a.Owner == fireball), Is.False);
            Assert.That(memory.CurrentMemory, Is.Zero);

            var arrow = actions.AddAction(player, Arrow)!.Value;
            Assert.That(system.TryRemember(player, fireball), Is.False);
            Assert.That(memory.CurrentMemory, Is.EqualTo(1));
            Assert.That(system.TryForget(player, arrow), Is.True);
            Assert.That(system.TryRemember(player, fireball), Is.True);
            Assert.That(system.TryRemember(player, fireball), Is.False);
            var action = entities.GetComponent<ActionComponent>(fireball);
            Assert.That(action.Cooldown!.Value.End - action.Cooldown.Value.Start, Is.EqualTo(action.UseDelay!.Value * 2));
            Assert.That(action.UseDelay, Is.EqualTo(TimeSpan.FromSeconds(180)));
            Assert.That(memory.CurrentMemory, Is.EqualTo(1));

            var stranger = entities.SpawnEntity(null, map.GridCoords);
            Assert.That(system.TryForget(stranger, fireball), Is.False);
            var npcSpell = actions.AddAction(stranger, Fireball)!.Value;
            Assert.That(entities.HasComponent<MagicMemoryComponent>(stranger), Is.False);
            Assert.That(entities.GetComponent<ActionComponent>(npcSpell).AttachedEntity, Is.EqualTo(stranger));
            entities.DeleteEntity(npcSpell);
            system.Refresh(stranger);
            var nocturne = actions.AddAction(stranger, Hypnosis)!.Value;
            Assert.That(entities.GetComponent<MagicMemoryComponent>(stranger).CurrentMemory, Is.Zero);
            Assert.That(entities.GetComponent<ActionComponent>(nocturne).AttachedEntity, Is.EqualTo(stranger));

            containers.RemoveAction(fireball);
            entities.DeleteEntity(fireball);
            Assert.That(memory.CurrentMemory, Is.Zero);
            entities.DeleteEntity(player);
            entities.DeleteEntity(stranger);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GrimoireRejectsOverflowAndUpgradesOnlyCurrentLevel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var player = entities.CreateEntityUninitialized(null, map.GridCoords);
            var skills = entities.AddComponent<SkillsComponent>(player);
            skills.Levels[SharedSkillsSystem.IntelligenceId] = 1;
            entities.InitializeAndStartEntity(player, map.MapId);
            var memory = entities.GetComponent<MagicMemoryComponent>(player);
            var book = entities.SpawnEntity("MedievalSpellBookBase", map.GridCoords);
            var binding = server.System<BindStoreOnEquipSystem>();
            Assert.That(binding.TryBindGrimoire(book, player), Is.True);
            var store = entities.GetComponent<ImperialStoreComponent>(book);
            var stores = server.System<ImperialStoreSystem>();
            stores.TryAddCurrency(new Dictionary<string, FixedPoint2> { ["MagicMedievalFire"] = 1000 }, book);
            var beginner = store.Listings.Single(l => l.ID == "MedievalSpellFireballBeginner");
            var middle = store.Listings.Single(l => l.ID == "MedievalSpellFireballMiddle");
            var senior = store.Listings.Single(l => l.ID == "MedievalSpellFireballSenior");

            void Buy(ImperialListingData listing)
            {
                entities.EventBus.RaiseLocalEvent(book, new ImperialStoreBuyListingMessage(listing) { Actor = player });
            }

            void Toggle(ImperialListingData listing)
            {
                entities.EventBus.RaiseLocalEvent(book, new ImperialStoreToggleSpellMemoryMessage(listing.ID) { Actor = player });
            }

            Buy(beginner);
            Assert.That(memory.CurrentMemory, Is.EqualTo(1));
            Assert.That(beginner.CanManageMemory, Is.True);
            var balance = store.Balance["MagicMedievalFire"];
            Buy(middle);
            Assert.That(middle.PurchaseAmount, Is.Zero);
            Assert.That(store.Balance["MagicMedievalFire"], Is.EqualTo(balance));

            skills.Levels[SharedSkillsSystem.IntelligenceId] = 2;
            Buy(middle);
            Assert.That(memory.CurrentMemory, Is.EqualTo(2));
            Assert.That(beginner.PurchasedActionEntity, Is.Null);
            Assert.That(middle.CanManageMemory, Is.True);
            Assert.That(store.LastAvailableListings.Any(l => l.ID == middle.ID), Is.True);
            Toggle(middle);
            Assert.That(memory.CurrentMemory, Is.Zero);
            Assert.That(middle.Forgotten, Is.True);
            Assert.That(middle.PurchaseAmount, Is.EqualTo(1));

            balance = store.Balance["MagicMedievalFire"];
            Buy(senior);
            Assert.That(senior.PurchaseAmount, Is.Zero);
            Assert.That(store.Balance["MagicMedievalFire"], Is.EqualTo(balance));
            skills.Levels[SharedSkillsSystem.IntelligenceId] = 3;
            Buy(senior);
            Assert.That(memory.CurrentMemory, Is.EqualTo(3));
            Assert.That(middle.PurchasedActionEntity, Is.Null);
            Assert.That(senior.CanManageMemory, Is.True);
            Toggle(senior);
            Assert.That(memory.CurrentMemory, Is.Zero);

            entities.DeleteEntity(book);
            book = entities.SpawnEntity("MedievalSpellBookBase", map.GridCoords);
            Assert.That(binding.TryRestoreGrimoire(player, book, entities.GetComponent<GrimoireOwnerComponent>(player)), Is.True);
            store = entities.GetComponent<ImperialStoreComponent>(book);
            senior = store.Listings.Single(l => l.ID == "MedievalSpellFireballSenior");
            Toggle(senior);
            Assert.That(memory.CurrentMemory, Is.EqualTo(3));
            Assert.That(senior.Forgotten, Is.False);
            var action = entities.GetComponent<ActionComponent>(senior.PurchasedActionEntity!.Value);
            Assert.That(action.Cooldown!.Value.End - action.Cooldown.Value.Start, Is.EqualTo(action.UseDelay!.Value * 2));

            entities.DeleteEntity(senior.PurchasedActionEntity.Value);
            entities.DeleteEntity(book);
            entities.DeleteEntity(player);
        });

        await pair.CleanReturnAsync();
    }
}
