using System;
using System.Linq;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Checks exact ground ownership and atomic consumable decisions without physics or view-owned effects.</summary>
    public sealed class GroundPickupTests
    {
        /// <summary>Every choice resolves once while exact ownership and the pre-effect cost tier remain authoritative.</summary>
        [TestCase(GroundPickupChoice.UseNow)]
        [TestCase(GroundPickupChoice.Store)]
        [TestCase(GroundPickupChoice.Leave)]
        public void MoveAndDecisionAreOneTurnAndRetainExactInstanceOrTransferIt(GroundPickupChoice choice)
        {
            Create(out BoardState board, out RunState run);
            var controller = new TurnController(board, run, new EntityId(1));
            var move = new MoveCommand(Direction.East);
            Assert.That(((RejectedTurnResult)controller.Resolve(move)).RejectionCode, Is.EqualTo(CommandRejectionCode.PickupRequired));
            GroundPickupOption option = new GroundPickupQuery().GetOption(board, run, new EntityId(1), move);
            var command = option.CreateCommand(choice, choice == GroundPickupChoice.Store
                ? new InventoryLayoutEntry(option.Item.InstanceId, InventoryLayoutEntry.MainGrid) : null);
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
            Assert.That(run.Satiety, Is.EqualTo(choice == GroundPickupChoice.UseNow ? 149 : 99));
            Assert.That(run.InventoryRevision, Is.EqualTo(choice == GroundPickupChoice.Store ? 1 : 0));
            Assert.That(board.TryGetGroundItem(new EntityId(3), out GroundItemState remaining), Is.EqualTo(choice == GroundPickupChoice.Leave));
            if (choice == GroundPickupChoice.Store)
                Assert.That(run.Inventory.Placements.Single().Item.InstanceId, Is.EqualTo(option.Item.InstanceId));
            if (choice == GroundPickupChoice.Leave) Assert.That(remaining, Is.SameAs(option.Item));
            Assert.That(controller.Resolve(command).Accepted, Is.False);
            Assert.That(run.Satiety, Is.EqualTo(choice == GroundPickupChoice.UseNow ? 149 : 99));
            Assert.That(new GroundPickupQuery().GetOption(board, run, new EntityId(1), new WaitCommand()), Is.Null);
        }

        private static void Create(out BoardState board, out RunState run)
        {
            var content = new InventoryContent(new ItemCatalog(new[] { new ItemDefinition("food", 1, 1, "satiety", "consumable", true, "eat") }),
                new ConsumableEffectCatalog(new[] { new ConsumableEffectDefinition("eat", ConsumableResource.Satiety, 50) }));
            run = new RunState("pickup", 105, new RunStateConfiguration(100, 100, 0, "start", inventoryContent: content));
            board = new BoardState();
            for (int column = 0; column < 4; column++)
                Assert.That(board.TryAdd(new BoardEntityState(new EntityId(10 + column), new BoardEntityDefinition(BoardLayer.Terrain,
                    new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false)), new GridPosition(column, 1))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(1), new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Player, "player", BoardEntityTraits.Default), new GridPosition(1, 1))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(3), new BoardEntityDefinition(BoardLayer.Item,
                EntityKind.Item, "food", BoardEntityTraits.Default), new GridPosition(2, 1))), Is.True);
            Assert.That(board.TryBindGroundItem(new GroundItemState(new EntityId(3), "pickup.board.item.3", "food")), Is.True);
        }

        internal static void BindConsumables(BoardState board, RunState run, BoardEntityDefinition extra = null)
        {
            var definitions = board.GetEntities().Select(entity => entity.Definition).Where(definition =>
                definition.AutomaticFoodReward > 0 || definition.ContentId == FirstAidInteractionAction.ContentId).ToList();
            if (extra != null) definitions.Add(extra);
            definitions = definitions.GroupBy(definition => definition.ContentId).Select(group => group.First()).ToList();
            var content = new InventoryContent(new ItemCatalog(definitions.Select(definition =>
                new ItemDefinition(definition.ContentId, 1, 1, "consumable", "consumable", true, "effect." + definition.ContentId))),
                new ConsumableEffectCatalog(definitions.Select(definition => new ConsumableEffectDefinition("effect." + definition.ContentId,
                    definition.ContentId == FirstAidInteractionAction.ContentId ? ConsumableResource.Health : ConsumableResource.Satiety,
                    definition.ContentId == FirstAidInteractionAction.ContentId ? FirstAidInteractionAction.HealAmount : definition.AutomaticFoodReward))));
            run.RestoreInventoryState(content, run.InventoryRevision, run.Inventory.Placements);
            foreach (BoardEntityState entity in board.GetEntities())
                if (definitions.Any(definition => definition.ContentId == entity.Definition.ContentId) && !board.TryGetGroundItem(entity.Id, out _))
                    Assert.That(board.TryBindGroundItem(new GroundItemState(entity.Id, GroundItemIdentity.Create(run, entity.Id), entity.Definition.ContentId)), Is.True);
        }

        internal static GroundPickupCommand ChooseUse(BoardState board, RunState run, EntityId playerId, PlayerCommand primary,
            GroundPickupQuery query = null, BoardEntityDefinition extra = null)
        {
            BindConsumables(board, run, extra);
            return (query ?? new GroundPickupQuery()).GetOption(board, run, playerId, primary).CreateCommand(GroundPickupChoice.UseNow);
        }

        /// <summary>Excavation creates exactly the predicted instance inside the same final turn and cannot bypass the decision.</summary>
        [TestCase(false, GroundPickupChoice.UseNow)]
        [TestCase(false, GroundPickupChoice.Store)]
        [TestCase(false, GroundPickupChoice.Leave)]
        [TestCase(true, GroundPickupChoice.UseNow)]
        [TestCase(true, GroundPickupChoice.Store)]
        [TestCase(true, GroundPickupChoice.Leave)]
        public void CacheFinalStepUsesTheSameAtomicDecision(bool shovel, GroundPickupChoice choice)
        {
            Create(out BoardState board, out RunState run);
            board.TryRemove(new EntityId(3));
            var marker = new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "cache", new BoardEntityTraits(false, false, true));
            board.TryAdd(new BoardEntityState(new EntityId(3), marker, new GridPosition(2, 1)));
            var rules = new ShovelRules("cache", "diggable", 3, new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item,
                "food", BoardEntityTraits.Default, 50), 4, 2, 2);
            var interactions = new InteractionQuery(new[] { new CacheDigAction(rules, false) });
            var tools = new ToolUseQuery(new[] { new ToolAction("shovel.basic", new CacheDigAction(rules, true), "Unearth", 2, 2) });
            if (shovel) run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3)));
            var query = new GroundPickupQuery(interactions, tools);
            var controller = new TurnController(board, run, new EntityId(1), new TurnResolver(interactions: interactions, tools: tools));
            PlayerCommand primary = shovel ? (PlayerCommand)tools.GetOptions(board, run, new EntityId(1)).Single().CreateCommand()
                : interactions.GetOptions(board, run, new EntityId(1)).Single().CreateCommand();
            if (!shovel)
                for (int step = 0; step < 2; step++)
                {
                    Assert.That(query.GetOption(board, run, new EntityId(1), primary), Is.Null);
                    Assert.That(controller.Resolve(primary).Accepted, Is.True);
                }
            GroundPickupOption option = query.GetOption(board, run, new EntityId(1), primary);
            Assert.That(board.HasRevealedCache(new EntityId(3)), Is.False);
            int before = run.Satiety;
            Assert.That(controller.Resolve(primary).Accepted, Is.False);
            int workBefore = board.GetCacheWork(new EntityId(3));
            Assert.That(controller.Resolve(option.CreateCommand(GroundPickupChoice.Store,
                new InventoryLayoutEntry(option.Item.InstanceId, InventoryLayoutEntry.MainGrid, 99, 99))).Accepted, Is.False);
            Assert.That(board.HasRevealedCache(new EntityId(3)), Is.False);
            Assert.That(board.GetCacheWork(new EntityId(3)), Is.EqualTo(workBefore));
            Assert.That(run.Satiety, Is.EqualTo(before));
            if (shovel) Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(3));
            var command = option.CreateCommand(choice, choice == GroundPickupChoice.Store
                ? new InventoryLayoutEntry(option.Item.InstanceId, InventoryLayoutEntry.LeftPocket) : null);
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Accepted, Is.True);
            CacheRewardItem reward = result.Events.OfType<CacheWorkedEvent>().Single().Reward;
            Assert.That(reward.InstanceId, Is.EqualTo(option.Item.InstanceId));
            Assert.That(reward.DefinitionId, Is.EqualTo(option.Item.DefinitionId));
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
            Assert.That(run.Satiety, Is.EqualTo(before - 1 + (choice == GroundPickupChoice.UseNow ? 50 : 0)));
            Assert.That(board.TryGetGroundItem(reward.Entity.Id, out GroundItemState remaining), Is.EqualTo(choice == GroundPickupChoice.Leave));
            if (choice == GroundPickupChoice.Leave) Assert.That(remaining.InstanceId, Is.EqualTo(reward.InstanceId));
            if (choice == GroundPickupChoice.Store) Assert.That(run.Inventory.Placements.Single().Item.InstanceId, Is.EqualTo(reward.InstanceId));
            if (shovel) Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            Assert.That(controller.Resolve(command).Accepted, Is.False);
            Assert.That(run.Satiety, Is.EqualTo(before - 1 + (choice == GroundPickupChoice.UseNow ? 50 : 0)));
        }

        /// <summary>A binding is stable, exclusive, and removed together with its board entity.</summary>
        [Test]
        public void GroundBindingRetainsExactInstanceAndRemovalClearsOwnershipTogether()
        {
            var board = new BoardState();
            var entityId = new EntityId(3);
            var definition = new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "food", BoardEntityTraits.Default);
            Assert.That(board.TryAdd(new BoardEntityState(entityId, definition, new GridPosition(1, 1))), Is.True);
            var item = new GroundItemState(entityId, "run.board.item.3", "food");
            Assert.That(board.TryBindGroundItem(item), Is.True);
            Assert.That(board.TryGetGroundItem(entityId, out GroundItemState retained), Is.True);
            Assert.That(retained, Is.SameAs(item));
            Assert.That(board.TryBindGroundItem(new GroundItemState(entityId, "replacement", "food")), Is.False);
            var otherId = new EntityId(4);
            Assert.That(board.TryAdd(new BoardEntityState(otherId, definition, new GridPosition(2, 1))), Is.True);
            Assert.That(board.TryBindGroundItem(new GroundItemState(otherId, item.InstanceId, "food")), Is.False);
            Assert.That(board.TryRemove(entityId), Is.True);
            Assert.That(board.TryGetGroundItem(entityId, out _), Is.False);
            Assert.That(board.TryGetEntity(entityId, out _), Is.False);
            Assert.That(board.TryBindGroundItem(item), Is.False);
            Assert.That(retained.InstanceId, Is.EqualTo("run.board.item.3"));
        }

        /// <summary>Every malformed/stale proposal is free and cannot move, restore, transfer, age a revision or start later phases.</summary>
        [TestCase("instance")]
        [TestCase("definition")]
        [TestCase("entity")]
        [TestCase("position")]
        [TestCase("player")]
        [TestCase("revision")]
        [TestCase("pickup-revision")]
        [TestCase("null-primary")]
        [TestCase("nested")]
        [TestCase("wait")]
        [TestCase("choice")]
        [TestCase("placement")]
        [TestCase("placement-instance")]
        [TestCase("container")]
        [TestCase("bounds")]
        [TestCase("pocket-anchor")]
        [TestCase("blocked")]
        [TestCase("extra-placement")]
        public void InvalidCompositeDoesNotMutateEitherOwner(string mutation)
        {
            Create(out BoardState board, out RunState run);
            GroundPickupOption option = new GroundPickupQuery().GetOption(board, run, new EntityId(1), new MoveCommand(Direction.East));
            var entry = new InventoryLayoutEntry(option.Item.InstanceId, InventoryLayoutEntry.MainGrid);
            if (mutation == "blocked") board.TryAdd(new BoardEntityState(new EntityId(6),
                new BoardEntityDefinition(BoardLayer.Obstacle, new EntityKind("wall"), "wall"), option.Position));
            var command = new GroundPickupCommand(mutation == "null-primary" ? null : mutation == "wait" ? new WaitCommand() :
                mutation == "nested" ? option.CreateCommand(GroundPickupChoice.Leave) : option.Primary,
                mutation == "player" ? new GridPosition(0, 1) : option.PlayerPosition,
                mutation == "revision" ? 1 : option.Revision,
                mutation == "entity" ? new EntityId(99) : option.Item.EntityId,
                mutation == "instance" ? "forged" : option.Item.InstanceId,
                mutation == "definition" ? "forged" : option.Item.DefinitionId,
                mutation == "position" ? new GridPosition(0, 1) : option.Position,
                mutation == "choice" ? (GroundPickupChoice)999 : mutation == "extra-placement" ? GroundPickupChoice.UseNow : GroundPickupChoice.Store,
                mutation == "placement" ? null : mutation == "placement-instance" ? new InventoryLayoutEntry("forged", InventoryLayoutEntry.MainGrid) :
                mutation == "container" ? new InventoryLayoutEntry(option.Item.InstanceId, "tools") :
                mutation == "bounds" ? new InventoryLayoutEntry(option.Item.InstanceId, InventoryLayoutEntry.MainGrid, 3, 0) :
                mutation == "pocket-anchor" ? new InventoryLayoutEntry(option.Item.InstanceId, InventoryLayoutEntry.LeftPocket, 1, 0) : entry,
                mutation == "pickup-revision" ? 1 : 0);
            InventoryState inventory = run.Inventory;
            int phases = 0;
            var handlers = new TurnPhaseHandlers((state, activeRun, playerId, events) => phases++);
            var result = new TurnController(board, run, new EntityId(1), phases: handlers).Resolve(command);
            Assert.That(result.Accepted, Is.False, mutation);
            Assert.That(run.Inventory, Is.SameAs(inventory));
            Assert.That(run.InventoryRevision, Is.Zero);
            Assert.That(board.PickupRevision, Is.Zero);
            Assert.That(board.TryGetGroundItem(option.Item.EntityId, out GroundItemState retained), Is.True);
            Assert.That(retained, Is.SameAs(option.Item));
            Assert.That(board.TryGetEntity(new EntityId(1), out BoardEntityState player), Is.True);
            Assert.That(player.Position, Is.EqualTo(option.PlayerPosition));
            Assert.That(run.Satiety, Is.EqualTo(100));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(phases, Is.Zero);
        }

        /// <summary>Capacity is independent of Use, cap waste is explicit, and no footprint rotates to fit fragmentation.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FullOrFragmentedBagDisablesStoreButNotCappedUse(bool fragmented)
        {
            Create(out BoardState board, out RunState run);
            var content = new InventoryContent(new ItemCatalog(new[]
            {
                new ItemDefinition("food", fragmented ? 2 : 1, fragmented ? 2 : 1, "food", "consumable", !fragmented, "eat"),
                new ItemDefinition("filler", 1, 1, "food", "consumable", true, "eat")
            }), run.InventoryContent.Effects);
            var placements = new List<InventoryPlacement>();
            for (int column = 0; column < 3; column++)
                for (int row = 0; row < 3; row++)
                    if (!fragmented || (column + row) % 2 == 0)
                        placements.Add(new InventoryPlacement(new ItemInstanceState("filler." + column + "." + row, "filler"), InventoryContainer.MainGrid, column, row));
            placements.Add(new InventoryPlacement(new ItemInstanceState("left", "filler"), InventoryContainer.LeftPocket));
            placements.Add(new InventoryPlacement(new ItemInstanceState("right", "filler"), InventoryContainer.RightPocket));
            run.RestoreInventoryState(content, 0, placements);
            run.RestoreSatiety(100);
            GroundPickupOption option = new GroundPickupQuery().GetOption(board, run, new EntityId(1), new MoveCommand(Direction.East));
            Assert.That(option.CanStore, Is.False);
            Assert.That(option.StoreUnavailableReason, Is.EqualTo("No legal placement"));
            Assert.That(option.UseAvailable, Is.True);
            var resolver = new TurnResolver();
            Assert.That(resolver.Resolve(board, run, new EntityId(1), option.CreateCommand(GroundPickupChoice.Store,
                new InventoryLayoutEntry(option.Item.InstanceId, InventoryLayoutEntry.MainGrid))).Accepted, Is.False);
            TurnResult use = resolver.Resolve(board, run, new EntityId(1), option.CreateCommand(GroundPickupChoice.UseNow));
            Assert.That(use.Accepted, Is.True);
            var effect = use.Events.OfType<GroundPickupResolvedEvent>().Single();
            Assert.That(effect.Applied, Is.Zero);
            Assert.That(effect.Wasted, Is.EqualTo(50));
            Assert.That(run.Satiety, Is.EqualTo(198));
            Assert.That(run.Inventory.Placements, Is.EqualTo(placements));
        }

        /// <summary>Old Leave cannot replay after returning to the same source cell; a fresh entry preview can finish normally.</summary>
        [Test]
        public void LeaveReentryRequiresFreshDecisionAndAgesExactlyOneHostileOpportunity()
        {
            Create(out BoardState board, out RunState run);
            int phases = 0;
            var controller = new TurnController(board, run, new EntityId(1), phases: new TurnPhaseHandlers(
                (state, activeRun, playerId, events) => phases++));
            var query = new GroundPickupQuery();
            GroundPickupOption option = query.GetOption(board, run, new EntityId(1), new MoveCommand(Direction.East));
            GroundPickupCommand old = option.CreateCommand(GroundPickupChoice.Leave);
            Assert.That(controller.Resolve(old).Accepted, Is.True);
            Assert.That(phases, Is.EqualTo(1));
            Assert.That(query.GetOption(board, run, new EntityId(1), new WaitCommand()), Is.Null);
            Assert.That(controller.Resolve(new MoveCommand(Direction.West)).Accepted, Is.True);
            Assert.That(controller.Resolve(old).Accepted, Is.False);
            GroundPickupOption fresh = query.GetOption(board, run, new EntityId(1), new MoveCommand(Direction.East));
            Assert.That(fresh.Item, Is.SameAs(option.Item));
            Assert.That(controller.Resolve(fresh.CreateCommand(GroundPickupChoice.Leave)).Accepted, Is.True);
            Assert.That(phases, Is.EqualTo(3));
            Assert.That(board.PickupRevision, Is.EqualTo(2));
        }

        /// <summary>Missing trusted definitions/effects cannot consume or store the item; Leave is still a valid costly movement.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MissingTrustedContentDisablesUseAndStoreWithoutLosingTheItem(bool missingEffect)
        {
            Create(out BoardState board, out RunState run);
            var content = missingEffect ? new InventoryContent(run.InventoryContent.Items,
                new ConsumableEffectCatalog(Array.Empty<ConsumableEffectDefinition>())) : InventoryContent.Empty;
            run.RestoreInventoryState(content, 0, Array.Empty<InventoryPlacement>());
            GroundPickupOption option = new GroundPickupQuery().GetOption(board, run, new EntityId(1), new MoveCommand(Direction.East));
            Assert.That(option.UseAvailable, Is.False);
            Assert.That(option.CanStore, Is.False);
            var controller = new TurnController(board, run, new EntityId(1));
            Assert.That(controller.Resolve(option.CreateCommand(GroundPickupChoice.UseNow)).Accepted, Is.False);
            Assert.That(controller.Resolve(option.CreateCommand(GroundPickupChoice.Store,
                new InventoryLayoutEntry(option.Item.InstanceId, InventoryLayoutEntry.MainGrid))).Accepted, Is.False);
            Assert.That(board.PickupRevision, Is.Zero);
            Assert.That(run.Satiety, Is.EqualTo(100));
            Assert.That(board.TryGetGroundItem(option.Item.EntityId, out GroundItemState same), Is.True);
            Assert.That(same, Is.SameAs(option.Item));
            Assert.That(controller.Resolve(option.CreateCommand(GroundPickupChoice.Leave)).Accepted, Is.True);
            Assert.That(run.Satiety, Is.EqualTo(99));
            Assert.That(board.TryGetGroundItem(option.Item.EntityId, out same), Is.True);
            Assert.That(same, Is.SameAs(option.Item));
        }

        /// <summary>Leaving medicine or an interactive revealed-ration definition cannot be followed by a free stationary pickup retry.</summary>
        [TestCase("legacy.aid", "use-first-aid")]
        [TestCase("shovel.cache-ration", "collect")]
        public void LeaveCannotBeRetriedThroughExistingItemInteraction(string definitionId, string actionId)
        {
            Create(out BoardState board, out RunState run);
            board.TryRemove(new EntityId(3));
            var content = new InventoryContent(new ItemCatalog(new[]
                { new ItemDefinition(definitionId, 1, 1, "consumable", "consumable", true, "effect") }),
                new ConsumableEffectCatalog(new[] { new ConsumableEffectDefinition("effect", ConsumableResource.Health, 10) }));
            run.RestoreInventoryState(content, 0, Array.Empty<InventoryPlacement>());
            var item = new GroundItemState(new EntityId(3), "left-consumable", definitionId);
            board.TryAdd(new BoardEntityState(item.EntityId, new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item,
                definitionId, new BoardEntityTraits(false, false, true), actionId == "collect" ? 10 : 0), new GridPosition(2, 1)));
            Assert.That(board.TryBindGroundItem(item), Is.True);
            var controller = new TurnController(board, run, new EntityId(1));
            var query = new GroundPickupQuery();
            GroundPickupOption entry = query.GetOption(board, run, new EntityId(1), new MoveCommand(Direction.East));
            Assert.That(controller.Resolve(entry.CreateCommand(GroundPickupChoice.Leave)).Accepted, Is.True);
            var interact = new InteractCommand(item.EntityId, entry.Position, actionId);
            Assert.That(new InteractionQuery().GetAvailability(board, run, new EntityId(1), interact), Is.EqualTo(InteractionAvailability.DecisionRequired));
            Assert.That(query.GetOption(board, run, new EntityId(1), interact), Is.Null);
            Assert.That(controller.Resolve(interact).Accepted, Is.False);
            foreach (GroundPickupChoice choice in new[] { GroundPickupChoice.UseNow, GroundPickupChoice.Store, GroundPickupChoice.Leave })
                Assert.That(controller.Resolve(new GroundPickupCommand(interact, entry.Position, run.InventoryRevision,
                    item.EntityId, item.InstanceId, item.DefinitionId, entry.Position, choice,
                    choice == GroundPickupChoice.Store ? new InventoryLayoutEntry(item.InstanceId, InventoryLayoutEntry.MainGrid) : null,
                    board.PickupRevision)).Accepted, Is.False);
            Assert.That(run.Satiety, Is.EqualTo(99));
            Assert.That(run.Inventory.Placements, Is.Empty);
            Assert.That(board.TryGetGroundItem(item.EntityId, out GroundItemState retained), Is.True);
            Assert.That(retained, Is.SameAs(item));
            Assert.That(controller.Resolve(new MoveCommand(Direction.West)).Accepted, Is.True);
            GroundPickupOption fresh = query.GetOption(board, run, new EntityId(1), new MoveCommand(Direction.East));
            Assert.That(fresh.Item, Is.SameAs(item));
            Assert.That(controller.Resolve(fresh.CreateCommand(GroundPickupChoice.Store,
                new InventoryLayoutEntry(item.InstanceId, InventoryLayoutEntry.MainGrid))).Accepted, Is.True);
            Assert.That(run.Inventory.Placements.Single().Item.InstanceId, Is.EqualTo(item.InstanceId));
        }
    }
}