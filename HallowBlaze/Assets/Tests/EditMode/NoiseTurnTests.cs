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
    /// <summary>Verifies accepted/rejected noise effects, phase ordering and deterministic command sequences.</summary>
    public sealed class NoiseTurnTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);

        /// <summary>An accepted movement emits the exact board snapshot after moving and charging once.</summary>
        [Test]
        public void AcceptedMovePublishesOriginalNoise()
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(ActionNoiseProfile.Prototype));
            TurnResult result = controller.Resolve(new MoveCommand(Direction.East));
            NoiseEvent emitted = result.Events.OfType<NoiseEvent>().Single();
            Assert.That(result.Accepted, Is.True);
            Assert.That(run.Food, Is.EqualTo(9));
            Assert.That(emitted.Noise, Is.SameAs(board.GetActiveNoises().Single()));
            Assert.That(emitted.Noise.SourceId, Is.EqualTo(PlayerId));
            Assert.That(emitted.Noise.Position, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(emitted.Noise.Strength, Is.EqualTo(2));
            Assert.That(emitted.Noise.RemainingTurns, Is.EqualTo(2));
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
        }

        /// <summary>Blocked, invalid and terminal commands neither emit nor age noise or consume resources.</summary>
        [TestCase("blocked")]
        [TestCase("invalid")]
        [TestCase("terminal")]
        public void RejectionIsNoiseAndCostFree(string reason)
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(ActionNoiseProfile.Prototype));
            if (reason == "terminal")
            {
                run.ConsumeFood(9);
                controller.Resolve(new WaitCommand());
            }
            BoardNoise noise = board.EmitNoise(PlayerId, new GridPosition(1, 1), 1, 1);
            int food = run.Food;
            PlayerCommand command = reason == "blocked" ? (PlayerCommand)new MoveCommand(Direction.West) : null;
            if (reason == "blocked")
                Assert.That(board.TryAdd(new BoardEntityState(new EntityId(800),
                    new BoardEntityDefinition(BoardLayer.Obstacle, EntityKind.Obstacle, "wall"), new GridPosition(0, 1))), Is.True);
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(food));
            Assert.That(board.GetActiveNoises().Single(), Is.SameAs(noise));
        }

        /// <summary>Wait remains silent, ages existing signals once and preserves an old event's immutable lifetime.</summary>
        [Test]
        public void SubsequentAcceptedCommandsExpireNoise()
        {
            BoardState board = CreateBoard();
            var controller = new TurnController(board, CreateRun(), PlayerId, new TurnResolver(ActionNoiseProfile.Prototype));
            NoiseEvent emitted = controller.Resolve(new MoveCommand(Direction.East)).Events.OfType<NoiseEvent>().Single();
            Assert.That(controller.Resolve(new WaitCommand()).Events.OfType<NoiseEvent>(), Is.Empty);
            Assert.That(board.GetActiveNoises().Single().RemainingTurns, Is.EqualTo(1));
            Assert.That(emitted.Noise.RemainingTurns, Is.EqualTo(2));
            controller.Resolve(new WaitCommand());
            Assert.That(board.GetActiveNoises(), Is.Empty);
        }

        /// <summary>Later emissions retain their full lifetime while older signals age before planning.</summary>
        [Test]
        public void OldNoiseAgesBeforeLaterPhasesButNewNoiseDoesNot()
        {
            BoardState board = CreateBoard();
            board.EmitNoise(PlayerId, new GridPosition(1, 1), 1, 1);
            var phases = new TurnPhaseHandlers(
                executeLockedIntents: (current, run, actor, events) =>
                {
                    Assert.That(current.GetActiveNoises().Count, Is.EqualTo(1));
                    Assert.That(current.GetActiveNoises()[0].RemainingTurns, Is.EqualTo(2));
                    Assert.That(current.GetActiveNoises()[0].Id, Is.EqualTo(1));
                });
            var controller = new TurnController(board, CreateRun(), PlayerId,
                new TurnResolver(ActionNoiseProfile.Prototype), phases);
            controller.Resolve(new MoveCommand(Direction.East));
        }

        /// <summary>Interaction uses configured strength at the actor, while invalid interaction is silent.</summary>
        [Test]
        public void InteractionUsesConfiguredProfile()
        {
            BoardState board = CreateBoard();
            var targetId = new EntityId(800);
            Assert.That(board.TryAdd(new BoardEntityState(targetId,
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "lever", new BoardEntityTraits(true, false, true)),
                new GridPosition(1, 2))), Is.True);
            var controller = new TurnController(board, CreateRun(), PlayerId, new TurnResolver(new ActionNoiseProfile(1, 5, 1)));
            NoiseEvent emitted = controller.Resolve(new InteractCommand(targetId)).Events.OfType<NoiseEvent>().Single();
            Assert.That(emitted.Noise.Strength, Is.EqualTo(5));
            Assert.That(emitted.Noise.Position, Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(controller.Resolve(new InteractCommand(new EntityId(999))).Accepted, Is.False);
            Assert.That(board.GetActiveNoises().Single(), Is.SameAs(emitted.Noise));
        }

        /// <summary>Starvation still emits an accepted action's noise before its terminal outcome.</summary>
        [Test]
        public void TerminalAcceptedMoveKeepsNoiseInAtomicResult()
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            run.ConsumeFood(9);
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(ActionNoiseProfile.Prototype));
            TurnResult result = controller.Resolve(new MoveCommand(Direction.East));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType),
                Is.EqualTo(new[] { "EntityMoved", "ActionCostApplied", "Noise", "PlayerStarved" }));
            Assert.That(controller.IsTerminal, Is.True);
        }

        /// <summary>Explicit commands reproduce the same noise IDs, cells and lifetimes on fresh boards.</summary>
        [Test]
        public void CommandSequenceRepeatsExactly()
        {
            string first = RunSequence();
            Assert.That(RunSequence(), Is.EqualTo(first));
        }

        /// <summary>Silent defaults preserve existing event contracts and reject malformed profile data eagerly.</summary>
        [Test]
        public void SilentDefaultsAndProfileValidationAreExplicit()
        {
            BoardState board = CreateBoard();
            TurnResult result = new TurnResolver().Resolve(board, CreateRun(), PlayerId, new MoveCommand(Direction.East));
            Assert.That(result.Events.OfType<NoiseEvent>(), Is.Empty);
            Assert.That(board.GetActiveNoises(), Is.Empty);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActionNoiseProfile(-1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActionNoiseProfile(1, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActionNoiseProfile(1, 1, 0));
            Assert.Throws<ArgumentNullException>(() => new NoiseEvent(null));
        }

        private static string RunSequence()
        {
            BoardState board = CreateBoard();
            var controller = new TurnController(board, CreateRun(), PlayerId, new TurnResolver(ActionNoiseProfile.Prototype));
            var sequence = new System.Collections.Generic.List<string>();
            foreach (PlayerCommand command in new PlayerCommand[] { new MoveCommand(Direction.East), new WaitCommand(), new MoveCommand(Direction.North) })
            {
                controller.Resolve(command);
                sequence.Add(string.Join(";", board.GetActiveNoises().Select(noise =>
                    $"{noise.Id}:{noise.Position}:{noise.Strength}:{noise.RemainingTurns}:{string.Join(",", noise.AudibleCells)}")));
            }
            return string.Join("|", sequence);
        }

        private static RunState CreateRun() => new RunState("m47-noise", 47, new RunStateConfiguration(10, 10, 0, "forest.start"));

        private static BoardState CreateBoard()
        {
            var board = new BoardState();
            for (int cellIndex = 0; cellIndex < 64; cellIndex++)
                Assert.That(board.TryAdd(new BoardEntityState(new EntityId(100 + cellIndex),
                    new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "ground", new BoardEntityTraits(true, false, false)),
                    new GridPosition(cellIndex / 8, cellIndex % 8))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(PlayerId,
                new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"), new GridPosition(1, 1))), Is.True);
            return board;
        }
    }
}