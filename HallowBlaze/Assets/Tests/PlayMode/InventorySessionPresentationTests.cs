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

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Checks the minimal bag adapter against real runtime composition, ordered replay and independent input owners.</summary>
    public sealed class InventorySessionPresentationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private BoardRuntime runtime;
        private BoardEventPresenter presenter;
        private InventorySessionPresenter adapter;
        private CommandPresentationCoordinator coordinator;
        private Sink sink;

        [UnityTest]
        public IEnumerator BagEffectsPrecedeOneRealHostilePhaseInEitherPresentationMode([Values(false, true)] bool animations)
        {
            CreateRuntime(true);
            presenter.AnimationsEnabled = animations;
            var bag = presenter.InventoryDisplay;
            Assert.That(bag.TryOpen(), Is.True);
            Assert.That(bag.Draft.TryStage(InventoryItemActionKind.Use, "medicine"), Is.True);
            Assert.That(bag.Draft.TryStage(InventoryItemActionKind.Use, "food"), Is.True);
            Task<CommandSubmission> finish = bag.FinishAsync();
            while (!finish.IsCompleted)
            {
                Assert.That(presenter.Coordinator.CanSubmit, Is.False);
                Assert.That(bag.FinishAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                yield return null;
            }
            Assert.That(finish.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(sink.Diagnostic, Is.Null);
            Assert.That(runtime.RunState.Health, Is.EqualTo(90));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(149));
            Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(1));
            Assert.That(sink.HealthGain, Is.EqualTo(5));
            Assert.That(sink.SatietyGain, Is.EqualTo(50));
            Assert.That(sink.Events.OfType<InventoryItemActionEvent>().Count(), Is.EqualTo(2));
            Assert.That(sink.Events.OfType<EnemyAttackResolvedEvent>().Count(), Is.EqualTo(1));
            Assert.That(sink.Events.FindIndex(value => value is EnemyAttackResolvedEvent),
                Is.GreaterThan(sink.Events.FindIndex(value => value is InventoryCommittedEvent)));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(bag.IsOpen, Is.False);
        }

        [TearDown]
        public void TearDown()
        {
            adapter?.Dispose();
            coordinator?.Dispose();
            presenter?.Dispose();
            runtime?.Dispose();
            foreach (GameObject instance in objects)
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            objects.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OrderedUsesUpdateAppliedGainsAndExactlyOneCostInBothPresentationModes(bool animations)
        {
            CreateRuntime();
            presenter.AnimationsEnabled = animations;
            var bag = presenter.InventoryDisplay;
            Assert.That(bag.TryOpen(), Is.True);
            Assert.That(presenter.Coordinator.CanSubmit, Is.False);
            Assert.That(presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(bag.Draft.TryStage(InventoryItemActionKind.Use, "food"), Is.True);
            Assert.That(bag.Draft.TryStage(InventoryItemActionKind.Use, "medicine"), Is.True);
            Assert.That(runtime.RunState.Health, Is.EqualTo(95));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(100));
            Assert.That(bag.FinishAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(sink.Diagnostic, Is.Null);
            Assert.That(bag.IsOpen, Is.False);
            Assert.That(bag.Draft, Is.Null);
            Assert.That(runtime.RunState.Inventory.Placements, Is.Empty);
            Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(1));
            Assert.That(sink.Health, Is.EqualTo(100));
            Assert.That(sink.Satiety, Is.EqualTo(149));
            Assert.That(sink.HealthGain, Is.EqualTo(5));
            Assert.That(sink.SatietyGain, Is.EqualTo(50));
            Assert.That(sink.Events.OfType<InventoryItemActionEvent>().Select(value => value.Wasted), Is.EqualTo(new[] { 0, 5 }));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(bag.FinishAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(presenter.Coordinator.CanSubmit, Is.True);
        }

        [UnityTest]
        public IEnumerator PendingFinishBlocksRapidInputAndKeepsOtherModalOwners()
        {
            CreateRuntime();
            var held = new TaskCompletionSource<bool>();
            var gate = new PresentationGate();
            gate.SetBlocked(PresentationInputBlock.Setup, false);
            var dispatcher = new OrderedEventDispatcher(value => sink.Diagnostic = value, () => { });
            dispatcher.Register<InventoryCommittedEvent>((value, token) => held.Task);
            dispatcher.Register<ActionCostAppliedEvent>((value, token) => Task.CompletedTask);
            coordinator = new CommandPresentationCoordinator(runtime, gate, dispatcher, value => { });
            adapter = new InventorySessionPresenter(runtime, coordinator);
            Assert.That(adapter.TryOpen(), Is.True);
            Task<CommandSubmission> finish = adapter.FinishAsync();
            Assert.That(finish.IsCompleted, Is.False);
            Assert.That(gate.State, Is.EqualTo(PresentationGateState.Presenting));
            Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(1));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(99));
            for (int index = 0; index < 10; index++)
            {
                Assert.That(adapter.FinishAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                Assert.That(coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            }
            gate.SetBlocked(PresentationInputBlock.Modal, true);
            held.SetResult(true);
            while (!finish.IsCompleted) yield return null;
            Assert.That(finish.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(adapter.IsOpen, Is.False);
            Assert.That(gate.Blocks, Is.EqualTo(PresentationInputBlock.Modal));
            Assert.That(adapter.TryOpen(), Is.False);
        }

        [Test]
        public void StaleFinishRebasesWithoutFreeCloseAndPauseCancellationAndDisposalRespectOwners()
        {
            CreateRuntime();
            var bag = presenter.InventoryDisplay;
            Assert.That(bag.TryOpen(), Is.True);
            Assert.That(bag.Draft.TryStage(InventoryItemActionKind.Use, "food"), Is.True);
            Assert.That(runtime.RunState.TryCommitInventory(0, runtime.RunState.Inventory.Placements, out _), Is.True);
            CommandSubmission stale = bag.FinishAsync().Result;
            Assert.That(stale.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(((RejectedTurnResult)stale.Result).RejectionCode, Is.EqualTo(CommandRejectionCode.StaleInventory));
            Assert.That(bag.IsOpen, Is.True);
            Assert.That(bag.Draft.ExpectedRevision, Is.EqualTo(1));
            Assert.That(bag.Draft.Actions, Is.Empty);
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(100));
            Assert.That(runtime.RunState.Inventory.Placements, Has.Count.EqualTo(2));
            var gate = presenter.Coordinator.Gate;
            gate.SetBlocked(PresentationInputBlock.Modal, true);
            Assert.That(bag.FinishAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            gate.SetBlocked(PresentationInputBlock.Modal, false);
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                Assert.That(bag.FinishAsync(canceled.Token).Result.Status, Is.EqualTo(CommandSubmissionStatus.Canceled));
            }
            Assert.That(bag.IsOpen, Is.True);
            Assert.That(gate.Blocks, Is.EqualTo(PresentationInputBlock.Inventory));
            Assert.That(bag.FinishAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(99));
            Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(2));
            Assert.That(bag.TryOpen(), Is.True);
            gate.SetBlocked(PresentationInputBlock.FieldNotes, true);
            presenter.Dispose();
            Assert.That(bag.IsOpen, Is.False);
            Assert.That(bag.Draft, Is.Null);
            Assert.That(gate.Blocks, Is.EqualTo(PresentationInputBlock.FieldNotes));
            Assert.That(bag.TryOpen(), Is.False);
        }

        [Test]
        public void AcceptedReplayFailureDoesNotReopenOrRepeatTheBagTransaction()
        {
            CreateRuntime();
            sink.ThrowOnCommit = true;
            var bag = presenter.InventoryDisplay;
            Assert.That(bag.TryOpen(), Is.True);
            Assert.That(bag.Draft.TryStage(InventoryItemActionKind.Discard, "food"), Is.True);
            CommandSubmission submission = bag.FinishAsync().Result;
            Assert.That(submission.Status, Is.EqualTo(CommandSubmissionStatus.PresentationFailed));
            Assert.That(submission.Result.Accepted, Is.True);
            Assert.That(bag.IsOpen, Is.False);
            Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(1));
            Assert.That(runtime.RunState.Inventory.Placements, Has.Count.EqualTo(1));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(99));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(bag.FinishAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
        }

        private void CreateRuntime(bool withEnemy = false)
        {
            var content = new InventoryContent(new ItemCatalog(new[]
            {
                new ItemDefinition("food", 1, 1, "satiety", "consumable", true, "food.effect"),
                new ItemDefinition("medicine", 1, 1, "health", "consumable", true, "health.effect")
            }), new ConsumableEffectCatalog(new[]
            {
                new ConsumableEffectDefinition("food.effect", ConsumableResource.Satiety, 50),
                new ConsumableEffectDefinition("health.effect", ConsumableResource.Health, 10)
            }));
            var run = new RunState("bag-presentation", 104, new RunStateConfiguration(95, 100, 0, "start", inventoryContent: content));
            run.RestoreInventoryState(content, 0, new[]
            {
                new InventoryPlacement(new ItemInstanceState("food", "food"), InventoryContainer.MainGrid),
                new InventoryPlacement(new ItemInstanceState("medicine", "medicine"), InventoryContainer.LeftPocket)
            });
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay, run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int column = 0; column < 3; column++)
                for (int row = 0; row < 2; row++) layout.Add(View(LegacyBoardContentKind.Floor, column, row));
            layout.Add(View(LegacyBoardContentKind.Player, 0, 0));
            layout.Add(View(LegacyBoardContentKind.Exit, 2, 1));
            if (withEnemy) layout.Add(View(LegacyBoardContentKind.Enemy, 1, 0, 10));
            runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 1), layout);
            sink = new Sink();
            presenter = new BoardEventPresenter(runtime, sink, sink, sink, value => sink.Diagnostic = value);
            presenter.SynchronizeFromState();
            presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
        }

        private LegacyBoardView View(LegacyBoardContentKind kind, int column, int row, int? enemyDamage = null)
        {
            var view = new GameObject(kind.ToString());
            view.transform.position = new Vector3(column, row, 0);
            objects.Add(view);
            return new LegacyBoardView(view, kind, new GridPosition(column, row), enemyDamage: enemyDamage);
        }

        private sealed class Sink : IRunHud, ITurnResourceGains, ITurnFeedback, IBoardOutcomeSink
        {
            public int Health, Satiety, HealthGain, SatietyGain;
            public bool ThrowOnCommit;
            public PresentationDiagnostic Diagnostic;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public void Refresh(int health, int satiety) { Health = health; Satiety = satiety; }
            public void AddResourceGains(int health, int satiety) { HealthGain += health; SatietyGain += satiety; }
            public void ClearResourceGains() { HealthGain = 0; SatietyGain = 0; }
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent)
            {
                Events.Add(gameEvent);
                if (ThrowOnCommit && gameEvent is InventoryCommittedEvent) throw new InvalidOperationException("Injected replay fault.");
            }
            public bool TryNotify(BoardRequest request, GameEvent outcome) => true;
        }
    }
}