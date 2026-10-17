using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
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
        private readonly List<EnemyAttackPresenter> attackPresenters = new List<EnemyAttackPresenter>();

        [TearDown]
        public void TearDown()
        {
            foreach (EnemyAttackPresenter attack in attackPresenters)
                attack.Dispose();
            attackPresenters.Clear();
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
            CollectionAssert.AreEqual(new[] { "EntityMoved", "Hud:100:109", "FoodRestored", "Hud:100:109", "Noise" }, animated.Sink.Trace);
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
            CollectionAssert.AreEqual(new[] { "EntityWaited", "Hud:100:99", "InteractionPerformed", "Hud:100:98", "Noise" }, fixture.Sink.Trace);
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

        [UnityTest]
        public IEnumerator EnemyAttackReplay_UsesRecordedOutcomeWithoutGameplayMutation()
        {
            var shapes = new HashSet<string>();
            foreach (bool animations in new[] { true, false })
            {
                foreach (string scenario in new[] { "hit10", "hit20", "miss", "replacement", "missing-attacker", "missing-affected", "canceled" })
                {
                    Fixture fixture = CreateFixture(animations);
                    bool hit = scenario == "hit10" || scenario == "hit20" || scenario == "missing-affected";
                    int damage = scenario == "hit20" ? 20 : 10;
                    Assert.That(fixture.Runtime.BoardState.TryRemove(fixture.AidId), Is.True);
                    var enemyDefinition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "attack.enemy", BoardEntityTraits.Default);
                    Assert.That(fixture.Runtime.BoardState.TryAdd(new BoardEntityState(fixture.AidId, enemyDefinition, new GridPosition(0, 1))), Is.True);
                    if (hit)
                        fixture.Runtime.RunState.TakeDamage(damage);
                    else
                    {
                        Assert.That(fixture.Runtime.BoardState.TryMove(fixture.Runtime.PlayerId, new GridPosition(1, 0)), Is.True);
                        fixture.Runtime.Views[fixture.Runtime.PlayerId].transform.position = new Vector3(1, 0, 3);
                    }
                    if (scenario == "replacement")
                        Assert.That(fixture.Runtime.BoardState.TryAdd(new BoardEntityState(new EntityId(500), enemyDefinition, new GridPosition(0, 0))), Is.True);
                    var gameEvent = new EnemyAttackResolvedEvent(fixture.AidId, new GridPosition(0, 0), hit,
                        hit ? (EntityId?)fixture.Runtime.PlayerId : null, hit ? -damage : 0);
                    var entitiesBefore = fixture.Runtime.BoardState.GetEntities();
                    int health = fixture.Runtime.RunState.Health;
                    int food = fixture.Runtime.RunState.Food;
                    var attack = new EnemyAttackPresenter(fixture.Runtime, fixture.Sink, fixture.Sink, 0.04f) { AnimationsEnabled = animations };
                    attackPresenters.Add(attack);
                    var dispatcher = new OrderedEventDispatcher(value => fixture.Sink.Diagnostic = value, fixture.Presenter.SynchronizeFromState);
                    dispatcher.Register<EntityWaitedEvent>((value, token) => { fixture.Sink.ShowEvent(value); return Task.CompletedTask; });
                    dispatcher.Register<EnemyAttackResolvedEvent>(attack.ReplayAsync);
                    if (scenario == "missing-attacker")
                        UnityEngine.Object.DestroyImmediate(fixture.Runtime.Views[fixture.AidId]);
                    if (scenario == "missing-affected")
                        UnityEngine.Object.DestroyImmediate(fixture.Runtime.Views[fixture.Runtime.PlayerId]);
                    using (var cancellation = new CancellationTokenSource())
                    {
                        if (scenario == "canceled" && !animations)
                            cancellation.Cancel();
                        Task<PresentationDiagnostic> pending = dispatcher.ReplayAsync(new GameEvent[] {
                            new EntityWaitedEvent(fixture.Runtime.PlayerId), gameEvent, new EntityWaitedEvent(fixture.Runtime.PlayerId) }, cancellation.Token);
                        if (scenario == "canceled" && animations)
                            cancellation.Cancel();
                        while (!pending.IsCompleted)
                            yield return null;
                        bool failure = scenario.StartsWith("missing-", StringComparison.Ordinal) || scenario == "canceled";
                        if (failure)
                        {
                            Assert.That(pending.Result.Code, Is.EqualTo(scenario == "canceled" ? PresentationDiagnosticCode.Canceled : PresentationDiagnosticCode.HandlerFailed));
                            if (scenario != "canceled" || animations)
                                Assert.That(pending.Result.EventType, Is.EqualTo(typeof(EnemyAttackResolvedEvent).FullName));
                            Assert.That(fixture.Sink.Diagnostic, Is.SameAs(pending.Result));
                            Assert.That(attack.TargetFeedback, Is.Null);
                            Assert.That(fixture.Sink.Trace.Last(), Is.EqualTo("Hud:" + health + ":" + food));
                            Assert.That(fixture.Sink.Trace.Count, Is.EqualTo(scenario == "canceled" && !animations ? 1 : 2));
                        }
                        else
                        {
                            Assert.That(pending.Result, Is.Null);
                            CollectionAssert.AreEqual(new[] { "EntityWaited", "Hud:" + health + ":" + food,
                                "EnemyAttackResolved", "EntityWaited" }, fixture.Sink.Trace);
                            Assert.That(fixture.Sink.LastAttack, Is.SameAs(gameEvent));
                            Assert.That(attack.TargetFeedback.transform.position, Is.EqualTo(new Vector3(0, 0, EnemyIntentView.MarkerDepth)));
                            Transform outcome = attack.TargetFeedback.transform.Find("Outcome");
                            LineRenderer[] lines = outcome.GetComponentsInChildren<LineRenderer>();
                            Assert.That(lines.Length, Is.EqualTo(hit ? 2 : 1));
                            Assert.That(lines.All(line => line.enabled && line.gameObject.activeInHierarchy &&
                                line.startColor == Color.white && line.endColor == Color.white), Is.True);
                            Assert.That(lines[0].loop, Is.EqualTo(!hit));
                            shapes.Add(hit ? "cross" : "ring");
                            LineRenderer sourceLink = attack.TargetFeedback.GetComponentsInChildren<LineRenderer>()
                                .Single(line => line.transform.parent == attack.TargetFeedback.transform);
                            Assert.That(sourceLink.transform.TransformPoint(sourceLink.GetPosition(0)), Is.EqualTo(new Vector3(0, 1, EnemyIntentView.MarkerDepth)));
                            Assert.That(fixture.Runtime.Views[fixture.AidId].transform.position, Is.EqualTo(new Vector3(0, 1, 3)));
                            Vector3 expectedPlayer = hit ? new Vector3(0, 0, 3) : new Vector3(1, 0, 3);
                            Assert.That(fixture.Runtime.Views[fixture.Runtime.PlayerId].transform.position, Is.EqualTo(expectedPlayer));
                        }
                    }
                    CollectionAssert.AreEqual(entitiesBefore, fixture.Runtime.BoardState.GetEntities());
                    Assert.That(fixture.Runtime.RunState.Health, Is.EqualTo(health));
                    Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(food));
                    Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
                    Assert.That(fixture.Runtime.IsDisposed, Is.False);
                    TestContext.WriteLine("Animations=" + animations + "; outcome=" + scenario + "; HP=" + health);
                    attack.Dispose();
                    attack.Dispose();
                    Assert.That(attack.TargetFeedback, Is.Null);
                }
            }
            Assert.That(shapes.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator RegisteredEnemyAttack_PreservesOrderedReplayAndTerminalSink()
        {
            foreach (bool animations in new[] { true, false })
            {
                Fixture fixture = CreateFixture(animations);
                Assert.That(fixture.Runtime.BoardState.TryRemove(fixture.AidId), Is.True);
                var definition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "registered.attack", BoardEntityTraits.Default);
                Assert.That(fixture.Runtime.BoardState.TryAdd(new BoardEntityState(fixture.AidId, definition, new GridPosition(1, 1))), Is.True);
                fixture.Runtime.Views[fixture.AidId].transform.position = new Vector3(1, 1, 3);
                Assert.That(fixture.Runtime.BoardState.TryMove(fixture.Runtime.PlayerId, new GridPosition(1, 0)), Is.True);
                fixture.Runtime.RunState.ConsumeFood(1);
                fixture.Runtime.RunState.TakeDamage(80);
                fixture.Runtime.RunState.TakeDamage(20);
                var attack = new EnemyAttackResolvedEvent(fixture.AidId, new GridPosition(1, 0), true, fixture.Runtime.PlayerId, -20);
                var terminal = new PlayerDiedEvent(fixture.Runtime.PlayerId);
                var events = new GameEvent[] { new EntityMovedEvent(fixture.Runtime.PlayerId, new GridPosition(0, 0), new GridPosition(1, 0)),
                    new ActionCostAppliedEvent(fixture.Runtime.PlayerId, 1), attack, terminal };
                var entitiesBefore = fixture.Runtime.BoardState.GetEntities();
                Assert.Throws<ArgumentException>(() => fixture.Presenter.Dispatcher.Register<EnemyAttackResolvedEvent>((value, token) => Task.CompletedTask));

                Task<PresentationDiagnostic> pending = fixture.Presenter.Dispatcher.ReplayAsync(events);

                if (animations)
                {
                    Assert.That(pending.IsCompleted, Is.False);
                    Assert.That(fixture.Sink.OutcomeCalls, Is.Zero);
                }
                else
                    Assert.That(pending.IsCompleted, Is.True);
                while (!pending.IsCompleted)
                    yield return null;
                Assert.That(pending.Result, Is.Null);
                CollectionAssert.AreEqual(new[] { "EntityMoved", "Hud:0:99", "Hud:0:99", "EnemyAttackResolved", "PlayerDied" }, fixture.Sink.Trace);
                Assert.That(fixture.Sink.Trace.Last(), Is.EqualTo(terminal.EventType));
                Assert.That(fixture.Sink.LastAttack, Is.SameAs(attack));
                Assert.That(fixture.Sink.AcceptedOutcomes, Is.EqualTo(1));
                Assert.That(fixture.Runtime.RunState.Health, Is.Zero);
                Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(99));
                Assert.That(fixture.Runtime.Views[fixture.AidId].transform.position, Is.EqualTo(new Vector3(1, 1, 3)));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
                CollectionAssert.AreEqual(entitiesBefore, fixture.Runtime.BoardState.GetEntities());
                fixture.Sink.Trace.Clear();

                PresentationDiagnostic unknown = fixture.Presenter.Dispatcher.ReplayAsync(new GameEvent[] { new UnknownEvent(), attack, terminal }).Result;

                Assert.That(unknown.Code, Is.EqualTo(PresentationDiagnosticCode.UnknownEvent));
                Assert.That(unknown.EventType, Is.EqualTo(typeof(UnknownEvent).FullName));
                CollectionAssert.AreEqual(new[] { "Hud:0:99" }, fixture.Sink.Trace);
                Assert.That(fixture.Sink.OutcomeCalls, Is.EqualTo(1));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
                CollectionAssert.AreEqual(entitiesBefore, fixture.Runtime.BoardState.GetEntities());
                fixture.Presenter.Dispose();
                Assert.That(fixture.Runtime.IsDisposed, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator InteractionPreviewAndCancel_PreserveExactGroundInstanceAndOtherInputOwners()
        {
            Fixture fixture = CreateFixture(false);
            var picker = fixture.Presenter.InteractionDisplay;
            fixture.Runtime.BoardState.TryGetEntity(fixture.AidId, out BoardEntityState original);
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.SelectedIndex, Is.EqualTo(-1));
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Assert.That(picker.Select(0), Is.True);
            Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Blocked));
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Modal, true);
            picker.Cancel();
            Assert.That(picker.IsOpen, Is.False);
            Assert.That(picker.Options, Is.Empty);
            Assert.That(picker.GetComponentsInChildren<LineRenderer>(), Is.Empty);
            fixture.Runtime.BoardState.TryGetEntity(fixture.AidId, out BoardEntityState retained);
            Assert.That(retained, Is.SameAs(original));
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
            yield return null;
            Assert.That(fixture.Presenter.Coordinator.Gate.Blocks, Is.EqualTo(PresentationInputBlock.Modal));
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Modal, false);
            Assert.That(fixture.Presenter.Coordinator.CanSubmit, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InteractionConfirm_CollectsSelectedInstanceOnceWithAnimationParity(bool animations)
        {
            Fixture fixture = CreateFixture(animations);
            MakeInteractiveFood(fixture);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.Options.Count, Is.EqualTo(2));
            int collectIndex = picker.Options.ToList().FindIndex(option => option.ActionId == "collect");
            Assert.That(picker.Select(collectIndex), Is.True);
            Assert.That(fixture.FoodView.activeSelf, Is.True);
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(109));
            Assert.That(fixture.FoodView.activeSelf, Is.False);
            Assert.That(fixture.Runtime.BoardState.TryGetEntity(fixture.AidId, out _), Is.True);
            Assert.That(picker.GetComponentsInChildren<LineRenderer>(), Is.Empty);
            CollectionAssert.AreEqual(new[] { "InteractionPerformed", "Hud:100:109", "FoodRestored", "Hud:100:109", "Noise" }, fixture.Sink.Trace);
        }

        [Test]
        public void InteractionStaleSelection_RejectsWithoutRemovingReplacementOrSpendingTurn()
        {
            Fixture fixture = CreateFixture(false);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.Select(0), Is.True);
            Assert.That(fixture.Runtime.BoardState.TryMove(fixture.AidId, new GridPosition(1, 1)), Is.True);
            CommandSubmission result = picker.ConfirmAsync().Result;
            Assert.That(result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(fixture.Runtime.BoardState.GetActiveNoises(), Is.Empty);
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(fixture.Runtime.BoardState.TryGetEntity(fixture.AidId, out _), Is.True);
        }

        [Test]
        public void InteractionTerminalCommit_DoesNotReacquireInputAfterOutcomeDisposesPicker()
        {
            Fixture fixture = CreateFixture(false, 1);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.Select(0), Is.True);
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(picker.IsDisposed, Is.True);
            Assert.That(fixture.Runtime.Controller.IsTerminal, Is.True);
            Assert.That(fixture.Presenter.Coordinator.Gate.Blocks & PresentationInputBlock.Interaction,
                Is.EqualTo(PresentationInputBlock.None));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
        }

        [Test]
        public void InteractionEmptyDisabledAndDisposed_AreFreeAndNeverHoldInput()
        {
            Fixture fixture = CreateFixture(false);
            var picker = fixture.Presenter.InteractionDisplay;
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Disabled, true);
            Assert.That(picker.Open(), Is.False);
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Disabled, false);
            Assert.That(picker.Open(), Is.True);
            picker.enabled = false;
            Assert.That(picker.IsOpen, Is.False);
            Assert.That(fixture.Presenter.Coordinator.CanSubmit, Is.True);
            picker.enabled = true;
            fixture.Runtime.BoardState.TryRemove(fixture.AidId);
            Assert.That(picker.Open(), Is.False);
            Assert.That(fixture.Presenter.Coordinator.CanSubmit, Is.True);
            fixture.Presenter.Dispose();
            Assert.That(picker.Open(), Is.False);
            Assert.That(fixture.Runtime.IsDisposed, Is.False);
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
        }

        [Test]
        public void InteractionMarkers_RenderEveryLegalTargetWithNumbersAndSelectedShapeWithoutAnimation()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            Fixture fixture = CreateFixture(false);
            MakeInteractiveFood(fixture);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.Select(0), Is.True);
            LineRenderer[] lines = picker.GetComponentsInChildren<LineRenderer>();
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(picker.GetComponentsInChildren<TextMesh>().Select(text => text.text), Is.EqualTo(new[] { ">1", "2" }));
            Assert.That(lines.All(line => line.startColor == Color.white && line.loop), Is.True);
            Assert.That(lines[0].GetPosition(0), Is.Not.EqualTo(lines[1].GetPosition(0)));
            var owner = new GameObject("Interaction evidence camera");
            objects.Add(owner);
            Camera camera = owner.AddComponent<Camera>();
            camera.transform.position = new Vector3(0.5f, 0.5f, -20f);
            camera.orthographic = true;
            camera.orthographicSize = 1.4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var target = new RenderTexture(512, 512, 24);
            var pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                pixels.Apply();
                foreach (var option in picker.Options)
                {
                    Vector3 center = camera.WorldToViewportPoint(new Vector3(option.TargetPosition.X, option.TargetPosition.Y, 0));
                    int marked = 0;
                    for (int vertical = -80; vertical <= 80; vertical++)
                        for (int horizontal = -80; horizontal <= 80; horizontal++)
                        {
                            Color color = pixels.GetPixel(Mathf.RoundToInt(center.x * 512) + horizontal,
                                Mathf.RoundToInt(center.y * 512) + vertical);
                            if (color.r > 0.8f && color.g > 0.8f && color.b > 0.8f) marked++;
                        }
                    Assert.That(marked, Is.GreaterThan(100), "Visible numbered target " + option.TargetId);
                }
                string image = Path.Combine(Path.GetTempPath(), "HB-M51-targets.png");
                File.WriteAllBytes(image, pixels.EncodeToPNG());
                TestContext.WriteLine("Interaction visual evidence: " + image);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(target);
            }
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator ToolPreviewCancelAndRapidConfirm_PreserveOneAtomicDecision()
        {
            var tools = new ToolUseQuery(new[] { new ToolAction("test.tool", new MoveToolTarget(), "Move target", 3, 2) },
                new ActionCostPolicy(2));
            Fixture fixture = CreateFixture(true, tools: tools);
            ToolInstanceState tool = fixture.Runtime.RunState.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3));
            fixture.Runtime.RunState.EquipTool(1, tool);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            int toolIndex = picker.Options.Count;
            Assert.That(picker.ToolOptions.Count, Is.EqualTo(1));
            Assert.That(picker.GetOptionLabel(toolIndex), Does.Contain("Tool 2: Move target").And.Contain("Food -2")
                .And.Contain("Charge -1 (3 left)").And.Contain("Noise 3 / 2 turns").And.Contain("1 turn"));
            Assert.That(picker.Select(toolIndex), Is.True);
            picker.Cancel();
            Assert.That(fixture.Runtime.RunState.ToolSlots[1].Instance.Charges, Is.EqualTo(3));
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
            yield return null;
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.Select(toolIndex), Is.True);
            Task<CommandSubmission> pending = picker.ConfirmAsync();
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(fixture.Runtime.RunState.ToolSlots[1].Instance.Charges, Is.EqualTo(2));
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
                Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status,
                    Is.EqualTo(CommandSubmissionStatus.Blocked));
            }
            while (!pending.IsCompleted) yield return null;
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(fixture.Sink.Diagnostic, Is.Null);
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(98));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(picker.ToolOptions, Is.Empty);
            Assert.That(fixture.Runtime.Views[fixture.AidId].transform.position, Is.EqualTo(new Vector3(1, 1, 3)));
            CollectionAssert.AreEqual(new[] { "ToolUsed", "EntityMoved", "ToolChargeSpent", "Hud:100:98", "Noise" }, fixture.Sink.Trace);
        }

        [Test]
        public void ToolReplacedAfterPreviewRejectsWithoutFallback()
        {
            var tools = new ToolUseQuery(new[] { new ToolAction("test.tool", new MoveToolTarget(), "Move target", 3, 2) });
            Fixture fixture = CreateFixture(false, tools: tools);
            ToolInstanceState tool = fixture.Runtime.RunState.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3));
            fixture.Runtime.RunState.EquipTool(0, tool);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.Select(picker.Options.Count), Is.True);
            fixture.Runtime.RunState.ReplaceTool(0, fixture.Runtime.RunState.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3)));
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(fixture.Sink.Rejection, Is.EqualTo(CommandRejectionCode.StaleTool));
            Assert.That(fixture.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(fixture.Runtime.BoardState.GetActiveNoises(), Is.Empty);
            Assert.That(fixture.Runtime.Views[fixture.AidId].transform.position, Is.EqualTo(new Vector3(0, 1, 3)));
        }

        [UnityTest]
        public IEnumerator ToolAnimationParityAndCanceledReplay_DoNotRefundOrDuplicateAcceptedCharges()
        {
            Fixture animated = CreateToolFixture(true);
            Fixture immediate = CreateToolFixture(false);
            UseToolCommand command = animated.Runtime.Tools.GetOptions(animated.Runtime.BoardState,
                animated.Runtime.RunState, animated.Runtime.PlayerId).Single().CreateCommand();
            var immediatePicker = immediate.Presenter.InteractionDisplay;
            Assert.That(immediatePicker.Open(), Is.True);
            Assert.That(immediatePicker.Select(immediatePicker.Options.Count), Is.True);
            Assert.That(immediatePicker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Task<CommandSubmission> pending = animated.Presenter.Coordinator.SubmitAsync(command);
            while (!pending.IsCompleted) yield return null;
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(animated.Runtime.RunState.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            Assert.That(immediate.Runtime.RunState.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            Assert.That(animated.Runtime.RunState.Food, Is.EqualTo(immediate.Runtime.RunState.Food));
            Assert.That(animated.Runtime.Views[animated.AidId].transform.position,
                Is.EqualTo(immediate.Runtime.Views[immediate.AidId].transform.position));
            Assert.That(animated.Sink.Trace, Is.EqualTo(immediate.Sink.Trace));

            Fixture canceled = CreateToolFixture(true);
            UseToolCommand canceledCommand = canceled.Runtime.Tools.GetOptions(canceled.Runtime.BoardState,
                canceled.Runtime.RunState, canceled.Runtime.PlayerId).Single().CreateCommand();
            using (var cancellation = new CancellationTokenSource())
            {
                Task<CommandSubmission> replay = canceled.Presenter.Coordinator.SubmitAsync(canceledCommand, cancellation.Token);
                cancellation.Cancel();
                while (!replay.IsCompleted) yield return null;
                Assert.That(replay.Result.Status, Is.EqualTo(CommandSubmissionStatus.PresentationFailed));
                Assert.That(replay.Result.Diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.Canceled));
                Assert.That(replay.Result.Result.Accepted, Is.True);
                Assert.That(canceled.Runtime.RunState.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
                Assert.That(canceled.Runtime.RunState.Food, Is.EqualTo(99));
                Assert.That(canceled.Runtime.Views[canceled.AidId].transform.position, Is.EqualTo(new Vector3(1, 1, 3)));
                Assert.That(canceled.Presenter.Coordinator.SubmitAsync(canceledCommand).Result.Status,
                    Is.EqualTo(CommandSubmissionStatus.Rejected));
                Assert.That(canceled.Sink.Rejection, Is.EqualTo(CommandRejectionCode.StaleTool));
                Assert.That(canceled.Runtime.RunState.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            }
        }

        [Test]
        public void ToolDepletedPreviewAndDisplacedTargetAreFreeAndNeverFallBackToOrdinaryInteraction()
        {
            Fixture depleted = CreateToolFixture(false);
            string instanceId = depleted.Runtime.RunState.ToolSlots[0].Instance.InstanceId;
            for (int charge = 0; charge < 3; charge++) depleted.Runtime.RunState.TrySpendToolCharge(0, instanceId);
            var disabledPicker = depleted.Presenter.InteractionDisplay;
            Assert.That(disabledPicker.Open(), Is.True);
            Assert.That(disabledPicker.ToolOptions.Single().RejectionCode, Is.EqualTo(CommandRejectionCode.ToolDepleted));
            Assert.That(disabledPicker.Select(disabledPicker.Options.Count), Is.False);
            Assert.That(disabledPicker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Assert.That(depleted.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(depleted.Presenter.Coordinator.ResolutionCount, Is.Zero);
            disabledPicker.Cancel();

            Fixture displaced = CreateToolFixture(false);
            var picker = displaced.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.Select(picker.Options.Count), Is.True);
            Assert.That(displaced.Runtime.BoardState.TryMove(displaced.AidId, new GridPosition(1, 1)), Is.True);
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(displaced.Sink.Rejection, Is.EqualTo(CommandRejectionCode.InvalidTarget));
            Assert.That(displaced.Runtime.RunState.ToolSlots[0].Instance.Charges, Is.EqualTo(3));
            Assert.That(displaced.Runtime.RunState.Food, Is.EqualTo(100));
            Assert.That(displaced.Runtime.BoardState.GetActiveNoises(), Is.Empty);
        }

        [Test]
        public void ToolTerminalConfirmationDisposesPickerWithoutReacquiringItsBlock()
        {
            Fixture fixture = CreateToolFixture(false, initialFood: 1);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.Select(picker.Options.Count), Is.True);
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(fixture.Runtime.RunState.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            Assert.That(fixture.Runtime.RunState.Food, Is.Zero);
            Assert.That(fixture.Runtime.Controller.IsTerminal, Is.True);
            Assert.That(picker.IsDisposed, Is.True);
            Assert.That(picker.ToolOptions, Is.Empty);
            Assert.That(fixture.Presenter.Coordinator.Gate.Blocks & PresentationInputBlock.Interaction,
                Is.EqualTo(PresentationInputBlock.None));
            Assert.That(fixture.Sink.Trace.Last(), Is.EqualTo("PlayerStarved"));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
        }

        private Fixture CreateToolFixture(bool animations, int initialFood = 100)
        {
            var tools = new ToolUseQuery(new[] { new ToolAction("test.tool", new MoveToolTarget(), "Move target", 3, 2) });
            Fixture fixture = CreateFixture(animations, initialFood, tools);
            fixture.Runtime.RunState.EquipTool(0,
                fixture.Runtime.RunState.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3)));
            return fixture;
        }

        private static void MakeInteractiveFood(Fixture fixture)
        {
            fixture.Runtime.BoardState.TryGetEntity(fixture.FoodId, out BoardEntityState item);
            Assert.That(fixture.Runtime.BoardState.TryRemove(item.Id), Is.True);
            Assert.That(fixture.Runtime.BoardState.TryAdd(new BoardEntityState(item.Id,
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "test.collectible",
                    new BoardEntityTraits(false, false, true), 10), item.Position)), Is.True);
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

        private Fixture CreateFixture(bool animations, int initialFood = 100, ToolUseQuery tools = null)
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
            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 1), layout, tools);
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
            public EnemyAttackResolvedEvent LastAttack;

            public void Refresh(int health, int food) => Trace.Add("Hud:" + health + ":" + food);
            public void ShowRejection(CommandRejectionCode reason) => Rejection = reason;
            public void ShowEvent(GameEvent gameEvent)
            {
                Trace.Add(gameEvent.EventType);
                if (gameEvent is EnemyAttackResolvedEvent attack)
                    LastAttack = attack;
            }

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

        private sealed class MoveToolTarget : IInteractionAction
        {
            public string Id => "test.move";
            public bool Supports(BoardEntityState target) => target.Definition.ContentId == "legacy.aid";
            public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
                => board.TryGetEntity(BoardLayer.Item, new GridPosition(1, 1), out _)
                    ? InteractionAvailability.Blocked : InteractionAvailability.Available;
            public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
            {
                Assert.That(board.TryMove(target.Id, new GridPosition(1, 1)), Is.True);
                events.Add(new EntityMovedEvent(target.Id, target.Position, new GridPosition(1, 1)));
            }
        }

        private sealed class UnknownEvent : GameEvent
        {
            public override string EventType => "Test.Unknown";
        }
    }
}