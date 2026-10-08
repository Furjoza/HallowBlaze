using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Tests.PlayMode
{
    internal sealed class OrderedBoardPresentationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<BoardEventPresenter> presenters = new List<BoardEventPresenter>();
        private readonly List<BoardRuntime> runtimes = new List<BoardRuntime>();

        [TearDown]
        public void TearDown()
        {
            foreach (BoardEventPresenter presenter in presenters)
                presenter.Dispose();
            foreach (BoardRuntime runtime in runtimes)
                runtime.Dispose();
            foreach (GameObject instance in objects)
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
            presenters.Clear();
            runtimes.Clear();
            objects.Clear();
        }

        [UnityTest]
        public IEnumerator AnimationOnOff_HasIdenticalFinalViewsHudAndGate_AndBlocksRapidInput()
        {
            Fixture animated = CreateFixture(true);
            Fixture immediate = CreateFixture(false);
            Task<CommandSubmission> pending = animated.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East));
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(animated.Runtime.RunState.Food, Is.EqualTo(109));
            Assert.That(animated.FoodView.activeSelf, Is.True);
            for (int attempt = 0; attempt < 20; attempt++)
                Assert.That(animated.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status,
                    Is.EqualTo(CommandSubmissionStatus.Blocked));
            while (!pending.IsCompleted)
                yield return null;

            CommandSubmission snap = immediate.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East)).Result;
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(snap.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(animated.Runtime.Views[animated.Runtime.PlayerId].transform.position,
                Is.EqualTo(immediate.Runtime.Views[immediate.Runtime.PlayerId].transform.position));
            Assert.That(animated.Runtime.Views[animated.Runtime.PlayerId].transform.position, Is.EqualTo(new Vector3(1, 0, 3)));
            Assert.That(animated.FoodView.activeSelf, Is.False);
            Assert.That(immediate.FoodView.activeSelf, Is.False);
            Assert.That(animated.Runtime.RunState.Food, Is.EqualTo(immediate.Runtime.RunState.Food));
            CollectionAssert.AreEqual(animated.Sink.Trace, immediate.Sink.Trace);
            CollectionAssert.AreEqual(new[] { "EntityMoved", "Hud:100:109", "FoodRestored", "Hud:100:109" }, animated.Sink.Trace);
            Assert.That(animated.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(immediate.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(animated.Presenter.Coordinator.CanSubmit, Is.True);
            Assert.That(immediate.Presenter.Coordinator.CanSubmit, Is.True);
        }

        [UnityTest]
        public IEnumerator CancellationDuringMove_SnapsAllViewsAndHudWithoutApplyingAnotherTurn()
        {
            Fixture fixture = CreateFixture(true);
            using (var cancellation = new CancellationTokenSource())
            {
                Task<CommandSubmission> pending = fixture.Presenter.Coordinator.SubmitAsync(
                    new MoveCommand(Direction.East), cancellation.Token);
                cancellation.Cancel();
                while (!pending.IsCompleted)
                    yield return null;
                Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.PresentationFailed));
                Assert.That(pending.Result.Result.Accepted, Is.True);
                Assert.That(fixture.Sink.Diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.Canceled));
                AssertRecovered(fixture);
            }
        }

        [UnityTest]
        public IEnumerator DisposalDuringMove_CancelsReplayAndReleasesSubmissionOwnership()
        {
            Fixture fixture = CreateFixture(true);
            Task<CommandSubmission> pending = fixture.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East));
            fixture.Presenter.Dispose();
            while (!pending.IsCompleted)
                yield return null;
            Assert.That(pending.Result.Diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.Canceled));
            Assert.That(fixture.Presenter.Coordinator.Gate.State, Is.EqualTo(PresentationGateState.Idle));
            Assert.That(fixture.Presenter.Coordinator.CanSubmit, Is.False);
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FullTerminalReplay_AnimationOnOffProducesIdenticalOrderedResults()
        {
            foreach (string kind in new[] { "exit", "starvation", "death" })
            {
                Fixture animated = CreateFixture(true);
                Fixture immediate = CreateFixture(false);
                GameEvent[] animatedEvents = ArrangeTerminalReplay(animated, kind);
                GameEvent[] immediateEvents = ArrangeTerminalReplay(immediate, kind);
                Task<PresentationDiagnostic> pending = animated.Presenter.Dispatcher.ReplayAsync(animatedEvents);
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(animated.Sink.OutcomeCalls, Is.Zero);
                Assert.That(animated.Sink.Trace, Is.Empty);
                Assert.That(animated.FoodView.activeSelf, Is.True);
                while (!pending.IsCompleted)
                    yield return null;
                Assert.That(pending.Result, Is.Null);
                Assert.That(immediate.Presenter.Dispatcher.ReplayAsync(immediateEvents).Result, Is.Null);
                CollectionAssert.AreEqual(animated.Sink.Trace, immediate.Sink.Trace);
                Assert.That(animated.Sink.Trace.Last(), Is.EqualTo(animatedEvents.Last().EventType));
                Assert.That(animated.Sink.AcceptedOutcomes, Is.EqualTo(1));
                Assert.That(immediate.Sink.AcceptedOutcomes, Is.EqualTo(1));
                Assert.That(animated.Runtime.Views[animated.Runtime.PlayerId].transform.position,
                    Is.EqualTo(immediate.Runtime.Views[immediate.Runtime.PlayerId].transform.position));
                Assert.That(animated.FoodView.activeSelf, Is.False);
                Assert.That(immediate.FoodView.activeSelf, Is.False);
                Assert.That(animated.Runtime.RunState.Food, Is.EqualTo(immediate.Runtime.RunState.Food));
                Assert.That(animated.Presenter.Coordinator.Gate.State, Is.EqualTo(immediate.Presenter.Coordinator.Gate.State));
            }
        }

        [Test]
        public void UnknownEvent_AbortsTerminalSinkAndResynchronizesRegisteredViewsAndHud()
        {
            Fixture fixture = CreateFixture(false);
            Assert.That(fixture.Runtime.BoardState.TryMove(fixture.Runtime.PlayerId, new GridPosition(1, 0)), Is.True);
            Assert.That(fixture.Runtime.BoardState.TryRemove(fixture.FoodId), Is.True);
            fixture.Runtime.RunState.ConsumeFood(1);
            var events = new GameEvent[] { new UnknownEvent(), new PlayerDiedEvent(fixture.Runtime.PlayerId) };
            PresentationDiagnostic diagnostic = fixture.Presenter.Dispatcher.ReplayAsync(events).Result;
            Assert.That(diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.UnknownEvent));
            Assert.That(diagnostic.EventType, Is.EqualTo(typeof(UnknownEvent).FullName));
            Assert.That(fixture.Sink.OutcomeCalls, Is.Zero);
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(fixture.Runtime.Views[fixture.Runtime.PlayerId].transform.position, Is.EqualTo(new Vector3(1, 0, 3)));
            Assert.That(fixture.FoodView.activeSelf, Is.False);
            CollectionAssert.AreEqual(new[] { "Hud:100:99" }, fixture.Sink.Trace);
        }

        [Test]
        public void MoveEvent_MustAgreeWithAuthoritativePosition()
        {
            Fixture fixture = CreateFixture(false);
            var movement = new EntityMovedEvent(fixture.Runtime.PlayerId, new GridPosition(0, 0), new GridPosition(1, 0));
            PresentationDiagnostic diagnostic = fixture.Presenter.Dispatcher.ReplayAsync(new[] { movement }).Result;
            Assert.That(diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.HandlerFailed));
            Assert.That(fixture.Runtime.Views[fixture.Runtime.PlayerId].transform.position, Is.EqualTo(new Vector3(0, 0, 3)));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
        }

        [Test]
        public void WaitAndInteraction_AreFeedbackOnlyAndHudUsesCurrentState()
        {
            Fixture fixture = CreateFixture(false);
            Vector3 origin = fixture.Runtime.Views[fixture.Runtime.PlayerId].transform.position;
            CommandSubmission wait = fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result;
            CommandSubmission interaction = fixture.Presenter.Coordinator.SubmitAsync(new InteractCommand(fixture.AidId)).Result;
            Assert.That(wait.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(interaction.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(98));
            Assert.That(fixture.Runtime.Views[fixture.Runtime.PlayerId].transform.position, Is.EqualTo(origin));
            Assert.That(fixture.Runtime.Views[fixture.AidId].activeSelf, Is.True);
            CollectionAssert.AreEqual(new[] { "EntityWaited", "Hud:100:99", "InteractionPerformed", "Hud:100:98" }, fixture.Sink.Trace);
        }

        [TestCase("exit")]
        [TestCase("starvation")]
        [TestCase("death")]
        public void TerminalSinks_RunAfterEarlierEvents_AndGuardDuplicateAndStaleOutcomes(string kind)
        {
            Fixture fixture = CreateFixture(false);
            GameEvent terminal;
            if (kind == "exit")
                terminal = new ExitReachedEvent(fixture.Runtime.PlayerId, fixture.ExitId);
            else if (kind == "starvation")
                terminal = new PlayerStarvedEvent(fixture.Runtime.PlayerId);
            else
                terminal = new PlayerDiedEvent(fixture.Runtime.PlayerId);
            var events = new GameEvent[] { new EntityWaitedEvent(fixture.Runtime.PlayerId),
                new ActionCostAppliedEvent(fixture.Runtime.PlayerId, 999), terminal };
            Assert.That(fixture.Presenter.Dispatcher.ReplayAsync(events).Result, Is.Null);
            CollectionAssert.AreEqual(new[] { "EntityWaited", "Hud:100:100", terminal.EventType }, fixture.Sink.Trace);
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(fixture.Sink.AcceptedOutcomes, Is.EqualTo(1));
            Assert.That(fixture.Presenter.Dispatcher.ReplayAsync(new[] { terminal }).Result, Is.Null);
            Assert.That(fixture.Sink.AcceptedOutcomes, Is.EqualTo(1));
            fixture.Sink.ActiveRequest = CreateFixture(false).Runtime.Request;
            Assert.That(fixture.Presenter.Dispatcher.ReplayAsync(new[] { terminal }).Result, Is.Null);
            Assert.That(fixture.Sink.AcceptedOutcomes, Is.EqualTo(1));
        }

        [Test]
        public void StarvationAnimationParity_HasSameOutcomeHudAndTerminalInputState()
        {
            Fixture animated = CreateFixture(true, 1);
            Fixture immediate = CreateFixture(false, 1);
            Assert.That(animated.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(immediate.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Presented));
            CollectionAssert.AreEqual(animated.Sink.Trace, immediate.Sink.Trace);
            CollectionAssert.AreEqual(new[] { "EntityWaited", "Hud:100:0", "PlayerStarved" }, animated.Sink.Trace);
            Assert.That(animated.Sink.AcceptedOutcomes, Is.EqualTo(1));
            Assert.That(immediate.Sink.AcceptedOutcomes, Is.EqualTo(1));
            Assert.That(animated.Presenter.Coordinator.Gate.State, Is.EqualTo(PresentationGateState.Idle));
            Assert.That(animated.Presenter.Coordinator.CanSubmit, Is.False);
            Assert.That(immediate.Presenter.Coordinator.CanSubmit, Is.False);
            Assert.That(animated.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(animated.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
        }

        [TestCase(PresentationInputBlock.Setup)]
        [TestCase(PresentationInputBlock.Modal)]
        [TestCase(PresentationInputBlock.Disabled)]
        public void DisabledInputAndFreeRejection_LeaveViewsHudAndResourcesUnchanged(PresentationInputBlock block)
        {
            Fixture fixture = CreateFixture(false);
            fixture.Presenter.Coordinator.Gate.SetBlocked(block, true);
            Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East)).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
            fixture.Presenter.Coordinator.Gate.SetBlocked(block, false);
            Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.West)).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(fixture.Sink.Rejection, Is.EqualTo(CommandRejectionCode.OutOfBounds));
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(fixture.FoodView.activeSelf, Is.True);
            Assert.That(fixture.Sink.Trace, Is.Empty);
            Assert.That(fixture.Presenter.Coordinator.CanSubmit, Is.True);
        }

        private static void AssertRecovered(Fixture fixture)
        {
            Assert.That(fixture.Runtime.Views[fixture.Runtime.PlayerId].transform.position, Is.EqualTo(new Vector3(1, 0, 3)));
            Assert.That(fixture.FoodView.activeSelf, Is.False);
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(109));
            CollectionAssert.AreEqual(new[] { "Hud:100:109" }, fixture.Sink.Trace);
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(fixture.Presenter.Coordinator.CanSubmit, Is.True);
        }

        private static GameEvent[] ArrangeTerminalReplay(Fixture fixture, string kind)
        {
            Assert.That(fixture.Runtime.BoardState.TryMove(fixture.Runtime.PlayerId, new GridPosition(1, 0)), Is.True);
            Assert.That(fixture.Runtime.BoardState.TryRemove(fixture.FoodId), Is.True);
            fixture.Runtime.RunState.RestoreFood(10);
            fixture.Runtime.RunState.ConsumeFood(1);
            GameEvent terminal = kind == "exit" ? (GameEvent)new ExitReachedEvent(fixture.Runtime.PlayerId, fixture.ExitId) :
                kind == "starvation" ? new PlayerStarvedEvent(fixture.Runtime.PlayerId) : new PlayerDiedEvent(fixture.Runtime.PlayerId);
            return new GameEvent[]
            {
                new EntityMovedEvent(fixture.Runtime.PlayerId, new GridPosition(0, 0), new GridPosition(1, 0)),
                new ItemCollectedEvent(fixture.Runtime.PlayerId, fixture.FoodId),
                new FoodRestoredEvent(fixture.Runtime.PlayerId, fixture.FoodId, 10),
                new ActionCostAppliedEvent(fixture.Runtime.PlayerId, 1), terminal
            };
        }

        private Fixture CreateFixture(bool animations, int initialFood = 100)
        {
            var run = new RunState("presentation-" + runtimes.Count, 12345,
                new RunStateConfiguration(100, initialFood, 0, "forest.start"));
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay,
                run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 3; horizontal++)
                for (int vertical = 0; vertical < 2; vertical++)
                    layout.Add(View(LegacyBoardContentKind.Floor, horizontal, vertical));
            layout.Add(View(LegacyBoardContentKind.Player, 0, 0));
            layout.Add(View(LegacyBoardContentKind.Exit, 2, 1));
            layout.Add(View(LegacyBoardContentKind.Food, 1, 0));
            layout.Add(View(LegacyBoardContentKind.Aid, 0, 1));
            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 1), layout);
            runtimes.Add(runtime);
            var sink = new TestSinks { ActiveRequest = request };
            var presenter = new BoardEventPresenter(runtime, sink, sink, sink, value => sink.Diagnostic = value, 0.05f)
                { AnimationsEnabled = animations };
            presenters.Add(presenter);
            presenter.SynchronizeFromState();
            sink.Trace.Clear();
            presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
            EntityId food = Find(runtime, "legacy.food");
            return new Fixture { Runtime = runtime, Presenter = presenter, Sink = sink, FoodId = food,
                FoodView = runtime.Views[food], AidId = Find(runtime, "legacy.aid"), ExitId = Find(runtime, "legacy.exit") };
        }

        private LegacyBoardView View(LegacyBoardContentKind kind, int horizontal, int vertical)
        {
            var instance = new GameObject(kind.ToString());
            instance.transform.position = new Vector3(horizontal, vertical, 3f);
            objects.Add(instance);
            return new LegacyBoardView(instance, kind, new GridPosition(horizontal, vertical));
        }

        private static EntityId Find(BoardRuntime runtime, string content) => runtime.BoardState.GetEntities()
            .Single(entity => entity.Definition.ContentId == content).Id;

        private sealed class Fixture
        {
            public BoardRuntime Runtime;
            public BoardEventPresenter Presenter;
            public TestSinks Sink;
            public EntityId FoodId;
            public GameObject FoodView;
            public EntityId AidId;
            public EntityId ExitId;
        }

        private sealed class TestSinks : IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public readonly List<string> Trace = new List<string>();
            public BoardRequest ActiveRequest;
            public PresentationDiagnostic Diagnostic;
            public CommandRejectionCode Rejection;
            public int OutcomeCalls;
            public int AcceptedOutcomes;

            public void Refresh(int health, int food) => Trace.Add("Hud:" + health + ":" + food);
            public void ShowRejection(CommandRejectionCode reason) => Rejection = reason;
            public void ShowEvent(GameEvent gameEvent) => Trace.Add(gameEvent.EventType);

            public bool TryNotify(BoardRequest request, GameEvent outcome)
            {
                OutcomeCalls++;
                if (!ReferenceEquals(request, ActiveRequest) || AcceptedOutcomes != 0)
                    return false;
                AcceptedOutcomes++;
                Trace.Add(outcome.EventType);
                return true;
            }
        }

        private sealed class UnknownEvent : GameEvent
        {
            public override string EventType => "Test.Unknown";
        }
    }
}