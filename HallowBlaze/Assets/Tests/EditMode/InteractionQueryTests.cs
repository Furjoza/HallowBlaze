using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies pure option discovery, exact target selection and atomic interaction effects.</summary>
    public sealed class InteractionQueryTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);
        private BoardState board;
        private RunState run;

        [SetUp]
        public void SetUp()
        {
            board = new BoardState();
            run = new RunState("m51", 51, new RunStateConfiguration(10, 5, 0, "forest.start"));
            Add(1, BoardLayer.Actor, EntityKind.Player, "player", new GridPosition(1, 1));
        }

        [Test]
        public void EmptyQueryDoesNotMutateState()
        {
            Assert.That(new InteractionQuery().GetOptions(board, run, PlayerId), Is.Empty);
            Assert.That(board.Count, Is.EqualTo(1));
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(board.GetActiveNoises(), Is.Empty);
        }

        /// <summary>A tool command reaches the resolver but empty equipment cannot consume an action or emit noise.</summary>
        [Test]
        public void EmptyToolSlotIsRejectedWithoutEffects()
        {
            var command = new UseToolCommand(0, "tool.1", 3, new EntityId(7), new GridPosition(1, 2), "test");
            TurnResult result = new TurnResolver(ActionNoiseProfile.Prototype).Resolve(board, run, PlayerId, command);
            Assert.That(result.Accepted, Is.False);
            Assert.That(((RejectedTurnResult)result).RejectionCode, Is.EqualTo(CommandRejectionCode.EmptyToolSlot));
            Assert.That(result.Events, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(board.Count, Is.EqualTo(1));
            Assert.That(board.GetActiveNoises(), Is.Empty);
        }

        [Test]
        public void PreviewRetainsExactInstanceAndCapturesActionAndPosition()
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(1, 2), 4);
            InteractionOption option = new InteractionQuery().GetOptions(board, run, PlayerId).Single();
            InteractCommand command = option.CreateCommand();
            Assert.That(option.IsAvailable, Is.True);
            Assert.That(command.ActionId, Is.EqualTo("collect"));
            Assert.That(command.TargetId, Is.EqualTo(item.Id));
            Assert.That(command.TargetPosition, Is.EqualTo(item.Position));
            Assert.That(board.TryGetEntity(item.Id, out BoardEntityState retained), Is.True);
            Assert.That(retained, Is.SameAs(item));
            Assert.That(run.Food, Is.EqualTo(5));
        }

        [Test]
        public void MultipleTargetsAndActionsHaveStableOrderWithoutAutomaticSelection()
        {
            Add(9, BoardLayer.Item, EntityKind.Item, "lever", new GridPosition(0, 1));
            Add(3, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            var options = PushQuery().GetOptions(board, run, PlayerId);
            Assert.That(options.Select(option => $"{option.TargetId.Value}:{option.ActionId}"),
                Is.EqualTo(new[] { "3:interact", "3:push", "9:interact" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDisplacedTargetIsRejectedWithoutCostOrNoise(bool displaced)
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(1, 2), 4);
            var query = new InteractionQuery();
            InteractCommand selection = query.GetOptions(board, run, PlayerId).Single().CreateCommand();
            if (displaced) board.TryMove(item.Id, new GridPosition(2, 1));
            else
            {
                board.TryRemove(item.Id);
                Add(8, BoardLayer.Item, EntityKind.Item, "carrot", item.Position, 4);
            }
            TurnResult result = new TurnResolver(ActionNoiseProfile.Prototype, query).Resolve(board, run, PlayerId, selection);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(board.GetActiveNoises(), Is.Empty);
            Assert.That(board.GetEntities().Single(entity => entity.Definition.Layer == BoardLayer.Item).Id,
                Is.EqualTo(displaced ? item.Id : new EntityId(8)));
        }

        [Test]
        public void CollectCommitsExactItemThenRewardThenOneCost()
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(1, 2), 4);
            var query = new InteractionQuery();
            TurnResult result = new TurnResolver(interactions: query).Resolve(board, run, PlayerId,
                query.GetOptions(board, run, PlayerId).Single().CreateCommand());
            Assert.That(result.Accepted, Is.True);
            Assert.That(board.TryGetEntity(item.Id, out _), Is.False);
            Assert.That(run.Food, Is.EqualTo(8));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "InteractionPerformed", "ItemCollected", "FoodRestored", "ActionCostApplied" }));
        }

        [Test]
        public void BlockedPushIsDisabledAndFreeWhileClearPushMovesOnlyTarget()
        {
            BoardEntityState crate = Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            var query = PushQuery();
            InteractionOption push = query.GetOptions(board, run, PlayerId).Single(option => option.ActionId == "push");
            Assert.That(push.Availability, Is.EqualTo(InteractionAvailability.Blocked));
            var resolver = new TurnResolver(interactions: query);
            Assert.That(resolver.Resolve(board, run, PlayerId, push.CreateCommand()).Accepted, Is.False);
            Assert.That(run.Food, Is.EqualTo(5));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            Assert.That(resolver.Resolve(board, run, PlayerId, push.CreateCommand()).Accepted, Is.True);
            Assert.That(board.TryGetEntity(crate.Id, out BoardEntityState moved), Is.True);
            Assert.That(moved.Position, Is.EqualTo(new GridPosition(3, 1)));
            Assert.That(board.TryGetEntity(PlayerId, out BoardEntityState player), Is.True);
            Assert.That(player.Position, Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(run.Food, Is.EqualTo(4));
        }

        [Test]
        public void DecisionRequiredPreviewCannotRemoveGroundInstance()
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "future.item", new GridPosition(1, 2));
            var query = new InteractionQuery(new[] { new DeferredPickup() });
            InteractionOption option = query.GetOptions(board, run, PlayerId).Single();
            Assert.That(option.Availability, Is.EqualTo(InteractionAvailability.DecisionRequired));
            Assert.That(new TurnResolver(interactions: query).Resolve(board, run, PlayerId, option.CreateCommand()).Accepted, Is.False);
            Assert.That(board.TryGetEntity(item.Id, out BoardEntityState retained), Is.True);
            Assert.That(retained, Is.SameAs(item));
            Assert.That(run.Food, Is.EqualTo(5));
        }

        [TestCase("unknown", 1, 2, InteractionAvailability.Unsupported)]
        [TestCase("collect", 3, 1, InteractionAvailability.OutOfRange)]
        public void UnavailableSelectionsUseTheSameQueryAndResolverValidation(string action, int horizontal, int vertical,
            InteractionAvailability expected)
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(horizontal, vertical), 4);
            var query = new InteractionQuery();
            var command = new InteractCommand(item.Id, item.Position, action);
            Assert.That(query.GetAvailability(board, run, PlayerId, command), Is.EqualTo(expected));
            Assert.That(new TurnResolver(interactions: query).Resolve(board, run, PlayerId, command).Accepted, Is.False);
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(board.TryGetEntity(item.Id, out BoardEntityState retained), Is.True);
            Assert.That(retained, Is.SameAs(item));
        }

        [Test]
        public void PushRevalidatesDestinationAfterPreviewAndDoesNotConsumeAnythingWhenBlocked()
        {
            BoardEntityState crate = Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            var query = PushQuery();
            InteractCommand command = query.GetOptions(board, run, PlayerId).Single(option => option.ActionId == "push").CreateCommand();
            Add(8, BoardLayer.Item, EntityKind.Item, "other", new GridPosition(3, 1));
            Assert.That(query.GetAvailability(board, run, PlayerId, command), Is.EqualTo(InteractionAvailability.Blocked));
            Assert.That(new TurnResolver(interactions: query).Resolve(board, run, PlayerId, command).Accepted, Is.False);
            Assert.That(board.TryGetEntity(crate.Id, out BoardEntityState retained), Is.True);
            Assert.That(retained, Is.SameAs(crate));
            Assert.That(run.Food, Is.EqualTo(5));
        }

        [Test]
        public void ControllerExecutesLaterPhasesOnlyForAcceptedInteraction()
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(1, 2), 4);
            BoardNoise existingNoise = board.EmitNoise(PlayerId, new GridPosition(1, 1), 1, 3);
            var phases = new List<string>();
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(ActionNoiseProfile.Prototype),
                new TurnPhaseHandlers(
                    (state, activeRun, actor, events) => phases.Add("enemies"),
                    (state, activeRun, actor, events) => phases.Add("environment"),
                    (state, activeRun, actor, events) => phases.Add("plan")));
            var query = new InteractionQuery();
            InteractCommand command = query.GetOptions(board, run, PlayerId).Single().CreateCommand();
            Assert.That(controller.Resolve(command).Accepted, Is.True);
            Assert.That(phases, Is.EqualTo(new[] { "enemies", "environment", "plan" }));
            Assert.That(board.GetActiveNoises().Single(noise => noise.Id == existingNoise.Id).RemainingTurns, Is.EqualTo(2));
            int foodAfterAccepted = run.Food;
            phases.Clear();
            Assert.That(controller.Resolve(command).Accepted, Is.False);
            Assert.That(phases, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(foodAfterAccepted));
            Assert.That(board.GetActiveNoises().Single(noise => noise.Id == existingNoise.Id).RemainingTurns, Is.EqualTo(2));
            Assert.That(board.TryGetEntity(item.Id, out _), Is.False);
        }

        private static InteractionQuery PushQuery() => new InteractionQuery(new IInteractionAction[]
            { new ContextInteractionAction(), new PushInteractionAction("crate") });

        /// <summary>One accepted use commits effect, charge, active policy cost and noise, then rejects replay for free.</summary>
        [Test]
        public void ToolUseCommitsOneAtomicResultAndRejectsDuplicateDecision()
        {
            BoardEntityState crate = Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            ToolInstanceState instance = run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3));
            run.EquipTool(1, instance);
            var tools = new ToolUseQuery(new[] { new ToolAction("test.tool", new PushInteractionAction("crate"), "Push", 4, 2) },
                new ActionCostPolicy(2));
            ToolUseOption option = tools.GetOptions(board, run, PlayerId).Single();
            Assert.That(option.FoodCost, Is.EqualTo(2));
            Assert.That(option.ChargeCost, Is.EqualTo(1));
            Assert.That(option.NoiseStrength, Is.EqualTo(4));
            Assert.That(option.ExpectedCharges, Is.EqualTo(3));
            Assert.That(run.Food, Is.EqualTo(5));
            var resolver = new TurnResolver(ActionNoiseProfile.Prototype, tools: tools);
            TurnResult result = resolver.Resolve(board, run, PlayerId, option.CreateCommand());
            Assert.That(result.Accepted && result.ConsumesTurn, Is.True);
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "ToolUsed", "EntityMoved", "ToolChargeSpent", "ActionCostApplied", "Noise" }));
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(run.ToolSlots[1].Instance.Charges, Is.EqualTo(2));
            Assert.That(instance.Charges, Is.EqualTo(3));
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(crate.Id)).Position, Is.EqualTo(new GridPosition(3, 1)));
            Assert.That(board.GetActiveNoises().Single().Strength, Is.EqualTo(4));
            TurnResult duplicate = resolver.Resolve(board, run, PlayerId, option.CreateCommand());
            Assert.That(((RejectedTurnResult)duplicate).RejectionCode, Is.EqualTo(CommandRejectionCode.StaleTool));
            Assert.That(duplicate.Events, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(run.ToolSlots[1].Instance.Charges, Is.EqualTo(2));
        }

        /// <summary>Every rejected tool decision leaves equipment, resources, board, noise age and later phases unchanged.</summary>
        [TestCase("slot-negative", CommandRejectionCode.InvalidToolSlot)]
        [TestCase("slot-overflow", CommandRejectionCode.InvalidToolSlot)]
        [TestCase("empty", CommandRejectionCode.EmptyToolSlot)]
        [TestCase("depleted", CommandRejectionCode.ToolDepleted)]
        [TestCase("replaced", CommandRejectionCode.StaleTool)]
        [TestCase("spent", CommandRejectionCode.StaleTool)]
        [TestCase("removed", CommandRejectionCode.InvalidTarget)]
        [TestCase("displaced", CommandRejectionCode.InvalidTarget)]
        [TestCase("distant", CommandRejectionCode.InvalidTarget)]
        [TestCase("self", CommandRejectionCode.InvalidTarget)]
        [TestCase("blocked", CommandRejectionCode.Blocked)]
        [TestCase("unknown-tool", CommandRejectionCode.UnsupportedTool)]
        [TestCase("unknown-action", CommandRejectionCode.UnsupportedTool)]
        [TestCase("unsupported-target", CommandRejectionCode.UnsupportedTool)]
        [TestCase("terminal", CommandRejectionCode.InvalidState)]
        [TestCase("starved", CommandRejectionCode.InvalidState)]
        [TestCase("dead", CommandRejectionCode.InvalidState)]
        [TestCase("missing-player", CommandRejectionCode.InvalidState)]
        public void InvalidToolDecisionsAreEffectFree(string scenario, CommandRejectionCode expected)
        {
            BoardEntityState crate = Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            ToolInstanceState tool = run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3));
            run.EquipTool(0, tool);
            ToolUseQuery tools = TestTools();
            UseToolCommand command = tools.GetOptions(board, run, PlayerId).Single().CreateCommand();
            if (scenario == "slot-negative" || scenario == "slot-overflow")
                command = new UseToolCommand(scenario == "slot-negative" ? -1 : 2, tool.InstanceId, 3, crate.Id, crate.Position, "push");
            if (scenario == "empty") run.RemoveTool(0);
            if (scenario == "depleted")
            {
                for (int charge = 0; charge < 3; charge++) run.TrySpendToolCharge(0, tool.InstanceId);
                command = new UseToolCommand(0, tool.InstanceId, 0, crate.Id, crate.Position, "push");
            }
            if (scenario == "replaced") run.ReplaceTool(0, run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3)));
            if (scenario == "spent") run.TrySpendToolCharge(0, tool.InstanceId);
            if (scenario == "removed")
            {
                board.TryRemove(crate.Id);
                Add(8, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", crate.Position);
            }
            if (scenario == "displaced") board.TryMove(crate.Id, new GridPosition(1, 2));
            if (scenario == "distant")
            {
                board.TryMove(crate.Id, new GridPosition(4, 1));
                command = new UseToolCommand(0, tool.InstanceId, 3, crate.Id, new GridPosition(4, 1), "push");
            }
            if (scenario == "self") command = new UseToolCommand(0, tool.InstanceId, 3, PlayerId, new GridPosition(1, 1), "push");
            if (scenario == "blocked") Add(8, BoardLayer.Item, EntityKind.Item, "blocker", new GridPosition(3, 1));
            if (scenario == "unknown-tool") tools = new ToolUseQuery();
            if (scenario == "unknown-action") command = new UseToolCommand(0, tool.InstanceId, 3, crate.Id, crate.Position, "unknown");
            if (scenario == "unsupported-target")
            {
                BoardEntityState other = Add(8, BoardLayer.Item, EntityKind.Item, "other", new GridPosition(1, 2));
                command = new UseToolCommand(0, tool.InstanceId, 3, other.Id, other.Position, "push");
            }
            if (scenario == "terminal") run.MarkWon();
            if (scenario == "starved") run.ConsumeFood(run.Food);
            if (scenario == "dead") run.TakeDamage(run.Health);
            if (scenario == "missing-player") board.TryRemove(PlayerId);
            BoardNoise previous = board.EmitNoise(PlayerId, new GridPosition(1, 1), 2, 3);
            string[] entitiesBefore = board.GetEntities().Select(entity => entity.Id + ":" + entity.Position).ToArray();
            ToolSlotState[] slotsBefore = run.ToolSlots.ToArray();
            int foodBefore = run.Food;
            int healthBefore = run.Health;
            int sequenceBefore = run.ToolInstanceSequence;
            int laterPhases = 0;
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(tools: tools), new TurnPhaseHandlers(
                (state, activeRun, actor, events) => laterPhases++,
                (state, activeRun, actor, events) => laterPhases++,
                (state, activeRun, actor, events) => laterPhases++));
            Assert.That(tools.GetRejection(board, run, PlayerId, command), Is.EqualTo(expected));
            TurnResult result = controller.Resolve(command);
            Assert.That(((RejectedTurnResult)result).RejectionCode, Is.EqualTo(expected));
            Assert.That(result.ConsumesTurn, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(laterPhases, Is.Zero);
            Assert.That(run.Food, Is.EqualTo(foodBefore));
            Assert.That(run.Health, Is.EqualTo(healthBefore));
            Assert.That(run.ToolInstanceSequence, Is.EqualTo(sequenceBefore));
            Assert.That(run.ToolSlots, Is.EqualTo(slotsBefore));
            Assert.That(board.GetEntities().Select(entity => entity.Id + ":" + entity.Position), Is.EqualTo(entitiesBefore));
            Assert.That(board.GetActiveNoises().Single(), Is.SameAs(previous));
            Assert.That(previous.RemainingTurns, Is.EqualTo(3));
            Assert.That(controller.IsResolving, Is.False);
        }

        /// <summary>Only accepted tool actions age old noise and execute exactly one enemy/environment/planning opportunity.</summary>
        [Test]
        public void ToolAcceptanceExecutesOnePhaseSequenceAndKeepsFreshNoiseLifetime()
        {
            Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 1)));
            ToolUseQuery tools = TestTools();
            UseToolCommand command = tools.GetOptions(board, run, PlayerId).Single().CreateCommand();
            BoardNoise old = board.EmitNoise(PlayerId, new GridPosition(1, 1), 1, 3);
            var phases = new List<string>();
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(tools: tools), new TurnPhaseHandlers(
                (state, activeRun, actor, events) => phases.Add("enemy"),
                (state, activeRun, actor, events) => phases.Add("environment"),
                (state, activeRun, actor, events) => phases.Add("plan")));
            TurnResult accepted = controller.Resolve(command);
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(phases, Is.EqualTo(new[] { "enemy", "environment", "plan" }));
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(run.ToolSlots[0].IsEmpty, Is.False);
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.Zero);
            Assert.That(board.GetActiveNoises().Single(noise => noise.Id == old.Id).RemainingTurns, Is.EqualTo(2));
            Assert.That(board.GetActiveNoises().Single(noise => noise.Id != old.Id).RemainingTurns, Is.EqualTo(2));
            Assert.That(accepted.Events.OfType<ToolChargeSpentEvent>().Single().RemainingCharges, Is.Zero);
            Assert.That(controller.Resolve(command).Accepted, Is.False);
            Assert.That(phases.Count, Is.EqualTo(3));
        }

        /// <summary>Preview lists generic slots deterministically and includes explicit disabled and consequence data for free.</summary>
        [Test]
        public void ToolPreviewOrdersSlotsAndShowsDepletionWithoutInventingPlaceholderEffects()
        {
            Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            ToolInstanceState first = run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 1));
            run.EquipTool(0, first);
            run.EquipTool(1, run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 1)));
            run.TrySpendToolCharge(0, first.InstanceId);
            ToolUseQuery tools = TestTools();
            var preview = tools.GetOptions(board, run, PlayerId);
            Assert.That(preview.Select(option => option.SlotIndex), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(preview[0].RejectionCode, Is.EqualTo(CommandRejectionCode.ToolDepleted));
            Assert.That(preview[1].IsAvailable, Is.True);
            Assert.That(preview[1].EffectLabel, Is.EqualTo("Push"));
            Assert.That(preview[1].NoiseStrength, Is.EqualTo(4));
            Assert.That(preview[1].NoiseLifetimeTurns, Is.EqualTo(2));
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(run.ToolSlots[1].Instance.Charges, Is.EqualTo(1));
            Assert.That(new ToolUseQuery().GetOptions(board, run, PlayerId), Is.Empty);
        }

        /// <summary>The central current policy governs ordinary commands as well as tools; no tool-only hard-coded food cost exists.</summary>
        [TestCase("wait")]
        [TestCase("move")]
        [TestCase("interact")]
        [TestCase("tool")]
        public void AllAcceptedActionsUseTheSharedCurrentCostPolicy(string action)
        {
            BoardEntityState crate = Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            Add(101, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(1, 2));
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3)));
            ToolUseQuery tools = TestTools(2);
            PlayerCommand command = action == "wait" ? (PlayerCommand)new WaitCommand() :
                action == "move" ? new MoveCommand(Direction.North) :
                action == "interact" ? (PlayerCommand)new InteractCommand(crate.Id) :
                tools.GetOptions(board, run, PlayerId).Single().CreateCommand();
            TurnResult result = new TurnResolver(tools: tools).Resolve(board, run, PlayerId, command);
            Assert.That(result.Accepted, Is.True);
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
        }

        /// <summary>Direct effects and the charge commit precede terminal starvation; no enemy phase runs afterward.</summary>
        [Test]
        public void ToolStarvationKeepsAcceptedEffectChargeAndNoiseBeforeTerminalEvent()
        {
            Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 1)));
            ToolUseQuery tools = TestTools(5);
            int enemyPhases = 0;
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(tools: tools), new TurnPhaseHandlers(
                (state, activeRun, actor, events) => enemyPhases++));
            TurnResult result = controller.Resolve(tools.GetOptions(board, run, PlayerId).Single().CreateCommand());
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "ToolUsed", "EntityMoved", "ToolChargeSpent", "ActionCostApplied", "Noise", "PlayerStarved" }));
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.Zero);
            Assert.That(run.Food, Is.Zero);
            Assert.That(enemyPhases, Is.Zero);
            Assert.That(controller.IsTerminal, Is.True);
        }

        /// <summary>Behavior rejection reasons are shared by disabled previews and commit; rejected effects are never invoked.</summary>
        [TestCase(InteractionAvailability.InvalidState, CommandRejectionCode.InvalidState)]
        [TestCase(InteractionAvailability.InvalidTarget, CommandRejectionCode.InvalidTarget)]
        [TestCase(InteractionAvailability.OutOfRange, CommandRejectionCode.InvalidTarget)]
        [TestCase(InteractionAvailability.Unsupported, CommandRejectionCode.NoInteractionAvailable)]
        [TestCase(InteractionAvailability.Blocked, CommandRejectionCode.Blocked)]
        [TestCase(InteractionAvailability.DecisionRequired, CommandRejectionCode.NoInteractionAvailable)]
        public void ToolBehaviorPreconditionsRejectWithoutCallingApply(InteractionAvailability availability, CommandRejectionCode expected)
        {
            Add(7, BoardLayer.Item, EntityKind.Item, "target", new GridPosition(1, 2));
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3)));
            var behavior = new UnavailableToolEffect(availability);
            var tools = new ToolUseQuery(new[] { new ToolAction("test.tool", behavior, "Test", 1, 2) });
            ToolUseOption option = tools.GetOptions(board, run, PlayerId).Single();
            Assert.That(option.RejectionCode, Is.EqualTo(expected));
            Assert.That(((RejectedTurnResult)new TurnResolver(tools: tools).Resolve(board, run, PlayerId,
                option.CreateCommand())).RejectionCode, Is.EqualTo(expected));
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(3));
            Assert.That(board.GetActiveNoises(), Is.Empty);
        }

        /// <summary>Direct rewards precede the current action cost and silent tool behavior never borrows ordinary interaction noise.</summary>
        [Test]
        public void ToolDirectRewardPrecedesCostAndSilentConfigurationEmitsNoNoise()
        {
            Add(7, BoardLayer.Item, EntityKind.Item, "reward", new GridPosition(1, 2), 4);
            run.ConsumeFood(4);
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3)));
            var tools = new ToolUseQuery(new[] { new ToolAction("test.tool", new CollectInteractionAction(), "Collect", 0, 1) },
                new ActionCostPolicy(2));
            TurnResult result = new TurnController(board, run, PlayerId, new TurnResolver(ActionNoiseProfile.Prototype, tools: tools))
                .Resolve(tools.GetOptions(board, run, PlayerId).Single().CreateCommand());
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "ToolUsed", "ItemCollected", "FoodRestored", "ToolChargeSpent", "ActionCostApplied" }));
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            Assert.That(board.GetActiveNoises(), Is.Empty);
        }

        /// <summary>Invalid authored dispatch, cost, snapshot or event data fails before any gameplay mutation.</summary>
        [Test]
        public void ToolContractsRejectInvalidConfigurationAndSnapshots()
        {
            var behavior = new ContextInteractionAction();
            Assert.Throws<System.ArgumentException>(() => new ToolAction(" ", behavior, "Effect", 1, 1));
            Assert.Throws<System.ArgumentNullException>(() => new ToolAction("tool", null, "Effect", 1, 1));
            Assert.Throws<System.ArgumentException>(() => new ToolAction("tool", behavior, "", 1, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ToolAction("tool", behavior, "Effect", -1, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ToolAction("tool", behavior, "Effect", 1, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ActionCostPolicy(0));
            var binding = new ToolAction("tool", behavior, "Effect", 1, 1);
            Assert.Throws<System.ArgumentException>(() => new ToolUseQuery(new[] { binding, binding }));
            Assert.Throws<System.ArgumentException>(() => new ToolUseQuery(new ToolAction[] { null }));
            Assert.Throws<System.ArgumentException>(() => new UseToolCommand(0, " ", 1, PlayerId, default, "interact"));
            Assert.Throws<System.ArgumentException>(() => new UseToolCommand(0, "tool.1", 1, PlayerId, default, " "));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new UseToolCommand(0, "tool.1", -1, PlayerId, default, "interact"));
            var depleted = new UseToolCommand(0, "tool.1", 0, PlayerId, default, "interact");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ToolChargeSpentEvent(PlayerId, depleted));
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
        }

        private sealed class UnavailableToolEffect : IInteractionAction
        {
            private readonly InteractionAvailability availability;
            public UnavailableToolEffect(InteractionAvailability availability) { this.availability = availability; }
            public string Id => "test.unavailable";
            public bool Supports(BoardEntityState target) => target.Definition.Layer == BoardLayer.Item;
            public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target) => availability;
            public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
                => Assert.Fail("A rejected tool behavior must never apply.");
        }

        private static ToolUseQuery TestTools(int foodCost = 1) => new ToolUseQuery(
            new[] { new ToolAction("test.tool", new PushInteractionAction("crate"), "Push", 4, 2) }, new ActionCostPolicy(foodCost));

        private BoardEntityState Add(long id, BoardLayer layer, EntityKind kind, string content, GridPosition position, int food = 0)
        {
            var entity = new BoardEntityState(new EntityId(id), new BoardEntityDefinition(layer, kind, content,
                new BoardEntityTraits(layer == BoardLayer.Terrain, false, layer != BoardLayer.Actor && layer != BoardLayer.Terrain), food), position);
            Assert.That(board.TryAdd(entity), Is.True);
            return entity;
        }

        private sealed class DeferredPickup : IInteractionAction
        {
            public string Id => "pickup";
            public bool Supports(BoardEntityState target) => target.Definition.Layer == BoardLayer.Item;
            public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
                => InteractionAvailability.DecisionRequired;
            public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
                => Assert.Fail("A deferred decision cannot commit.");
        }
    }
}