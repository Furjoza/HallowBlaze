using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Validates one-pocket composite turns through the authoritative resolver and controller.</summary>
    public sealed class QuickPocketTests
    {
        /// <summary>A caller cannot alter the submitted choice count or identity by mutating its original collection.</summary>
        [Test]
        public void PayloadOwnsDetachedSelectionsWithoutHidingMalformedCounts()
        {
            var item = new QuickItemUse(InventoryLayoutEntry.LeftPocket, "berry.1", "berry");
            var items = new List<QuickItemUse> { item };
            var primary = new WaitCommand();
            var command = new QuickPocketCommand(primary, new GridPosition(1, 1), 7, items);
            items.Add(new QuickItemUse(InventoryLayoutEntry.RightPocket, "berry.2", "berry"));
            Assert.That(command.Items, Has.Count.EqualTo(1));
            Assert.That(command.Items[0], Is.SameAs(item));
            Assert.That(command.Primary, Is.SameAs(primary));
            Assert.That(command.ExpectedRevision, Is.EqualTo(7));
            Assert.That(command.PlayerPosition, Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(new QuickPocketCommand(primary, command.PlayerPosition, 7, items).Items, Has.Count.EqualTo(2));
        }

        /// <summary>Both pockets accompany either Wait or Move with one cost/revision/hostile opportunity and ordered effects.</summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void OnePocketAndPrimaryCommitOnce(bool right, bool move)
        {
            BoardState board = NewBoard();
            RunState run = NewRun();
            int phases = 0;
            var handlers = new TurnPhaseHandlers((state, current, actor, events) => phases++,
                (state, current, actor, events) => phases++);
            var controller = new TurnController(board, run, PlayerId, phases: handlers);
            string pocket = right ? InventoryLayoutEntry.RightPocket : InventoryLayoutEntry.LeftPocket;
            QuickPocketOption option = new QuickPocketQuery().GetOption(board, run, PlayerId, pocket);
            PlayerCommand primary = move ? (PlayerCommand)new MoveCommand(Direction.East) : new WaitCommand();
            QuickPocketCommand command = option.CreateCommand(primary);
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Events.Select(value => value.EventType), Is.EqualTo(new[]
                { "QuickItemUsed", "InventoryCommitted", move ? "EntityMoved" : "EntityWaited", "ActionCostApplied" }));
            QuickItemUsedEvent used = result.Events.OfType<QuickItemUsedEvent>().Single();
            Assert.That(used.Item.PocketId, Is.EqualTo(pocket));
            Assert.That(used.Item.InstanceId, Is.EqualTo(right ? "medicine" : "food"));
            Assert.That(used.EffectId, Is.EqualTo(right ? "heal" : "eat"));
            Assert.That(used.Applied, Is.EqualTo(right ? 5 : 10));
            Assert.That(used.Wasted, Is.EqualTo(right ? 5 : 0));
            Assert.That(run.Satiety, Is.EqualTo(right ? 99 : 109));
            Assert.That(run.Health, Is.EqualTo(right ? 100 : 95));
            Assert.That(run.InventoryRevision, Is.EqualTo(1));
            Assert.That(run.Inventory.Placements, Has.Count.EqualTo(1));
            Assert.That(phases, Is.EqualTo(2));
            string beforeDuplicate = PersistenceJsonSerializer.SerializeRun(run);
            Assert.That(controller.Resolve(command).Accepted, Is.False);
            Assert.That(PersistenceJsonSerializer.SerializeRun(run), Is.EqualTo(beforeDuplicate));
            Assert.That(phases, Is.EqualTo(2));
        }

        /// <summary>A blocked primary cannot consume an otherwise legal pocket effect or advance any phase.</summary>
        [Test]
        public void RejectedMovementPreservesPocketResourcesAndBoard()
        {
            BoardState board = NewBoard();
            RunState run = NewRun();
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(99), new BoardEntityDefinition(BoardLayer.Obstacle,
                new EntityKind("wall"), "wall", BoardEntityTraits.Default), new GridPosition(2, 1))), Is.True);
            QuickPocketCommand command = new QuickPocketQuery().GetOption(board, run, PlayerId, InventoryLayoutEntry.LeftPocket)
                .CreateCommand(new MoveCommand(Direction.East));
            string before = PersistenceJsonSerializer.SerializeRun(run);
            BoardEntityState[] entities = board.GetEntities().ToArray();
            int phases = 0;
            var controller = new TurnController(board, run, PlayerId,
                phases: new TurnPhaseHandlers((state, current, actor, events) => phases++));
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Accepted || result.ConsumesTurn, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(PersistenceJsonSerializer.SerializeRun(run), Is.EqualTo(before));
            Assert.That(board.GetEntities(), Is.EqualTo(entities));
            Assert.That(phases, Is.Zero);
        }

        private static IEnumerable<TestCaseData> PrimaryCases()
        {
            foreach (string primary in new[] { "Wait", "Move", "Interact", "UseTool", "PickupTool", "GroundUse", "GroundStore", "GroundLeave",
                "CacheUse", "CacheStore", "CacheLeave", "ShovelUse", "ShovelStore", "ShovelLeave" })
                foreach (bool right in new[] { false, true })
                    foreach (bool stale in new[] { false, true })
                        yield return new TestCaseData(primary, right, stale);
        }

        /// <summary>Both pockets and all supported contracts either resolve once or preserve every owner when their primary becomes stale.</summary>
        [TestCaseSource(nameof(PrimaryCases))]
        public void SupportedPrimaryMatrixPreservesAtomicity(string kind, bool right, bool stale)
        {
            BoardState board = NewBoard();
            RunState run = NewRun();
            PlayerCommand primary = Primary(kind, board, run, right, out TurnResolver resolver);
            QuickPocketCommand command = new QuickPocketQuery().GetOption(board, run, PlayerId,
                right ? InventoryLayoutEntry.RightPocket : InventoryLayoutEntry.LeftPocket).CreateCommand(primary);
            if (stale)
            {
                if (primary is GroundPickupCommand pickup) Assert.That(board.TryCommitPickupRevision(pickup.ExpectedPickupRevision), Is.True);
                else if (primary is InteractCommand || primary is PickupToolCommand) Assert.That(board.TryMove(new EntityId(99), new GridPosition(3, 1)), Is.True);
                else if (primary is UseToolCommand) Assert.That(run.TrySpendToolCharge(0, run.ToolSlots[0].Instance.InstanceId), Is.True);
                else if (primary is MoveCommand) Assert.That(board.TryAdd(new BoardEntityState(new EntityId(98),
                    new BoardEntityDefinition(BoardLayer.Obstacle, new EntityKind("wall"), "wall"), new GridPosition(2, 1))), Is.True);
                else Assert.That(run.TryCommitInventory(0, run.Inventory.Placements, out _), Is.True);
            }
            string before = PersistenceJsonSerializer.SerializeRun(run);
            InventoryState inventory = run.Inventory;
            BoardEntityState[] entities = board.GetEntities().ToArray();
            BoardNoise[] noises = board.GetActiveNoises().ToArray();
            int work = board.GetCacheWork(new EntityId(99)), revision = board.PickupRevision;
            int phases = 0;
            var controller = new TurnController(board, run, PlayerId, resolver,
                new TurnPhaseHandlers((state, current, actor, events) => phases++, (state, current, actor, events) => phases++,
                    (state, current, actor, events) => phases++));
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Accepted, Is.EqualTo(!stale));
            if (stale)
            {
                Assert.That(result.ConsumesTurn, Is.False);
                Assert.That(result.Events, Is.Empty);
                Assert.That(PersistenceJsonSerializer.SerializeRun(run), Is.EqualTo(before));
                Assert.That(run.Inventory, Is.SameAs(inventory));
                Assert.That(board.GetEntities(), Is.EqualTo(entities));
                Assert.That(board.GetActiveNoises(), Is.EqualTo(noises));
                Assert.That(board.GetCacheWork(new EntityId(99)), Is.EqualTo(work));
                Assert.That(board.PickupRevision, Is.EqualTo(revision));
                Assert.That(phases, Is.Zero);
                return;
            }
            Assert.That(result.Events.OfType<QuickItemUsedEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<InventoryCommittedEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Single().CostAmount, Is.EqualTo(1));
            Assert.That(run.InventoryRevision, Is.EqualTo(1));
            Assert.That(phases, Is.EqualTo(3));
            Assert.That(run.Inventory.TryGetPlacement(right ? "medicine" : "food", out _), Is.False);
            Assert.That(run.Satiety, Is.EqualTo(99 + (right ? 0 : 10) + (kind.EndsWith("Use") ? 10 : 0)));
            Assert.That(run.Health, Is.EqualTo(right ? 100 : 95));
            if (kind == "UseTool" || kind.StartsWith("Shovel"))
                Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            if (primary is GroundPickupCommand ground)
            {
                Assert.That(board.TryGetGroundItem(ground.EntityId, out GroundItemState retained), Is.EqualTo(ground.Choice == GroundPickupChoice.Leave));
                if (ground.Choice == GroundPickupChoice.Leave) Assert.That(retained.InstanceId, Is.EqualTo(ground.InstanceId));
                if (ground.Choice == GroundPickupChoice.Store)
                    Assert.That(run.Inventory.GetPocketItem(right ? InventoryContainer.RightPocket : InventoryContainer.LeftPocket).InstanceId,
                        Is.EqualTo(ground.InstanceId));
                Assert.That(result.Events.OfType<GroundPickupResolvedEvent>().Count(), Is.EqualTo(1));
            }
        }

        /// <summary>Only one exact current pocket instance is admitted; neither nested envelopes nor bag/unknown commands can spend either pocket.</summary>
        [TestCase("two")]
        [TestCase("same-twice")]
        [TestCase("empty-list")]
        [TestCase("null-list")]
        [TestCase("null-item")]
        [TestCase("empty-pocket")]
        [TestCase("main-grid")]
        [TestCase("ineligible")]
        [TestCase("wrong-pocket")]
        [TestCase("instance")]
        [TestCase("definition")]
        [TestCase("player")]
        [TestCase("revision")]
        [TestCase("exhausted")]
        [TestCase("bag")]
        [TestCase("nested")]
        [TestCase("ground-nested")]
        [TestCase("null-primary")]
        [TestCase("unknown-primary")]
        [TestCase("raw-ground")]
        public void MalformedOrIncompatibleProposalsLeaveEverythingUnchanged(string mutation)
        {
            BoardState board = NewBoard();
            RunState run = NewRun();
            var position = new GridPosition(1, 1);
            var item = new QuickItemUse(InventoryLayoutEntry.LeftPocket, "food", "berry");
            IEnumerable<QuickItemUse> items = new[] { item };
            PlayerCommand primary = new WaitCommand();
            int revision = 0;
            switch (mutation)
            {
                case "two": items = new[] { item, new QuickItemUse(InventoryLayoutEntry.RightPocket, "medicine", "medicine") }; break;
                case "same-twice": items = new[] { item, item }; break;
                case "empty-list": items = Array.Empty<QuickItemUse>(); break;
                case "null-list": items = null; break;
                case "null-item": items = new QuickItemUse[] { null }; break;
                case "empty-pocket": run.RestoreInventoryState(run.InventoryContent, 0, Array.Empty<InventoryPlacement>()); break;
                case "main-grid": items = new[] { new QuickItemUse(InventoryLayoutEntry.MainGrid, "food", "berry") }; break;
                case "ineligible":
                    var content = new InventoryContent(new ItemCatalog(new[] { new ItemDefinition("wide", 2, 1, "food", "consumable", false, "eat") }), run.InventoryContent.Effects);
                    run.RestoreInventoryState(content, 0, new[] { new InventoryPlacement(new ItemInstanceState("food", "wide"), InventoryContainer.MainGrid) });
                    items = new[] { new QuickItemUse(InventoryLayoutEntry.LeftPocket, "food", "wide") }; break;
                case "wrong-pocket": items = new[] { new QuickItemUse(InventoryLayoutEntry.RightPocket, "food", "berry") }; break;
                case "instance": items = new[] { new QuickItemUse(InventoryLayoutEntry.LeftPocket, "forged", "berry") }; break;
                case "definition": items = new[] { new QuickItemUse(InventoryLayoutEntry.LeftPocket, "food", "medicine") }; break;
                case "player": position = new GridPosition(0, 1); break;
                case "revision": revision = 1; break;
                case "exhausted": revision = int.MaxValue; run.RestoreInventoryState(run.InventoryContent, revision, run.Inventory.Placements); break;
                case "bag": primary = new InventoryDraft(run).CreateCommand(); break;
                case "nested": primary = new QuickPocketCommand(primary, position, 0, items); break;
                case "ground-nested": primary = new GroundPickupCommand(new QuickPocketCommand(primary, position, 0, items), position, 0,
                    new EntityId(99), "ground", "berry", new GridPosition(2, 1), GroundPickupChoice.Leave); break;
                case "null-primary": primary = null; break;
                case "unknown-primary": primary = new UnsupportedCommand(); break;
                case "raw-ground":
                    board.TryAdd(new BoardEntityState(new EntityId(99), new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "berry"), new GridPosition(2, 1)));
                    board.TryBindGroundItem(new GroundItemState(new EntityId(99), "ground", "berry"));
                    primary = new MoveCommand(Direction.East); break;
            }
            string before = PersistenceJsonSerializer.SerializeRun(run);
            BoardEntityState[] entities = board.GetEntities().ToArray();
            int phases = 0;
            var controller = new TurnController(board, run, PlayerId, phases: new TurnPhaseHandlers((state, current, actor, events) => phases++));
            TurnResult result = controller.Resolve(new QuickPocketCommand(primary, position, revision, items));
            Assert.That(result.Accepted || result.ConsumesTurn, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(PersistenceJsonSerializer.SerializeRun(run), Is.EqualTo(before));
            Assert.That(board.GetEntities(), Is.EqualTo(entities));
            Assert.That(board.PickupRevision, Is.Zero);
            Assert.That(phases, Is.Zero);
        }

        /// <summary>Sequential fresh selections may consume different pockets, but never before the first turn's phases finish.</summary>
        [Test]
        public void ConsecutivePocketsNeedTwoTurnsAndSecondCostUsesNewTier()
        {
            BoardState board = NewBoard();
            RunState run = NewRun();
            int phases = 0;
            var controller = new TurnController(board, run, PlayerId, phases: new TurnPhaseHandlers((state, current, actor, events) => phases++));
            foreach (string pocket in new[] { InventoryLayoutEntry.LeftPocket, InventoryLayoutEntry.RightPocket })
                Assert.That(controller.Resolve(new QuickPocketQuery().GetOption(board, run, PlayerId, pocket).CreateCommand(new WaitCommand())).Accepted, Is.True);
            Assert.That(run.Inventory.Placements, Is.Empty);
            Assert.That(run.InventoryRevision, Is.EqualTo(2));
            Assert.That(run.Satiety, Is.EqualTo(107));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(phases, Is.EqualTo(2));
        }

        /// <summary>Cost is sampled before quick restoration; starvation wins over exit and suppresses all later phases.</summary>
        [TestCase(false, 1)]
        [TestCase(true, 1)]
        [TestCase(false, 199)]
        [TestCase(true, 199)]
        public void ExitStarvationAndCapsFollowOnePreselectedCost(bool right, int satiety)
        {
            BoardState board = NewBoard();
            RunState run = NewRun(satiety);
            Assert.That(board.TryGetEntity(BoardLayer.Terrain, new GridPosition(2, 1), out BoardEntityState old), Is.True);
            board.TryRemove(old.Id);
            board.TryAdd(new BoardEntityState(old.Id, new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "exit",
                new BoardEntityTraits(true, true, false)), old.Position));
            int phases = 0;
            var controller = new TurnController(board, run, PlayerId, phases: new TurnPhaseHandlers((state, current, actor, events) => phases++));
            TurnResult result = controller.Resolve(new QuickPocketQuery().GetOption(board, run, PlayerId,
                right ? InventoryLayoutEntry.RightPocket : InventoryLayoutEntry.LeftPocket).CreateCommand(new MoveCommand(Direction.East)));
            Assert.That(result.Accepted, Is.True);
            bool starved = right && satiety == 1;
            Assert.That(result.Events.OfType<PlayerStarvedEvent>().Count(), Is.EqualTo(starved ? 1 : 0));
            Assert.That(result.Events.OfType<ExitReachedEvent>().Count(), Is.EqualTo(starved ? 0 : 1));
            Assert.That(phases, Is.Zero);
            Assert.That(controller.IsTerminal, Is.True);
            QuickItemUsedEvent effect = result.Events.OfType<QuickItemUsedEvent>().Single();
            Assert.That(effect.Applied, Is.EqualTo(right ? 5 : satiety == 199 ? 1 : 10));
            Assert.That(effect.Wasted, Is.EqualTo(right ? 5 : satiety == 199 ? 9 : 0));
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Single().CostAmount, Is.EqualTo(satiety == 199 ? 2 : 1));
            Assert.That(result.Events.First(), Is.TypeOf<QuickItemUsedEvent>());
        }

        private static PlayerCommand Primary(string kind, BoardState board, RunState run, bool right, out TurnResolver resolver)
        {
            resolver = new TurnResolver();
            if (kind == "Wait") return new WaitCommand();
            if (kind == "Move") return new MoveCommand(Direction.East);
            if (kind == "PickupTool")
            {
                ToolInstanceState instance = run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3));
                var ground = new GroundToolState(new EntityId(99), instance.InstanceId, instance.DefinitionId, instance.Charges);
                Assert.That(board.TryAddGroundTool(new BoardEntityState(ground.EntityId, new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item,
                    ground.DefinitionId), new GridPosition(2, 1)), ground), Is.True);
                return new ToolPickupQuery().GetOptions(board, run, PlayerId).Single().Choices.Single();
            }
            if (kind.StartsWith("Ground"))
            {
                board.TryAdd(new BoardEntityState(new EntityId(99), new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "berry"), new GridPosition(2, 1)));
                board.TryBindGroundItem(new GroundItemState(new EntityId(99), "ground.berry.99", "berry"));
                GroundPickupOption option = new GroundPickupQuery().GetOption(board, run, PlayerId, new MoveCommand(Direction.East));
                GroundPickupChoice choice = kind.EndsWith("Use") ? GroundPickupChoice.UseNow : kind.EndsWith("Store") ? GroundPickupChoice.Store : GroundPickupChoice.Leave;
                return option.CreateCommand(choice, choice == GroundPickupChoice.Store ? new InventoryLayoutEntry(option.Item.InstanceId,
                    right ? InventoryLayoutEntry.RightPocket : InventoryLayoutEntry.LeftPocket) : null);
            }
            board.TryAdd(new BoardEntityState(new EntityId(99), new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item,
                "cache", new BoardEntityTraits(false, false, true)), new GridPosition(2, 1)));
            var rules = new ShovelRules("cache", "diggable", 3,
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "berry", BoardEntityTraits.Default, 10), 4, 2, 2);
            var interactions = new InteractionQuery(new[] { new CacheDigAction(rules, false) });
            bool final = kind.StartsWith("Cache") || kind.StartsWith("Shovel");
            bool shovel = kind.StartsWith("Shovel") || kind == "UseTool";
            var tools = new ToolUseQuery(new[] { new ToolAction("shovel.basic", new CacheDigAction(rules, final), "Unearth", 2, 2) });
            resolver = new TurnResolver(interactions: interactions, tools: tools);
            if (shovel) run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3)));
            PlayerCommand primary = shovel ? (PlayerCommand)tools.GetOptions(board, run, PlayerId).Single().CreateCommand() :
                interactions.GetOptions(board, run, PlayerId).Single().CreateCommand();
            if (!final) return primary;
            if (!shovel)
                for (int step = 0; step < 2; step++)
                {
                    Assert.That(resolver.Resolve(board, run, PlayerId, primary).Accepted, Is.True);
                    run.RestoreSatiety(1);
                }
            GroundPickupOption reward = new GroundPickupQuery(interactions, tools).GetOption(board, run, PlayerId, primary);
            GroundPickupChoice cacheChoice = kind.EndsWith("Use") ? GroundPickupChoice.UseNow : kind.EndsWith("Store") ? GroundPickupChoice.Store : GroundPickupChoice.Leave;
            return reward.CreateCommand(cacheChoice, cacheChoice == GroundPickupChoice.Store ?
                new InventoryLayoutEntry(reward.Item.InstanceId, right ? InventoryLayoutEntry.RightPocket : InventoryLayoutEntry.LeftPocket) : null);
        }

        private sealed class UnsupportedCommand : PlayerCommand { public override string CommandType => "Unsupported"; }

        private static readonly EntityId PlayerId = new EntityId(1);

        private static RunState NewRun(int satiety = 100)
        {
            var content = new InventoryContent(new ItemCatalog(new[]
            {
                new ItemDefinition("berry", 1, 1, "satiety", "consumable", true, "eat"),
                new ItemDefinition("medicine", 1, 2, "health", "consumable", true, "heal")
            }), new ConsumableEffectCatalog(new[]
            {
                new ConsumableEffectDefinition("eat", ConsumableResource.Satiety, 10),
                new ConsumableEffectDefinition("heal", ConsumableResource.Health, 10)
            }));
            var run = new RunState("quick", 106, new RunStateConfiguration(95, satiety, 0, "start", inventoryContent: content));
            run.RestoreInventoryState(content, 0, new[]
            {
                new InventoryPlacement(new ItemInstanceState("food", "berry"), InventoryContainer.LeftPocket),
                new InventoryPlacement(new ItemInstanceState("medicine", "medicine"), InventoryContainer.RightPocket)
            });
            return run;
        }

        private static BoardState NewBoard()
        {
            var board = new BoardState();
            Assert.That(board.TryAdd(new BoardEntityState(PlayerId, new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Player, "player", BoardEntityTraits.Default), new GridPosition(1, 1))), Is.True);
            int identity = 10;
            for (int column = 0; column < 4; column++)
                for (int row = 0; row < 3; row++)
                    Assert.That(board.TryAdd(new BoardEntityState(new EntityId(identity++), new BoardEntityDefinition(
                        BoardLayer.Terrain, new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false)),
                        new GridPosition(column, row))), Is.True);
            return board;
        }
    }
}