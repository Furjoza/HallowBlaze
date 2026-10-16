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
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;
using Object = UnityEngine.Object;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Exercises production digging, ordered observation and remaining-work/lifetime/charge UI with real sprite assets.</summary>
    public sealed class ShovelPresentationTests
    {
        private readonly List<Fixture> fixtures = new List<Fixture>();

        /// <summary>Releases fixture-owned presentation, borrowed views and isolated temporary save directories.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (Fixture fixture in fixtures) fixture.Dispose();
            fixtures.Clear();
        }

        /// <summary>Manual and shovel choices both use the production picker; preview/cancel is free and confirmed rewards precede cost.</summary>
        [UnityTest]
        public IEnumerator CacheAlternativesPresentProgressRewardAndChargesWithoutKnowledgeUnlock()
        {
            foreach (bool shovel in new[] { false, true })
            foreach (bool animations in new[] { false, true })
            {
                Fixture fixture = CreateFixture(true, shovel, false, animations);
                var picker = fixture.Presenter.InteractionDisplay;
                Assert.That(fixture.Labels, Does.Contain("CACHE 0/3"));
                Assert.That(picker.Open(), Is.True);
                Assert.That(picker.Options.Single().ActionId, Is.EqualTo("uncover"));
                Assert.That(picker.GetOptionLabel(0), Does.Contain("3 actions left").And.Contain("Food -1").And.Contain("Charge 0"));
                picker.Cancel();
                Assert.That(fixture.Run.Food, Is.EqualTo(20));
                Assert.That(fixture.Runtime.BoardState.GetCacheWork(fixture.CacheId), Is.Zero);
                yield return null;
                int steps = shovel ? 1 : 3;
                for (int step = 0; step < steps; step++)
                {
                    Assert.That(picker.Open(), Is.True);
                    int selected = shovel ? picker.Options.Count + picker.ToolOptions.ToList().FindIndex(option => option.ActionId == "unearth") : 0;
                    if (shovel) Assert.That(picker.GetOptionLabel(selected), Does.Contain("1 action").And.Contain("Charge -1").And.Contain("Noise 2 / 2"));
                    Assert.That(picker.Select(selected), Is.True);
                    CommandSubmission submission = picker.ConfirmAsync().Result;
                    Assert.That(submission.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                    Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
                    if (!shovel && step < 2) Assert.That(fixture.Labels, Does.Contain("CACHE " + (step + 1) + "/3"));
                    if (step == steps - 1)
                    {
                        CacheRewardItem item = submission.Result.Events.OfType<CacheWorkedEvent>().Single().Reward;
                        Assert.That(item.DefinitionId, Is.EqualTo("shovel.cache-ration"));
                        Assert.That(item.SourceEntityId, Is.EqualTo(fixture.CacheId));
                    }
                    yield return null;
                }
                Assert.That(fixture.CacheView.activeSelf, Is.False);
                Assert.That(fixture.Run.Food, Is.EqualTo(shovel ? 29 : 27));
                fixture.Hud.Refresh();
                Assert.That(fixture.Hud.GetSlotText(1), Is.EqualTo(shovel ? "Shovel  2" : "Empty"));
                Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
                Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Is.Empty);
                fixture.Dispose();
                yield return null;
            }
        }

        /// <summary>The actual entry is visible before knowledge persists; full lifetime is shown, accepted commands count down and reset preserves facts.</summary>
        [UnityTest]
        public IEnumerator PitShowsLifetimeAndHeldShamblerBeforePersistingObservedFact()
        {
            foreach (bool animations in new[] { false, true })
            {
                Fixture fixture = CreateFixture(false, true, true, animations);
                var picker = fixture.Presenter.InteractionDisplay;
                Assert.That(picker.Open(), Is.True);
                int selected = picker.Options.Count + picker.ToolOptions.ToList().FindIndex(option => option.IsAvailable && option.ActionId == "dig-pit");
                Assert.That(picker.GetOptionLabel(selected), Does.Contain("4 turns").And.Contain("Shambler waits until expiry").And.Contain("Charge -1"));
                Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
                Assert.That(picker.Select(selected), Is.True);
                UseToolCommand command = picker.ToolOptions[selected - picker.Options.Count].CreateCommand();
                Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(fixture.Labels, Does.Contain("PIT T4\nWAIT"));
                Assert.That(fixture.Profile.DiscoveredFactIds, Is.EqualTo(new[] { ShamblerTrappedEvent.FactId }));
                Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Is.EqualTo(new[] { ShamblerTrappedEvent.FactId }));
                Assert.That(fixture.Runtime.Enemies.Single().LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
                Assert.That(fixture.Store.SaveRun(fixture.Run).IsSuccess, Is.True);
                RunState restored = fixture.Store.LoadRun().Data;
                Assert.That(restored.ToolSlots[1].Instance.InstanceId, Is.EqualTo(command.InstanceId));
                Assert.That(restored.ToolSlots[1].Instance.Charges, Is.EqualTo(2));
                yield return null;
                Assert.That(fixture.Presenter.Coordinator.SubmitAsync(command).Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
                Assert.That(fixture.Labels, Does.Contain("PIT T4\nWAIT"));
                for (int remaining = 3; remaining >= 0; remaining--)
                {
                    yield return null;
                    CommandSubmission submission = fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result;
                    Assert.That(submission.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                    Assert.That(submission.Result.Events.OfType<EnemyAttackResolvedEvent>(), Is.Empty);
                    Assert.That(fixture.Labels, remaining > 0 ? Does.Contain("PIT T" + remaining + "\nWAIT") : Does.Not.Contain("PIT T1\nWAIT"));
                }
                Assert.That(fixture.Runtime.BoardState.GetPits(), Is.Empty);
                Assert.That(fixture.Run.Health, Is.EqualTo(100));
                restored.Reset("after-pit", 55, new RunStateConfiguration(100, 20, 0, "forest.start"));
                Assert.That(restored.ToolSlots.All(slot => slot.IsEmpty), Is.True);
                Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Does.Contain(ShamblerTrappedEvent.FactId));
                fixture.Presenter.Dispose();
                Assert.That(fixture.Presenter.DiggingDisplay.IsDisposed, Is.True);
                fixture.Dispose();
                yield return null;
            }
        }

        /// <summary>Preview, cancellation and merely resolving an unpresented trap cannot write the profile fact.</summary>
        [Test]
        public void UnpresentedTrapDoesNotDiscoverKnowledge()
        {
            Fixture fixture = CreateFixture(false, true, true, false);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            UseToolCommand command = picker.ToolOptions.Single(option => option.IsAvailable).CreateCommand();
            picker.Cancel();
            Assert.That(fixture.Runtime.Controller.Resolve(command).Events.OfType<ShamblerTrappedEvent>().Count(), Is.EqualTo(1));
            Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
            Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Is.Empty);
        }

        /// <summary>Actual sprite and overlay pixels show manual progress and an identifiable pit/hold with current charge values.</summary>
        [TestCase(true)]
        [TestCase(false)]
        public void DiggingAndPitRenderVisibleReadableEvidence(bool cache)
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            Fixture fixture = CreateFixture(cache, !cache, !cache, false);
            PlayerCommand command = cache ? (PlayerCommand)fixture.Runtime.Interactions.GetOptions(fixture.Runtime.BoardState, fixture.Run,
                fixture.Runtime.PlayerId).Single().CreateCommand() : fixture.Runtime.Tools.GetOptions(fixture.Runtime.BoardState, fixture.Run,
                fixture.Runtime.PlayerId).Single(option => option.IsAvailable).CreateCommand();
            Assert.That(fixture.Presenter.Coordinator.SubmitAsync(command).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(fixture.Labels, Does.Contain(cache ? "CACHE 1/3" : "PIT T4\nWAIT"));
            fixture.Hud.Refresh();
            Assert.That(fixture.Hud.GetSlotText(1), Is.EqualTo(cache ? "Empty" : "Shovel  2"));
            var cameraOwner = new GameObject("Shovel evidence camera");
            fixture.Objects.Add(cameraOwner);
            Camera camera = cameraOwner.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 2.7f;
            camera.transform.position = new Vector3(2, 1, -20);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.06f);
            var target = new RenderTexture(960, 540, 24);
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                Canvas canvas = fixture.Hud.Root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                pixels.Apply();
                Color32[] colors = pixels.GetPixels32();
                Assert.That(colors.Count(color => color.r > 180 && color.g > 180 && color.b > 180), Is.GreaterThan(150));
                if (!cache) Assert.That(colors.Count(color => color.r < 80 && color.g > 180 && color.b > 100), Is.GreaterThan(30));
                string image = Path.Combine(Path.GetTempPath(), cache ? "HB-M55-cache.png" : "HB-M55-pit.png");
                File.WriteAllBytes(image, pixels.EncodeToPNG());
                TestContext.WriteLine("Shovel evidence: " + image);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
            }
        }

        private Fixture CreateFixture(bool cache, bool shovel, bool enemy, bool animations)
        {
            var fixture = new Fixture();
            fixtures.Add(fixture);
            fixture.SaveRoot = Path.Combine(Path.GetTempPath(), "HB-M55-save-" + Guid.NewGuid().ToString("N"));
            fixture.Store = new FileSystemSaveStore(fixture.SaveRoot);
            fixture.Profile = new ProfileState("shovel-profile", "forest", 1);
            Assert.That(fixture.Store.SaveProfile(fixture.Profile).IsSuccess, Is.True);
            fixture.Run = new RunState("shovel-presentation", 55, new RunStateConfiguration(100, 20, 0, "forest.start"));
            if (shovel) fixture.Run.EquipTool(1, fixture.Run.CreateToolInstance(ToolContentCatalog.Load()[ShovelRules.DefinitionId]));
            var request = new BoardRequest(fixture.Run.RunId, fixture.Run.RunSeed, fixture.Run.WorldNodeId, fixture.Run.CurrentDay,
                fixture.Run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 5; horizontal++)
            for (int vertical = 0; vertical < 3; vertical++)
                layout.Add(View(fixture, horizontal == 1 && vertical == 1 ? LegacyBoardContentKind.DiggableGround : LegacyBoardContentKind.Floor,
                    horizontal, vertical, "Assets/Prefabs/Tiles/Floor1.prefab"));
            layout.Add(View(fixture, LegacyBoardContentKind.Player, 0, 1, "Assets/Prefabs/Player.prefab"));
            layout.Add(View(fixture, LegacyBoardContentKind.Exit, 4, 2, "Assets/Prefabs/Tiles/Exit.prefab"));
            if (enemy) layout.Add(View(fixture, LegacyBoardContentKind.Enemy, 2, 1, "Assets/Prefabs/Enemy1.prefab"));
            if (cache)
            {
                LegacyBoardView marker = View(fixture, LegacyBoardContentKind.BuriedFood, 1, 1, "Assets/Prefabs/Tiles/Carrot.prefab");
                fixture.CacheView = marker.View;
                layout.Add(marker);
            }
            fixture.Runtime = LegacyBoardRuntimeComposer.Compose(request, fixture.Run, new GridBounds(0, 0, 4, 2), layout);
            if (cache) fixture.CacheId = fixture.Runtime.BoardState.GetEntities().Single(entity => entity.Definition.ContentId == "legacy.buried-food").Id;
            fixture.Presenter = new BoardEventPresenter(fixture.Runtime, fixture, fixture, fixture,
                diagnostic => Assert.Fail(diagnostic.Code + ": " + diagnostic.Error), 0f, observeFact: fact =>
                {
                    Assert.That(fixture.LastEvent, Is.EqualTo("ShamblerTrapped"));
                    Assert.That(fixture.Labels, Does.Contain("PIT T4\nWAIT"));
                    fixture.Profile.DiscoverFact(fact);
                    Assert.That(fixture.Store.SaveProfile(fixture.Profile).IsSuccess, Is.True);
                }) { AnimationsEnabled = animations };
            fixture.Presenter.SynchronizeFromState();
            fixture.Hud = new ToolHudPresenter(fixture.Run, null, new Vector2(960, 540));
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
            return fixture;
        }

        private static LegacyBoardView View(Fixture fixture, LegacyBoardContentKind kind, int horizontal, int vertical, string path)
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
            var instance = new GameObject(kind.ToString());
            fixture.Objects.Add(instance);
            instance.transform.position = new Vector3(horizontal, vertical, 0);
            SpriteRenderer renderer = instance.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = kind == LegacyBoardContentKind.Floor || kind == LegacyBoardContentKind.DiggableGround ? "Floor" : "Units";
            return new LegacyBoardView(instance, kind, new GridPosition(horizontal, vertical),
                kind == LegacyBoardContentKind.Enemy ? (int?)10 : null);
        }

        private sealed class Fixture : IDisposable, IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public readonly List<GameObject> Objects = new List<GameObject>();
            public RunState Run;
            public BoardRuntime Runtime;
            public BoardEventPresenter Presenter;
            public ToolHudPresenter Hud;
            public EntityId CacheId;
            public GameObject CacheView;
            public ProfileState Profile;
            public FileSystemSaveStore Store;
            public string SaveRoot;
            public string LastEvent;
            private bool disposed;
            public string[] Labels => Presenter.DiggingDisplay.Root.GetComponentsInChildren<TextMesh>().Select(text => text.text).ToArray();
            public void Refresh(int health, int food) { }
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) { LastEvent = gameEvent.EventType; }
            public bool TryNotify(BoardRequest request, GameEvent outcome) => true;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Presenter?.Dispose();
                Hud?.Dispose();
                Runtime?.Dispose();
                foreach (GameObject instance in Objects) if (instance != null) Object.DestroyImmediate(instance);
                if (SaveRoot != null && Directory.Exists(SaveRoot)) Directory.Delete(SaveRoot, true);
            }
        }
    }
}
#endif