using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Exercises the manual route and faster noisy axe route through the authoritative command resolver.</summary>
    public sealed class AxeObstacleTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);
        private static readonly EntityId WallId = new EntityId(2);

        /// <summary>Both alternatives open the same route; only the axe consumes a charge and its single emission is louder.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ManualAndAxeClearTheSameRouteWithExplicitCosts(bool usesAxe)
        {
            BoardState board = CreateBoard();
            var run = new RunState("m54", 54, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var rules = new ObstacleClearingRules(new[] { "soft.wood" }, 3, 1, 6, 2);
            var interactions = new InteractionQuery(new[] { new ClearObstacleAction(rules, false) });
            var tools = new ToolUseQuery(new[] { new ToolAction("axe.basic", new ClearObstacleAction(rules, true), "Chop: clear obstacle", 6, 2,
                ObstacleClearingRules.AxeNoiseFactId) });
            if (usesAxe) run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3)));
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(interactions: interactions, tools: tools));
            Assert.That(controller.Resolve(new MoveCommand(Direction.East)).Accepted, Is.False);
            int actions = usesAxe ? 1 : rules.RequiredWork;
            for (int actionIndex = 0; actionIndex < actions; actionIndex++)
            {
                PlayerCommand command = usesAxe ? (PlayerCommand)tools.GetOptions(board, run, PlayerId).Single().CreateCommand() :
                    interactions.GetOptions(board, run, PlayerId).Single().CreateCommand();
                TurnResult result = controller.Resolve(command);
                Assert.That(result.Accepted, Is.True);
                ObstacleWorkedEvent work = result.Events.OfType<ObstacleWorkedEvent>().Single();
                Assert.That(work.IsCleared, Is.EqualTo(actionIndex == actions - 1));
                Assert.That(result.Events.OfType<NoiseEvent>().Single().Noise.Strength, Is.EqualTo(usesAxe ? 6 : 1));
                Assert.That(result.Events.OfType<NoiseEvent>().Single().ObservedFactId,
                    Is.EqualTo(usesAxe ? ObstacleClearingRules.AxeNoiseFactId : null));
                Assert.That(result.Events.OfType<ToolChargeSpentEvent>().Count(), Is.EqualTo(usesAxe ? 1 : 0));
            }
            Assert.That(run.Food, Is.EqualTo(20 - actions));
            Assert.That(board.TryGetEntity(WallId, out _), Is.False);
            Assert.That(controller.Resolve(new MoveCommand(Direction.East)).Accepted, Is.True);
            if (usesAxe) Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            else Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            controller.Resolve(new MoveCommand(Direction.East));
            controller.Resolve(new MoveCommand(Direction.East));
            Assert.That(controller.Resolve(new MoveCommand(Direction.East)).Events.OfType<ExitReachedEvent>().Count(), Is.EqualTo(1));
            Assert.That(controller.IsTerminal, Is.True, "Even a single blocked corridor has a tool-free route to its mandatory exit.");
        }

        /// <summary>The Listener hears the identical post-clearing noise snapshot and plans toward its source only on its next opportunity.</summary>
        [Test]
        public void AxeNoiseIsTheExactListenerSignalAndKnowledgeDoesNotUnlockTheEffect()
        {
            BoardState board = CreateBoard();
            var listenerId = new EntityId(3);
            Assert.That(board.TryAdd(new BoardEntityState(listenerId, new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Enemy, "enemy.listener"), new GridPosition(4, 0))), Is.True);
            board.RegisterListener(listenerId);
            var listener = new ListenerState(listenerId, new ListenerDefinition());
            var phases = new MixedEnemyTurnPhases(new ShamblerState[0], new[] { listener });
            phases.PlanInitialIntents(board, PlayerId);
            var run = new RunState("m54-listener", 54, new RunStateConfiguration(100, 20, 0, "forest.start"));
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3)));
            var rules = new ObstacleClearingRules(new[] { "soft.wood" }, 3, 1, 6, 2);
            var tools = new ToolUseQuery(new[] { new ToolAction("axe.basic", new ClearObstacleAction(rules, true), "Chop", 6, 2,
                ObstacleClearingRules.AxeNoiseFactId) });
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(tools: tools), phases.Handlers);
            TurnResult result = controller.Resolve(tools.GetOptions(board, run, PlayerId).Single().CreateCommand());
            NoiseEvent emission = result.Events.OfType<NoiseEvent>().Single();
            Assert.That(board.TryGetListenerNoise(listenerId, out BoardNoise heard), Is.True);
            Assert.That(heard, Is.SameAs(emission.Noise));
            Assert.That(heard.IsAudible(new GridPosition(4, 0)), Is.True, "The cleared obstacle no longer blocks emission paths.");
            Assert.That(board.TryGetEntity(listenerId, out BoardEntityState current), Is.True);
            Assert.That(current.Position, Is.EqualTo(new GridPosition(4, 0)), "The previously shown Wait executes before new planning.");
            Assert.That(controller.Resolve(new WaitCommand()).Accepted, Is.True);
            board.TryGetEntity(listenerId, out current);
            Assert.That(current.Position, Is.EqualTo(new GridPosition(3, 0)));
            Assert.That(emission.ObservedFactId, Is.EqualTo(ObstacleClearingRules.AxeNoiseFactId));
        }

        /// <summary>Invalid configuration cannot erase the manual alternative or make the axe no faster/louder.</summary>
        [Test]
        public void ClearingConfigurationRequiresExplicitSoftContentAndMeaningfulTradeoff()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ObstacleClearingRules(new[] { "soft.wood" }, 1, 1, 6, 2));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ObstacleClearingRules(new[] { "soft.wood" }, 3, 2, 2, 2));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ObstacleClearingRules(new[] { "soft.wood" }, 3, -1, 6, 2));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ObstacleClearingRules(new[] { "soft.wood" }, 3, 1, 6, 0));
            Assert.Throws<System.ArgumentException>(() => new ObstacleClearingRules(new string[0], 3, 1, 6, 2));
            Assert.Throws<System.ArgumentException>(() => new ObstacleClearingRules(new[] { "soft.wood", "soft.wood" }, 3, 1, 6, 2));
            var rules = new ObstacleClearingRules(new[] { "soft.wood" }, 3, 1, 6, 2);
            Assert.That(rules.Supports(new BoardEntityState(new EntityId(9), new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Enemy, "soft.wood"), new GridPosition(1, 0))), Is.False);
            Assert.That(rules.Supports(new BoardEntityState(new EntityId(9), new BoardEntityDefinition(BoardLayer.Obstacle,
                EntityKind.Obstacle, "hard.stone"), new GridPosition(1, 0))), Is.False);
        }

        /// <summary>Invalid axe decisions cannot partially work an obstacle, consume a charge, emit noise or advance enemy phases.</summary>
        [TestCase("empty")]
        [TestCase("depleted")]
        [TestCase("stone")]
        [TestCase("actor")]
        [TestCase("shovel")]
        [TestCase("stale")]
        [TestCase("distant")]
        [TestCase("terminal")]
        public void RejectedAxeIsEffectFreeAndCannotChopArbitraryContent(string invalid)
        {
            BoardState board = CreateBoard();
            var run = new RunState("m54-reject", 54, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var rules = new ObstacleClearingRules(new[] { "soft.wood" }, 3, 1, 6, 2);
            var tools = new ToolUseQuery(new[] { new ToolAction("axe.basic", new ClearObstacleAction(rules, true), "Chop", 6, 2,
                ObstacleClearingRules.AxeNoiseFactId) });
            ToolInstanceState axe = run.CreateToolInstance(new ToolDefinition(invalid == "shovel" ? "shovel.basic" : "axe.basic", "Tool", 3));
            run.EquipTool(0, axe);
            var command = new UseToolCommand(0, axe.InstanceId, 3, WallId, new GridPosition(1, 0), "chop");
            if (invalid == "empty") run.RemoveTool(0);
            if (invalid == "depleted")
            {
                for (int charge = 0; charge < 3; charge++) run.TrySpendToolCharge(0, axe.InstanceId);
                command = new UseToolCommand(0, axe.InstanceId, 0, WallId, new GridPosition(1, 0), "chop");
            }
            if (invalid == "stone" || invalid == "actor")
            {
                board.TryRemove(WallId);
                Assert.That(board.TryAdd(new BoardEntityState(WallId, new BoardEntityDefinition(
                    invalid == "stone" ? BoardLayer.Obstacle : BoardLayer.Actor,
                    invalid == "stone" ? EntityKind.Obstacle : EntityKind.Enemy,
                    invalid == "stone" ? "hard.stone" : "soft.wood"), new GridPosition(1, 0))), Is.True);
            }
            if (invalid == "stale") board.TryMove(WallId, new GridPosition(2, 0));
            if (invalid == "distant")
            {
                board.TryMove(WallId, new GridPosition(3, 0));
                command = new UseToolCommand(0, axe.InstanceId, 3, WallId, new GridPosition(3, 0), "chop");
            }
            if (invalid == "terminal") run.MarkDead();
            var before = board.GetEntities();
            var equipment = run.ToolSlots.ToArray();
            BoardNoise old = board.EmitNoise(PlayerId, new GridPosition(0, 0), 1, 3);
            int laterPhases = 0;
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(tools: tools), new TurnPhaseHandlers(
                (state, activeRun, actor, events) => laterPhases++));
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Accepted || result.ConsumesTurn, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(board.GetEntities(), Is.EqualTo(before));
            Assert.That(board.GetObstacleWork(WallId), Is.Zero);
            Assert.That(run.ToolSlots, Is.EqualTo(equipment));
            Assert.That(run.Food, Is.EqualTo(20));
            Assert.That(board.GetActiveNoises().Single(), Is.SameAs(old));
            Assert.That(old.RemainingTurns, Is.EqualTo(3));
            Assert.That(laterPhases, Is.Zero);
        }

        /// <summary>Manual progress and a later axe use share one obstacle counter; a stale tool command cannot repeat the effect.</summary>
        [Test]
        public void AxeFinishesExistingManualProgressAndRejectsDuplicateWithoutRefill()
        {
            BoardState board = CreateBoard();
            var run = new RunState("m54-mixed", 54, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var rules = new ObstacleClearingRules(new[] { "soft.wood" }, 4, 0, 5, 3);
            var interactions = new InteractionQuery(new[] { new ClearObstacleAction(rules, false) });
            var tools = new ToolUseQuery(new[] { new ToolAction("axe.basic", new ClearObstacleAction(rules, true), "Chop", 5, 3) });
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3)));
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(ActionNoiseProfile.Prototype, interactions, tools));
            Assert.That(controller.Resolve(interactions.GetOptions(board, run, PlayerId).Single().CreateCommand()).Accepted, Is.True);
            Assert.That(board.GetObstacleWork(WallId), Is.EqualTo(1));
            Assert.That(board.GetActiveNoises(), Is.Empty, "Explicit silent manual config does not borrow generic interaction noise.");
            Assert.That(interactions.GetOptions(board, run, PlayerId).Single().EffectLabel, Is.EqualTo("Clear by hand: 3 actions left"));
            UseToolCommand selection = tools.GetOptions(board, run, PlayerId).Single().CreateCommand();
            Assert.That(controller.Resolve(selection).Accepted, Is.True);
            Assert.That(controller.Resolve(selection).Accepted, Is.False);
            Assert.That(board.TryGetEntity(WallId, out _), Is.False);
            Assert.That(run.Food, Is.EqualTo(18));
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
        }

        private static BoardState CreateBoard()
        {
            var board = new BoardState(new GridBounds(0, 0, 4, 0));
            for (int horizontal = 0; horizontal <= 4; horizontal++)
                Assert.That(board.TryAdd(new BoardEntityState(new EntityId(10 + horizontal),
                    new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "floor", new BoardEntityTraits(true, horizontal == 4, false)),
                    new GridPosition(horizontal, 0))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(PlayerId,
                new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"), new GridPosition(0, 0))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(WallId,
                new BoardEntityDefinition(BoardLayer.Obstacle, EntityKind.Obstacle, "soft.wood"), new GridPosition(1, 0))), Is.True);
            return board;
        }
    }
}