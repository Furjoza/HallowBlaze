using System;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies atomic single-obstacle pushes through authoritative layered occupancy, never physics.</summary>
    public sealed class BoulderPushTests
    {
        /// <summary>Both layers update together while old snapshots and an independent clone remain unchanged.</summary>
        [Test]
        public void BoardPushCommitsBothPositionsAndPreservesClone()
        {
            BoardState board = Board();
            BoardState clone = board.Clone();
            var before = board.GetEntities().ToArray();
            Assert.That(board.CanPush(new EntityId(1), new EntityId(2)), Is.True);
            Assert.That(board.GetEntities(), Is.EqualTo(before));
            Assert.That(board.TryPush(new EntityId(1), new EntityId(2)), Is.True);
            Assert.That(board.TryGetEntity(BoardLayer.Actor, new GridPosition(2, 1), out BoardEntityState player), Is.True);
            Assert.That(player.Id, Is.EqualTo(new EntityId(1)));
            Assert.That(board.TryGetEntity(BoardLayer.Obstacle, new GridPosition(3, 1), out BoardEntityState boulder), Is.True);
            Assert.That(boulder.Id, Is.EqualTo(new EntityId(2)));
            Assert.That(board.TryGetEntity(BoardLayer.Actor, new GridPosition(1, 1), out _), Is.False);
            Assert.That(board.TryGetEntity(BoardLayer.Obstacle, new GridPosition(2, 1), out _), Is.False);
            Assert.That(clone.GetEntities(), Is.EqualTo(before));
        }

        /// <summary>Missing/blocked terrain, exits and occupied cells cannot partially advance either participant.</summary>
        [TestCase("obstacle")]
        [TestCase("actor")]
        [TestCase("item")]
        [TestCase("missing-terrain")]
        [TestCase("blocked-terrain")]
        [TestCase("exit")]
        [TestCase("target-actor")]
        [TestCase("target-item")]
        [TestCase("source-missing-terrain")]
        [TestCase("source-blocked-terrain")]
        [TestCase("source-exit")]
        public void RejectedBoardPushPreservesEveryEntity(string block)
        {
            BoardState board = Board();
            var destination = new GridPosition(3, 1);
            if (block == "obstacle" || block == "actor" || block == "item" || block.StartsWith("target-", StringComparison.Ordinal))
            {
                BoardLayer layer = block == "obstacle" ? BoardLayer.Obstacle : block.EndsWith("actor", StringComparison.Ordinal) ? BoardLayer.Actor : BoardLayer.Item;
                var position = block.StartsWith("target-", StringComparison.Ordinal) ? new GridPosition(2, 1) : destination;
                board.TryAdd(new BoardEntityState(new EntityId(200), new BoardEntityDefinition(layer, EntityKind.Obstacle, "blocker"), position));
            }
            else
            {
                GridPosition position = block.StartsWith("source-", StringComparison.Ordinal) ? new GridPosition(2, 1) : destination;
                board.TryGetEntity(BoardLayer.Terrain, position, out BoardEntityState terrain);
                board.TryRemove(terrain.Id);
                if (!block.EndsWith("missing-terrain", StringComparison.Ordinal)) board.TryAdd(new BoardEntityState(terrain.Id,
                    new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "changed",
                        new BoardEntityTraits(block.EndsWith("exit", StringComparison.Ordinal), block.EndsWith("exit", StringComparison.Ordinal), false)), position));
            }
            var before = board.GetEntities().ToArray();
            Assert.That(board.CanPush(new EntityId(1), new EntityId(2)), Is.False);
            Assert.That(board.TryPush(new EntityId(1), new EntityId(2)), Is.False);
            Assert.That(board.GetEntities(), Is.EqualTo(before));
        }

        /// <summary>The shared query commits both positions for one normal action and emits noise at the advanced player cell.</summary>
        [Test]
        public void ExplicitPushHasOneCostAndNoiseThenRejectsReplay()
        {
            BoardState board = Board();
            var run = new RunState("boulder", 62, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var query = new InteractionQuery();
            InteractionOption option = query.GetOptions(board, run, new EntityId(1)).Single();
            Assert.That(option.ActionId, Is.EqualTo(BoulderPushAction.ActionId));
            Assert.That(option.EffectLabel, Does.Contain("advance one cell"));
            Assert.That(option.NoiseStrength, Is.EqualTo(4));
            var resolver = new TurnResolver(ActionNoiseProfile.Prototype, query);
            TurnResult result = resolver.Resolve(board, run, new EntityId(1), option.CreateCommand());
            Assert.That(result.Accepted && result.ConsumesTurn, Is.True);
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "InteractionPerformed", "BoulderPushed", "ActionCostApplied", "Noise" }));
            BoulderPushedEvent pushed = result.Events.OfType<BoulderPushedEvent>().Single();
            Assert.That(pushed.PlayerFrom, Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(pushed.PlayerTo, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(pushed.BoulderTo, Is.EqualTo(new GridPosition(3, 1)));
            Assert.That(board.GetActiveNoises().Single().Position, Is.EqualTo(pushed.PlayerTo));
            Assert.That(run.Food, Is.EqualTo(19));
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(resolver.Resolve(board, run, new EntityId(1), option.CreateCommand()).Accepted, Is.False);
            Assert.That(run.Food, Is.EqualTo(19));
            Assert.That(board.GetActiveNoises().Count, Is.EqualTo(1));
        }

        /// <summary>All four cardinal pushes use the same atomic grid operation, including a walkable plate-like terrain cell without activation rules.</summary>
        [TestCase("North")]
        [TestCase("East")]
        [TestCase("South")]
        [TestCase("West")]
        public void EveryCardinalDirectionCanAdvanceOntoWalkableAuthoredTerrain(string directionName)
        {
            Direction direction = directionName == "North" ? Direction.North : directionName == "East" ? Direction.East :
                directionName == "South" ? Direction.South : Direction.West;
            BoardState board = Board();
            var boulderCell = new GridPosition(2, 1);
            GridPosition destination = boulderCell.Move(direction);
            var playerCell = new GridPosition(2 * boulderCell.X - destination.X, 2 * boulderCell.Y - destination.Y);
            Assert.That(board.TryMove(new EntityId(1), playerCell), Is.True);
            board.TryGetEntity(BoardLayer.Terrain, destination, out BoardEntityState terrain);
            board.TryRemove(terrain.Id);
            board.TryAdd(new BoardEntityState(terrain.Id, new BoardEntityDefinition(BoardLayer.Terrain,
                new EntityKind("terrain"), "landmark.plate", new BoardEntityTraits(true, false, false)), destination));
            var run = CreateRun();
            TurnResult result = new TurnController(board, run, new EntityId(1)).Resolve(
                new InteractCommand(new EntityId(2), boulderCell, BoulderPushAction.ActionId));
            Assert.That(result.Accepted, Is.True);
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(new EntityId(1))).Position, Is.EqualTo(boulderCell));
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(new EntityId(2))).Position, Is.EqualTo(destination));
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(terrain.Id)).Definition.ContentId, Is.EqualTo("landmark.plate"));
            Assert.That(run.Food, Is.EqualTo(19));
        }

        /// <summary>Out-of-bounds, overflow, diagonal, same-cell and wrong identities fail without changing either layer.</summary>
        [TestCase("bounds")]
        [TestCase("overflow")]
        [TestCase("diagonal")]
        [TestCase("same-cell")]
        [TestCase("missing-player")]
        [TestCase("missing-boulder")]
        [TestCase("wrong-layer")]
        public void InvalidGeometryAndIdentitiesAreAtomic(string scenario)
        {
            BoardState board = Board();
            if (scenario == "overflow")
            {
                board = new BoardState(new GridBounds(int.MaxValue - 1, 0, int.MaxValue, 0));
                board.TryAdd(new BoardEntityState(new EntityId(1), new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"),
                    new GridPosition(int.MaxValue - 1, 0)));
                board.TryAdd(new BoardEntityState(new EntityId(2), BoulderPushAction.Definition, new GridPosition(int.MaxValue, 0)));
            }
            else if (scenario == "bounds")
            {
                board.TryMove(new EntityId(1), new GridPosition(3, 1));
                board.TryMove(new EntityId(2), new GridPosition(4, 1));
            }
            else if (scenario == "diagonal") board.TryMove(new EntityId(1), new GridPosition(1, 0));
            else if (scenario == "same-cell") board.TryMove(new EntityId(1), new GridPosition(2, 1));
            EntityId playerId = new EntityId(scenario == "missing-player" ? 999 : 1);
            EntityId boulderId = new EntityId(scenario == "missing-boulder" ? 999 : scenario == "wrong-layer" ? 10 : 2);
            var before = board.GetEntities().ToArray();
            Assert.That(board.CanPush(playerId, boulderId), Is.False);
            Assert.That(board.TryPush(playerId, boulderId), Is.False);
            Assert.That(board.GetEntities(), Is.EqualTo(before));
        }

        /// <summary>A moved, removed or newly blocked target invalidates a captured option without aging noise, running phases or spending resources.</summary>
        [TestCase("moved")]
        [TestCase("removed")]
        [TestCase("blocked")]
        public void StaleCommandIsFreeAndDoesNotAdvancePhases(string scenario)
        {
            BoardState board = Board();
            RunState run = CreateRun();
            InteractCommand command = new InteractionQuery().GetOptions(board, run, new EntityId(1)).Single().CreateCommand();
            board.EmitNoise(new EntityId(1), new GridPosition(1, 1), 2, 3);
            if (scenario == "moved") board.TryMove(new EntityId(2), new GridPosition(3, 1));
            else if (scenario == "removed") board.TryRemove(new EntityId(2));
            else board.TryAdd(new BoardEntityState(new EntityId(200), BoulderPushAction.Definition, new GridPosition(3, 1)));
            var before = board.GetEntities().ToArray();
            var noises = board.GetActiveNoises().ToArray();
            int phases = 0;
            TurnPhaseHandler phase = (state, activeRun, playerId, events) => phases++;
            var controller = new TurnController(board, run, new EntityId(1), phases: new TurnPhaseHandlers(phase, phase, phase));
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Accepted || result.ConsumesTurn, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(phases, Is.Zero);
            Assert.That(run.Food, Is.EqualTo(20));
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(board.GetEntities(), Is.EqualTo(before));
            Assert.That(board.GetActiveNoises(), Is.EqualTo(noises));
        }

        /// <summary>Accepted pushes run each later phase once on the committed occupancy; starvation keeps the push but stops later phases.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CommittedOccupancyPrecedesEveryLaterPhaseAndTerminalCheck(bool starvation)
        {
            BoardState board = Board();
            RunState run = CreateRun(starvation ? 1 : 20);
            int phases = 0;
            TurnPhaseHandler phase = (state, activeRun, playerId, events) =>
            {
                Assert.That(state.TryGetEntity(BoardLayer.Actor, new GridPosition(2, 1), out BoardEntityState player), Is.True);
                Assert.That(player.Id, Is.EqualTo(playerId));
                Assert.That(state.TryGetEntity(BoardLayer.Obstacle, new GridPosition(3, 1), out _), Is.True);
                Assert.That(state.TryGetEntity(BoardLayer.Obstacle, new GridPosition(2, 1), out _), Is.False);
                phases++;
            };
            var controller = new TurnController(board, run, new EntityId(1), phases: new TurnPhaseHandlers(phase, phase, phase));
            TurnResult result = controller.Resolve(new InteractCommand(new EntityId(2), new GridPosition(2, 1), BoulderPushAction.ActionId));
            Assert.That(result.Accepted && result.ConsumesTurn, Is.True);
            Assert.That(result.Events.OfType<BoulderPushedEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<NoiseEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<PlayerStarvedEvent>().Count(), Is.EqualTo(starvation ? 1 : 0));
            Assert.That(phases, Is.EqualTo(starvation ? 0 : 3));
            Assert.That(controller.IsTerminal, Is.EqualTo(starvation));
            Assert.That(run.Food, Is.EqualTo(starvation ? 0 : 19));
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(new EntityId(1))).Position, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(new EntityId(2))).Position, Is.EqualTo(new GridPosition(3, 1)));
        }

        /// <summary>A retained enemy step invalidated by the moved boulder waits instead of replanning or attacking the advanced player.</summary>
        [Test]
        public void EnemySeesMovedBoulderBeforeExecutingItsRetainedStep()
        {
            BoardState board = Board();
            var enemyId = new EntityId(3);
            board.TryAdd(new BoardEntityState(enemyId, new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy"), new GridPosition(3, 2)));
            var enemy = new ShamblerState(enemyId, new ShamblerDefinition(10));
            enemy.LockIntent(new EnemyIntent(enemyId, EnemyIntentKind.Move, new GridPosition(3, 1), new EntityId(1), true));
            var batch = new EnemyBatchTurnPhases(new[] { enemy });
            RunState run = CreateRun();
            var controller = new TurnController(board, run, new EntityId(1), phases: batch.Handlers);
            TurnResult result = controller.Resolve(new InteractCommand(new EntityId(2), new GridPosition(2, 1), BoulderPushAction.ActionId));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "InteractionPerformed", "BoulderPushed", "ActionCostApplied", "Noise", "EntityWaited" }));
            Assert.That(result.Events.OfType<EntityWaitedEvent>().Single().EntityId, Is.EqualTo(enemyId));
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(enemyId)).Position, Is.EqualTo(new GridPosition(3, 2)));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(run.Health, Is.EqualTo(100));
            BoardNoise noise = board.GetActiveNoises().Single();
            Assert.That(noise.IsAudible(new GridPosition(2, 1)), Is.True);
            Assert.That(noise.IsAudible(new GridPosition(3, 1)), Is.False);
        }

        /// <summary>A fresh path query immediately sees the freed cell and advanced player rather than a cached route around the old boulder.</summary>
        [Test]
        public void PathPlannerSeesNewOccupancyImmediatelyAfterThePlayerPhase()
        {
            BoardState board = Board();
            var enemyId = new EntityId(3);
            board.TryAdd(new BoardEntityState(enemyId, new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy"), new GridPosition(2, 2)));
            var enemy = new ShamblerState(enemyId, new ShamblerDefinition(10));
            var planner = new ShamblerPlanner();
            EnemyIntent before = planner.Plan(board, enemy, new EntityId(1));
            Assert.That(before.Kind, Is.EqualTo(EnemyIntentKind.Move));
            Assert.That(before.TargetPosition, Is.EqualTo(new GridPosition(1, 2)));
            Assert.That(new TurnResolver().Resolve(board, CreateRun(), new EntityId(1),
                new InteractCommand(new EntityId(2), new GridPosition(2, 1), BoulderPushAction.ActionId)).Accepted, Is.True);
            EnemyIntent after = planner.Plan(board, enemy, new EntityId(1));
            Assert.That(after.Kind, Is.EqualTo(EnemyIntentKind.Attack));
            Assert.That(after.TargetPosition, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(before.TargetPosition, Is.EqualTo(new GridPosition(1, 2)));
            Assert.That(board.TryGetEntity(BoardLayer.Obstacle, new GridPosition(3, 1), out _), Is.True);
        }

        private static RunState CreateRun(int food = 20) => new RunState("boulder", 62, new RunStateConfiguration(100, food, 0, "forest.start"));

        private static BoardState Board()
        {
            var board = new BoardState(new GridBounds(0, 0, 4, 2));
            int nextId = 10;
            for (int horizontal = 0; horizontal < 5; horizontal++)
            for (int vertical = 0; vertical < 3; vertical++)
                board.TryAdd(new BoardEntityState(new EntityId(nextId++), new BoardEntityDefinition(BoardLayer.Terrain,
                    new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false)), new GridPosition(horizontal, vertical)));
            board.TryAdd(new BoardEntityState(new EntityId(1), new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"), new GridPosition(1, 1)));
            board.TryAdd(new BoardEntityState(new EntityId(2), new BoardEntityDefinition(BoardLayer.Obstacle, EntityKind.Obstacle,
                "landmark.boulder", new BoardEntityTraits(false, false, true)), new GridPosition(2, 1)));
            return board;
        }
    }
}