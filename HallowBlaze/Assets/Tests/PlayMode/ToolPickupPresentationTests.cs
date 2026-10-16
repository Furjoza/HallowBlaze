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

        private Fixture CreateFixture(int occupied, bool animations, RunState restored = null)
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
            fixture.Runtime = LegacyBoardRuntimeComposer.Compose(request, fixture.Run, new GridBounds(0, 0, 2, 0), layout);
            fixture.ToolEntityId = fixture.Runtime.BoardState.GetEntities().Single(entity => entity.Definition.ContentId == "shovel.basic").Id;
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