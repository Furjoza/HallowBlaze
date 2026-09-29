using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies complete turn ordering, terminal boundaries, and exclusive resolution.</summary>
    public sealed class TurnControllerTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);
        private static readonly GridPosition Start = new GridPosition(1, 1);
        private static readonly GridPosition Destination = new GridPosition(2, 1);

        /// <summary>Player effects and one cost precede locked intents, environment, planning, and unlock.</summary>
        [Test]
        public void AcceptedTurnOrdersEveryPhaseAndChargesOnce()
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            TurnController controller = null;
            var phases = new TurnPhaseHandlers(
                (b, r, p, events) =>
                {
                    Assert.That(controller.IsResolving, Is.True);
                    Assert.That(r.Food, Is.EqualTo(4));
                    Assert.That(b.TryGetEntity(p, out BoardEntityState player), Is.True);
                    Assert.That(player.Position, Is.EqualTo(Destination));
                    events.Add(new MarkerEvent("LockedIntents"));
                },
                (b, r, p, events) =>
                {
                    Assert.That(controller.IsResolving, Is.True);
                    events.Add(new MarkerEvent("Environment"));
                },
                (b, r, p, events) =>
                {
                    Assert.That(controller.IsResolving, Is.True);
                    events.Add(new MarkerEvent("NextIntents"));
                });
            controller = new TurnController(board, run, PlayerId, phases: phases);

            TurnResult result = controller.Resolve(new MoveCommand(Direction.East));

            Assert.That(result.Accepted && result.ConsumesTurn, Is.True);
            AssertEvents(result, "EntityMoved", "ActionCostApplied", "LockedIntents", "Environment", "NextIntents");
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(controller.IsResolving || controller.IsTerminal, Is.False);
        }

        /// <summary>Rejection preserves all state and unlocks input without invoking later phases.</summary>
        [Test]
        public void RejectedCommandHasNoEffectsAndAllowsNextCommand()
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            var calls = new List<string>();
            var controller = new TurnController(board, run, PlayerId, phases: RecordingPhases(calls));
            string before = Snapshot(board, run);

            TurnResult rejected = controller.Resolve(new MoveCommand(Direction.West));

            AssertRejected(rejected, CommandRejectionCode.Blocked);
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
            Assert.That(calls, Is.Empty);
            Assert.That(controller.IsResolving || controller.IsTerminal, Is.False);
            Assert.That(controller.Resolve(new WaitCommand()).Accepted, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "LockedIntents", "Environment", "NextIntents" }));
            Assert.That(run.Food, Is.EqualTo(4));
        }

        /// <summary>Null input is rejected by the player boundary with no later phases.</summary>
        [Test]
        public void NullCommandIsRejectedWithoutCost()
        {
            RunState run = CreateRun();
            var controller = new TurnController(CreateBoard(), run, PlayerId, phases: ForbiddenPhases());
            AssertRejected(controller.Resolve(null), CommandRejectionCode.InvalidCommand);
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(controller.IsResolving, Is.False);
        }

        /// <summary>Immediate starvation ends the board before enemies, environment, or planning.</summary>
        [Test]
        public void StarvationStopsLaterPhasesAndFurtherCommands()
        {
            RunState run = CreateRun(food: 1);
            var controller = new TurnController(CreateBoard(), run, PlayerId, phases: ForbiddenPhases());
            TurnResult result = controller.Resolve(new WaitCommand());
            AssertEvents(result, "EntityWaited", "ActionCostApplied", "PlayerStarved");
            Assert.That(controller.IsTerminal, Is.True);
            Assert.That(controller.IsResolving, Is.False);
            AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active), "Lifecycle owns run completion.");
        }

        /// <summary>A live exit ends the board before enemies even though the run remains active.</summary>
        [Test]
        public void ExitStopsLaterPhasesAndFurtherCommands()
        {
            RunState run = CreateRun();
            var controller = new TurnController(CreateBoard(exit: true), run, PlayerId, phases: ForbiddenPhases());
            AssertEvents(controller.Resolve(new MoveCommand(Direction.East)), "EntityMoved", "ActionCostApplied", "ExitReached");
            Assert.That(controller.IsTerminal, Is.True);
            AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active));
        }

        /// <summary>O-002 resolves starvation ahead of an exit reached by the same action.</summary>
        [Test]
        public void SimultaneousExitAndStarvationReportsOnlyStarvation()
        {
            var controller = new TurnController(CreateBoard(exit: true), CreateRun(1), PlayerId, phases: ForbiddenPhases());
            AssertEvents(controller.Resolve(new MoveCommand(Direction.East)), "EntityMoved", "ActionCostApplied", "PlayerStarved");
            Assert.That(controller.IsTerminal, Is.True);
        }

        /// <summary>A food reward precedes cost and can preserve a live exit.</summary>
        [Test]
        public void FoodOnExitRescuesPlayerBeforeActionCost()
        {
            BoardState board = CreateBoard(exit: true, reward: 2);
            RunState run = CreateRun(1);
            var controller = new TurnController(board, run, PlayerId, phases: ForbiddenPhases());
            AssertEvents(controller.Resolve(new MoveCommand(Direction.East)),
                "EntityMoved", "ItemCollected", "FoodRestored", "ActionCostApplied", "ExitReached");
            Assert.That(run.Food, Is.EqualTo(2));
            Assert.That(board.TryGetEntity(new EntityId(200), out _), Is.False);
        }

        /// <summary>Health death during either later phase is reported at phase 7 and prevents planning.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LaterHealthDeathSkipsPlanning(bool duringEnvironment)
        {
            var calls = new List<string>();
            var phases = new TurnPhaseHandlers(
                (b, r, p, events) =>
                {
                    calls.Add("LockedIntents");
                    if (!duringEnvironment) r.TakeDamage(r.Health);
                },
                (b, r, p, events) =>
                {
                    calls.Add("Environment");
                    if (duringEnvironment) r.TakeDamage(r.Health);
                },
                FailPhase);
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, phases: phases);
            TurnResult result = controller.Resolve(new WaitCommand());
            Assert.That(calls, Is.EqualTo(new[] { "LockedIntents", "Environment" }));
            AssertEvents(result, "EntityWaited", "ActionCostApplied", "PlayerDied");
            Assert.That(((PlayerDiedEvent)result.Events[2]).EntityId, Is.EqualTo(PlayerId));
            Assert.That(controller.IsTerminal, Is.True);
        }

        /// <summary>Starvation or an explicit dead status after environment is terminal without duplicate events.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LaterStarvationOrDeadStatusEndsBoard(bool explicitDeadStatus)
        {
            var phases = new TurnPhaseHandlers(applyEnvironment: (b, r, p, events) =>
            {
                if (explicitDeadStatus) r.MarkDead();
                else r.ConsumeFood(r.Food);
            }, planNextIntents: FailPhase);
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, phases: phases);
            AssertEvents(controller.Resolve(new WaitCommand()), "EntityWaited", "ActionCostApplied",
                explicitDeadStatus ? "PlayerDied" : "PlayerStarved");
            Assert.That(controller.IsTerminal, Is.True);
            AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
        }

        /// <summary>The second terminal check respects live exit and death precedence over a simultaneous exit.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LaterExitIsTerminalUnlessPlayerDies(bool dies)
        {
            var phases = new TurnPhaseHandlers(applyEnvironment: (b, r, p, events) =>
            {
                events.Add(new ExitReachedEvent(p, new EntityId(101)));
                if (dies) r.ConsumeFood(r.Food);
            }, planNextIntents: FailPhase);
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, phases: phases);
            AssertEvents(controller.Resolve(new WaitCommand()), "EntityWaited", "ActionCostApplied",
                dies ? "PlayerStarved" : "ExitReached");
            Assert.That(controller.IsTerminal, Is.True);
        }

        /// <summary>A future command uses the same pipeline with its own validated cost policy.</summary>
        [Test]
        public void NewCommandTypeAndDifferentCostNeedNoOrchestrationChange()
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            var calls = new List<string>();
            var resolver = new CallbackResolver((b, r, p, command) =>
            {
                if (!(command is CustomCommand custom) || !custom.Valid)
                    return new RejectedTurnResult(CommandRejectionCode.InvalidCommand);
                r.ConsumeFood(2);
                return new AcceptedTurnResult(new GameEvent[] { new MarkerEvent("CustomEffect"), new ActionCostAppliedEvent(p, 2) });
            });
            var controller = new TurnController(board, run, PlayerId, resolver, RecordingPhases(calls));
            string before = Snapshot(board, run);
            AssertRejected(controller.Resolve(new CustomCommand(false)), CommandRejectionCode.InvalidCommand);
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
            Assert.That(calls, Is.Empty);

            TurnResult result = controller.Resolve(new CustomCommand(true));

            AssertEvents(result, "CustomEffect", "ActionCostApplied", "LockedIntents", "Environment", "NextIntents");
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(((ActionCostAppliedEvent)result.Events[1]).CostAmount, Is.EqualTo(2));
        }

        /// <summary>Nested commands from the player resolver or later phases cannot start another turn.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ReentrantResolutionIsRejectedAcrossAllPhases(int stage)
        {
            RunState run = CreateRun();
            TurnController controller = null;
            Action tryNested = () =>
            {
                int foodBefore = run.Food;
                AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
                Assert.That(run.Food, Is.EqualTo(foodBefore));
                Assert.That(controller.IsResolving, Is.True);
            };
            var resolver = new CallbackResolver((b, r, p, c) =>
            {
                if (stage == 0) tryNested();
                return new TurnResolver().Resolve(b, r, p, c);
            });
            var phases = new TurnPhaseHandlers(
                (b, r, p, e) => { if (stage == 1) tryNested(); },
                (b, r, p, e) => { if (stage == 2) tryNested(); },
                (b, r, p, e) => { if (stage == 3) tryNested(); });
            controller = new TurnController(CreateBoard(), run, PlayerId, resolver, phases);
            Assert.That(controller.Resolve(new WaitCommand()).Accepted, Is.True);
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(controller.IsResolving, Is.False);
        }

        /// <summary>A truly concurrent call is rejected while the first resolver owns the board.</summary>
        [Test]
        public void ConcurrentCallCannotEnterPlayerResolver()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                int count = 0;
                RunState run = CreateRun();
                var resolver = new CallbackResolver((b, r, p, c) =>
                {
                    Interlocked.Increment(ref count);
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                    return new TurnResolver().Resolve(b, r, p, c);
                });
                var controller = new TurnController(CreateBoard(), run, PlayerId, resolver);
                Task<TurnResult> first = Task.Run(() => controller.Resolve(new WaitCommand()));
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
                    AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
                    Assert.That(count, Is.EqualTo(1));
                }
                finally
                {
                    release.Set();
                    Assert.That(first.Wait(TimeSpan.FromSeconds(10)), Is.True);
                }
                Assert.That(first.Result.Accepted, Is.True);
                Assert.That(run.Food, Is.EqualTo(4));
                Assert.That(controller.IsResolving, Is.False);
            }
        }

        /// <summary>Implementation exceptions propagate while the resolution lock is always released.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ExceptionInAnyPhaseReleasesLock(int stage)
        {
            bool fail = true;
            Action throwOnce = () => { if (fail) throw new InvalidOperationException("Test phase fault."); };
            var resolver = new CallbackResolver((b, r, p, c) =>
            {
                if (stage == 0) throwOnce();
                return new TurnResolver().Resolve(b, r, p, c);
            });
            var phases = new TurnPhaseHandlers(
                (b, r, p, e) => { if (stage == 1) throwOnce(); },
                (b, r, p, e) => { if (stage == 2) throwOnce(); },
                (b, r, p, e) => { if (stage == 3) throwOnce(); });
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, resolver, phases);
            Assert.Throws<InvalidOperationException>(() => controller.Resolve(new WaitCommand()));
            Assert.That(controller.IsResolving, Is.False);
            fail = false;
            Assert.That(controller.Resolve(new WaitCommand()).Accepted, Is.True);
        }

        /// <summary>Returned events are detached from the phase collection and subsequent turns.</summary>
        [Test]
        public void ResultOwnsImmutableEventSnapshot()
        {
            ICollection<GameEvent> retained = null;
            var phases = new TurnPhaseHandlers(planNextIntents: (b, r, p, e) => retained = e);
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, phases: phases);
            TurnResult first = controller.Resolve(new WaitCommand());
            retained.Add(new MarkerEvent("UnownedLateWrite"));
            controller.Resolve(new WaitCommand());
            AssertEvents(first, "EntityWaited", "ActionCostApplied");
        }

        /// <summary>The default phases give reproducible results without Unity or presentation.</summary>
        [Test]
        public void IdenticalInputsProduceIdenticalCompleteTurns()
        {
            var results = new List<string>();
            for (int repeat = 0; repeat < 2; repeat++)
            {
                BoardState board = CreateBoard();
                RunState run = CreateRun();
                var controller = new TurnController(board, run, PlayerId);
                TurnResult result = controller.Resolve(new MoveCommand(Direction.East));
                results.Add(Snapshot(board, run) + ";" + string.Join(",", result.Events.Select(e => e.EventType)));
            }
            Assert.That(results[0], Is.EqualTo(results[1]));
        }

        private static TurnPhaseHandlers ForbiddenPhases() => new TurnPhaseHandlers(FailPhase, FailPhase, FailPhase);

        private static void FailPhase(BoardState board, RunState run, EntityId playerId, ICollection<GameEvent> events)
        {
            Assert.Fail("This phase must not execute after rejection or the applicable terminal check.");
        }

        private static TurnPhaseHandlers RecordingPhases(List<string> calls)
        {
            TurnPhaseHandler Record(string name) => (b, r, p, events) =>
            {
                calls.Add(name);
                events.Add(new MarkerEvent(name));
            };
            return new TurnPhaseHandlers(Record("LockedIntents"), Record("Environment"), Record("NextIntents"));
        }

        private static BoardState CreateBoard(bool exit = false, int reward = 0)
        {
            var board = new BoardState();
            Add(board, 100, BoardLayer.Terrain, new EntityKind("terrain"), Start, new BoardEntityTraits(true, false, false));
            Add(board, 101, BoardLayer.Terrain, new EntityKind("terrain"), Destination, new BoardEntityTraits(true, exit, false));
            Add(board, 1, BoardLayer.Actor, EntityKind.Player, Start, BoardEntityTraits.Default);
            if (reward > 0) Add(board, 200, BoardLayer.Item, EntityKind.Item, Destination, BoardEntityTraits.Default, reward);
            return board;
        }

        private static void Add(BoardState board, int id, BoardLayer layer, EntityKind kind,
            GridPosition position, BoardEntityTraits traits, int reward = 0)
        {
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(id),
                new BoardEntityDefinition(layer, kind, "test." + id, traits, reward), position)), Is.True);
        }

        private static RunState CreateRun(int food = 5) =>
            new RunState("m3.5-test-run", 3500, new RunStateConfiguration(10, food, 0, "forest.start"));

        private static void AssertEvents(TurnResult result, params string[] names) =>
            Assert.That(result.Events.Select(e => e.EventType), Is.EqualTo(names));

        private static void AssertRejected(TurnResult result, CommandRejectionCode code)
        {
            Assert.That(result, Is.TypeOf<RejectedTurnResult>());
            Assert.That(((RejectedTurnResult)result).RejectionCode, Is.EqualTo(code));
            Assert.That(result.Accepted || result.ConsumesTurn, Is.False);
            Assert.That(result.Events, Is.Empty);
        }

        private static string Snapshot(BoardState board, RunState run) =>
            string.Join("|", board.GetEntities().Select(e => $"{e.Id.Value}:{e.Position.X}:{e.Position.Y}")) +
            $";{run.Food};{run.Health};{run.Status}";

        private sealed class CallbackResolver : IPlayerPhaseResolver
        {
            private readonly Func<BoardState, RunState, EntityId, PlayerCommand, TurnResult> callback;
            internal CallbackResolver(Func<BoardState, RunState, EntityId, PlayerCommand, TurnResult> callback) => this.callback = callback;
            /// <inheritdoc />
            public TurnResult Resolve(BoardState boardState, RunState runState, EntityId playerId, PlayerCommand command) =>
                callback(boardState, runState, playerId, command);
        }

        private sealed class MarkerEvent : GameEvent
        {
            internal MarkerEvent(string name) => EventType = name;
            /// <inheritdoc />
            public override string EventType { get; }
        }

        private sealed class CustomCommand : PlayerCommand
        {
            internal CustomCommand(bool valid) => Valid = valid;
            internal bool Valid { get; }
            /// <inheritdoc />
            public override string CommandType => "Custom";
        }
    }
}
