using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Exercises the real pre-command modal, ordered runtime replay and entry-only triggers.</summary>
    public sealed class GroundPickupPresentationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private BoardRuntime runtime;
        private BoardEventPresenter presenter;
        private Sink sink;

        /// <summary>Cancel is free, Leave moves once, standing does not prompt, and reentry stores the unchanged exact identity.</summary>
        [UnityTest]
        public IEnumerator LeaveAndReenterPreservesIdentityAndBlocksRapidInput([Values(false, true)] bool animations)
        {
            Create();
            presenter.AnimationsEnabled = animations;
            var modal = presenter.PickupDisplay;
            Assert.That(presenter.SubmitAsync(new MoveCommand(Direction.East)).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            string identity = modal.Option.Item.InstanceId;
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(100));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(presenter.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            Assert.That(modal.Cancel(), Is.True);
            yield return null;
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(100));
            Assert.That(presenter.SubmitAsync(new MoveCommand(Direction.East)).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Task<CommandSubmission> leave = modal.ConfirmAsync(GroundPickupChoice.Leave);
            while (!leave.IsCompleted)
            {
                Assert.That(modal.ConfirmAsync(GroundPickupChoice.Leave).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                yield return null;
            }
            Assert.That(leave.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(99));
            Assert.That(modal.IsOpen, Is.False);
            Task<CommandSubmission> wait = presenter.SubmitAsync(new WaitCommand());
            while (!wait.IsCompleted) yield return null;
            Assert.That(modal.IsOpen, Is.False);
            Task<CommandSubmission> back = presenter.SubmitAsync(new MoveCommand(Direction.West));
            while (!back.IsCompleted) yield return null;
            Assert.That(presenter.SubmitAsync(new MoveCommand(Direction.East)).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Assert.That(modal.Option.Item.InstanceId, Is.EqualTo(identity));
            Assert.That(modal.SelectPlacement(InventoryLayoutEntry.MainGrid, 1, 1), Is.True);
            Task<CommandSubmission> store = modal.ConfirmAsync(GroundPickupChoice.Store);
            while (!store.IsCompleted) yield return null;
            Assert.That(store.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.Inventory.Placements.Single().Item.InstanceId, Is.EqualTo(identity));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(96));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(4));
            Assert.That(sink.Diagnostic, Is.Null);
            Assert.That(sink.Events.OfType<GroundPickupResolvedEvent>().Count(), Is.EqualTo(2));
        }

        /// <summary>Independent pause/inspection owners block confirmation; stale and canceled previews never remove an item or resolve half a turn.</summary>
        [UnityTest]
        public IEnumerator PauseStaleAndCancellationKeepTheBoardAndOtherInputOwnersIntact()
        {
            Create();
            var modal = presenter.PickupDisplay;
            Assert.That(modal.TryOpen(new MoveCommand(Direction.East)), Is.True);
            var gate = presenter.Coordinator.Gate;
            gate.SetBlocked(PresentationInputBlock.Modal, true);
            Assert.That(modal.ConfirmAsync(GroundPickupChoice.UseNow).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
            gate.SetBlocked(PresentationInputBlock.Modal, false);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.That(modal.ConfirmAsync(GroundPickupChoice.UseNow, cancellation.Token).Result.Status, Is.EqualTo(CommandSubmissionStatus.Canceled));
            }
            Assert.That(modal.IsOpen, Is.True);
            Assert.That(presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(runtime.RunState.TryCommitInventory(0, runtime.RunState.Inventory.Placements, out _), Is.True);
            Assert.That(modal.ConfirmAsync(GroundPickupChoice.UseNow).Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(modal.IsOpen, Is.False);
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(100));
            Assert.That(runtime.BoardState.TryGetEntity(runtime.PlayerId, out var player), Is.True);
            Assert.That(player.Position, Is.EqualTo(new GridPosition(0, 0)));
            Assert.That(modal.TryOpen(new MoveCommand(Direction.East)), Is.True);
            gate.SetBlocked(PresentationInputBlock.FieldNotes, true);
            presenter.Dispose();
            Assert.That(gate.Blocks, Is.EqualTo(PresentationInputBlock.FieldNotes));
            yield return null;
        }

        /// <summary>Continue rolls back unsaved choices to the exact entry identity, and a committed Store round-trips schema v8 without rerolling.</summary>
        [Test]
        public void ReloadUsesTheSameEntryIdentityAndTrustedStoreContentWithoutSerializingAModal()
        {
            Create();
            string root = Path.Combine(Path.GetTempPath(), "HB-M105-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileSystemSaveStore(root, inventoryContent: GroundPickupContent.Load());
                Assert.That(store.SaveRun(runtime.RunState).IsSuccess, Is.True);
                Assert.That(presenter.PickupDisplay.TryOpen(new MoveCommand(Direction.East)), Is.True);
                string identity = presenter.PickupDisplay.Option.Item.InstanceId;
                Assert.That(presenter.PickupDisplay.ConfirmAsync(GroundPickupChoice.Leave).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                RunState continued = store.LoadRun().Data;
                Assert.That(continued.Satiety, Is.EqualTo(100));
                Assert.That(continued.Inventory.Placements, Is.Empty);
                Assert.That(HallowBlaze.Core.Turns.Resolution.GroundItemIdentity.Create(continued,
                    sink.Events.OfType<GroundPickupResolvedEvent>().Single().Command.EntityId), Is.EqualTo(identity));
                presenter.Dispose();
                runtime.Dispose();
                Create(continued);
                presenter.SubmitAsync(new MoveCommand(Direction.East)).GetAwaiter().GetResult();
                Assert.That(presenter.PickupDisplay.Option.Item.InstanceId, Is.EqualTo(identity));
                Assert.That(presenter.PickupDisplay.SelectPlacement(InventoryLayoutEntry.RightPocket), Is.True);
                Assert.That(presenter.PickupDisplay.ConfirmAsync(GroundPickupChoice.Store).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(store.SaveRun(runtime.RunState).IsSuccess, Is.True);
                RunState saved = store.LoadRun().Data;
                Assert.That(saved.InventoryRevision, Is.EqualTo(1));
                Assert.That(saved.Inventory.Placements.Single().Item.InstanceId, Is.EqualTo(identity));
                Assert.That(saved.Inventory.Placements.Single().Item.DefinitionId, Is.EqualTo("legacy.food"));
                Assert.That(saved.InventoryContent, Is.SameAs(GroundPickupContent.Load()));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        /// <summary>Use presents only the actual capped gain and no silent automatic reward in either animation mode.</summary>
        [UnityTest]
        public IEnumerator ConsciousUsePresentsExactAppliedGain([Values(false, true)] bool animations)
        {
            Create();
            presenter.AnimationsEnabled = animations;
            presenter.SubmitAsync(new MoveCommand(Direction.East)).GetAwaiter().GetResult();
            Task<CommandSubmission> use = presenter.PickupDisplay.ConfirmAsync(GroundPickupChoice.UseNow);
            while (!use.IsCompleted) yield return null;
            Assert.That(use.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(104));
            Assert.That(sink.SatietyGain, Is.EqualTo(5));
            Assert.That(sink.Events.OfType<GroundPickupResolvedEvent>().Single().Applied, Is.EqualTo(5));
            Assert.That(sink.Diagnostic, Is.Null);
            Assert.That(presenter.PickupDisplay.IsOpen, Is.False);
        }

        /// <summary>One pickup move executes the shown conditional Shambler response once after its effect and preselected cost.</summary>
        [UnityTest]
        public IEnumerator UseThenOneActualHostileResponsePreservesChronology([Values(false, true)] bool animations)
        {
            Create(withEnemy: true);
            presenter.AnimationsEnabled = animations;
            presenter.SubmitAsync(new MoveCommand(Direction.East)).GetAwaiter().GetResult();
            Task<CommandSubmission> use = presenter.PickupDisplay.ConfirmAsync(GroundPickupChoice.UseNow);
            while (!use.IsCompleted) yield return null;
            Assert.That(use.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(use.Result.Result.Events.OfType<EnemyAttackResolvedEvent>().Count(), Is.EqualTo(1));
            Assert.That(use.Result.Result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
            Assert.That(runtime.RunState.Health, Is.EqualTo(85));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(104));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(sink.Events.FindIndex(value => value is EnemyAttackResolvedEvent),
                Is.GreaterThan(sink.Events.FindIndex(value => value is GroundPickupResolvedEvent)));
            Assert.That(sink.Diagnostic, Is.Null);
        }

        /// <summary>A revealed cache reward left in place cannot be retried through collect; entering its tile opens a fresh exact decision.</summary>
        [UnityTest]
        public IEnumerator LeftCacheRewardRequiresEntryRatherThanAnotherInteraction()
        {
            Create(withCache: true);
            var use = runtime.Tools.GetOptions(runtime.BoardState, runtime.RunState, runtime.PlayerId).Single(option => option.ActionId == "unearth");
            Assert.That(presenter.SubmitAsync(use.CreateCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            string identity = presenter.PickupDisplay.Option.Item.InstanceId;
            Assert.That(presenter.PickupDisplay.ConfirmAsync(GroundPickupChoice.Leave).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(99));
            var collect = runtime.Interactions.GetOptions(runtime.BoardState, runtime.RunState, runtime.PlayerId).Single(option => option.ActionId == "collect");
            Assert.That(collect.IsAvailable, Is.False);
            Assert.That(presenter.SubmitAsync(collect.CreateCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(presenter.PickupDisplay.IsOpen, Is.False);
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(99));
            Assert.That(runtime.RunState.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            Assert.That(presenter.SubmitAsync(new MoveCommand(Direction.East)).Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Assert.That(presenter.PickupDisplay.Option.Item.InstanceId, Is.EqualTo(identity));
            Assert.That(presenter.PickupDisplay.SelectPlacement(InventoryLayoutEntry.MainGrid), Is.True);
            Assert.That(presenter.PickupDisplay.ConfirmAsync(GroundPickupChoice.Store).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.Inventory.Placements.Single().Item.InstanceId, Is.EqualTo(identity));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(98));
            Assert.That(sink.Diagnostic, Is.Null);
            yield return null;
        }

        /// <summary>Renders the real Leave and reentry board states with existing sprite assets, never a fabricated UI image.</summary>
        [UnityTest]
        [Category("PickupVisualEvidence")]
        public IEnumerator LeaveThenReentryRendersTheSameGroundInstance()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            Create();
#if UNITY_EDITOR
            foreach (GameObject view in runtime.Views.Values)
            {
                string path = view.name == "Floor" ? "Assets/Prefabs/Tiles/Floor1.prefab" :
                    view.name == "Player" ? "Assets/Prefabs/Player.prefab" : view.name == "Food" ?
                    "Assets/Prefabs/Tiles/Raspberries.prefab" : "Assets/Prefabs/Tiles/Exit.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                Sprite sprite = prefab.GetComponentInChildren<SpriteRenderer>(true).sprite;
                if (sprite == null)
                {
                    Animator animator = prefab.GetComponentInChildren<Animator>(true);
                    sprite = animator.runtimeAnimatorController.animationClips.SelectMany(clip =>
                        AnimationUtility.GetObjectReferenceCurveBindings(clip).Where(binding => binding.type == typeof(SpriteRenderer))
                            .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(clip, binding)))
                        .Select(frame => frame.value as Sprite).First(candidate => candidate != null);
                }
                SpriteRenderer renderer = view.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingLayerName = view.name == "Floor" ? "Floor" : "Units";
            }
#endif
            presenter.SubmitAsync(new MoveCommand(Direction.East)).GetAwaiter().GetResult();
            string identity = presenter.PickupDisplay.Option.Item.InstanceId;
            presenter.PickupDisplay.ConfirmAsync(GroundPickupChoice.Leave).GetAwaiter().GetResult();
            CaptureEvidence("leave", identity);
            presenter.SubmitAsync(new MoveCommand(Direction.West)).GetAwaiter().GetResult();
            presenter.SubmitAsync(new MoveCommand(Direction.East)).GetAwaiter().GetResult();
            Assert.That(presenter.PickupDisplay.IsOpen, Is.True);
            Assert.That(presenter.PickupDisplay.Option.Item.InstanceId, Is.EqualTo(identity));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(98));
            CaptureEvidence("reentry-preview", identity);
            yield return null;
        }

        private void CaptureEvidence(string stage, string identity)
        {
            var owner = new GameObject("Pickup evidence camera");
            objects.Add(owner);
            Camera camera = owner.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1.7f;
            camera.transform.position = new Vector3(1.5f, 0.5f, -20f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.1f, 0.08f);
            var target = new RenderTexture(960, 540, 24);
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                pixels.Apply();
                Assert.That(pixels.GetPixels32().Count(color => color.r > 90 || color.g > 90 || color.b > 90), Is.GreaterThan(1000));
                string path = Path.Combine(Path.GetTempPath(), "HB-M105-" + stage + ".png");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                TestContext.WriteLine(stage + ": " + identity + "; Satiety=" + runtime.RunState.Satiety + "; " + path);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        private void Create(RunState restored = null, bool withEnemy = false, bool withCache = false)
        {
            var run = restored ?? new RunState("pickup-presentation", 105, new RunStateConfiguration(95, 100, 0, "start", inventoryContent: GroundPickupContent.Load()));
            if (withCache) run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3)));
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay, run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int column = 0; column < 4; column++)
                for (int row = 0; row < 2; row++) layout.Add(View(LegacyBoardContentKind.Floor, column, row));
            layout.Add(View(LegacyBoardContentKind.Player, 0, 0));
            layout.Add(View(withCache ? LegacyBoardContentKind.BuriedFood : LegacyBoardContentKind.Food, 1, 0));
            layout.Add(View(LegacyBoardContentKind.Exit, 3, 1));
            if (withEnemy) layout.Add(View(LegacyBoardContentKind.Enemy, 2, 0));
            runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 3, 1), layout);
            sink = new Sink();
            presenter = new BoardEventPresenter(runtime, sink, sink, sink, value => sink.Diagnostic = value);
            presenter.AnimationsEnabled = false;
            presenter.SynchronizeFromState();
            presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
        }
        private LegacyBoardView View(LegacyBoardContentKind kind, int column, int row)
        {
            var view = new GameObject(kind.ToString());
            view.transform.position = new Vector3(column, row, 0);
            objects.Add(view);
            return new LegacyBoardView(view, kind, new GridPosition(column, row),
                enemyDamage: kind == LegacyBoardContentKind.Enemy ? (int?)10 : null);
        }
        /// <summary>Releases borrowed test views and all board-owned modal/presentation lifetimes.</summary>
        [TearDown]
        public void TearDown()
        {
            presenter?.Dispose(); runtime?.Dispose();
            foreach (GameObject instance in objects) if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            objects.Clear();
        }
        private sealed class Sink : IRunHud, ITurnFeedback, IBoardOutcomeSink, ITurnResourceGains
        {
            public PresentationDiagnostic Diagnostic;
            public int SatietyGain;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public void Refresh(int health, int satiety) { }
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) => Events.Add(gameEvent);
            public bool TryNotify(BoardRequest request, GameEvent outcome) => true;
            public void AddResourceGains(int health, int satiety) => SatietyGain += satiety;
            public void ClearResourceGains() => SatietyGain = 0;
        }
    }
}