using System.Linq;
using System;
using System.IO;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies conscious equipment transfers, exact ground ownership and free stale-decision rejection.</summary>
    public sealed class ToolPickupTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);
        private static readonly EntityId ToolId = new EntityId(3);

        /// <summary>The first free generic cell receives the same instance/charges, with one cost and no allocation at commit.</summary>
        [TestCase(0)]
        [TestCase(1)]
        public void ConsciousPickupFillsFirstFreeSlotOnce(int occupied)
        {
            RunState run = CreateRun();
            if (occupied == 1) run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 2)));
            BoardState board = CreateBoard(run);
            var query = new ToolPickupQuery();
            ToolPickupOption option = query.GetOptions(board, run, PlayerId).Single();
            Assert.That(option.RequiresReplacement, Is.False);
            Assert.That(option.Choices.Single().SlotIndex, Is.EqualTo(occupied));
            int sequence = run.ToolInstanceSequence;
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(ActionNoiseProfile.Prototype, pickups: query));
            TurnResult result = controller.Resolve(option.Choices.Single());
            Assert.That(result.Accepted, Is.True);
            Assert.That(run.ToolSlots[occupied].Instance.InstanceId, Is.EqualTo(option.Tool.InstanceId));
            Assert.That(run.ToolSlots[occupied].Instance.Charges, Is.EqualTo(3));
            Assert.That(run.ToolInstanceSequence, Is.EqualTo(sequence));
            Assert.That(run.Food, Is.EqualTo(19));
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<NoiseEvent>().Count(), Is.EqualTo(1));
            Assert.That(board.GetActiveNoises().Single().Strength, Is.EqualTo(ActionNoiseProfile.Prototype.InteractionStrength));
            Assert.That(result.Events.OfType<ToolPickedUpEvent>().Single().Command.Tool.SourceId, Is.EqualTo("source.shovel"));
            Assert.That(board.TryGetGroundTool(ToolId, out _), Is.False);
            Assert.That(controller.Resolve(option.Choices.Single()).Accepted, Is.False);
            Assert.That(run.Food, Is.EqualTo(19));
        }

        /// <summary>Confirmed replacement affects one cell and preserves the displaced identity/charges at the original ground binding.</summary>
        [TestCase(0)]
        [TestCase(1)]
        public void ReplacementPreservesOldToolOnGroundWithoutDuplicatingEitherInstance(int slot)
        {
            RunState run = CreateRun();
            for (int index = 0; index < 2; index++)
            {
                run.EquipTool(index, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", index + 1)));
                run.TrySpendToolCharge(index, run.ToolSlots[index].Instance.InstanceId);
            }
            BoardState board = CreateBoard(run);
            var query = new ToolPickupQuery();
            ToolPickupOption option = query.GetOptions(board, run, PlayerId).Single();
            ToolInstanceState old = run.ToolSlots[slot].Instance;
            ToolInstanceState untouched = run.ToolSlots[1 - slot].Instance;
            Assert.That(option.RequiresReplacement, Is.True);
            var resolver = new TurnResolver(pickups: query);
            Assert.That(resolver.Resolve(board, run, PlayerId, option.Choices[slot]).Accepted, Is.True);
            Assert.That(run.ToolSlots[slot].Instance.InstanceId, Is.EqualTo(option.Tool.InstanceId));
            Assert.That(run.ToolSlots[1 - slot].Instance, Is.SameAs(untouched));
            Assert.That(board.TryGetGroundTool(ToolId, out GroundToolState displaced), Is.True);
            Assert.That(displaced.InstanceId, Is.EqualTo(old.InstanceId));
            Assert.That(displaced.Charges, Is.EqualTo(old.Charges));
            Assert.That(displaced.SourceId, Is.Null);
            Assert.That(board.TryGetEntity(ToolId, out BoardEntityState entity), Is.True);
            Assert.That(entity.Definition.ContentId, Is.EqualTo(old.DefinitionId));
            Assert.That(entity.Position, Is.EqualTo(option.Position));
            Assert.That(resolver.Resolve(board, run, PlayerId, option.Choices[slot]).Accepted, Is.False);
            Assert.That(run.Food, Is.EqualTo(19));
        }

        /// <summary>A changed replacement, target or capacity rejects without touching the same ground tool, costs or noise lifetime.</summary>
        [TestCase("charges")]
        [TestCase("slot")]
        [TestCase("free")]
        [TestCase("removed")]
        [TestCase("moved")]
        [TestCase("full-refusal")]
        public void StaleAndIncompleteChoicesAreFree(string invalid)
        {
            RunState run = CreateRun();
            for (int index = 0; index < 2; index++) run.EquipTool(index, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 2)));
            BoardState board = CreateBoard(run);
            var query = new ToolPickupQuery();
            PickupToolCommand command = query.GetOptions(board, run, PlayerId).Single().Choices[0];
            if (invalid == "charges") run.TrySpendToolCharge(0, run.ToolSlots[0].Instance.InstanceId);
            if (invalid == "slot") run.ReplaceTool(0, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 2)));
            if (invalid == "free") run.RemoveTool(1);
            if (invalid == "removed") board.TryRemove(ToolId);
            if (invalid == "moved") board.TryMove(ToolId, new GridPosition(2, 0));
            if (invalid == "full-refusal") command = new PickupToolCommand(command.Tool, command.TargetPosition, -1);
            BoardNoise noise = board.EmitNoise(PlayerId, new GridPosition(0, 0), 2, 2);
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(ActionNoiseProfile.Prototype, pickups: query));
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(20));
            Assert.That(board.GetActiveNoises().Single(), Is.SameAs(noise));
            if (invalid != "removed")
            {
                board.TryGetGroundTool(ToolId, out GroundToolState ground);
                Assert.That(ground, Is.SameAs(command.Tool));
            }
        }

        /// <summary>Read/clone preserve the original tool; removal affects only the owning board and duplicate identities are rejected.</summary>
        [Test]
        public void GroundToolKeepsEntityInstanceDefinitionAndSourceSeparate()
        {
            var board = new BoardState(new GridBounds(0, 0, 2, 0));
            var tool = new GroundToolState(new EntityId(3), "tool.1", "axe.basic", 2, "source.axe.clearing");
            var entity = new BoardEntityState(tool.EntityId, new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item,
                tool.DefinitionId), new GridPosition(1, 0));
            Assert.That(board.TryAddGroundTool(entity, tool), Is.True);
            Assert.That(board.TryGetGroundTool(tool.EntityId, out GroundToolState found), Is.True);
            Assert.That(found, Is.SameAs(tool));
            Assert.That(found.SourceId, Is.EqualTo("source.axe.clearing"));
            Assert.That(board.TryAddGroundTool(new BoardEntityState(new EntityId(4), entity.Definition, new GridPosition(2, 0)),
                new GroundToolState(new EntityId(4), tool.InstanceId, tool.DefinitionId, 2)), Is.False);
            BoardState clone = board.Clone();
            Assert.That(clone.TryMove(tool.EntityId, new GridPosition(2, 0)), Is.True);
            Assert.That(clone.TryGetGroundTool(tool.EntityId, out found), Is.True);
            Assert.That(found.InstanceId, Is.EqualTo(tool.InstanceId));
            Assert.That(clone.TryRemove(tool.EntityId), Is.True);
            Assert.That(clone.TryGetGroundTool(tool.EntityId, out _), Is.False);
            Assert.That(board.TryGetGroundTool(tool.EntityId, out found), Is.True);
            Assert.That(found.Charges, Is.EqualTo(2));
        }

        /// <summary>Entering a tool cell is ordinary movement, while food still restores before its existing movement cost under O-003.</summary>
        [Test]
        public void MovementLeavesToolAndAutomaticFoodRemainsRewardBeforeCost()
        {
            RunState run = CreateRun();
            BoardState board = CreateBoard(run);
            var resolver = new TurnResolver();
            board.TryGetGroundTool(ToolId, out GroundToolState tool);
            Assert.That(resolver.Resolve(board, run, PlayerId, new MoveCommand(Direction.East)).Accepted, Is.True);
            Assert.That(board.TryGetGroundTool(ToolId, out GroundToolState remaining), Is.True);
            Assert.That(remaining, Is.SameAs(tool));
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(run.Food, Is.EqualTo(19));
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(4), new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item,
                "food", BoardEntityTraits.Default, 10), new GridPosition(2, 0))), Is.True);
            run.ConsumeFood(18);
            TurnResult move = resolver.Resolve(board, run, PlayerId, new MoveCommand(Direction.East));
            Assert.That(move.Accepted, Is.True);
            Assert.That(run.Food, Is.EqualTo(10));
            Assert.That(move.Events.OfType<PlayerStarvedEvent>(), Is.Empty);
            var eventTypes = move.Events.Select(gameEvent => gameEvent.EventType).ToList();
            Assert.That(eventTypes.IndexOf("FoodRestored"), Is.LessThan(eventTypes.IndexOf("ActionCostApplied")));
        }

        /// <summary>The same preview cannot be replayed even after another accepted exchange returns the original tool to the original cell.</summary>
        [Test]
        public void RoundTripExchangeDoesNotReviveOldCommand()
        {
            RunState run = CreateRun();
            for (int slot = 0; slot < 2; slot++) run.EquipTool(slot, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 2)));
            BoardState board = CreateBoard(run);
            var query = new ToolPickupQuery();
            PickupToolCommand original = query.GetOptions(board, run, PlayerId).Single().Choices[0];
            var resolver = new TurnResolver(pickups: query);
            Assert.That(resolver.Resolve(board, run, PlayerId, original).Accepted, Is.True);
            Assert.That(resolver.Resolve(board, run, PlayerId, query.GetOptions(board, run, PlayerId).Single().Choices[0]).Accepted, Is.True);
            Assert.That(query.GetRejection(board, run, PlayerId, original), Is.EqualTo(CommandRejectionCode.StaleTool));
            Assert.That(resolver.Resolve(board, run, PlayerId, original).Accepted, Is.False);
            Assert.That(run.Food, Is.EqualTo(18));
        }

        /// <summary>Ground tool ownership cannot be bypassed by a generic context/collect interaction, even on an authored interactive item.</summary>
        [Test]
        public void GroundToolRequiresDedicatedPickupDecision()
        {
            RunState run = CreateRun();
            BoardState board = CreateBoard(run);
            board.TryGetGroundTool(ToolId, out GroundToolState tool);
            board.TryRemove(ToolId);
            board.TryAddGroundTool(new BoardEntityState(ToolId, new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item,
                tool.DefinitionId, new BoardEntityTraits(false, false, true)), new GridPosition(1, 0)), tool);
            Assert.That(new InteractionQuery().GetOptions(board, run, PlayerId), Is.Empty);
            Assert.That(new TurnResolver().Resolve(board, run, PlayerId,
                new InteractCommand(ToolId, new GridPosition(1, 0), "interact")).Accepted, Is.False);
            Assert.That(run.Food, Is.EqualTo(20));
            Assert.That(board.TryGetGroundTool(ToolId, out GroundToolState same), Is.True);
            Assert.That(same, Is.SameAs(tool));
            for (int slot = 0; slot < 2; slot++) run.EquipTool(slot, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 2)));
            PickupToolCommand exchange = new ToolPickupQuery().GetOptions(board, run, PlayerId).Single().Choices[0];
            Assert.That(new TurnResolver().Resolve(board, run, PlayerId, exchange).Accepted, Is.True);
            board.TryGetEntity(ToolId, out BoardEntityState replaced);
            Assert.That(replaced.Definition.Traits, Is.EqualTo(new BoardEntityTraits(false, false, true)));
            Assert.That(new InteractionQuery().GetOptions(board, run, PlayerId), Is.Empty);
        }

        /// <summary>Both empty-slot pickup and replacement retain exact accepted identities/charges in the existing v4 save, without profile changes.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AcceptedEquipmentRoundTripsWithoutNewSchemaOrReallocatedIds(bool replace)
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M56-domain-" + Guid.NewGuid().ToString("N"));
            try
            {
                RunState run = CreateRun();
                if (replace)
                    for (int slot = 0; slot < 2; slot++) run.EquipTool(slot, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 2)));
                BoardState board = CreateBoard(run);
                PickupToolCommand command = new ToolPickupQuery().GetOptions(board, run, PlayerId).Single().Choices[0];
                Assert.That(new TurnResolver().Resolve(board, run, PlayerId, command).Accepted, Is.True);
                var store = new FileSystemSaveStore(root);
                Assert.That(store.SaveRun(run).IsSuccess, Is.True);
                RunState restored = store.LoadRun().Data;
                Assert.That(restored.ToolInstanceSequence, Is.EqualTo(run.ToolInstanceSequence));
                for (int slot = 0; slot < RunState.ToolSlotCount; slot++)
                {
                    Assert.That(restored.ToolSlots[slot].Instance?.InstanceId, Is.EqualTo(run.ToolSlots[slot].Instance?.InstanceId));
                    Assert.That(restored.ToolSlots[slot].Instance?.DefinitionId, Is.EqualTo(run.ToolSlots[slot].Instance?.DefinitionId));
                    Assert.That(restored.ToolSlots[slot].Instance?.Charges, Is.EqualTo(run.ToolSlots[slot].Instance?.Charges));
                }
                ToolInstanceState allocated = restored.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3));
                Assert.That(allocated.InstanceId, Is.EqualTo("tool." + (run.ToolInstanceSequence + 1)));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        /// <summary>Invalid targets, destination indices and run states do not transfer ground ownership or spend resources.</summary>
        [TestCase("range")]
        [TestCase("invalid-slot")]
        [TestCase("occupied")]
        [TestCase("obstacle")]
        [TestCase("terrain")]
        [TestCase("unallocated")]
        [TestCase("equipped")]
        [TestCase("terminal")]
        public void InvalidPickupPreservesExactGroundInstance(string invalid)
        {
            RunState run = CreateRun();
            BoardState board = CreateBoard(run);
            var query = new ToolPickupQuery();
            PickupToolCommand command = query.GetOptions(board, run, PlayerId).Single().Choices.Single();
            if (invalid == "range") board.TryMove(PlayerId, new GridPosition(2, 0));
            if (invalid == "range") board.TryMove(ToolId, new GridPosition(0, 0));
            if (invalid == "invalid-slot") command = new PickupToolCommand(command.Tool, command.TargetPosition, 2);
            if (invalid == "occupied") board.TryAdd(new BoardEntityState(new EntityId(4),
                new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy"), new GridPosition(1, 0)));
            if (invalid == "obstacle") board.TryAdd(new BoardEntityState(new EntityId(4),
                new BoardEntityDefinition(BoardLayer.Obstacle, new EntityKind("obstacle"), "wall"), new GridPosition(1, 0)));
            if (invalid == "terrain") board.TryRemove(new EntityId(11));
            if (invalid == "terminal") run.MarkDead();
            if (invalid == "equipped") run.EquipTool(1, new ToolInstanceState(command.Tool.InstanceId, command.Tool.DefinitionId, command.Tool.Charges));
            if (invalid == "unallocated")
            {
                board.TryRemove(ToolId);
                var foreign = new GroundToolState(ToolId, "tool.999", "shovel.basic", 3);
                board.TryAddGroundTool(new BoardEntityState(ToolId, new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, foreign.DefinitionId),
                    new GridPosition(1, 0)), foreign);
                command = new PickupToolCommand(foreign, new GridPosition(1, 0), 0);
            }
            board.TryGetGroundTool(ToolId, out GroundToolState original);
            TurnResult result = new TurnResolver().Resolve(board, run, PlayerId, command);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(20));
            Assert.That(board.TryGetGroundTool(ToolId, out GroundToolState after), Is.True);
            Assert.That(after, Is.SameAs(original));
        }

        /// <summary>Pickup transfers a depleted concrete tool without refilling it and still pays exactly the normal accepted-action cost.</summary>
        [Test]
        public void DepletedToolCanBePickedUpWithoutRefillOrAllocatorAdvance()
        {
            RunState run = CreateRun();
            BoardState board = CreateBoard(run);
            board.TryGetGroundTool(ToolId, out GroundToolState original);
            board.TryRemove(ToolId);
            var depleted = new GroundToolState(ToolId, original.InstanceId, original.DefinitionId, 0);
            board.TryAddGroundTool(new BoardEntityState(ToolId, new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, depleted.DefinitionId),
                new GridPosition(1, 0)), depleted);
            var query = new ToolPickupQuery();
            PickupToolCommand command = query.GetOptions(board, run, PlayerId).Single().Choices.Single();
            run.ConsumeFood(19);
            TurnResult result = new TurnController(board, run, PlayerId, new TurnResolver(pickups: query)).Resolve(command);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Events.OfType<PlayerStarvedEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<ToolChargeSpentEvent>(), Is.Empty);
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.Zero);
            Assert.That(run.ToolInstanceSequence, Is.EqualTo(1));
            Assert.That(run.Food, Is.Zero);
        }

        private static RunState CreateRun() => new RunState("pickup-tests", 56, new RunStateConfiguration(100, 20, 0, "forest.start"));

        private static BoardState CreateBoard(RunState run)
        {
            var board = new BoardState(new GridBounds(0, 0, 2, 0));
            for (int horizontal = 0; horizontal <= 2; horizontal++)
                board.TryAdd(new BoardEntityState(new EntityId(10 + horizontal), new BoardEntityDefinition(BoardLayer.Terrain,
                    new EntityKind("terrain"), "floor", new BoardEntityTraits(true, horizontal == 2, false)), new GridPosition(horizontal, 0)));
            board.TryAdd(new BoardEntityState(PlayerId, new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"), new GridPosition(0, 0)));
            ToolInstanceState instance = run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3));
            board.TryAddGroundTool(new BoardEntityState(ToolId, new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, instance.DefinitionId),
                new GridPosition(1, 0)), new GroundToolState(ToolId, instance.InstanceId, instance.DefinitionId, instance.Charges, "source.shovel"));
            return board;
        }
    }
}