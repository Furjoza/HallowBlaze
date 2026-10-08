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

namespace HallowBlaze.Tests.EditMode
{
    internal sealed class CommandPresentationPipelineTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private BoardRuntime runtime;
        private CommandPresentationCoordinator coordinator;
        private PresentationGate gate;
        private OrderedEventDispatcher dispatcher;
        private readonly List<string> trace = new List<string>();
        private PresentationDiagnostic diagnostic;
        private int recoveryCount;
        private CommandRejectionCode rejection;

        [SetUp]
        public void SetUp()
        {
            var run = new RunState("pipeline-tests", 12345, new RunStateConfiguration(100, 100, 0, "forest.start"));
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay,
                run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 3; horizontal++)
                for (int vertical = 0; vertical < 2; vertical++)
                    layout.Add(View(LegacyBoardContentKind.Floor, horizontal, vertical));
            layout.Add(View(LegacyBoardContentKind.Player, 0, 0));
            layout.Add(View(LegacyBoardContentKind.Exit, 2, 1));
            runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 1), layout);
            gate = new PresentationGate();
            gate.SetBlocked(PresentationInputBlock.Setup, false);
            dispatcher = new OrderedEventDispatcher(value => diagnostic = value, () => recoveryCount++);
            coordinator = new CommandPresentationCoordinator(runtime, gate, dispatcher, value => rejection = value);
            dispatcher.Register<ActionCostAppliedEvent>((gameEvent, token) => Record(gameEvent));
            trace.Clear();
            diagnostic = null;
            recoveryCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            coordinator?.Dispose();
            runtime?.Dispose();
            foreach (GameObject instance in objects)
                UnityEngine.Object.DestroyImmediate(instance);
            objects.Clear();
        }

        [UnityTest]
        public IEnumerator PendingHandler_BlocksRapidSubmissionsAndPreservesModalOwner()
        {
            var pending = new TaskCompletionSource<bool>();
            dispatcher.Register<EntityMovedEvent>((gameEvent, token) =>
            {
                trace.Add(gameEvent.EventType);
                return pending.Task;
            });
            Task<CommandSubmission> first = coordinator.SubmitAsync(new MoveCommand(Direction.East));
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(gate.State, Is.EqualTo(PresentationGateState.Presenting));
            for (int attempt = 0; attempt < 10; attempt++)
                Assert.That(coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(runtime.RunState.Food, Is.EqualTo(99));
            CollectionAssert.AreEqual(new[] { "EntityMoved" }, trace);

            gate.SetBlocked(PresentationInputBlock.Modal, true);
            pending.SetResult(true);
            while (!first.IsCompleted)
                yield return null;
            Assert.That(first.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            CollectionAssert.AreEqual(new[] { "EntityMoved", "ActionCostApplied" }, trace);
            Assert.That(gate.State, Is.EqualTo(PresentationGateState.Idle));
            Assert.That(coordinator.CanSubmit, Is.False);
            gate.SetBlocked(PresentationInputBlock.Modal, false);
            Assert.That(coordinator.CanSubmit, Is.True);
        }

        [TestCase(PresentationInputBlock.Setup)]
        [TestCase(PresentationInputBlock.Modal)]
        [TestCase(PresentationInputBlock.Disabled)]
        public void ExternalBlocks_DoNotCallControllerOrSampleSource(PresentationInputBlock block)
        {
            var source = new DraftSource();
            gate.SetBlocked(block, true);
            Assert.That(coordinator.SubmitFromAsync(source).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(source.ReadCount, Is.Zero);
            Assert.That(coordinator.ResolutionCount, Is.Zero);
            Assert.That(runtime.RunState.Food, Is.EqualTo(100));
        }

        [Test]
        public void CancelDraftAndCancelBeforeSubmit_AreFree()
        {
            var source = new DraftSource();
            source.CancelDraft();
            Assert.That(coordinator.SubmitFromAsync(source).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.That(coordinator.SubmitAsync(new WaitCommand(), cancellation.Token).Result.Status,
                    Is.EqualTo(CommandSubmissionStatus.Canceled));
            }
            Assert.That(coordinator.ResolutionCount, Is.Zero);
            Assert.That(runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(coordinator.CanSubmit, Is.True);
        }

        [Test]
        public void Rejection_ReportsStableReasonWithoutReplayOrCost()
        {
            Vector3 original = runtime.Views[runtime.PlayerId].transform.position;
            CommandSubmission submission = coordinator.SubmitAsync(new MoveCommand(Direction.West)).Result;
            Assert.That(submission.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(rejection, Is.EqualTo(CommandRejectionCode.OutOfBounds));
            Assert.That(runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(runtime.Views[runtime.PlayerId].transform.position, Is.EqualTo(original));
            Assert.That(trace, Is.Empty);
            Assert.That(recoveryCount, Is.Zero);
            Assert.That(coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(coordinator.CanSubmit, Is.True);
        }

        [Test]
        public void ZeroDurationWait_ReplaysInOrderAndReleasesGate()
        {
            dispatcher.Register<EntityWaitedEvent>((gameEvent, token) => Record(gameEvent));
            CommandSubmission submission = coordinator.SubmitAsync(new WaitCommand()).Result;
            Assert.That(submission.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            CollectionAssert.AreEqual(new[] { "EntityWaited", "ActionCostApplied" }, trace);
            Assert.That(coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(runtime.RunState.Food, Is.EqualTo(99));
            Assert.That(coordinator.CanSubmit, Is.True);
        }

        [Test]
        public void UnknownEvent_AbortsLaterEventsAndRecoversOnce()
        {
            var events = new GameEvent[] { new UnknownEvent(), new ActionCostAppliedEvent(runtime.PlayerId, 1) };
            PresentationDiagnostic result = dispatcher.ReplayAsync(events).Result;
            Assert.That(result.Code, Is.EqualTo(PresentationDiagnosticCode.UnknownEvent));
            Assert.That(result.EventType, Is.EqualTo(typeof(UnknownEvent).FullName));
            Assert.That(result.EventIndex, Is.Zero);
            Assert.That(diagnostic, Is.SameAs(result));
            Assert.That(recoveryCount, Is.EqualTo(1));
            Assert.That(trace, Is.Empty);
            Assert.That(coordinator.ResolutionCount, Is.Zero);
        }

        [Test]
        public void UnknownResolvedEvent_KeepsAppliedResultAndReleasesGate()
        {
            CommandSubmission submission = coordinator.SubmitAsync(new WaitCommand()).Result;
            Assert.That(submission.Status, Is.EqualTo(CommandSubmissionStatus.PresentationFailed));
            Assert.That(submission.Result.Accepted, Is.True);
            Assert.That(submission.Diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.UnknownEvent));
            Assert.That(runtime.RunState.Food, Is.EqualTo(99));
            Assert.That(coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(recoveryCount, Is.EqualTo(1));
            Assert.That(coordinator.CanSubmit, Is.True);
        }

        [Test]
        public void HandlerException_AbortsAndRecoversWithoutResolvingAgain()
        {
            dispatcher.Register<EntityWaitedEvent>((gameEvent, token) => throw new InvalidOperationException("handler fault"));
            CommandSubmission submission = coordinator.SubmitAsync(new WaitCommand()).Result;
            Assert.That(submission.Diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.HandlerFailed));
            Assert.That(trace, Is.Empty);
            Assert.That(recoveryCount, Is.EqualTo(1));
            Assert.That(coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(coordinator.CanSubmit, Is.True);
        }

        [Test]
        public void FaultyDiagnosticAndRecovery_DoNotStrandGate()
        {
            coordinator.Dispose();
            dispatcher = new OrderedEventDispatcher(value => throw new InvalidOperationException("diagnostic fault"),
                () => throw new InvalidOperationException("recovery fault"));
            coordinator = new CommandPresentationCoordinator(runtime, gate, dispatcher, value => rejection = value);
            CommandSubmission submission = coordinator.SubmitAsync(new WaitCommand()).Result;
            Assert.That(submission.Diagnostic.RecoveryErrors, Has.Count.EqualTo(2));
            Assert.That(coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(coordinator.CanSubmit, Is.True);
        }

        [Test]
        public void DisposedRuntime_DoesNotResolve()
        {
            runtime.Dispose();
            Assert.That(coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(coordinator.ResolutionCount, Is.Zero);
        }

        [Test]
        public void ResolvingAdmission_BlocksReentrantSubmissionBeforeControllerCall()
        {
            dispatcher.Register<EntityWaitedEvent>((gameEvent, token) => Record(gameEvent));
            var source = new CallbackSource(() =>
            {
                Assert.That(gate.State, Is.EqualTo(PresentationGateState.Resolving));
                Assert.That(coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                return new WaitCommand();
            });
            Assert.That(coordinator.SubmitFromAsync(source).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(runtime.RunState.Food, Is.EqualTo(99));
        }

        [Test]
        public void SourceOpeningModal_DoesNotResolveAndKeepsModalBlocked()
        {
            var source = new CallbackSource(() =>
            {
                gate.SetBlocked(PresentationInputBlock.Modal, true);
                return new WaitCommand();
            });
            Assert.That(coordinator.SubmitFromAsync(source).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(coordinator.ResolutionCount, Is.Zero);
            Assert.That(runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(gate.State, Is.EqualTo(PresentationGateState.Idle));
            Assert.That(gate.Blocks, Is.EqualTo(PresentationInputBlock.Modal));
        }

        [Test]
        public void SourceCancellationBeforeResolve_IsFreeAndReleasesGate()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var source = new CallbackSource(() =>
                {
                    cancellation.Cancel();
                    return new WaitCommand();
                });
                Assert.That(coordinator.SubmitFromAsync(source, cancellation.Token).Result.Status,
                    Is.EqualTo(CommandSubmissionStatus.Canceled));
                Assert.That(coordinator.ResolutionCount, Is.Zero);
                Assert.That(runtime.RunState.Food, Is.EqualTo(100));
                Assert.That(coordinator.CanSubmit, Is.True);
            }
        }

        [Test]
        public void SourceException_ReleasesGateWithoutResolving()
        {
            var source = new CallbackSource(() => throw new InvalidOperationException("source fault"));
            Assert.Throws<InvalidOperationException>(() => coordinator.SubmitFromAsync(source).GetAwaiter().GetResult());
            Assert.That(coordinator.ResolutionCount, Is.Zero);
            Assert.That(coordinator.CanSubmit, Is.True);
        }

        [Test]
        public void RejectionCallbackException_ReleasesGateWithoutResourceOrViewChanges()
        {
            coordinator.Dispose();
            coordinator = new CommandPresentationCoordinator(runtime, gate, dispatcher,
                value => throw new InvalidOperationException("feedback fault"));
            Vector3 original = runtime.Views[runtime.PlayerId].transform.position;
            Assert.Throws<InvalidOperationException>(() => coordinator.SubmitAsync(new MoveCommand(Direction.West)).GetAwaiter().GetResult());
            Assert.That(coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(runtime.Views[runtime.PlayerId].transform.position, Is.EqualTo(original));
            Assert.That(recoveryCount, Is.Zero);
            Assert.That(coordinator.CanSubmit, Is.True);
        }

        [Test]
        public void GateOwners_AreIndependent_AndSetupStartsBlocked()
        {
            var separate = new PresentationGate();
            Assert.That(separate.CanSubmit, Is.False);
            separate.SetBlocked(PresentationInputBlock.Modal, true);
            separate.SetBlocked(PresentationInputBlock.Setup, false);
            Assert.That(separate.CanSubmit, Is.False);
            separate.SetBlocked(PresentationInputBlock.Modal, false);
            Assert.That(separate.CanSubmit, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => separate.SetBlocked((PresentationInputBlock)8, true));
        }

        [Test]
        public void HandlerRegistration_IsExactUniqueAndFrozenDuringReplay()
        {
            dispatcher.Register<EntityWaitedEvent>((gameEvent, token) =>
            {
                Assert.Throws<InvalidOperationException>(() => dispatcher.Register<UnknownEvent>((unknown, unused) => Task.CompletedTask));
                return Task.CompletedTask;
            });
            Assert.Throws<ArgumentException>(() => dispatcher.Register<EntityWaitedEvent>((gameEvent, token) => Task.CompletedTask));
            Assert.That(coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
        }

        [Test]
        public void DefaultHandlers_CoverEveryCurrentEventWithoutReapplyingAmounts()
        {
            var sinks = new TestSinks();
            using (var presenter = new BoardEventPresenter(runtime, sinks, sinks, sinks, value => diagnostic = value, 0f))
            {
                var events = new GameEvent[]
                {
                    new EntityMovedEvent(runtime.PlayerId, new GridPosition(0, 0), new GridPosition(0, 0)),
                    new EntityWaitedEvent(runtime.PlayerId),
                    new InteractionPerformedEvent(runtime.PlayerId, runtime.PlayerId),
                    new FoodRestoredEvent(runtime.PlayerId, runtime.PlayerId, 999),
                    new ActionCostAppliedEvent(runtime.PlayerId, 999),
                    new ExitReachedEvent(runtime.PlayerId, runtime.PlayerId),
                    new PlayerStarvedEvent(runtime.PlayerId),
                    new PlayerDiedEvent(runtime.PlayerId)
                };
                Assert.That(presenter.Dispatcher.ReplayAsync(events).Result, Is.Null);
                Assert.That(runtime.RunState.Food, Is.EqualTo(100));
                CollectionAssert.AreEqual(new[] { "EntityMoved", "EntityWaited", "InteractionPerformed", "Hud:100:100", "FoodRestored", "Hud:100:100",
                    "ExitReached", "PlayerStarved", "PlayerDied" }, sinks.Trace);
                var removed = runtime.BoardState.GetEntities().First(entity => entity.Definition.ContentId == "legacy.exit");
                Assert.That(runtime.BoardState.TryRemove(removed.Id), Is.True);
                Assert.That(presenter.Dispatcher.ReplayAsync(new[] { new ItemCollectedEvent(runtime.PlayerId, removed.Id) }).Result, Is.Null);
                Assert.That(runtime.Views[removed.Id].activeSelf, Is.False);
            }
        }

        [TestCase(KeyCode.W, 0, 1)]
        [TestCase(KeyCode.UpArrow, 0, 1)]
        [TestCase(KeyCode.D, 1, 0)]
        [TestCase(KeyCode.RightArrow, 1, 0)]
        [TestCase(KeyCode.S, 0, -1)]
        [TestCase(KeyCode.DownArrow, 0, -1)]
        [TestCase(KeyCode.A, -1, 0)]
        [TestCase(KeyCode.LeftArrow, -1, 0)]
        public void PcSource_MapsCardinalCommandsOncePerFrame(KeyCode key, int horizontal, int vertical)
        {
            var source = new PcCommandSource(candidate => candidate == key, () => 1);
            Assert.That(source.TryTakeCommand(out PlayerCommand command), Is.True);
            Assert.That(command, Is.TypeOf<MoveCommand>());
            Assert.That(((MoveCommand)command).Direction, Is.EqualTo(new Direction(horizontal, vertical)));
            Assert.That(source.TryTakeCommand(out _), Is.False);
        }

        [Test]
        public void PcSource_WaitCancelAndExtension_DoNotRequireACommandSwitch()
        {
            int frame = 1;
            KeyCode key = KeyCode.Space;
            var source = new PcCommandSource(candidate => candidate == key, () => frame);
            Assert.That(source.TryTakeCommand(out PlayerCommand command), Is.True);
            Assert.That(command, Is.TypeOf<WaitCommand>());
            frame++;
            source.CancelDraft();
            Assert.That(source.TryTakeCommand(out _), Is.False);
            frame++;
            key = KeyCode.Return;
            var future = new FutureCommand();
            source.Bind(key, () => future);
            Assert.That(source.TryTakeCommand(out command), Is.True);
            Assert.That(command, Is.SameAs(future));
        }

        [Test]
        public void PcSource_AbsentOrAmbiguousInput_IsFreeNoCommand()
        {
            var absent = new PcCommandSource(candidate => false, () => 1);
            var ambiguous = new PcCommandSource(candidate => candidate == KeyCode.W || candidate == KeyCode.D, () => 1);
            Assert.That(coordinator.SubmitFromAsync(absent).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Assert.That(coordinator.SubmitFromAsync(ambiguous).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Assert.That(coordinator.ResolutionCount, Is.Zero);
            Assert.That(runtime.RunState.Food, Is.EqualTo(100));
        }

        private Task Record(GameEvent gameEvent)
        {
            trace.Add(gameEvent.EventType);
            return Task.CompletedTask;
        }

        private LegacyBoardView View(LegacyBoardContentKind kind, int horizontal, int vertical)
        {
            var instance = new GameObject(kind.ToString());
            instance.transform.position = new Vector3(horizontal, vertical, 0f);
            objects.Add(instance);
            return new LegacyBoardView(instance, kind, new GridPosition(horizontal, vertical));
        }

        private sealed class UnknownEvent : GameEvent
        {
            public override string EventType => "Test.Unknown";
        }

        private sealed class FutureCommand : PlayerCommand
        {
            public override string CommandType => "Test.Future";
        }

        private sealed class CallbackSource : ICommandSource
        {
            private readonly Func<PlayerCommand> take;
            public CallbackSource(Func<PlayerCommand> take) => this.take = take;
            public bool TryTakeCommand(out PlayerCommand command)
            {
                command = take();
                return command != null;
            }
            public void CancelDraft() { }
        }

        private sealed class TestSinks : IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public readonly List<string> Trace = new List<string>();
            public void Refresh(int health, int food) => Trace.Add("Hud:" + health + ":" + food);
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) => Trace.Add(gameEvent.EventType);
            public bool TryNotify(BoardRequest request, GameEvent outcome)
            {
                Trace.Add(outcome.EventType);
                return true;
            }
        }

        private sealed class DraftSource : ICommandSource
        {
            private PlayerCommand draft = new WaitCommand();
            public int ReadCount { get; private set; }

            public bool TryTakeCommand(out PlayerCommand command)
            {
                ReadCount++;
                command = draft;
                draft = null;
                return command != null;
            }

            public void CancelDraft() => draft = null;
        }
    }
}