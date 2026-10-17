#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Verifies actual presented observations, durable profile UI, independent input ownership and spoiler-free rendered notes.</summary>
    public sealed class FieldNotesPresentationTests
    {
        private readonly List<Fixture> fixtures = new List<Fixture>();

        /// <summary>Disposes fixture-owned managers, views and isolated save directories without touching the player's profile.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (Fixture fixture in fixtures) fixture.Dispose();
            fixtures.Clear();
        }

        /// <summary>Only actual axe noise and the later executed Listener investigation discover facts, once, with and without interpolation.</summary>
        [UnityTest]
        public IEnumerator PresentedAxeAndListenerDiscoverOnceAndNotesCannotReleaseAnotherModal()
        {
            foreach (bool animations in new[] { false, true })
            {
                Fixture fixture = CreateFixture(false, animations);
                yield return null;
                var notes = fixture.Notes;
                Assert.That(notes.Open(), Is.True);
                Assert.That(notes.Entries, Is.Empty);
                Assert.That(notes.Select("AXE_CREATES_LOUD_NOISE"), Is.False);
                Assert.That(AllText(notes), Does.Not.Contain("axe").And.Not.Contain("Shambler").And.Not.Contain("Listener"));
                notes.Close();
                yield return null;
                var picker = fixture.Presenter.InteractionDisplay;
                Assert.That(picker.Open(), Is.True);
                Assert.That(notes.Open(), Is.False);
                UseToolCommand command = picker.ToolOptions.Single().CreateCommand();
                picker.Cancel();
                Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
                yield return null;
                var operation = fixture.Presenter.Coordinator.SubmitAsync(command);
                while (!operation.IsCompleted) yield return null;
                Assert.That(operation.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(fixture.Profile.DiscoveredFactIds, Is.EqualTo(new[] { "AXE_CREATES_LOUD_NOISE" }));
                Assert.That(notes.FeedbackTitle, Is.EqualTo("A loud shortcut"));
                Assert.That(notes.DiscoveryFeedbackCount, Is.EqualTo(1));
                operation = fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand());
                while (!operation.IsCompleted) yield return null;
                Assert.That(operation.Result.Result.Events.OfType<ListenerInvestigatedEvent>().Count(), Is.EqualTo(1));
                Assert.That(fixture.Profile.DiscoveredFactIds, Is.EqualTo(new[] { "AXE_CREATES_LOUD_NOISE", "LISTENER_FOLLOWS_NOISE" }));
                Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Is.EqualTo(fixture.Profile.DiscoveredFactIds));
                Assert.That(notes.DiscoveryFeedbackCount, Is.EqualTo(2));
                Assert.That(notes.Open(), Is.True);
                Assert.That(notes.Select("LISTENER_FOLLOWS_NOISE"), Is.True);
                Assert.That(notes.SelectedText, Does.Contain("announced step stays fixed"));
                Assert.That(AllText(notes), Does.Not.Contain("Held by the ground").And.Not.Contain("PIT_TRAPS_SHAMBLER"));
                int resolutions = fixture.Presenter.Coordinator.ResolutionCount;
                int food = fixture.Session.ActiveRun.Food;
                Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(resolutions));
                Assert.That(fixture.Session.ActiveRun.Food, Is.EqualTo(food));
                Invoke(fixture.Manager, "SetGameplayInputBlocked", true);
                notes.Close();
                Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                yield return null;
                Assert.That(fixture.Presenter.Coordinator.Gate.Blocks & PresentationInputBlock.Modal, Is.EqualTo(PresentationInputBlock.Modal));
                Assert.That(fixture.Presenter.Coordinator.Gate.Blocks & PresentationInputBlock.FieldNotes, Is.EqualTo(PresentationInputBlock.None));
                Invoke(fixture.Manager, "SetGameplayInputBlocked", false);
                yield return null;
                notes.Acknowledge();
                Assert.That(notes.FeedbackTitle, Is.EqualTo("Following the sound"));
                notes.Acknowledge();
                Assert.That(notes.FeedbackTitle, Is.Empty);
                operation = fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand());
                while (!operation.IsCompleted) yield return null;
                Assert.That(notes.DiscoveryFeedbackCount, Is.EqualTo(2));
                fixture.Dispose();
                yield return null;
            }
        }

        /// <summary>Actual Shambler entry persists once; death and a new run retain notes while explicit New Game replaces the profile and UI lifetime.</summary>
        [UnityTest]
        public IEnumerator TrapDiscoveryReopensAfterDeathAndNewRunButNewGameResetsProfile()
        {
            Fixture fixture = CreateFixture(true, false);
            yield return null;
            UseToolCommand command = fixture.Runtime.Tools.GetOptions(fixture.Runtime.BoardState,
                fixture.Session.ActiveRun, fixture.Runtime.PlayerId).Single(option => option.IsAvailable).CreateCommand();
            CommandSubmission result = fixture.Presenter.Coordinator.SubmitAsync(command).Result;
            Assert.That(result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(result.Result.Events.OfType<ShamblerTrappedEvent>().Count(), Is.EqualTo(1));
            FieldNotesPresenter notes = fixture.Notes;
            Assert.That(notes.FeedbackTitle, Is.EqualTo("Held by the ground"));
            Assert.That(notes.DiscoveryFeedbackCount, Is.EqualTo(1));
            ProfileState profile = fixture.Profile;
            Invoke(fixture.Manager, "GameOver", false);
            Assert.That(fixture.Session.ActiveRun, Is.Null);
            Assert.That(((Behaviour)fixture.Manager).enabled, Is.False);
            Assert.That(notes.Open(), Is.True);
            Assert.That(notes.Entries.Single().FactId, Is.EqualTo("PIT_TRAPS_SHAMBLER"));
            Assert.That(notes.SelectedText, Does.Contain("causes no damage"));
            notes.Resize(new Vector2(320, 480));
            yield return null;
            Assert.That(notes.Root.transform.Find("Field Notes").GetComponent<RectTransform>().rect.width,
                Is.EqualTo(Screen.width), "The disabled manager must not own the surviving reader's resize.");
            notes.Close();
            yield return null;
            Invoke(fixture.Manager, "StartNewRun");
            yield return null;
            Assert.That(fixture.Profile, Is.SameAs(profile));
            Assert.That(fixture.Notes, Is.SameAs(notes));
            Assert.That(notes.Open(), Is.True);
            Assert.That(notes.Entries.Single().FactId, Is.EqualTo("PIT_TRAPS_SHAMBLER"));
            Assert.That(notes.DiscoveryFeedbackCount, Is.EqualTo(1));
            notes.Close();
            yield return null;
            Assert.That((bool)Invoke(fixture.Manager, "TryStartNewGame"), Is.True);
            Assert.That(notes.IsDisposed, Is.True);
            Assert.That(fixture.Profile, Is.Not.SameAs(profile));
            Assert.That(fixture.Notes.Entries, Is.Empty);
            Assert.That(fixture.Notes.DiscoveryFeedbackCount, Is.Zero);
            Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Is.Empty);
        }

        /// <summary>Domain execution without replay is not player observation, even when it creates real axe/listener or pit consequences.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UnpresentedConsequencesDoNotDiscoverFacts(bool pit)
        {
            Fixture fixture = CreateFixture(pit, false);
            UseToolCommand command = fixture.Runtime.Tools.GetOptions(fixture.Runtime.BoardState,
                fixture.Session.ActiveRun, fixture.Runtime.PlayerId).Single(option => option.IsAvailable).CreateCommand();
            var result = fixture.Runtime.Controller.Resolve(command);
            Assert.That(result, Is.TypeOf<AcceptedTurnResult>());
            if (pit) Assert.That(result.Events.OfType<ShamblerTrappedEvent>().Count(), Is.EqualTo(1));
            else
            {
                Assert.That(result.Events.OfType<NoiseEvent>().Count(), Is.EqualTo(1));
                Assert.That(fixture.Runtime.Controller.Resolve(new WaitCommand()).Events.OfType<ListenerInvestigatedEvent>().Count(), Is.EqualTo(1));
            }
            Assert.That(fixture.Profile.DiscoveredFactIds, Is.Empty);
            Assert.That(fixture.Store.LoadProfile().Data.DiscoveredFactIds, Is.Empty);
            Assert.That(fixture.Notes.DiscoveryFeedbackCount, Is.Zero);
        }

        /// <summary>Known text and distinct bitmap icons render at desktop and minimum portrait sizes, including a safe missing-definition placeholder.</summary>
        [TestCase(1280, 720)]
        [TestCase(320, 480)]
        public void NotesRenderKnownContentWithoutClippingOrUnknownChecklist(int width, int height)
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            var owner = new GameObject("Field Notes rendered fixture");
            var cameraOwner = new GameObject("Field Notes camera");
            var notes = owner.AddComponent<FieldNotesPresenter>();
            Camera camera = cameraOwner.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                var profile = new ProfileState("notes-visual", "forest", 1);
                FactKnowledge knowledge = FactContentCatalog.Load();
                knowledge.Observe(profile, FactObservation.AxeNoisePresented);
                knowledge.Observe(profile, FactObservation.ShamblerTrappedPresented);
                knowledge.Observe(profile, FactObservation.ListenerInvestigationPresented);
                profile.DiscoverFact("missing.fact");
                notes.Bind(profile, knowledge, () => true, _ => { }, new Vector2(width, height));
                Assert.That(notes.Open(), Is.True);
                Canvas canvas = notes.Root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                notes.Refresh();
                Assert.That(notes.Select("LISTENER_FOLLOWS_NOISE"), Is.True);
                Canvas.ForceUpdateCanvases();
                foreach (Text text in notes.Root.GetComponentsInChildren<Text>())
                    Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1), text.name + ": " + text.text);
                Assert.That(notes.Entries, Has.Count.EqualTo(4));
                Assert.That(notes.Root.GetComponentsInChildren<Image>().Where(image => image.sprite != null)
                    .Select(image => image.sprite.name).Distinct().Count(), Is.GreaterThanOrEqualTo(4));
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                Assert.That(pixels.GetPixels32().Count(color => color.r < 100 && color.g < 140 && color.b < 120), Is.GreaterThan(800));
                string path = Path.Combine(Path.GetTempPath(), "HB-M57-notes-" + width + "x" + height + ".png");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                TestContext.WriteLine("Field Notes evidence: " + path);
                Assert.That(notes.Select("missing.fact"), Is.True);
                Assert.That(notes.SelectedText, Does.Contain("Uncatalogued observation").And.Not.Contain("missing.fact"));
                Assert.That(profile.DiscoveredFactIds, Does.Contain("missing.fact"));
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                notes.Dispose();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(cameraOwner);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
            }
        }

        private Fixture CreateFixture(bool pit, bool animations)
        {
            var fixture = new Fixture();
            fixtures.Add(fixture);
            fixture.Root = Path.Combine(Path.GetTempPath(), "HB-M57-play-" + Guid.NewGuid().ToString("N"));
            fixture.Store = new FileSystemSaveStore(fixture.Root);
            Type managerType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("GameManager")).First(type => type != null);
            fixture.Override = managerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic);
            fixture.PreviousRoot = fixture.Override.GetValue(null);
            fixture.Override.SetValue(null, fixture.Root);
            Assert.That(managerType.GetField("instance").GetValue(null), Is.Null);
            GameObject managerOwner = fixture.Track(new GameObject("M5.7 manager"));
            fixture.Manager = managerOwner.AddComponent(managerType);
            Invoke(fixture.Manager, "EnsurePersistence");
            Invoke(fixture.Manager, "StartNewRun");
            RunState run = fixture.Session.ActiveRun;
            run.EquipTool(1, run.CreateToolInstance(ToolContentCatalog.Load()[pit ? "shovel.basic" : "axe.basic"]));
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 5; horizontal++)
            for (int vertical = 0; vertical < 3; vertical++)
                layout.Add(View(fixture, pit && horizontal == 1 && vertical == 1 ? LegacyBoardContentKind.DiggableGround : LegacyBoardContentKind.Floor, horizontal, vertical));
            GameObject player = fixture.Track(new GameObject("Observed player"));
            player.AddComponent(managerType.Assembly.GetType("PlayerScript", true));
            player.transform.position = new Vector3(0, 1, 0);
            layout.Add(new LegacyBoardView(player, LegacyBoardContentKind.Player, new GridPosition(0, 1)));
            layout.Add(View(fixture, LegacyBoardContentKind.Exit, 4, 2));
            if (pit) layout.Add(View(fixture, LegacyBoardContentKind.Enemy, 2, 1));
            else
            {
                layout.Add(View(fixture, LegacyBoardContentKind.Wall, 1, 1));
                layout.Add(View(fixture, LegacyBoardContentKind.Listener, 4, 1));
            }
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay, run.GetBoardSeed(), "forest", "temperate", 1);
            fixture.Runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 4, 2), layout);
            Invoke(fixture.Manager, "PublishBoardRuntime", fixture.Runtime);
            fixture.Presenter.AnimationsEnabled = animations;
            return fixture;
        }

        private static LegacyBoardView View(Fixture fixture, LegacyBoardContentKind kind, int horizontal, int vertical)
        {
            GameObject owner = fixture.Track(new GameObject(kind.ToString()));
            owner.transform.position = new Vector3(horizontal, vertical, 0);
            return new LegacyBoardView(owner, kind, new GridPosition(horizontal, vertical), kind == LegacyBoardContentKind.Enemy ? (int?)10 : null);
        }

        private static string AllText(FieldNotesPresenter notes) => string.Join(" ", notes.Root.GetComponentsInChildren<Text>(true).Select(text => text.text));
        private static object Invoke(Component owner, string method, params object[] arguments) => owner.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(owner, arguments);

        private sealed class Fixture : IDisposable
        {
            public string Root;
            public FileSystemSaveStore Store;
            public FieldInfo Override;
            public object PreviousRoot;
            public Component Manager;
            public BoardRuntime Runtime;
            public readonly List<GameObject> Objects = new List<GameObject>();
            private bool disposed;
            public GameSession Session => (GameSession)Manager.GetType().GetProperty("Session").GetValue(Manager);
            public ProfileState Profile => Session.Profile;
            public FieldNotesPresenter Notes => (FieldNotesPresenter)Manager.GetType().GetProperty("FieldNotes").GetValue(Manager);
            public BoardEventPresenter Presenter => (BoardEventPresenter)Manager.GetType().GetProperty("ActiveBoardPresenter").GetValue(Manager);
            public GameObject Track(GameObject owner) { Objects.Add(owner); return owner; }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                foreach (GameObject owner in Objects) if (owner != null) Object.DestroyImmediate(owner);
                Runtime?.Dispose();
                Override?.SetValue(null, PreviousRoot);
                if (Root != null && Directory.Exists(Root)) Directory.Delete(Root, true);
            }
        }
    }
}
#endif