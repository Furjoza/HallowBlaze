using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Verifies real pocket selection, single-submit replay and locked hostile response without a second command path.</summary>
    public sealed class QuickPocketPresentationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private BoardRuntime runtime;
        private BoardEventPresenter presenter;
        private Sink sink;

        /// <summary>One chosen pocket applies before one real locked attack, in either animation mode.</summary>
        [UnityTest]
        public IEnumerator QuickUseAndWaitHaveOneRealHostileResponse([Values(false, true)] bool animations, [Values(false, true)] bool right)
        {
            Create(withEnemy: true);
            presenter.AnimationsEnabled = animations;
            string pocket = right ? InventoryLayoutEntry.RightPocket : InventoryLayoutEntry.LeftPocket;
            Assert.That(presenter.QuickDisplay.TrySelect(pocket), Is.True);
            Assert.That(runtime.RunState.InventoryRevision, Is.Zero);
            Assert.That(runtime.RunState.Health, Is.EqualTo(95));
            Task<CommandSubmission> submit = presenter.SubmitAsync(new WaitCommand());
            while (!submit.IsCompleted)
            {
                Assert.That(presenter.QuickDisplay.TrySelect(pocket), Is.False);
                Assert.That(presenter.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                yield return null;
            }
            Assert.That(submit.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(sink.Diagnostic, Is.Null);
            Assert.That(runtime.RunState.Health, Is.EqualTo(right ? 90 : 85));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(right ? 99 : 104));
            Assert.That(sink.HealthGain, Is.EqualTo(right ? 5 : 0));
            Assert.That(sink.SatietyGain, Is.EqualTo(right ? 0 : 5));
            Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(1));
            Assert.That(sink.Events.OfType<QuickItemUsedEvent>().Single().Item.PocketId, Is.EqualTo(pocket));
            Assert.That(sink.Events.OfType<EnemyAttackResolvedEvent>().Count(), Is.EqualTo(1));
            Assert.That(submit.Result.Result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
            Assert.That(sink.Events.FindIndex(value => value is EnemyAttackResolvedEvent),
                Is.GreaterThan(sink.Events.FindIndex(value => value is QuickItemUsedEvent)));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(presenter.QuickDisplay.Selection, Is.Null);
        }

        /// <summary>A full bag can Store into the selected pocket being consumed, with one final inventory commit.</summary>
        [UnityTest]
        public IEnumerator PickupStoresIntoTheFreedPocketWithoutAnyPreviewMutation([Values(false, true)] bool animations)
        {
            Create(withGround: true, full: true);
            presenter.AnimationsEnabled = animations;
            Assert.That(presenter.QuickDisplay.TrySelect(InventoryLayoutEntry.LeftPocket), Is.True);
            var inventory = runtime.RunState.Inventory;
            Assert.That(presenter.SubmitAsync(new MoveCommand(Direction.East)).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            var modal = presenter.PickupDisplay;
            Assert.That(modal.Option.CanStore, Is.True);
            Assert.That(modal.SelectPlacement(InventoryLayoutEntry.LeftPocket), Is.True);
            Assert.That(modal.SelectPlacement(InventoryLayoutEntry.RightPocket), Is.False);
            Assert.That(runtime.RunState.Inventory, Is.SameAs(inventory));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(100));
            string ground = modal.Option.Item.InstanceId;
            Task<CommandSubmission> submit = modal.ConfirmAsync(GroundPickupChoice.Store);
            while (!submit.IsCompleted) yield return null;
            Assert.That(submit.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(sink.Diagnostic, Is.Null);
            Assert.That(runtime.RunState.Inventory.GetPocketItem(InventoryContainer.LeftPocket).InstanceId, Is.EqualTo(ground));
            Assert.That(runtime.RunState.Inventory.TryGetPlacement("food", out _), Is.False);
            Assert.That(runtime.RunState.Inventory.Placements, Has.Count.EqualTo(11));
            Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(1));
            Assert.That(sink.Events.OfType<InventoryCommittedEvent>().Count(), Is.EqualTo(1));
            Assert.That(sink.Events.OfType<GroundPickupResolvedEvent>().Count(), Is.EqualTo(1));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(104));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(presenter.QuickDisplay.Selection, Is.Null);
        }

        /// <summary>Pause/cancellation are free, rejected snapshots are forgotten rather than replaced, and disposal preserves other owners.</summary>
        [Test]
        public void PauseCancellationStalenessAndDisposalNeverConsumeASelection()
        {
            Create();
            var quick = presenter.QuickDisplay;
            var gate = presenter.Coordinator.Gate;
            string before = PersistenceJsonSerializer.SerializeRun(runtime.RunState);
            Assert.That(quick.TrySelect(InventoryLayoutEntry.LeftPocket), Is.True);
            Assert.That(quick.TrySelect(InventoryLayoutEntry.RightPocket), Is.True);
            Assert.That(quick.Selection.Item.InstanceId, Is.EqualTo("medicine"));
            Assert.That(quick.ClearSelection(), Is.True);
            Assert.That(quick.TrySelect(InventoryLayoutEntry.LeftPocket), Is.True);
            gate.SetBlocked(PresentationInputBlock.Modal, true);
            Assert.That(quick.TrySelect(InventoryLayoutEntry.RightPocket), Is.False);
            Assert.That(presenter.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(quick.Selection.Item.InstanceId, Is.EqualTo("food"));
            gate.SetBlocked(PresentationInputBlock.Modal, false);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.That(presenter.SubmitAsync(new WaitCommand(), cancellation.Token).Result.Status, Is.EqualTo(CommandSubmissionStatus.Canceled));
            }
            Assert.That(quick.Selection.Item.InstanceId, Is.EqualTo("food"));
            Assert.That(PersistenceJsonSerializer.SerializeRun(runtime.RunState), Is.EqualTo(before));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(runtime.RunState.TryCommitInventory(0, runtime.RunState.Inventory.Placements, out _), Is.True);
            string stale = PersistenceJsonSerializer.SerializeRun(runtime.RunState);
            Assert.That(presenter.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(quick.Selection, Is.Null);
            Assert.That(PersistenceJsonSerializer.SerializeRun(runtime.RunState), Is.EqualTo(stale));
            Assert.That(quick.TrySelect(InventoryLayoutEntry.RightPocket), Is.True);
            gate.SetBlocked(PresentationInputBlock.FieldNotes, true);
            presenter.Dispose();
            Assert.That(quick.Selection, Is.Null);
            Assert.That(quick.TrySelect(InventoryLayoutEntry.LeftPocket), Is.False);
            Assert.That(gate.Blocks, Is.EqualTo(PresentationInputBlock.FieldNotes));
            Assert.That(PersistenceJsonSerializer.SerializeRun(runtime.RunState), Is.EqualTo(stale));
        }

        /// <summary>Canceling the whole move/pickup leaves the pocket snapshot and both ownership states intact.</summary>
        [UnityTest]
        public IEnumerator PickupCancelAndStaleConfirmationCannotPartiallyUseThePocket()
        {
            Create(withGround: true);
            Assert.That(presenter.QuickDisplay.TrySelect(InventoryLayoutEntry.LeftPocket), Is.True);
            string before = PersistenceJsonSerializer.SerializeRun(runtime.RunState);
            Assert.That(presenter.SubmitAsync(new MoveCommand(Direction.East)).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Assert.That(presenter.QuickDisplay.TrySelect(InventoryLayoutEntry.RightPocket), Is.False);
            Assert.That(presenter.PickupDisplay.Cancel(), Is.True);
            Assert.That(PersistenceJsonSerializer.SerializeRun(runtime.RunState), Is.EqualTo(before));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(presenter.QuickDisplay.Selection.Item.InstanceId, Is.EqualTo("food"));
            yield return null;
            Assert.That(presenter.SubmitAsync(new MoveCommand(Direction.East)).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            var modal = presenter.PickupDisplay;
            string ground = modal.Option.Item.InstanceId;
            Assert.That(modal.SelectPlacement(InventoryLayoutEntry.LeftPocket), Is.True);
            Assert.That(runtime.RunState.TryCommitInventory(0, runtime.RunState.Inventory.Placements, out _), Is.True);
            string stale = PersistenceJsonSerializer.SerializeRun(runtime.RunState);
            Assert.That(modal.ConfirmAsync(GroundPickupChoice.Store).Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(modal.IsOpen, Is.False);
            Assert.That(presenter.QuickDisplay.Selection, Is.Null);
            Assert.That(PersistenceJsonSerializer.SerializeRun(runtime.RunState), Is.EqualTo(stale));
            Assert.That(runtime.BoardState.TryGetGroundItem(runtime.BoardState.GetEntities().Single(entity =>
                entity.Definition.ContentId == "legacy.food").Id, out var item), Is.True);
            Assert.That(item.InstanceId, Is.EqualTo(ground));
        }

        /// <summary>Bag sessions never inherit the quick selector; after a bag result its old snapshot cannot consume a newly moved item.</summary>
        [Test]
        public void BagFinishAndQuickPayloadCannotCombineAndARepackedPocketNeedsAFreshSelection()
        {
            Create();
            Assert.That(presenter.QuickDisplay.TrySelect(InventoryLayoutEntry.LeftPocket), Is.True);
            string before = PersistenceJsonSerializer.SerializeRun(runtime.RunState);
            Assert.That(presenter.SubmitAsync(new InventoryDraft(runtime.RunState).CreateCommand()).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(presenter.QuickDisplay.Selection, Is.Null);
            Assert.That(PersistenceJsonSerializer.SerializeRun(runtime.RunState), Is.EqualTo(before));
            Assert.That(presenter.QuickDisplay.TrySelect(InventoryLayoutEntry.LeftPocket), Is.True);
            Assert.That(presenter.InventoryDisplay.TryOpen(), Is.True);
            Assert.That(presenter.QuickDisplay.TrySelect(InventoryLayoutEntry.RightPocket), Is.False);
            Assert.That(presenter.InventoryDisplay.FinishAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.Inventory.Placements, Has.Count.EqualTo(2));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(99));
            string afterBag = PersistenceJsonSerializer.SerializeRun(runtime.RunState);
            Assert.That(presenter.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(PersistenceJsonSerializer.SerializeRun(runtime.RunState), Is.EqualTo(afterBag));
            Assert.That(presenter.QuickDisplay.TrySelect(InventoryLayoutEntry.RightPocket), Is.True);
            Assert.That(presenter.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(2));
        }

        /// <summary>Cancellation after accepted mutation recovers presentation but cannot retry or refund the one consumed item.</summary>
        [UnityTest]
        public IEnumerator AcceptedCancellationDoesNotReapplyQuickUse()
        {
            Create();
            presenter.AnimationsEnabled = true;
            Assert.That(presenter.QuickDisplay.TrySelect(InventoryLayoutEntry.LeftPocket), Is.True);
            using (var cancellation = new CancellationTokenSource())
            {
                Task<CommandSubmission> submit = presenter.SubmitAsync(new MoveCommand(Direction.East), cancellation.Token);
                Assert.That(submit.IsCompleted, Is.False);
                Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(1));
                cancellation.Cancel();
                while (!submit.IsCompleted) yield return null;
                Assert.That(submit.Result.Status, Is.EqualTo(CommandSubmissionStatus.PresentationFailed));
                Assert.That(submit.Result.Result.Accepted, Is.True);
            }
            Assert.That(presenter.QuickDisplay.Selection, Is.Null);
            Assert.That(runtime.RunState.Inventory.TryGetPlacement("food", out _), Is.False);
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(104));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(presenter.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.InventoryRevision, Is.EqualTo(1));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(102));
        }

        /// <summary>The existing v8 boundary stores only completed ownership, never a selected pocket or pending envelope.</summary>
        [Test]
        public void SelectionIsNotSavedAndAcceptedResultRoundTripsTheExistingSchema()
        {
            Create();
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HB-M106-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileSystemSaveStore(root, inventoryContent: runtime.RunState.InventoryContent);
                Assert.That(presenter.QuickDisplay.TrySelect(InventoryLayoutEntry.LeftPocket), Is.True);
                Assert.That(store.SaveRun(runtime.RunState).IsSuccess, Is.True);
                RunState before = store.LoadRun().Data;
                Assert.That(before.InventoryRevision, Is.Zero);
                Assert.That(before.Inventory.GetPocketItem(InventoryContainer.LeftPocket).InstanceId, Is.EqualTo("food"));
                Assert.That(presenter.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(store.SaveRun(runtime.RunState).IsSuccess, Is.True);
                RunState after = store.LoadRun().Data;
                Assert.That(after.InventoryRevision, Is.EqualTo(1));
                Assert.That(after.Inventory.GetPocketItem(InventoryContainer.LeftPocket), Is.Null);
                Assert.That(after.Inventory.GetPocketItem(InventoryContainer.RightPocket).InstanceId, Is.EqualTo("medicine"));
                string json = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "run.json"));
                Assert.That(json, Does.Contain("\"schemaVersion\": 8").And.Not.Contain("QuickPocket").And.Not.Contain("expectedRevision"));
            }
            finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
        }

        [TearDown]
        public void TearDown()
        {
            presenter?.Dispose();
            runtime?.Dispose();
            foreach (GameObject instance in objects)
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            objects.Clear();
        }

        private void Create(bool withEnemy = false, bool withGround = false, bool full = false)
        {
            InventoryContent content = GroundPickupContent.Load();
            var run = new RunState("quick-presentation", 106, new RunStateConfiguration(95, 100, 0, "start", inventoryContent: content));
            var placements = new List<InventoryPlacement>
            {
                new InventoryPlacement(new ItemInstanceState("food", "legacy.food"), InventoryContainer.LeftPocket),
                new InventoryPlacement(new ItemInstanceState("medicine", "legacy.aid"), InventoryContainer.RightPocket)
            };
            if (full)
                for (int column = 0; column < 3; column++)
                    for (int row = 0; row < 3; row++) placements.Add(new InventoryPlacement(
                        new ItemInstanceState("filler." + column + "." + row, "legacy.food"), InventoryContainer.MainGrid, column, row));
            run.RestoreInventoryState(content, 0, placements);
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay, run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int column = 0; column < 3; column++)
                for (int row = 0; row < 2; row++) layout.Add(View(LegacyBoardContentKind.Floor, column, row));
            layout.Add(View(LegacyBoardContentKind.Player, 0, 0));
            layout.Add(View(LegacyBoardContentKind.Exit, 2, 1));
            if (withEnemy) layout.Add(View(LegacyBoardContentKind.Enemy, 1, 0, 10));
            if (withGround) layout.Add(View(LegacyBoardContentKind.Food, 1, 0));
            runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 1), layout);
            sink = new Sink();
            presenter = new BoardEventPresenter(runtime, sink, sink, sink, value => sink.Diagnostic = value) { AnimationsEnabled = false };
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
            internal int HealthGain, SatietyGain;
            internal PresentationDiagnostic Diagnostic;
            internal readonly List<GameEvent> Events = new List<GameEvent>();
            public void Refresh(int health, int satiety) { }
            public void AddResourceGains(int health, int satiety) { HealthGain += health; SatietyGain += satiety; }
            public void ClearResourceGains() { HealthGain = 0; SatietyGain = 0; }
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) => Events.Add(gameEvent);
            public bool TryNotify(BoardRequest request, GameEvent outcome) => true;
        }
    }
}