#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;
using Object = UnityEngine.Object;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Uses the production picker/modal, composer and event pipeline for conscious equipment transfer and free cancellation.</summary>
    public sealed class ToolPickupPresentationTests
    {
        private readonly List<Fixture> fixtures = new List<Fixture>();

        /// <summary>Closes only fixture-owned views and isolated persistence roots.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (Fixture fixture in fixtures) fixture.Dispose();
            fixtures.Clear();
        }

        /// <summary>The first empty slot needs one conscious confirmation, never movement entry or a separate replacement turn.</summary>
        [UnityTest]
        public IEnumerator EmptySlotPickupUsesExactGroundInstanceAndResolvesOnce()
        {
            foreach (bool animations in new[] { false, true })
            {
                Fixture fixture = CreateFixture(1, animations);
                var picker = fixture.Presenter.InteractionDisplay;
                Assert.That(fixture.Runtime.BoardState.TryGetGroundTool(fixture.ToolEntityId, out GroundToolState ground), Is.True);
                Assert.That(picker.Open(), Is.True);
                Assert.That(picker.PickupOptions.Single().Tool, Is.SameAs(ground));
                Assert.That(picker.GetOptionLabel(0), Does.Contain("Shovel (3 charges)").And.Contain("Food -1"));
                Assert.That(picker.Select(0), Is.True);
                Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(picker.IsReplacing, Is.False);
                Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
                Assert.That(fixture.Run.ToolSlots[1].Instance.InstanceId, Is.EqualTo(ground.InstanceId));
                Assert.That(fixture.Hud.GetSlotText(1), Is.EqualTo("Shovel  3"));
                Assert.That(fixture.ToolView.activeSelf, Is.False);
                Assert.That(fixture.Run.Food, Is.EqualTo(19));
                fixture.Dispose();
                yield return null;
            }
        }

        /// <summary>Opening, choosing a slot, cancelling and Leave are free; only a complete confirmed replacement submits a turn.</summary>
        [UnityTest]
        public IEnumerator FullEquipmentModalComparesBothSlotsAndPreservesOldToolOnGround()
        {
            foreach (bool animations in new[] { false, true })
            {
                Fixture fixture = CreateFixture(2, animations);
                var picker = fixture.Presenter.InteractionDisplay;
                GroundToolState incoming = fixture.Ground;
                ToolInstanceState old = fixture.Run.ToolSlots[0].Instance;
                foreach (bool leave in new[] { false, true })
                {
                    Assert.That(picker.Open(), Is.True);
                    Assert.That(picker.Select(0), Is.True);
                    Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
                    Assert.That(picker.IsReplacing, Is.True);
                    Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                    Assert.That(picker.ReplacementLabels[0], Does.Contain("Axe (2 charges)").And.Contain("Shovel (3 charges)").And.Contain("Old tool remains here"));
                    Assert.That(picker.ReplacementLabels[1], Does.Contain("Axe (1 charges)").And.Contain("Shovel (3 charges)"));
                    Assert.That(picker.SelectReplacement(0), Is.True);
                    if (leave) picker.Leave(); else picker.Cancel();
                    Assert.That(fixture.Ground, Is.SameAs(incoming));
                    Assert.That(fixture.Run.ToolSlots[0].Instance, Is.SameAs(old));
                    Assert.That(fixture.Run.Food, Is.EqualTo(20));
                    Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
                    Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                    yield return null;
                }
                Assert.That(picker.Open(), Is.True);
                Assert.That(picker.Select(0), Is.True);
                picker.ConfirmAsync();
                Assert.That(picker.SelectReplacement(0), Is.True);
                CommandSubmission result = picker.ConfirmAsync().Result;
                Assert.That(result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(result.Result.Events.OfType<ToolPickedUpEvent>().Single().DisplacedTool.InstanceId, Is.EqualTo(old.InstanceId));
                Assert.That(fixture.Ground.InstanceId, Is.EqualTo(old.InstanceId));
                Assert.That(fixture.Ground.Charges, Is.EqualTo(2));
                Assert.That(fixture.ToolView.activeSelf, Is.True);
                Assert.That(fixture.Presenter.GroundToolsDisplay.Root.GetComponentsInChildren<TextMesh>().Single().text, Is.EqualTo("Axe\n2 charges"));
                Assert.That(fixture.Hud.GetSlotText(0), Is.EqualTo("Shovel  3"));
                Assert.That(fixture.Run.Food, Is.EqualTo(19));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
                Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
                fixture.Dispose();
                yield return null;
            }
        }

        /// <summary>Mutation after the modal snapshot rejects the exact choice; no other tool, turn or noise is substituted.</summary>
        [UnityTest]
        public IEnumerator StaleReplacementAndExternalModalOwnershipKeepGroundToolAndResources()
        {
            Fixture fixture = CreateFixture(2, false);
            var picker = fixture.Presenter.InteractionDisplay;
            GroundToolState ground = fixture.Ground;
            Assert.That(picker.Open(), Is.True);
            picker.Select(0);
            picker.ConfirmAsync();
            Assert.That(picker.SelectReplacement(0), Is.True);
            fixture.Run.TrySpendToolCharge(0, fixture.Run.ToolSlots[0].Instance.InstanceId);
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(fixture.Ground, Is.SameAs(ground));
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
            Assert.That(fixture.Runtime.BoardState.GetActiveNoises(), Is.Empty);
            yield return null;
            Assert.That(picker.Open(), Is.True);
            picker.Select(0);
            picker.ConfirmAsync();
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Modal, true);
            Assert.That(picker.SelectReplacement(0), Is.False);
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            picker.Cancel();
            yield return null;
            Assert.That(fixture.Presenter.Coordinator.Gate.Blocks & PresentationInputBlock.Modal, Is.Not.EqualTo(PresentationInputBlock.None));
            Assert.That(fixture.Ground, Is.SameAs(ground));
        }

        /// <summary>A confirmed exchange survives save and scene reload; an open replacement draft releases its old input lifetime without a turn.</summary>
        [UnityTest]
        public IEnumerator ReplacementSaveAndOpenDraftSurviveSceneBoundaryWithoutDuplicateTransfer()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene temporary = SceneManager.CreateScene("M5.6 Tool pickup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(temporary);
            Fixture fixture = CreateFixture(2, false);
            var picker = fixture.Presenter.InteractionDisplay;
            picker.Open();
            picker.Select(0);
            picker.ConfirmAsync();
            picker.SelectReplacement(1);
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            string identity = fixture.Run.ToolSlots[1].Instance.InstanceId;
            Assert.That(fixture.Store.SaveRun(fixture.Run).IsSuccess, Is.True);
            RunState restored = fixture.Store.LoadRun().Data;
            yield return null;
            Assert.That(picker.Open(), Is.True);
            picker.Select(0);
            picker.ConfirmAsync();
            Assert.That(picker.IsReplacing, Is.True);
            Assert.That(fixture.Run.Food, Is.EqualTo(19));
            fixture.Dispose();
            SceneManager.SetActiveScene(previous);
            yield return SceneManager.UnloadSceneAsync(temporary);
            Assert.That(picker.IsDisposed, Is.True);
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
            Fixture next = CreateFixture(0, false, restored);
            Assert.That(next.Hud.GetSlotText(0), Is.EqualTo("Axe  2"));
            Assert.That(next.Hud.GetSlotText(1), Is.EqualTo("Shovel  3"));
            Assert.That(next.Run.ToolSlots[1].Instance.InstanceId, Is.EqualTo(identity));
            Assert.That(next.Run.Food, Is.EqualTo(19));
            Assert.That(next.Presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(next.Ground.InstanceId, Is.Not.EqualTo(identity));
        }

        /// <summary>After an exchange the current ground tool and equipment labels render in desktop and narrow viewports.</summary>
        [TestCase(960, 540)]
        [TestCase(320, 640)]
        public void ExchangedGroundToolAndEquipmentRenderReadablePixels(int width, int height)
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            Fixture fixture = CreateFixture(2, false);
            var picker = fixture.Presenter.InteractionDisplay;
            picker.Open();
            picker.Select(0);
            picker.ConfirmAsync();
            picker.SelectReplacement(0);
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(fixture.Hud.GetSlotText(0), Is.EqualTo("Shovel  3"));
            Assert.That(fixture.Presenter.GroundToolsDisplay.Root.GetComponentsInChildren<TextMesh>().Single().text, Is.EqualTo("Axe\n2 charges"));
            fixture.Hud.Resize(new Vector2(width, height));
            var cameraOwner = new GameObject("Tool pickup evidence camera");
            fixture.Objects.Add(cameraOwner);
            Camera camera = cameraOwner.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = width < height ? 3.4f : 1.7f;
            camera.transform.position = new Vector3(1, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.06f);
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                Canvas canvas = fixture.Hud.Root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                foreach (UnityEngine.UI.Text label in fixture.Hud.Root.GetComponentsInChildren<UnityEngine.UI.Text>())
                    Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width), label.text);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                int groundBright = 0;
                int hudBright = 0;
                for (int horizontal = 0; horizontal < width; horizontal++)
                for (int vertical = 0; vertical < height; vertical++)
                {
                    Color color = pixels.GetPixel(horizontal, vertical);
                    if (color.r < 0.7f || color.g < 0.7f || color.b < 0.7f) continue;
                    if (vertical > height - 68) hudBright++;
                    else groundBright++;
                }
                Assert.That(hudBright, Is.GreaterThan(100));
                Assert.That(groundBright, Is.GreaterThan(30));
                string image = Path.Combine(Path.GetTempPath(), "HB-M56-exchange-" + width + "x" + height + ".png");
                File.WriteAllBytes(image, pixels.EncodeToPNG());
                TestContext.WriteLine("Tool exchange evidence: " + image);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>Rendered replacement focus remains visible when its viewport shrinks without another selection edge.</summary>
        [UnityTest]
        public IEnumerator PickerRevealsRetainedRowAfterViewportShrinks()
        {
            Fixture fixture = CreateFixture(2, false);
            var picker = fixture.Presenter.InteractionDisplay;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            GameObject cameraOwner = new GameObject("Picker render camera", typeof(Camera));
            fixture.Objects.Add(cameraOwner);
            cameraOwner.transform.position = new Vector3(1, 0, -10);
            EditorWindow gameView = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            Vector2 size = new Vector2(960, 720);
            picker.GetType().GetField("viewportSize", flags).SetValue(picker, (Func<Vector2>)(() => size));
            Assert.That(picker.Open(), Is.True);
            picker.Select(0);
            picker.ConfirmAsync();
            picker.SelectReplacement(1);
            for (int frame = 0; frame < 10; frame++) { gameView.Repaint(); yield return null; }
            float originalHeight = (float)picker.GetType().GetField("scrollViewportHeight", flags).GetValue(picker);
            Assert.That(originalHeight, Is.GreaterThan(0), "The production picker must actually repaint.");
            size = new Vector2(320, 360);
            for (int frame = 0; frame < 10; frame++) { gameView.Repaint(); yield return null; }
            float height = (float)picker.GetType().GetField("scrollViewportHeight", flags).GetValue(picker);
            Rect row = (Rect)picker.GetType().GetField("selectedRow", flags).GetValue(picker);
            Vector2 offset = (Vector2)picker.GetType().GetField("scroll", flags).GetValue(picker);
            Assert.That(height, Is.LessThan(originalHeight));
            Assert.That(offset.y, Is.GreaterThan(0));
            Assert.That(row.yMin, Is.GreaterThanOrEqualTo(offset.y - 1));
            if (row.height <= height) Assert.That(row.yMax, Is.LessThanOrEqualTo(offset.y + height + 1));
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
        }

        /// <summary>F cancels a nested comparison, repeat samples do not retoggle, and Space never leaks a Wait.</summary>
        [UnityTest]
        public IEnumerator ContextualReplacementToggleAndFrameOwnershipStayFree()
        {
            Fixture fixture = CreateFixture(2, false);
            var picker = fixture.Presenter.InteractionDisplay;
            picker.SampleInputAsync(key => key == KeyCode.F, -100);
            picker.SampleInputAsync(key => key == KeyCode.F, -100);
            Assert.That(picker.IsOpen, Is.True);
            picker.SampleInputAsync(key => key == KeyCode.E, -99);
            picker.SampleInputAsync(key => key == KeyCode.Space, -98);
            Assert.That(picker.IsReplacing, Is.True);
            picker.SampleInputAsync(key => key == KeyCode.E, -97);
            picker.SampleInputAsync(key => key == KeyCode.F || key == KeyCode.Space, -96);
            Assert.That(picker.IsOpen, Is.False);
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
            Assert.That(fixture.Run.ToolSlots[0].Instance.DefinitionId, Is.EqualTo("axe.basic"));
            Assert.That(fixture.Presenter.Coordinator.SubmitFromAsync(new PcCommandSource(key => key == KeyCode.Space)).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Blocked));
            yield return null;
            picker.SampleInputAsync(key => key == KeyCode.F, -95);
            Assert.That(picker.IsOpen, Is.True);
            picker.SampleInputAsync(key => false, -94);
            Assert.That(picker.IsOpen, Is.True, "A held key has no new key-down edge.");
            picker.SampleInputAsync(key => key == KeyCode.Escape, -93);
            Assert.That(picker.IsOpen, Is.False);
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
        }

        /// <summary>Object selection is ordered by cell and identity, wraps both ways and retains valid refreshed selection.</summary>
        [Test]
        public void ContextualObjectSelectionWrapsAndRefreshesWithoutAnAction()
        {
            Fixture fixture = CreateFixture(0, false, secondTool: true);
            var picker = fixture.Presenter.InteractionDisplay;
            picker.SampleInputAsync(key => key == KeyCode.F, -90);
            Assert.That(picker.PickupOptions.Count, Is.EqualTo(2));
            picker.SampleInputAsync(key => key == KeyCode.E, -89);
            Assert.That(picker.PickupOptions[picker.SelectedIndex].Position, Is.EqualTo(new GridPosition(0, 0)));
            picker.SampleInputAsync(key => key == KeyCode.Q, -88);
            Assert.That(picker.PickupOptions[picker.SelectedIndex].Position, Is.EqualTo(new GridPosition(1, 0)));
            int retained = picker.SelectedIndex;
            Assert.That(picker.RefreshOptions(), Is.True);
            Assert.That(picker.SelectedIndex, Is.EqualTo(retained));
            picker.SampleInputAsync(key => key == KeyCode.Q || key == KeyCode.E, -87);
            Assert.That(picker.SelectedIndex, Is.EqualTo(retained));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
        }

        /// <summary>Empty, stale, paused and disposed picker contexts never consume a turn or select a replacement target.</summary>
        [Test]
        public void ContextualEmptyStaleAndUnavailableBoardsRejectFree()
        {
            Fixture empty = CreateFixture(0, false);
            empty.Runtime.BoardState.TryRemove(empty.ToolEntityId);
            empty.Presenter.InteractionDisplay.SampleInputAsync(key => key == KeyCode.F, -85);
            Assert.That(empty.Presenter.InteractionDisplay.IsOpen, Is.False);
            Assert.That(empty.Presenter.Coordinator.ResolutionCount, Is.Zero);
            empty.Dispose();
            Fixture fixture = CreateFixture(0, false);
            var picker = fixture.Presenter.InteractionDisplay;
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Modal, true);
            picker.SampleInputAsync(key => key == KeyCode.F, -80);
            Assert.That(picker.IsOpen, Is.False);
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Modal, false);
            picker.SampleInputAsync(key => key == KeyCode.F, -79);
            picker.SampleInputAsync(key => key == KeyCode.E, -78);
            fixture.Runtime.BoardState.TryRemove(fixture.ToolEntityId);
            Assert.That(picker.SampleInputAsync(key => key == KeyCode.Space, -77).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
            Assert.That(fixture.Runtime.BoardState.GetActiveNoises(), Is.Empty);
            picker.Dispose();
            picker.SampleInputAsync(key => key == KeyCode.F, -76);
            Assert.That(picker.IsOpen, Is.False);
        }

        private Fixture CreateFixture(int occupied, bool animations, RunState restored = null, bool secondTool = false)
        {
            var fixture = new Fixture();
            fixtures.Add(fixture);
            fixture.SaveRoot = Path.Combine(Path.GetTempPath(), "HB-M56-save-" + Guid.NewGuid().ToString("N"));
            fixture.Store = new FileSystemSaveStore(fixture.SaveRoot);
            fixture.Run = restored ?? new RunState("tool-pickup-presentation", 56, new RunStateConfiguration(100, 20, 0, "forest.start"));
            for (int slot = 0; slot < occupied; slot++)
                fixture.Run.EquipTool(slot, fixture.Run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 2 - slot)));
            ToolInstanceState tool = fixture.Run.CreateToolInstance(ToolContentCatalog.Load()["shovel.basic"]);
            var request = new BoardRequest(fixture.Run.RunId, fixture.Run.RunSeed, fixture.Run.WorldNodeId, fixture.Run.CurrentDay,
                fixture.Run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 3; horizontal++)
                layout.Add(View(fixture, LegacyBoardContentKind.Floor, horizontal, "Assets/Prefabs/Tiles/Floor1.prefab"));
            layout.Add(View(fixture, LegacyBoardContentKind.Player, 0, "Assets/Prefabs/Player.prefab"));
            layout.Add(View(fixture, LegacyBoardContentKind.Exit, 2, "Assets/Prefabs/Tiles/Exit.prefab"));
            LegacyBoardView generic = View(fixture, LegacyBoardContentKind.Tool, 1, "Assets/Prefabs/Tiles/Food.prefab");
            fixture.ToolView = generic.View;
            layout.Add(new LegacyBoardView(generic.View, LegacyBoardContentKind.Tool, generic.Position, tool: tool, sourceId: "source.shovel.camp"));
            if (secondTool)
            {
                LegacyBoardView additional = View(fixture, LegacyBoardContentKind.Tool, 0, "Assets/Prefabs/Tiles/Food.prefab");
                layout.Add(new LegacyBoardView(additional.View, LegacyBoardContentKind.Tool, additional.Position,
                    tool: fixture.Run.CreateToolInstance(ToolContentCatalog.Load()["shovel.basic"]), sourceId: "source.shovel.second"));
            }
            fixture.Runtime = LegacyBoardRuntimeComposer.Compose(request, fixture.Run, new GridBounds(0, 0, 2, 0), layout);
            fixture.ToolEntityId = fixture.Runtime.BoardState.GetEntities().Single(entity => entity.Definition.ContentId == "shovel.basic" && entity.Position.X == 1).Id;
            fixture.Hud = new ToolHudPresenter(fixture.Run, null, new Vector2(960, 540));
            fixture.Presenter = new BoardEventPresenter(fixture.Runtime, fixture, fixture, fixture,
                diagnostic => Assert.Fail(diagnostic.Code + ": " + diagnostic.Error), 0f) { AnimationsEnabled = animations };
            fixture.Presenter.SynchronizeFromState();
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
            return fixture;
        }

        private static LegacyBoardView View(Fixture fixture, LegacyBoardContentKind kind, int horizontal, string path)
        {
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
            var owner = new GameObject(kind.ToString());
            fixture.Objects.Add(owner);
            owner.transform.position = new Vector3(horizontal, 0, 0);
            SpriteRenderer renderer = owner.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = kind == LegacyBoardContentKind.Floor ? "Floor" : "Units";
            return new LegacyBoardView(owner, kind, new GridPosition(horizontal, 0));
        }

        private sealed class Fixture : IDisposable, IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public readonly List<GameObject> Objects = new List<GameObject>();
            public RunState Run;
            public BoardRuntime Runtime;
            public BoardEventPresenter Presenter;
            public ToolHudPresenter Hud;
            public GameObject ToolView;
            public EntityId ToolEntityId;
            public FileSystemSaveStore Store;
            public string SaveRoot;
            private bool disposed;
            public GroundToolState Ground => Runtime.BoardState.TryGetGroundTool(ToolEntityId, out GroundToolState ground) ? ground : null;
            public void Refresh(int health, int food) => Hud.Refresh();
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) { }
            public bool TryNotify(BoardRequest request, GameEvent outcome) => true;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Presenter?.Dispose();
                Hud?.Dispose();
                Runtime?.Dispose();
                foreach (GameObject owner in Objects) if (owner != null) Object.DestroyImmediate(owner);
                if (SaveRoot != null && Directory.Exists(SaveRoot)) Directory.Delete(SaveRoot, true);
            }
        }
    }
}
#endif