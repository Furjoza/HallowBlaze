#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Persistence;
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
    /// <summary>Runs production clearing actions with real sprite assets, ordered replay, Listener hearing and isolated knowledge saves.</summary>
    public sealed class AxePresentationTests
    {
        private readonly List<Fixture> fixtures = new List<Fixture>();

        /// <summary>Releases only fixture-owned displays, views and isolated persistence directories.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (Fixture fixture in fixtures) fixture.Dispose();
            fixtures.Clear();
        }

        /// <summary>Both legal routes work through the picker, with equivalent animation modes and no legacy wall-HP mutation.</summary>
        [UnityTest]
        public IEnumerator ManualAndAxeUseProductionPickerWithVisibleCostsAndIndependentDomainProgress()
        {
            foreach (bool axe in new[] { false, true })
            foreach (bool animations in new[] { false, true })
            {
                Fixture fixture = CreateFixture(axe, animations);
                var picker = fixture.Presenter.InteractionDisplay;
                Assert.That(picker.Open(), Is.True);
                Assert.That(picker.Options.Single().EffectLabel, Is.EqualTo("Clear by hand: 3 actions left"));
                Assert.That(picker.GetOptionLabel(0), Does.Contain("Food -1").And.Contain("Charge 0").And.Contain("Noise 1 / 2"));
                Assert.That(picker.ToolOptions.Count, Is.EqualTo(axe ? 1 : 0));
                int selected = axe ? picker.Options.Count : 0;
                if (axe) Assert.That(picker.GetOptionLabel(selected), Does.Contain("Chop: clear obstacle").And.Contain("Charge -1")
                    .And.Contain("Noise 6 / 2"));
                Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
                Assert.That(picker.Select(selected), Is.True);
                Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
                Assert.That(fixture.Run.Food, Is.EqualTo(19));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
                Assert.That(fixture.WallHp, Is.EqualTo(4));
                if (!axe)
                {
                    Assert.That(fixture.WallView.activeSelf, Is.True);
                    Assert.That(fixture.Presenter.ObstacleDisplay.Root.GetComponentsInChildren<TextMesh>().Single().text, Is.EqualTo("1/3"));
                    Assert.That(fixture.Presenter.NoiseDisplay.Projections.Single().Strength, Is.EqualTo(1));
                    Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
                    for (int action = 1; action < 3; action++)
                    {
                        yield return null;
                        Assert.That(picker.Open(), Is.True);
                        Assert.That(picker.Select(0), Is.True);
                        Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                    }
                }
                Assert.That(fixture.WallView.activeSelf, Is.False);
                Assert.That(fixture.Runtime.BoardState.TryGetEntity(fixture.WallId, out _), Is.False);
                Assert.That(fixture.Run.Food, Is.EqualTo(axe ? 19 : 17));
                Assert.That(fixture.WallHp, Is.EqualTo(4));
                Assert.That(fixture.Profile.DiscoveredFactIds, axe ? Is.EqualTo(new[] { ObstacleClearingRules.AxeNoiseFactId }) : Is.Empty);
                if (axe) Assert.That(fixture.Run.ToolSlots[1].Instance.Charges, Is.EqualTo(2));
                yield return null;
                Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East)).Result.Status,
                    Is.EqualTo(CommandSubmissionStatus.Presented));
                fixture.Dispose();
            }
        }

        /// <summary>The visible emitted snapshot is the Listener's hearing memory and only then documents the durable fact.</summary>
        [UnityTest]
        public IEnumerator AxeNoisePresentationDocumentsFactAndNextListenerStepUsesThatSignal()
        {
            Fixture fixture = CreateFixture(true, false);
            Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            Assert.That(picker.Select(picker.Options.Count), Is.True);
            UseToolCommand selection = picker.ToolOptions.Single().CreateCommand();
            picker.Cancel();
            Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
            yield return null;
            CommandSubmission submission = fixture.Presenter.Coordinator.SubmitAsync(selection).Result;
            Assert.That(submission.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            NoiseEvent emission = submission.Result.Events.OfType<NoiseEvent>().Single();
            Assert.That(fixture.Presenter.NoiseDisplay.Projections.Single(), Is.SameAs(emission.Noise));
            Assert.That(fixture.Runtime.BoardState.TryGetListenerNoise(fixture.ListenerId, out BoardNoise heard), Is.True);
            Assert.That(heard, Is.SameAs(emission.Noise));
            Assert.That(fixture.Runtime.Listeners.Single().LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Investigate));
            Assert.That(fixture.Runtime.BoardState.TryGetEntity(fixture.ListenerId, out BoardEntityState listener), Is.True);
            Assert.That(listener.Position, Is.EqualTo(new GridPosition(4, 1)));
            Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Is.EqualTo(new[] { ObstacleClearingRules.AxeNoiseFactId }));
            Assert.That(fixture.Store.SaveRun(fixture.Run).IsSuccess, Is.True);
            RunState restored = fixture.Store.LoadRun().Data;
            Assert.That(restored.ToolSlots[1].Instance.InstanceId, Is.EqualTo(selection.InstanceId));
            Assert.That(restored.ToolSlots[1].Instance.Charges, Is.EqualTo(2));
            Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status,
                Is.EqualTo(CommandSubmissionStatus.Presented));
            fixture.Runtime.BoardState.TryGetEntity(fixture.ListenerId, out listener);
            Assert.That(listener.Position, Is.EqualTo(new GridPosition(3, 1)));
            restored.Reset("new-run", 55, new RunStateConfiguration(100, 20, 0, "forest.start"));
            Assert.That(restored.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Does.Contain(ObstacleClearingRules.AxeNoiseFactId));
        }

        /// <summary>Missing/displaced targets and depleted tools cannot add progress, noise, facts or resource cost.</summary>
        [TestCase("removed")]
        [TestCase("displaced")]
        [TestCase("depleted")]
        public void RejectedAxeCannotDocumentKnowledgeOrMutateObstacleProgress(string invalid)
        {
            Fixture fixture = CreateFixture(true, false);
            ToolUseOption option = fixture.Runtime.Tools.GetOptions(fixture.Runtime.BoardState, fixture.Run, fixture.Runtime.PlayerId).Single();
            UseToolCommand command = option.CreateCommand();
            if (invalid == "removed") fixture.Runtime.BoardState.TryRemove(fixture.WallId);
            if (invalid == "displaced") fixture.Runtime.BoardState.TryMove(fixture.WallId, new GridPosition(1, 2));
            if (invalid == "depleted")
            {
                for (int charge = 0; charge < 3; charge++) fixture.Run.TrySpendToolCharge(1, option.InstanceId);
                command = new UseToolCommand(1, option.InstanceId, 0, option.TargetId, option.TargetPosition, option.ActionId);
            }
            Assert.That(fixture.Presenter.Coordinator.SubmitAsync(command).Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(fixture.Runtime.BoardState.GetObstacleWork(fixture.WallId), Is.Zero);
            Assert.That(fixture.Runtime.BoardState.GetActiveNoises(), Is.Empty);
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
            Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
            Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Is.Empty);
        }

        /// <summary>The existing wall clip is presented after resolution, without legacy damage or a second resource mutation.</summary>
        [Test]
        public void WallAudioAdapterUsesAuthoredChopClipWithoutApplyingLegacyDamage()
        {
            Fixture fixture = CreateFixture(true, false);
            Type soundType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("SoundManager"))
                .First(type => type != null);
            var singleton = soundType.GetField("instance");
            object previous = singleton.GetValue(null);
            var soundOwner = new GameObject("Isolated clearing sound");
            soundOwner.SetActive(false);
            fixture.Objects.Add(soundOwner);
            AudioSource source = soundOwner.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.mute = true;
            Behaviour sound = (Behaviour)soundOwner.AddComponent(soundType);
            sound.enabled = false;
            soundType.GetField("efxSource").SetValue(sound, source);
            singleton.SetValue(null, sound);
            try
            {
                soundOwner.SetActive(true);
                UseToolCommand command = fixture.Runtime.Tools.GetOptions(fixture.Runtime.BoardState, fixture.Run,
                    fixture.Runtime.PlayerId).Single().CreateCommand();
                Assert.That(fixture.Presenter.Coordinator.SubmitAsync(command).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                MonoBehaviour wall = fixture.WallView.GetComponents<MonoBehaviour>().Single(component => component.GetType().Name == "Wall");
                Assert.That(source.clip, Is.Not.Null);
                Assert.That(source.clip, Is.SameAs(wall.GetType().GetField("chopSound1").GetValue(wall)));
                Assert.That(fixture.WallHp, Is.EqualTo(4));
                Assert.That(fixture.Run.ToolSlots[1].Instance.Charges, Is.EqualTo(2));
                Assert.That(fixture.Run.Food, Is.EqualTo(19));
            }
            finally { singleton.SetValue(null, previous); }
        }

        /// <summary>Real sprite captures prove visible manual progress and the larger axe footprint without relying on sound or animation.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ClearingNoiseAndProgressRenderReadableProductionEvidence(bool axe)
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            Fixture fixture = CreateFixture(axe, false);
            PlayerCommand command = axe ? (PlayerCommand)fixture.Runtime.Tools.GetOptions(fixture.Runtime.BoardState, fixture.Run,
                fixture.Runtime.PlayerId).Single().CreateCommand() : fixture.Runtime.Interactions.GetOptions(fixture.Runtime.BoardState,
                fixture.Run, fixture.Runtime.PlayerId).Single().CreateCommand();
            Assert.That(fixture.Presenter.Coordinator.SubmitAsync(command).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            fixture.Hud.Refresh();
            Assert.That(fixture.Hud.GetSlotText(1), Is.EqualTo(axe ? "Axe  2" : "Empty"));
            Assert.That(fixture.Presenter.NoiseDisplay.GetComponentInChildren<TextMesh>().text, Is.EqualTo(axe ? "S6 T2" : "S1 T2"));
            var cameraOwner = new GameObject("Axe evidence camera");
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
                Assert.That(pixels.GetPixels32().Count(color => color.r > 180 && color.g > 180 && color.b > 180), Is.GreaterThan(150));
                Assert.That(fixture.Presenter.NoiseDisplay.GetComponentsInChildren<LineRenderer>().Length,
                    Is.EqualTo(fixture.Runtime.BoardState.GetActiveNoises().Single().AudibleCells.Count));
                string image = Path.Combine(Path.GetTempPath(), axe ? "HB-M54-axe.png" : "HB-M54-manual.png");
                File.WriteAllBytes(image, pixels.EncodeToPNG());
                TestContext.WriteLine("Axe evidence: " + image);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
            }
        }

        private Fixture CreateFixture(bool axe, bool animations)
        {
            var fixture = new Fixture();
            fixtures.Add(fixture);
            fixture.SaveRoot = Path.Combine(Path.GetTempPath(), "HB-M54-save-" + Guid.NewGuid().ToString("N"));
            fixture.Store = new FileSystemSaveStore(fixture.SaveRoot);
            fixture.Profile = new ProfileState("axe-profile", "forest", 1);
            Assert.That(fixture.Store.SaveProfile(fixture.Profile).IsSuccess, Is.True);
            fixture.Run = new RunState("axe-presentation", 54, new RunStateConfiguration(100, 20, 0, "forest.start"));
            if (axe) fixture.Run.EquipTool(1, fixture.Run.CreateToolInstance(ToolContentCatalog.Load()["axe.basic"]));
            var request = new BoardRequest(fixture.Run.RunId, fixture.Run.RunSeed, fixture.Run.WorldNodeId, fixture.Run.CurrentDay,
                fixture.Run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 5; horizontal++)
            for (int vertical = 0; vertical < 3; vertical++)
                layout.Add(View(fixture, LegacyBoardContentKind.Floor, horizontal, vertical, "Assets/Prefabs/Tiles/Floor1.prefab"));
            layout.Add(View(fixture, LegacyBoardContentKind.Player, 0, 1, "Assets/Prefabs/Player.prefab"));
            layout.Add(View(fixture, LegacyBoardContentKind.Listener, 4, 1, "Assets/Prefabs/Enemy1.prefab"));
            layout.Add(View(fixture, LegacyBoardContentKind.Exit, 4, 2, "Assets/Prefabs/Tiles/Exit.prefab"));
            GameObject wall = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/Wall1.prefab"));
            wall.transform.position = new Vector3(1, 1, 0);
            foreach (Collider2D collider in wall.GetComponents<Collider2D>()) collider.enabled = false;
            fixture.Objects.Add(wall);
            fixture.WallView = wall;
            layout.Add(new LegacyBoardView(wall, LegacyBoardContentKind.Wall, new GridPosition(1, 1)));
            fixture.Runtime = LegacyBoardRuntimeComposer.Compose(request, fixture.Run, new GridBounds(0, 0, 4, 2), layout);
            fixture.WallId = fixture.Runtime.BoardState.GetEntities().Single(entity => entity.Definition.ContentId == "legacy.wall").Id;
            fixture.ListenerId = fixture.Runtime.Listeners.Single().ActorId;
            fixture.Presenter = new BoardEventPresenter(fixture.Runtime, fixture, fixture, fixture,
                diagnostic => Assert.Fail(diagnostic.Code + ": " + diagnostic.Error), 0f, observeFact: fact =>
                {
                    Assert.That(fixture.LastEvent, Is.EqualTo("Noise"));
                    Assert.That(fixture.Presenter.NoiseDisplay.Projections, Is.Not.Empty);
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
            renderer.sortingLayerName = kind == LegacyBoardContentKind.Floor ? "Floor" : "Units";
            return new LegacyBoardView(instance, kind, new GridPosition(horizontal, vertical));
        }

        private sealed class Fixture : IDisposable, IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public readonly List<GameObject> Objects = new List<GameObject>();
            public RunState Run;
            public BoardRuntime Runtime;
            public BoardEventPresenter Presenter;
            public ToolHudPresenter Hud;
            public GameObject WallView;
            public EntityId WallId;
            public EntityId ListenerId;
            public ProfileState Profile;
            public FileSystemSaveStore Store;
            public string SaveRoot;
            public string LastEvent;
            private bool disposed;
            public int WallHp => (int)WallView.GetComponents<MonoBehaviour>().Single(component => component.GetType().Name == "Wall")
                .GetType().GetField("hp").GetValue(WallView.GetComponents<MonoBehaviour>().Single(component => component.GetType().Name == "Wall"));
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