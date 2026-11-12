using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace HallowBlaze.Tests.PlayMode
{
    public sealed class GameSessionLifecycleTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private Type gameManagerType;
        private Type playerType;
        private Type restartButtonType;
        private Type startButtonType;
        private Type soundManagerType;
        private bool hadHighScore;
        private int originalHighScore;
        private string persistenceRoot;

        [SetUp]
        public void SetUp()
        {
            hadHighScore = PlayerPrefs.HasKey("HighScore");
            originalHighScore = PlayerPrefs.GetInt("HighScore");

            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            gameManagerType = RequireType(gameAssembly, "GameManager");
            playerType = RequireType(gameAssembly, "PlayerScript");
            restartButtonType = RequireType(gameAssembly, "RestartBttnScript");
            startButtonType = RequireType(gameAssembly, "StartBttnScript");
            DestroyGameManagerSingleton();
            persistenceRoot = Path.Combine(
                Path.GetTempPath(),
                "HallowBlaze-M1.7-Lifecycle-" + Guid.NewGuid().ToString("N"));
            SetStaticField(gameManagerType, "PersistenceRootOverride", persistenceRoot);
            InvokeStatic(gameManagerType, "RequestNewRun");
        }

        [TearDown]
        public void TearDown()
        {
            DestroyGameManagerSingleton();
            SetStaticField(gameManagerType, "PersistenceRootOverride", null);
            Time.timeScale = 1f;
            if (Directory.Exists(persistenceRoot))
                Directory.Delete(persistenceRoot, true);

            if (hadHighScore)
                PlayerPrefs.SetInt("HighScore", originalHighScore);
            else
                PlayerPrefs.DeleteKey("HighScore");
        }

        [Test]
        public void NewRunCreatesFreshStateAndPreservesProfile()
        {
            Component manager = CreateManager("M1.4 Lifecycle Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            RunState firstRun = session.ActiveRun;
            session.ConsumeFood(25);
            session.TakeDamage(15);

            Invoke(manager, "StartNewRun");

            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(session.ActiveRun, Is.Not.SameAs(firstRun));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(100));
            Assert.That(session.ActiveRun.CurrentDay, Is.Zero);
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Active));
        }

        /// <summary>Production menu transitions retain valid focus using keyboard activation and cancellation only.</summary>
        [UnityTest]
        public IEnumerator KeyboardMenuSceneFocusAndBackArePointerFree()
        {
            yield return SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            GameObject main = GameObject.Find("MainMenuPanel");
            KeyboardMenuFocus focus = main.GetComponent<KeyboardMenuFocus>();
            Assert.That(focus, Is.Not.Null);
            var events = UnityEngine.EventSystems.EventSystem.current;
            Assert.That(events.currentSelectedGameObject, Is.Not.Null);
            Assert.That(events.currentSelectedGameObject.GetComponent<Selectable>().IsInteractable(), Is.True);
            Button help = main.GetComponentsInChildren<Button>().Single(button => button.name == "HelpBttn");
            for (int index = 0; index < 20 && events.currentSelectedGameObject != help.gameObject; index++)
                focus.HandleKey(KeyCode.Tab);
            Assert.That(events.currentSelectedGameObject, Is.SameAs(help.gameObject));
            focus.HandleKey(KeyCode.Return);
            float deadline = Time.realtimeSinceStartup + 4;
            while (GameObject.Find("HelpMenuPanel") == null && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            GameObject helpRoot = GameObject.Find("HelpMenuPanel");
            Assert.That(helpRoot, Is.Not.Null);
            KeyboardMenuFocus helpFocus = helpRoot.GetComponent<KeyboardMenuFocus>();
            Assert.That(helpFocus, Is.Not.Null);
            helpFocus.HandleKey(KeyCode.Escape);
            deadline = Time.realtimeSinceStartup + 4;
            while ((!main.activeInHierarchy || helpRoot.activeInHierarchy) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Assert.That(main.activeInHierarchy, Is.True);
            Assert.That(helpRoot.activeInHierarchy, Is.False);
            focus.RecoverFocus();
            Assert.That(events.currentSelectedGameObject, Is.SameAs(help.gameObject));
            Scene cleanup = SceneManager.CreateScene("Keyboard menu cleanup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            yield return SceneManager.UnloadSceneAsync("Menu");
        }

        /// <summary>Real player input rotates visible art freely, preserves absolute movement and respects modal/replay/disposal ownership.</summary>
        [UnityTest]
        public IEnumerator ProductionVisualTurnsAreVisibleFreeAndGated()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            Invoke(manager, "HideLevelImage");
            yield return null;
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            BoardEventPresenter presenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            presenter.AnimationsEnabled = false;
            Component player = runtime.Views[runtime.PlayerId].GetComponent(playerType);
            PlayerVisualFacing facing = player.GetComponent<PlayerVisualFacing>();
            Assert.That(facing, Is.Not.Null);
            string before = HallowBlaze.Core.Persistence.PersistenceJsonSerializer.SerializeTravelRun(runtime.RunState);
            Vector3 position = player.transform.position;
            Quaternion rotation = player.transform.rotation;
            Color32[] previous = null;
            for (int turn = 0; turn < 4; turn++)
            {
                KeyCode facingKey = turn % 2 == 0 ? KeyCode.E : KeyCode.Q;
                Assert.That((bool)Invoke(player, "TryTurnVisual", (Func<KeyCode, bool>)(key => key == facingKey), -200 + turn), Is.True);
                yield return null;
                Texture2D capture = CaptureProductionCanvas(manager, 960, 640);
                Color32[] pixels = capture.GetPixels32();
                if (previous != null)
                    Assert.That(pixels.Where((pixel, index) => !pixel.Equals(previous[index])).Count(), Is.GreaterThan(10));
                previous = pixels;
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M718-facing-" + facing.QuarterTurns + ".png"), capture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(capture);
                Assert.That(player.transform.position, Is.EqualTo(position));
                Assert.That(player.transform.rotation, Is.EqualTo(rotation));
            }
            Assert.That(facing.QuarterTurns, Is.EqualTo(3));
            Assert.That(HallowBlaze.Core.Persistence.PersistenceJsonSerializer.SerializeTravelRun(runtime.RunState), Is.EqualTo(before));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.Zero);
            Invoke(manager, "SetGameplayInputBlocked", true);
            Assert.That((bool)Invoke(player, "TryTurnVisual", (Func<KeyCode, bool>)(key => key == KeyCode.Q), -190), Is.False);
            Invoke(manager, "SetGameplayInputBlocked", false);
            yield return null;
            FieldNotesPresenter notes = GetProperty<FieldNotesPresenter>(manager, "FieldNotes");
            Assert.That(notes.Open(), Is.True);
            Assert.That((bool)Invoke(player, "TryTurnVisual", (Func<KeyCode, bool>)(key => key == KeyCode.E), -189), Is.False);
            notes.Close();
            yield return null;
            Assert.That((bool)Invoke(player, "TryTurnVisual", (Func<KeyCode, bool>)(key => key == KeyCode.E), -187), Is.True);
            Assert.That(facing.QuarterTurns, Is.EqualTo(1));
            presenter.AnimationsEnabled = true;
            SetField(player, "moveTime", 0.2f);
            Direction direction = FindExitPath(runtime).First();
            object[] arguments = { new MoveCommand(direction), null };
            Assert.That((bool)Invoke(player, "TrySubmitCommand", arguments), Is.True);
            var operation = (Task<CommandSubmission>)arguments[1];
            Assert.That(operation.IsCompleted, Is.False);
            while (!operation.IsCompleted)
            {
                Assert.That((bool)Invoke(player, "TryTurnVisual", (Func<KeyCode, bool>)(key => key == KeyCode.Q), Time.frameCount), Is.False);
                yield return null;
            }
            Assert.That(operation.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(facing.QuarterTurns, Is.EqualTo(1));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
            yield return null;
            int food = runtime.RunState.Food;
            arguments = new object[] { new WaitCommand(), null };
            Assert.That((bool)Invoke(player, "TrySubmitCommand", arguments), Is.True);
            operation = (Task<CommandSubmission>)arguments[1];
            while (!operation.IsCompleted) yield return null;
            Assert.That(runtime.RunState.Food, Is.EqualTo(food - 1));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(2));
            Assert.That(facing.QuarterTurns, Is.EqualTo(1));
            Invoke(manager, "GameOver", false);
            Assert.That((bool)Invoke(player, "TryTurnVisual", (Func<KeyCode, bool>)(key => key == KeyCode.Q), -186), Is.False);
            ((Behaviour)player).enabled = false;
            Assert.That((bool)Invoke(player, "TryTurnVisual", (Func<KeyCode, bool>)(key => key == KeyCode.Q), -188), Is.False);
            Scene cleanup = SceneManager.CreateScene("Visual facing cleanup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        /// <summary>Main menu Controls is keyboard-reachable, accurately illustrated and readable across desktop/narrow context layouts.</summary>
        [UnityTest]
        public IEnumerator MainControlsDiagramAndContextsStayReadable()
        {
            yield return SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            GameObject main = GameObject.Find("MainMenuPanel");
            KeyboardMenuFocus focus = main.GetComponent<KeyboardMenuFocus>();
            var events = UnityEngine.EventSystems.EventSystem.current;
            for (int index = 0; index < 20 && events.currentSelectedGameObject.name != "Controls"; index++) focus.HandleKey(KeyCode.Tab);
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("Controls"));
            Button controlsButton = events.currentSelectedGameObject.GetComponent<Button>();
            Assert.That(controlsButton.targetGraphic, Is.TypeOf<Text>());
            Assert.That(controlsButton.colors.highlightedColor, Is.Not.EqualTo(controlsButton.colors.normalColor));
            focus.HandleKey(KeyCode.Return);
            KeyboardControlsView view = KeyboardControlsView.Current;
            Assert.That(view, Is.Not.Null);
            var cameraOwner = new GameObject("Controls evidence camera", typeof(Camera));
            Camera camera = cameraOwner.GetComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            Canvas canvas = view.GetComponent<Canvas>();
            Canvas.ForceUpdateCanvases();
            Assert.That(canvas.isRootCanvas, Is.True, "Controls must not inherit the menu panel's position or scale.");
            RectTransform controlsRect = (RectTransform)view.transform;
            Vector2 controlsCenter = RectTransformUtility.WorldToScreenPoint(null, controlsRect.TransformPoint(controlsRect.rect.center));
            Assert.That(controlsCenter.x, Is.EqualTo(Screen.width / 2f).Within(1));
            Assert.That(controlsCenter.y, Is.EqualTo(Screen.height / 2f).Within(1));
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            try
            {
                foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(320, 480), new Vector2Int(480, 640) })
                foreach (PcInputContext context in Enum.GetValues(typeof(PcInputContext)))
                {
                    var target = new RenderTexture(size.x, size.y, 24);
                    var pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    RenderTexture previous = RenderTexture.active;
                    try
                    {
                        camera.targetTexture = target;
                        view.Resize(size);
                        view.SelectContext(context);
                        Canvas.ForceUpdateCanvases();
                        foreach (var hint in PcInputBindings.GetHints(context))
                        {
                            Assert.That(view.Hints.Any(displayed => displayed.Label == hint.Label), Is.True);
                            foreach (KeyCode key in hint.Keys) Assert.That(view.DiagramKeys, Does.Contain(key));
                        }
                        foreach (Text text in view.GetComponentsInChildren<Text>())
                        {
                            Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1), text.text);
                            Assert.That(text.cachedTextGenerator.GetVerticesArray().All(vertex => float.IsFinite(vertex.position.x)), Is.True);
                        }
                        Assert.That(view.GetComponentsInChildren<Transform>().Any(child => child.name == "Keyboard focus"), Is.False);
                        ScrollRect scroll = view.GetComponentInChildren<ScrollRect>();
                        Selectable scrollFocus = scroll.GetComponent<Selectable>();
                        events.SetSelectedGameObject(scroll.gameObject);
                        Assert.That(scrollFocus.targetGraphic.color.a, Is.EqualTo(1));
                        Assert.That(scrollFocus.targetGraphic.canvasRenderer.GetColor(), Is.Not.EqualTo(scrollFocus.colors.normalColor));
                        Assert.That(scrollFocus.targetGraphic.canvasRenderer.GetColor(), Is.EqualTo(scrollFocus.colors.selectedColor));
                        scroll.verticalNormalizedPosition = 0;
                        Canvas.ForceUpdateCanvases();
                        Text lastAction = view.GetComponentsInChildren<Text>().Last(text => text.name == "Binding action");
                        Bounds lastBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, lastAction.rectTransform);
                        Assert.That(lastBounds.min.y, Is.GreaterThanOrEqualTo(scroll.viewport.rect.yMin - 1));
                        Assert.That(lastBounds.max.y, Is.LessThanOrEqualTo(scroll.viewport.rect.yMax + 1));
                        camera.Render();
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                        pixels.Apply();
                        Assert.That(pixels.GetPixels32().Count(pixel => pixel.g < 150 && pixel.r < 150), Is.GreaterThan(1000));
                        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M713-" + context + "-" + size.x + "x" + size.y + ".png"), pixels.EncodeToPNG());
                    }
                    finally
                    {
                        camera.targetTexture = null;
                        RenderTexture.active = previous;
                        target.Release();
                        UnityEngine.Object.DestroyImmediate(target);
                        UnityEngine.Object.DestroyImmediate(pixels);
                    }
                }
                view.GetComponent<KeyboardMenuFocus>().HandleKey(KeyCode.Escape);
                Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("Controls"));
                Assert.That(main.activeInHierarchy, Is.True);
            }
            finally
            {
                if (view != null && view.IsOpen) view.Close();
                UnityEngine.Object.DestroyImmediate(cameraOwner);
            }
            Scene cleanup = SceneManager.CreateScene("Controls cleanup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            yield return SceneManager.UnloadSceneAsync("Menu");
        }

        /// <summary>Actual production pause Controls preserves the full saved run, resolver count and input ownership.</summary>
        [UnityTest]
        public IEnumerator ProductionPauseControlsPreservesRunAndFocus()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            Invoke(manager, "HideLevelImage");
            yield return null;
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            BoardEventPresenter presenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            Type pauseType = gameManagerType.Assembly.GetType("PauseMenuController", true);
            Component pause = UnityEngine.Object.FindFirstObjectByType(pauseType) as Component;
            Assert.That(pause, Is.Not.Null);
            string before = HallowBlaze.Core.Persistence.PersistenceJsonSerializer.SerializeTravelRun(runtime.RunState);
            Invoke(pause, "OpenPause");
            GameObject pauseRoot = GetField<GameObject>(pause, "pauseRoot");
            KeyboardMenuFocus focus = pauseRoot.GetComponent<KeyboardMenuFocus>();
            var events = UnityEngine.EventSystems.EventSystem.current;
            for (int index = 0; index < 10 && events.currentSelectedGameObject.name != "Controls"; index++) focus.HandleKey(KeyCode.Tab);
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("Controls"));
            focus.HandleKey(KeyCode.Return);
            Assert.That(KeyboardControlsView.Current.IsOpen, Is.True);
            KeyboardControlsView.Current.GetComponent<KeyboardMenuFocus>().HandleKey(KeyCode.Escape);
            Invoke(pause, "HandleEscape");
            Assert.That(GetProperty<object>(pause, "CurrentState").ToString(), Is.EqualTo("PauseRoot"));
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("Controls"));
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(presenter.Coordinator.ResolutionCount, Is.Zero);
            Assert.That(HallowBlaze.Core.Persistence.PersistenceJsonSerializer.SerializeTravelRun(runtime.RunState), Is.EqualTo(before));
            Invoke(pause, "Resume");
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            yield return null;
            Scene cleanup = SceneManager.CreateScene("Pause Controls cleanup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        /// <summary>Production death actions are reachable instead of assigning focus to their noninteractive backdrop.</summary>
        [UnityTest]
        public IEnumerator KeyboardTerminalFocusBelongsToVisibleActions()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)gameManagerType.GetField("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            Invoke(manager, "HideLevelImage");
            Invoke(manager, "GameOver", false);
            GameObject restart = GameObject.Find("RestartBttn");
            GameObject menu = GameObject.Find("MenuBttn");
            Assert.That(restart, Is.Not.Null);
            KeyboardMenuFocus focus = restart.GetComponentInParent<KeyboardMenuFocus>();
            Assert.That(focus, Is.Not.Null);
            Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject, Is.SameAs(restart));
            focus.HandleKey(KeyCode.Tab);
            Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject, Is.SameAs(menu));
            Scene cleanup = SceneManager.CreateScene("Keyboard terminal cleanup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        [UnityTest]
        public IEnumerator CorruptRunKeepsContinueLabelStable()
        {
            Component manager = CreateManager("Continue Label Save Manager");
            Invoke(manager, "EnsurePersistence");
            Invoke(manager, "StartNewRun");

            Directory.CreateDirectory(persistenceRoot);
            string profilePath = Path.Combine(persistenceRoot, "profile.json");
            string runPath = Path.Combine(persistenceRoot, "run.json");
            Assert.That(File.Exists(profilePath), Is.True);
            Assert.That(File.Exists(runPath), Is.True);
            DestroyGameManagerSingleton();

            File.WriteAllText(runPath, "{ invalid json");
            SaveStoreResult<RunState> inspection = RunLifecycleService.InspectContinue(
                new FileSystemSaveStore(persistenceRoot));
            Assert.That(inspection.Type, Is.EqualTo(SaveStoreResultType.Corrupt));

            AsyncOperation menuLoad = SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);
            while (!menuLoad.isDone)
                yield return null;
            yield return null;

            AssertSceneHasNoMissingComponents("Menu");
            GameObject continueButton = GameObject.Find("ContinueBttn");
            Assert.That(continueButton, Is.Not.Null);
            Assert.That(continueButton.GetComponent<Button>().interactable, Is.False);
            Text continueLabel = continueButton.GetComponentInChildren<Text>(true);
            Assert.That(continueLabel, Is.Not.Null);
            Assert.That(continueLabel.text, Is.EqualTo("Continue"));

            Scene cleanupScene = SceneManager.CreateScene(
                "Continue Label Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Menu");
        }

        [UnityTest]
        public IEnumerator ActiveSceneBoundaryPreservesSessionAndRun()
        {
            Component manager = CreateManager("M1.4 Scene Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            RunState run = session.ActiveRun;
            session.ConsumeFood(13);
            session.TakeDamage(7);
            Scene originalScene = SceneManager.GetActiveScene();
            Scene nextScene = SceneManager.CreateScene("M1.4 Session Boundary " + Guid.NewGuid().ToString("N"));

            Assert.That(SceneManager.SetActiveScene(nextScene), Is.True);
            yield return null;

            Assert.That(GetStaticField(gameManagerType, "instance"), Is.SameAs(manager));
            Assert.That(GetProperty<GameSession>(manager, "Session"), Is.SameAs(session));
            Assert.That(session.ActiveRun, Is.SameAs(run));
            Assert.That(run.Food, Is.EqualTo(87));
            Assert.That(run.Health, Is.EqualTo(93));

            Assert.That(SceneManager.SetActiveScene(originalScene), Is.True);
            yield return SceneManager.UnloadSceneAsync(nextScene);
        }

        [UnityTest]
        public IEnumerator RealMainSceneReloadPreservesRunAndRebindsPlayerUi()
        {
            AsyncOperation firstLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!firstLoad.isDone)
                yield return null;
            yield return null;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(manager, Is.Not.Null);
            yield return StartFirstRoute(manager);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            RunState run = session.ActiveRun;
            Assert.That(run, Is.Not.Null);
            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));

            int expectedFood = run.Food - 14;
            session.ConsumeFood(14);
            session.TakeDamage(11);
            Component firstPlayer = FindSceneComponent(playerType, "Main");
            Assert.That(firstPlayer, Is.Not.Null);
            BoardRuntime oldRuntime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            BoardEventPresenter oldPresenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            oldPresenter.SynchronizeFromState();
            int firstPlayerInstanceId = firstPlayer.GetInstanceID();
            Assert.That(GetField<Text>(firstPlayer, "foodText").text, Is.EqualTo("Food: " + expectedFood));
            Assert.That(GetField<Text>(firstPlayer, "healthText").text, Is.EqualTo("Health: 89"));

            AsyncOperation reload = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!reload.isDone)
                yield return null;
            yield return null;

            Component reloadedManager = GetStaticField(gameManagerType, "instance") as Component;
            Component replacementPlayer = FindSceneComponent(playerType, "Main");
            Assert.That(reloadedManager, Is.SameAs(manager));
            Assert.That(GetProperty<GameSession>(reloadedManager, "Session"), Is.SameAs(session));
            Assert.That(session.ActiveRun, Is.SameAs(run));
            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(run.Food, Is.EqualTo(expectedFood));
            Assert.That(run.Health, Is.EqualTo(89));
            Assert.That(replacementPlayer, Is.Not.Null);
            Assert.That(replacementPlayer.GetInstanceID(), Is.Not.EqualTo(firstPlayerInstanceId));
            Assert.That(oldRuntime.IsDisposed, Is.True);
            Assert.That(oldPresenter.Coordinator.CanSubmit, Is.False);
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").Coordinator.ResolutionCount, Is.Zero);
            Assert.That(GetField<Text>(replacementPlayer, "foodText").text, Is.EqualTo("Food: " + expectedFood));
            Assert.That(GetField<Text>(replacementPlayer, "healthText").text, Is.EqualTo("Health: 89"));
            Assert.That(GetField<GameObject>(manager, "levelImage").activeSelf, Is.False,
                "Reload of Consumed must not repeat the intro.");

            ProfileState profile = session.Profile;
            GameObject restartButton = GetField<GameObject>(manager, "restartButton");
            GameObject menuButton = GetField<GameObject>(manager, "menuButton");
            Assert.That(restartButton, Is.Not.Null);
            Assert.That(menuButton, Is.Not.Null);
            Assert.That(restartButton.activeSelf, Is.False);
            Assert.That(menuButton.activeSelf, Is.False);
            PlayerPrefs.SetInt("HighScore", 0);

            yield return PresentHealthDeath(manager);

            Assert.That(run.Health, Is.Zero);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(((Behaviour)manager).enabled, Is.False);
            Assert.That(restartButton.activeSelf, Is.True);
            Assert.That(menuButton.activeSelf, Is.True);
            Assert.That(PlayerPrefs.GetInt("HighScore"), Is.EqualTo(1));

            Component restartController = restartButton.GetComponent(restartButtonType);
            Assert.That(restartController, Is.Not.Null);
            Button restartButtonControl = restartButton.GetComponent<Button>();
            Assert.That(restartButtonControl, Is.Not.Null);
            Assert.That(restartButtonControl.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(restartButtonControl.onClick.GetPersistentTarget(0), Is.SameAs(restartController));
            Assert.That(restartButtonControl.onClick.GetPersistentMethodName(0), Is.EqualTo("Restart"));
            restartButtonControl.onClick.Invoke();
            yield return null;

            RunState restartedRun = session.ActiveRun;
            Assert.That(GetProperty<GameSession>(manager, "Session"), Is.SameAs(session));
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(restartedRun, Is.Not.SameAs(run));
            Assert.That(restartedRun.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(restartedRun.CurrentDay, Is.EqualTo(1));
            Assert.That(restartedRun.CompletedDayCount, Is.Zero);
            Assert.That(restartedRun.Food, Is.EqualTo(100));
            Assert.That(restartedRun.Health, Is.EqualTo(100));
            Assert.That(((Behaviour)manager).enabled, Is.True);
            Assert.That(restartedRun.WorldNodeId, Is.EqualTo("shelter.warm-up"));
            Assert.That(restartedRun.ActiveLeg.EdgeId, Is.EqualTo("road.shelter-warm-up"));
            Assert.That(restartedRun.ActiveLeg.DayNumber, Is.EqualTo(1));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Not.Null);

            Scene cleanupScene = SceneManager.CreateScene("M1.4 Smoke Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        [UnityTest]
        public IEnumerator GameOverMenuButtonLoadsMenuWithoutNewRun()
        {
            AsyncOperation mainLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!mainLoad.isDone)
                yield return null;
            yield return null;
            AssertSceneHasNoMissingComponents("Main");

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(manager, Is.Not.Null);
            yield return StartFirstRoute(manager);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            RunState completedRun = session.ActiveRun;
            Component player = FindSceneComponent(playerType, "Main");
            Assert.That(player, Is.Not.Null);

            yield return PresentHealthDeath(manager);
            GameObject menuButton = GameObject.Find("MenuBttn");
            Assert.That(menuButton, Is.Not.Null);
            Assert.That(menuButton.activeSelf, Is.True);
            Component menuController = menuButton.GetComponent(restartButtonType);
            Assert.That(menuController, Is.Not.Null);
            Button menuButtonControl = menuButton.GetComponent<Button>();
            Assert.That(menuButtonControl, Is.Not.Null);
            Assert.That(menuButtonControl.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(menuButtonControl.onClick.GetPersistentTarget(0), Is.SameAs(menuController));
            Assert.That(menuButtonControl.onClick.GetPersistentMethodName(0), Is.EqualTo("ReturnToMenu"));

            Time.timeScale = 0f;
            Invoke(manager, "SetGameplayInputBlocked", true);
            menuButtonControl.onClick.Invoke();

            float loadDeadline = Time.realtimeSinceStartup + 5f;
            while (SceneManager.GetActiveScene().name != "Menu" &&
                Time.realtimeSinceStartup < loadDeadline)
                yield return null;
            yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Menu"));
            AssertSceneHasNoMissingComponents("Menu");
            GameObject continueButton = GameObject.Find("ContinueBttn");
            Assert.That(continueButton, Is.Not.Null);
            Assert.That(continueButton.GetComponent<Button>().interactable, Is.False);
            Text continueLabel = continueButton.GetComponentInChildren<Text>(true);
            Assert.That(continueLabel, Is.Not.Null);
            Assert.That(continueLabel.text, Is.EqualTo("Continue"));
            Assert.That(GetStaticField(gameManagerType, "instance"), Is.SameAs(manager));
            Assert.That(((Behaviour)manager).enabled, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(profile.RunSummaries.Count, Is.EqualTo(1));
            Assert.That(profile.RunSummaries[0].RunId, Is.EqualTo(completedRun.RunId));
            Assert.That(profile.RunSummaries[0].Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "profile.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.json")), Is.False);

            Scene cleanupScene = SceneManager.CreateScene("M1.11 Menu Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Menu");
            }

        /// <summary>The explicit world goal closes the run once and offers keyboard menu/new-run actions without erasing atlas knowledge.</summary>
        [UnityTest]
        public IEnumerator VictoryActionsPreserveProfileAndAllowMenuOrNewRun([Values(false, true)] bool restart)
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            Invoke(manager, "HideLevelImage");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            int knownNodes = profile.NodeDiscoveries.Count;
            HallowBlaze.Core.World.WorldDefinition world = GetField<HallowBlaze.Core.World.WorldDefinition>(manager, "worldDefinition");
            RunState won = session.StartNewRun("victory-smoke", 4321, new RunStateConfiguration(100, 100, 5, world.GoalNodeId));
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.True);
            Assert.That(won.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(profile.WonRunCount, Is.EqualTo(1));
            Assert.That(profile.NodeDiscoveries.Count, Is.GreaterThanOrEqualTo(knownNodes));
            Assert.That(GetField<Text>(manager, "levelText").text, Does.Contain("destination").And.Contain("5"));
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.json")), Is.False);
            Assert.That(new FileSystemSaveStore(persistenceRoot).LoadProfile().Data.WonRunCount, Is.EqualTo(1));
            GameObject action = GetField<GameObject>(manager, restart ? "restartButton" : "menuButton");
            Assert.That(action.activeInHierarchy, Is.True);
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(320, 480) })
            {
                Texture2D capture = CaptureProductionCanvas(manager, size.x, size.y, () =>
                {
                    RectTransform bar = (RectTransform)action.transform.parent;
                    foreach (RectTransform element in new[] { (RectTransform)action.transform, GetField<Text>(manager, "levelText").rectTransform })
                    {
                        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(bar, element);
                        Assert.That(bounds.min.x, Is.GreaterThanOrEqualTo(bar.rect.xMin - 1));
                        Assert.That(bounds.max.x, Is.LessThanOrEqualTo(bar.rect.xMax + 1));
                        Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(bar.rect.yMin - 1));
                        Assert.That(bounds.max.y, Is.LessThanOrEqualTo(bar.rect.yMax + 1));
                    }
                });
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M719-victory-" + restart + "-" + size.x + "x" + size.y + ".png"), capture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(capture);
            }
            KeyboardMenuFocus focus = action.GetComponentInParent<KeyboardMenuFocus>();
            FieldNotesPresenter knowledge = GetProperty<FieldNotesPresenter>(manager, "FieldNotes");
            Invoke(manager, "ObservePresentedObservation", FactObservation.ShamblerTrappedPresented);
            Component atlas = manager.GetComponentsInChildren<MonoBehaviour>().Single(component =>
                component.GetType().FullName == "HallowBlaze.Presentation.Atlas.AtlasScreenController");
            foreach (int closeMode in new[] { 0, 1, 2 })
            {
                Assert.That(knowledge.SampleInput(key => key == KeyCode.N, -200 - closeMode * 2), Is.True);
                Assert.That(knowledge.IsOpen, Is.True);
                Assert.That(focus.isActiveAndEnabled, Is.False);
                Assert.That(action.activeInHierarchy, Is.False, "Terminal actions must not cover or receive pointer input over the reader.");
                yield return null;
                KeyboardMenuFocus readerFocus = atlas.GetComponent<KeyboardMenuFocus>();
                Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("AtlasObservation"));
                readerFocus.HandleKey(KeyCode.Return);
                Assert.That(GetProperty<string>(atlas, "InspectedDetails"), Does.Contain("causes no damage"));
                readerFocus.HandleKey(KeyCode.Tab);
                Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.transform.IsChildOf(atlas.transform), Is.True);
                Assert.That(session.ActiveRun, Is.Null, "Reading/confirmation must never restart the completed run.");
                Assert.That(profile.WonRunCount, Is.EqualTo(1));
                if (closeMode == 0) knowledge.SampleInput(key => key == KeyCode.N, -201 - closeMode * 2);
                else if (closeMode == 1) readerFocus.HandleKey(KeyCode.Escape);
                else atlas.GetComponentsInChildren<Button>().Single(button => button.name == "Close Atlas").onClick.Invoke();
                yield return null;
                Assert.That(knowledge.IsOpen, Is.False);
                Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.True, "Closing reading must preserve the final atlas.");
                Assert.That(focus.isActiveAndEnabled && action.activeInHierarchy, Is.True);
                Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.transform.IsChildOf(action.transform.parent), Is.True);
                Assert.That(session.ActiveRun, Is.Null);
            }
            for (int index = 0; index < 10 && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != action; index++)
                focus.HandleKey(KeyCode.D);
            Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject, Is.SameAs(action));
            focus.HandleKey(KeyCode.Return);
            float deadline = Time.realtimeSinceStartup + 5f;
            while ((restart ? session.ActiveRun == null : SceneManager.GetActiveScene().name != "Menu") && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(profile.NodeDiscoveries.Count, Is.GreaterThanOrEqualTo(knownNodes));
            Assert.That(profile.WonRunCount, Is.EqualTo(1));
            if (restart)
            {
                Assert.That(session.ActiveRun, Is.Not.Null);
                Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Active));
                Assert.That(session.ActiveRun.WorldNodeId, Is.EqualTo("shelter.warm-up"));
            }
            else
            {
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Menu"));
                Assert.That(session.ActiveRun, Is.Null);
                Assert.That(GameObject.Find("ContinueBttn").GetComponent<Button>().interactable, Is.False);
            }
            Assert.That(GameObject.Find("Victory actions"), Is.Null, "Terminal UI must not survive menu or new-run transition.");
            if (!restart)
                Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.transform.IsChildOf(GameObject.Find("MainMenuPanel").transform), Is.True);
        }

        [UnityTest]
        public IEnumerator RealMainSceneHidesResourceHudDuringRouteIntro()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            AssertOpeningWarmUp(manager);
            Component player = FindSceneComponent(playerType, "Main");
            GameObject levelImage = GetField<GameObject>(manager, "levelImage");
            GameObject foodText = GetField<Text>(player, "foodText").gameObject;
            GameObject healthText = GetField<Text>(player, "healthText").gameObject;
            Assert.That(levelImage, Is.Not.Null);
            Assert.That(foodText, Is.Not.Null);
            Assert.That(healthText, Is.Not.Null);
            Canvas resourceCanvas = foodText.GetComponentInParent<Canvas>();
            Assert.That(resourceCanvas, Is.Not.Null);
            Assert.That(resourceCanvas.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(foodText.transform.parent, Is.SameAs(levelImage.transform.parent));
            Assert.That(healthText.transform.parent, Is.SameAs(levelImage.transform.parent));
            Assert.That(levelImage.activeSelf, Is.True);
            Assert.That(Camera.main.cullingMask, Is.Zero,
                "The board and player must not render behind the Day transition.");
            Assert.That(foodText.activeInHierarchy, Is.False);
            Assert.That(healthText.activeInHierarchy, Is.False);
            Assert.That(GetField<Text>(manager, "levelText").text, Is.EqualTo("Day: 1"));
            Assert.That(new FileSystemSaveStore(persistenceRoot).LoadRun().Data.ActiveLeg.IntroState,
                Is.EqualTo(DayIntroState.Consumed));
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                string screenshot = Path.Combine(Path.GetTempPath(), "HB-M312-Day1-" + Screen.width + "x" + Screen.height + ".png");
                Texture2D capture = CaptureProductionCanvas(manager, Screen.width, Screen.height);
                Assert.That(capture, Is.Not.Null);
                try
                {
                    Assert.That(capture.GetPixels32().Distinct().Count(), Is.GreaterThan(2), "The rendered intro must not be blank.");
                    File.WriteAllBytes(screenshot, capture.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(capture); }
                TestContext.WriteLine("Day transition capture: " + screenshot);
            }
            Invoke(manager, "HideLevelImage");
            yield return null;
            Assert.That(Camera.main.cullingMask, Is.Not.Zero);
            Assert.That(levelImage.activeSelf, Is.False);
            Assert.That(foodText.activeInHierarchy && healthText.activeInHierarchy, Is.True);
        }

        /// <summary>Every rendered frame clears reserved margins instead of retaining the previous board image.</summary>
        [UnityTest]
        public IEnumerator BoardViewportClearsOutsideTheGameplayCamera()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            Invoke(manager, "HideLevelImage");
            yield return null;
            Camera camera = Camera.main;
            Assert.That(camera.rect.height, Is.LessThan(1));
            Assert.That(GetField<Camera>(manager, "viewportClearCamera").isActiveAndEnabled, Is.True);
            Texture2D capture = CaptureWorldCameras();
            try
            {
                Color margin = capture.GetPixel(0, capture.height - 1);
                Assert.That(Vector4.Distance(margin, Color.magenta), Is.GreaterThan(0.1f),
                    "A reserved margin must be cleared each frame, not retain prior board pixels.");
            }
            finally { UnityEngine.Object.DestroyImmediate(capture); }
        }

        /// <summary>Notes, legend and board-owned controls cannot leak across modal and board publication boundaries.</summary>
        [UnityTest]
        public IEnumerator BoardOverlaysCloseAtModalAndSceneBoundaries()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            Invoke(manager, "HideLevelImage");
            yield return null;
            EnemyIntentLegend legend = GetField<EnemyIntentLegend>(manager, "activeIntentLegend");
            FieldNotesPresenter notes = GetProperty<FieldNotesPresenter>(manager, "FieldNotes");
            ToolHudPresenter tools = GetProperty<ToolHudPresenter>(manager, "ActiveToolHud");
            legend.Show();
            Assert.That(notes.Open(), Is.True);
            Assert.That(legend.IsVisible, Is.False);
            Assert.That(legend.Root.activeInHierarchy, Is.False);
            Assert.That(tools.Root.activeInHierarchy, Is.False);
            notes.Close();
            yield return null;
            Assert.That(legend.Root.activeInHierarchy, Is.True);
            Assert.That(legend.IsVisible, Is.False);
            Assert.That(tools.Root.activeInHierarchy, Is.True);

            Assert.That(notes.Open(), Is.True);
            Invoke(manager, "BlockBoardStartup");
            Assert.That(notes.IsOpen, Is.False);
            Assert.That(notes.Root.activeInHierarchy, Is.False);
            Assert.That(legend.Root.activeInHierarchy, Is.False);
            Assert.That(Camera.main.cullingMask, Is.Zero);
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            legend = GetField<EnemyIntentLegend>(manager, "activeIntentLegend");
            Assert.That(legend.IsVisible, Is.False);
            Assert.That(notes.IsOpen, Is.False);
            Assert.That(GetField<GameObject>(manager, "levelImage").activeSelf, Is.False);
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome",
                BoardOutcome.ExitReached(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"))), Is.True);
            Assert.That(notes.Root.activeInHierarchy, Is.False);
            Assert.That(Camera.main.cullingMask, Is.Zero);
        }

        private static Texture2D CaptureWorldCameras()
        {
            Camera[] cameras = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>())
                .Where(camera => camera.isActiveAndEnabled).OrderBy(camera => camera.depth).ToArray();
            RenderTexture[] previousTargets = cameras.Select(camera => camera.targetTexture).ToArray();
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(Screen.width, Screen.height, 24);
            target.Create();
            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, Color.magenta);
                foreach (Camera camera in cameras)
                {
                    camera.targetTexture = target;
                    camera.Render();
                }
                RenderTexture.active = target;
                var capture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                capture.Apply();
                return capture;
            }
            finally
            {
                for (int index = 0; index < cameras.Length; index++)
                    cameras[index].targetTexture = previousTargets[index];
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>Real production layers remain coherent across desktop, portrait, overlay and differently colored board frames.</summary>
        [UnityTest]
        public IEnumerator ProductionLayerPixelsStayCleanAcrossResizeAndBoards()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            ((MonoBehaviour)manager).CancelInvoke("HideLevelImage");
            FieldNotesPresenter notes = GetProperty<FieldNotesPresenter>(manager, "FieldNotes");
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(320, 480), new Vector2Int(320, 640) })
            {
                Texture2D intro = CaptureProductionCanvas(manager, size.x, size.y);
                try
                {
                    Assert.That(Camera.main.cullingMask, Is.Zero);
                    Assert.That(intro.GetPixels32().Distinct().Count(), Is.GreaterThan(2));
                    File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M72-Day-" + size.x + "x" + size.y + ".png"), intro.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(intro); }
            }
            Invoke(manager, "HideLevelImage");
            yield return null;
            foreach (bool animations in new[] { false, true })
            {
                GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").AnimationsEnabled = animations;
                BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
                foreach (GameObject view in runtime.Views.Values)
                    foreach (SpriteRenderer renderer in view.GetComponentsInChildren<SpriteRenderer>())
                        renderer.color = animations ? Color.green : Color.red;
                foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(320, 480), new Vector2Int(320, 640) })
                {
                    EnemyIntentLegend legend = GetField<EnemyIntentLegend>(manager, "activeIntentLegend");
                    legend.Show();
                    Assert.That(notes.Open(), Is.True);
                    Texture2D modal = CaptureProductionCanvas(manager, size.x, size.y);
                    try
                    {
                        Color top = modal.GetPixel(4, size.y - 4);
                        Assert.That(Vector4.Distance(top, (Color)new Color32(39, 45, 42, 255)), Is.LessThan(0.05f));
                        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M72-Notes-" + size.x + "x" + size.y + ".png"), modal.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(modal); }
                    notes.Close();
                    yield return null;
                    Assert.That(legend.IsVisible, Is.False);
                    Texture2D board = CaptureProductionCanvas(manager, size.x, size.y);
                    try
                    {
                        Color margin = board.GetPixel(0, size.y - 1);
                        Color background = Camera.main.backgroundColor;
                        Assert.That(Vector3.Distance(new Vector3(margin.r, margin.g, margin.b),
                            new Vector3(background.r, background.g, background.b)), Is.LessThan(0.05f));
                        Assert.That(GetField<GameObject>(manager, "levelImage").activeSelf, Is.False);
                        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M72-Board-" + size.x + "x" + size.y + ".png"), board.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(board); }
                }
                if (!animations)
                {
                    yield return AdvanceOpeningToFinalSegment(manager);
                    Assert.That((bool)Invoke(manager, "HandleBoardOutcome",
                        BoardOutcome.ExitReached(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"))), Is.True);
                    Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-west-trail"), Is.True);
                    yield return null;
                    yield return null;
                    Invoke(manager, "HideLevelImage");
                    yield return null;
                }
            }
        }

        /// <summary>The production board and long resource totals/gains share the window center without clipped labels at supported aspect ratios.</summary>
        [UnityTest]
        public IEnumerator ProductionBoardAndResourceHudStayCenteredAndFit()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            Invoke(manager, "HideLevelImage");
            yield return null;
            Component player = FindSceneComponent(playerType, "Main");
            Invoke(manager, "ObservePresentedObservation", FactObservation.ShamblerTrappedPresented);
            FieldNotesPresenter notes = GetProperty<FieldNotesPresenter>(manager, "FieldNotes");
            Assert.That(notes.FeedbackTitle, Is.EqualTo("Held by the ground"));
            Assert.That(new FileSystemSaveStore(persistenceRoot).LoadProfile().Data.DiscoveredFactIds,
                Does.Contain("PIT_TRAPS_SHAMBLER"));
            var failures = new List<string>();
            for (int boardIndex = 0; boardIndex < 2; boardIndex++)
            {
            Assert.That(notes.Open(), Is.True);
            notes.Close();
            yield return null;
            ((IRunHud)player).Refresh(int.MaxValue, int.MaxValue);
            for (int index = 0; index < 64; index++)
                ((ITurnResourceGains)player).AddResourceGains(int.MaxValue, int.MaxValue);
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 720),
                new Vector2Int(320, 480), new Vector2Int(320, 640) })
            {
                var resourceFrames = new List<Rect>();
                Texture2D capture = CaptureProductionCanvas(manager, size.x, size.y, () =>
                {
                    Rect board = (Rect)Invoke(manager, "InspectedBoardArea");
                    if (Mathf.Abs(board.center.x - size.x / 2f) > 1)
                        failures.Add(size + " board center " + board.center.x + " != " + size.x / 2f);
                    Rect viewport = new Rect(0, 0, size.x, size.y);
                    if (!viewport.Contains(board.min) || !viewport.Contains(board.max))
                        failures.Add(size + " board outside viewport: " + board);
                    if (Mathf.Min(board.width, board.height) < 160)
                        failures.Add(size + " inspected board below 160 pixels: " + board);
                    Rect ScreenBounds(RectTransform rect)
                    {
                        var corners = new Vector3[4];
                        rect.GetWorldCorners(corners);
                        Canvas canvas = rect.GetComponentInParent<Canvas>();
                        Vector2 minimum = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[0]);
                        Vector2 maximum = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[2]);
                        return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
                    }
                    Text[] labels = { GetField<Text>(player, "foodText"), GetField<Text>(player, "healthText") };
                    Rect[] frames = labels.Select(text => ScreenBounds(text.rectTransform)).ToArray();
                    Rect group = Rect.MinMaxRect(frames.Min(rect => rect.xMin), frames.Min(rect => rect.yMin),
                        frames.Max(rect => rect.xMax), frames.Max(rect => rect.yMax));
                    if (Mathf.Abs(group.center.x - board.center.x) > 1)
                        failures.Add(size + " resource center differs from board: " + group);
                    EnemyIntentLegend legend = GetField<EnemyIntentLegend>(manager, "activeIntentLegend");
                    Rect tools = GetProperty<ToolHudPresenter>(manager, "ActiveToolHud").Bounds;
                    Button book = GetProperty<FieldNotesPresenter>(manager, "FieldNotes").Root
                        .GetComponentsInChildren<Button>(true).Single(button => button.name == "Open Atlas");
                    Rect bookBounds = ScreenBounds((RectTransform)book.transform);
                    RectTransform notice = notes.Root.GetComponentsInChildren<RectTransform>(true)
                        .Single(rect => rect.name == "Committed discovery");
                    Assert.That(notice.gameObject.activeInHierarchy, Is.EqualTo(boardIndex == 0));
                    Rect noticeBounds = ScreenBounds(notice);
                    foreach (Text label in notice.GetComponentsInChildren<Text>())
                        if (label.preferredHeight > label.rectTransform.rect.height + 1)
                            failures.Add(size + " discovery text overflows: " + label.name);
                    legend.Show();
                    Invoke(manager, "LayoutFieldNotesFeedback");
                    Assert.That(notice.gameObject.activeSelf, Is.False);
                    legend.Hide();
                    Invoke(manager, "LayoutFieldNotesFeedback");
                    Assert.That(notice.gameObject.activeInHierarchy, Is.EqualTo(boardIndex == 0));
                    Assert.That(notes.DiscoveryFeedbackCount, Is.EqualTo(1));
                    foreach (Rect reserved in new[] { legend.ControlBounds, legend.PanelBounds, tools, bookBounds }
                        .Concat(boardIndex == 0 ? new[] { noticeBounds } : Array.Empty<Rect>()))
                        if (reserved.Overlaps(board) || reserved.Overlaps(group))
                            failures.Add(size + " control overlaps board/resources: " + reserved);
                    foreach (Text text in labels)
                    {
                        Rect frame = ScreenBounds(text.rectTransform);
                        resourceFrames.Add(frame);
                        Vector2 minimum = frame.min;
                        Vector2 maximum = frame.max;
                        if (minimum.x < 0 || maximum.x > size.x || minimum.y < 0 || maximum.y > size.y)
                            failures.Add(size + " resource bounds " + text.name + ": " + minimum + ".." + maximum);
                        if (text.preferredHeight > text.rectTransform.rect.height + 1)
                            failures.Add(size + " resource height " + text.name + ": " + text.preferredHeight + " > " + text.rectTransform.rect.height);
                        TextGenerationSettings settings = text.GetGenerationSettings(Vector2.zero);
                        foreach (string word in text.text.Split(' '))
                            if (text.cachedTextGeneratorForLayout.GetPreferredWidth(word, settings) / text.pixelsPerUnit > text.rectTransform.rect.width + 1)
                                failures.Add(size + " resource word does not fit: " + text.name + " " + word);
                        IList<UIVertex> vertices = text.cachedTextGenerator.verts;
                        if (vertices.Count <= 4)
                            failures.Add(size + " no resource glyphs: " + text.name);
                        foreach (UIVertex vertex in vertices.Take(Mathf.Max(0, vertices.Count - 4)))
                        {
                            Vector3 point = vertex.position / text.pixelsPerUnit;
                            Rect glyphFrame = text.rectTransform.rect;
                            if (point.x < glyphFrame.xMin - 1 || point.x > glyphFrame.xMax + 1 ||
                                point.y < glyphFrame.yMin - 1 || point.y > glyphFrame.yMax + 1)
                                failures.Add(size + " glyph outside resource frame: " + text.name);
                        }
                    }
                    ((IRunHud)player).Refresh(9, 9);
                    Canvas.ForceUpdateCanvases();
                    for (int index = 0; index < labels.Length; index++)
                        if (ScreenBounds(labels[index].rectTransform) != frames[index])
                            failures.Add(size + " resource frame moved with digit count");
                    ((IRunHud)player).Refresh(int.MaxValue, int.MaxValue);
                    Canvas.ForceUpdateCanvases();
                });
                try
                {
                    foreach (Rect frame in resourceFrames)
                    {
                        int left = Mathf.Clamp(Mathf.CeilToInt(frame.xMin), 0, size.x - 1);
                        int bottom = Mathf.Clamp(Mathf.CeilToInt(frame.yMin), 0, size.y - 1);
                        int width = Mathf.Min(Mathf.FloorToInt(frame.width), size.x - left);
                        int height = Mathf.Min(Mathf.FloorToInt(frame.height), size.y - bottom);
                        if (capture.GetPixels(left, bottom, width, height).Distinct().Count() < 3)
                            failures.Add(size + " resource raster is blank: " + frame);
                    }
                    File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M77-layout-after-board" + boardIndex + "-" + size.x + "x" + size.y + ".png"), capture.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(capture); }
            }
            if (boardIndex == 0)
            {
                yield return AdvanceOpeningToFinalSegment(manager);
                Assert.That((bool)Invoke(manager, "HandleBoardOutcome",
                    BoardOutcome.ExitReached(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"))), Is.True);
                Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-west-trail"), Is.True);
                yield return null;
                yield return null;
                Invoke(manager, "HideLevelImage");
                yield return null;
                player = FindSceneComponent(playerType, "Main");
            }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            GetField<EnemyIntentLegend>(manager, "activeIntentLegend").Show();
            Invoke(manager, "LayoutFieldNotesFeedback");
            Invoke(manager, "DisposeIntentUi");
            Assert.That(notes.Root.GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.name == "Committed discovery").gameObject.activeSelf, Is.False,
                "A later board must not resurrect a previous board's transient discovery.");
        }

        /// <summary>Combines the real opening, first route, safe later entry, overlays and pinned Continue in both presentation modes using isolated storage.</summary>
        [UnityTest]
        public IEnumerator IntegratedRepairOpeningOverlaysAndContinueStayCoherent()
        {
            foreach (bool animations in new[] { false, true })
            {
                DestroyGameManagerSingleton();
                var fixtureSession = new GameSession(new ProfileState("m78-repair-profile", "world-1", 1));
                var store = new FileSystemSaveStore(persistenceRoot);
                var lifecycle = new RunLifecycleService(fixtureSession, store);
                Assert.That(lifecycle.StartNewRun("m78-repair-" + animations, 904,
                    new RunStateConfiguration(100, 100, 0, "shelter.warm-up")).IsSuccess, Is.True);
                InvokeStatic(gameManagerType, "RequestContinue");
                yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
                yield return null;
                Component manager = (Component)GetStaticField(gameManagerType, "instance");
                AssertOpeningWarmUp(manager);
                Assert.That(Camera.main.cullingMask, Is.Zero);
                FieldNotesPresenter notes = GetProperty<FieldNotesPresenter>(manager, "FieldNotes");
                Assert.That(notes.Open(), Is.False);
                Invoke(manager, "HideLevelImage");
                yield return null;
                GameSession session = GetProperty<GameSession>(manager, "Session");
                BoardEventPresenter presenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
                presenter.AnimationsEnabled = animations;
                Component player = FindSceneComponent(playerType, "Main");
                session.RestoreHealth(7);
                Assert.That(GetField<Text>(player, "healthText").text, Does.EndWith(" +7"));
                BoardRuntime opening = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
                GridPosition origin = opening.BoardState.GetEntities().Single(entity => entity.Id.Equals(opening.PlayerId)).Position;
                Direction blocked = new[] { Direction.North, Direction.East, Direction.South, Direction.West }
                    .First(direction => !opening.BoardState.GetEntities().Any(entity =>
                        entity.Definition.Layer == BoardLayer.Terrain && entity.Definition.Traits.IsWalkable &&
                        entity.Position.Equals(new GridPosition(origin.X + direction.DeltaX, origin.Y + direction.DeltaY))));
                int food = session.ActiveRun.Food;
                int sounds = GetProperty<int>(player, "PresentedSoundCount");
                object[] rejectedArguments = { new MoveCommand(blocked), null };
                Assert.That((bool)Invoke(manager, "TrySubmitCommand", rejectedArguments), Is.True);
                var rejection = (Task<CommandSubmission>)rejectedArguments[1];
                while (!rejection.IsCompleted) yield return null;
                Assert.That(rejection.Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
                Assert.That(session.ActiveRun.Food, Is.EqualTo(food));
                Assert.That(GetProperty<int>(player, "PresentedSoundCount"), Is.EqualTo(sounds));
                Assert.That(GetField<Text>(player, "healthText").text, Does.EndWith(" +7"));
                yield return PresentExit(manager, false, animations);
                Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
                Assert.That(session.ActiveRun.CompletedDayCount, Is.EqualTo(1));
                Assert.That(Camera.main.cullingMask, Is.Zero);
                Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-west-trail"), Is.True);
                yield return null;
                yield return null;
                Assert.That(session.ActiveRun.CurrentDay, Is.EqualTo(2));
                Assert.That(GetField<GameObject>(manager, "levelImage").activeSelf, Is.True);
                Assert.That(Camera.main.cullingMask, Is.Zero);
                Invoke(manager, "HideLevelImage");
                yield return null;
                BoardRuntime later = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
                presenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
                presenter.AnimationsEnabled = animations;
                player = FindSceneComponent(playerType, "Main");
                Component adapter = GetField<Component>(manager, "boardScript");
                BoardBlueprint blueprint = GetProperty<BoardBlueprint>(adapter, "ActiveBlueprint");
                BoardEntrySafetyResult safety = BoardEntrySafety.Check(blueprint, OrdinaryBoardContent.CreateCatalog());
                Assert.That(safety.HasEscape, Is.True);
                RunState checkpoint = store.LoadRun().Data;
                BoardAddress address = later.Request.Address;
                int health = later.RunState.Health;
                foreach (PlayerCommand command in safety.EscapeCommands)
                {
                    int before = GetProperty<int>(player, "PresentedSoundCount");
                    object[] arguments = { command, null };
                    Assert.That((bool)Invoke(manager, "TrySubmitCommand", arguments), Is.True);
                    var operation = (Task<CommandSubmission>)arguments[1];
                    while (!operation.IsCompleted) yield return null;
                    Assert.That(operation.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                    Assert.That(later.RunState.Health, Is.EqualTo(health));
                    FoodRestoredEvent[] rewards = operation.Result.Result.Events.OfType<FoodRestoredEvent>().ToArray();
                    Assert.That(GetProperty<int>(player, "PresentedSoundCount") - before,
                        Is.EqualTo(rewards.Length + (animations && command is MoveCommand ? 1 : 0)));
                    if (rewards.Length > 0)
                        Assert.That(GetField<Text>(player, "foodText").text,
                            Does.EndWith(" +" + rewards.Sum(reward => reward.Amount)));
                }
                Invoke(manager, "ObservePresentedObservation", FactObservation.ShamblerTrappedPresented);
                foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(320, 480) })
                {
                    GetField<EnemyIntentLegend>(manager, "activeIntentLegend").Show();
                    Assert.That(notes.Open(), Is.True);
                    Texture2D capture = CaptureProductionCanvas(manager, size.x, size.y);
                    try
                    {
                        Assert.That(capture.GetPixels32().Distinct().Count(), Is.GreaterThan(2));
                        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M78-notes-" + animations + "-" + size.x + "x" + size.y + ".png"), capture.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(capture); }
                    notes.Close();
                    yield return null;
                    Assert.That(GetField<EnemyIntentLegend>(manager, "activeIntentLegend").IsVisible, Is.False);
                    Assert.That(GetField<Text>(player, "healthText").gameObject.activeInHierarchy, Is.True);
                    Assert.That(Camera.main.cullingMask, Is.Not.Zero);
                }
                DestroyGameManagerSingleton();
                InvokeStatic(gameManagerType, "RequestContinue");
                yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
                yield return null;
                manager = (Component)GetStaticField(gameManagerType, "instance");
                RunState continued = GetProperty<GameSession>(manager, "Session").ActiveRun;
                Assert.That(GetProperty<BoardRequest>(manager, "ActiveBoardRequest").Address, Is.EqualTo(address));
                Assert.That(continued.CurrentDay, Is.EqualTo(2));
                Assert.That(continued.Health, Is.EqualTo(checkpoint.Health));
                Assert.That(continued.Food, Is.EqualTo(checkpoint.Food));
                Assert.That(GetProperty<GameSession>(manager, "Session").Profile.DiscoveredFactIds,
                    Does.Contain("PIT_TRAPS_SHAMBLER"));
                Assert.That(GetField<GameObject>(manager, "levelImage").activeSelf, Is.False);
                Assert.That(Camera.main.cullingMask, Is.Not.Zero);
            }
        }

        private static Texture2D CaptureProductionCanvas(Component manager, int width, int height, Action inspectLayout = null)
        {
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Camera[] cameras = camera.transform.root.GetComponentsInChildren<Camera>()
                .Where(item => item.isActiveAndEnabled).OrderBy(item => item.depth).ToArray();
            Canvas[] overlays = SceneManager.GetActiveScene().GetRootGameObjects().Concat(new[] { manager.transform.root.gameObject })
                .SelectMany(root => root.GetComponentsInChildren<Canvas>())
                .Where(canvas => canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay).Distinct().ToArray();
            Camera[] previousCameras = overlays.Select(canvas => canvas.worldCamera).ToArray();
            float[] previousDistances = overlays.Select(canvas => canvas.planeDistance).ToArray();
            Transform[] graphics = overlays.SelectMany(canvas => canvas.GetComponentsInChildren<Transform>(true)).Distinct().ToArray();
            int[] previousLayers = graphics.Select(item => item.gameObject.layer).ToArray();
            RenderTexture[] previousTargets = cameras.Select(item => item.targetTexture).ToArray();
            Rect previousRect = camera.rect;
            float previousSize = camera.orthographicSize;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = new RenderTexture(width, height, 24);
            var uiOwner = new GameObject("Production capture UI camera");
            Camera uiCamera = uiOwner.AddComponent<Camera>();
            uiCamera.enabled = false;
            uiCamera.clearFlags = CameraClearFlags.Depth;
            uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
            uiCamera.targetTexture = target;
            target.Create();
            try
            {
                foreach (Camera item in cameras) item.targetTexture = target;
                var size = new Vector2(width, height);
                Invoke(manager, "ReserveIntentCameraMargin", size);
                GetProperty<ToolHudPresenter>(manager, "ActiveToolHud")?.Resize(size);
                GetProperty<FieldNotesPresenter>(manager, "FieldNotes")?.Resize(size);
                GetField<EnemyIntentLegend>(manager, "activeIntentLegend")?.Resize(size, (Rect)Invoke(manager, "LegendInspectedArea"));
                Invoke(manager, "LayoutFieldNotesFeedback");
                foreach (Transform item in graphics) item.gameObject.layer = LayerMask.NameToLayer("UI");
                foreach (Canvas canvas in overlays)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = uiCamera;
                    canvas.planeDistance = uiCamera.nearClipPlane + 1;
                }
                Canvas.ForceUpdateCanvases();
                foreach (MonoBehaviour atlas in manager.GetComponentsInChildren<MonoBehaviour>().Where(component =>
                    component.GetType().FullName == "HallowBlaze.Presentation.Atlas.AtlasScreenController"))
                    Invoke(atlas, "Resize", size);
                inspectLayout?.Invoke();
                RenderTexture.active = target;
                GL.Clear(true, true, Color.magenta);
                foreach (Camera item in cameras) item.Render();
                uiCamera.Render();
                RenderTexture.active = target;
                Texture2D capture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                capture.Apply();
                return capture;
            }
            finally
            {
                for (int index = 0; index < overlays.Length; index++)
                {
                    overlays[index].renderMode = RenderMode.ScreenSpaceOverlay;
                    overlays[index].worldCamera = previousCameras[index];
                    overlays[index].planeDistance = previousDistances[index];
                }
                for (int index = 0; index < graphics.Length; index++) graphics[index].gameObject.layer = previousLayers[index];
                for (int index = 0; index < cameras.Length; index++) cameras[index].targetTexture = previousTargets[index];
                camera.rect = previousRect;
                camera.orthographicSize = previousSize;
                var size = new Vector2(Screen.width, Screen.height);
                Invoke(manager, "ReserveIntentCameraMargin", size);
                GetProperty<ToolHudPresenter>(manager, "ActiveToolHud")?.Resize(size);
                GetProperty<FieldNotesPresenter>(manager, "FieldNotes")?.Resize(size);
                GetField<EnemyIntentLegend>(manager, "activeIntentLegend")?.Resize(size, (Rect)Invoke(manager, "LegendInspectedArea"));
                Invoke(manager, "LayoutFieldNotesFeedback");
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(uiOwner);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                Canvas.ForceUpdateCanvases();
            }
        }

        [UnityTest]
        public IEnumerator ContinueRestoresLastCommittedDayWithoutAdvancing()
        {
            AsyncOperation firstLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!firstLoad.isDone)
                yield return null;
            yield return null;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            GameSession firstSession = GetProperty<GameSession>(manager, "Session");
            RunState firstRun = firstSession.ActiveRun;
            AssertOpeningWarmUp(manager);
            BoardAddress openingAddress = GetProperty<BoardRequest>(manager, "ActiveBoardRequest").Address;
            Assert.That(firstRun.CurrentDay, Is.EqualTo(1));
            Assert.That(firstRun.CompletedDayCount, Is.Zero);
            firstSession.ConsumeFood(9);
            Assert.That((bool)Invoke(manager, "ExitToMenu"), Is.True);
            Assert.That(firstSession.ActiveRun, Is.Null);

            InvokeStatic(gameManagerType, "RequestContinue");
            AsyncOperation reload = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!reload.isDone)
                yield return null;
            yield return null;

            GameSession continuedSession = GetProperty<GameSession>(manager, "Session");
            Assert.That(continuedSession.ActiveRun.RunId, Is.EqualTo(firstRun.RunId));
            Assert.That(continuedSession.ActiveRun.CurrentDay, Is.EqualTo(1));
            Assert.That(continuedSession.ActiveRun.CompletedDayCount, Is.Zero);
            Assert.That(continuedSession.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(continuedSession.ActiveRun.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Consumed));
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime").Request.Address,
                Is.EqualTo(openingAddress));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);

            Scene cleanupScene = SceneManager.CreateScene("M1.7 Continue Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        /// <summary>Production menu startup, presented exits, and checkpoint continuation render the actual atlas without implicitly selecting a route.</summary>
        [UnityTest]
        public IEnumerator MenuOpeningExitsShowTheActualAtlas([Values(false, true)] bool animations)
        {
            yield return SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            GameObject menu = GameObject.Find("MainMenuPanel");
            Button start = menu.GetComponentsInChildren<Button>().Single(button => button.name == "StartBttn");
            start.Select();
            menu.GetComponent<KeyboardMenuFocus>().HandleKey(KeyCode.Return);
            yield return null;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main"));
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            AssertOpeningWarmUp(manager);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            RunState run = session.ActiveRun;
            for (int index = 0; index < 3; index++)
            {
                Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(index));
                yield return PresentSegmentExit(manager, animations);
                yield return null;
                yield return null;
                Component atlas = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                    .SingleOrDefault(component => component.GetType().FullName ==
                        "HallowBlaze.Presentation.Atlas.AtlasScreenController");
                Assert.That(atlas, Is.Not.Null);
                Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.EqualTo(index == 2));
                Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.EqualTo(index == 2));
            }
            Assert.That(run.ActiveLeg, Is.Null);
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.CurrentDay, Is.EqualTo(1));
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.start"));
            Component visibleAtlas = manager.GetComponentsInChildren<MonoBehaviour>().Single(component =>
                component.GetType().FullName == "HallowBlaze.Presentation.Atlas.AtlasScreenController");
            Assert.That(GetProperty<int>(visibleAtlas, "RouteButtonCount"), Is.EqualTo(2));
            Assert.That(visibleAtlas.GetComponent<CanvasGroup>().interactable &&
                visibleAtlas.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
            Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.transform.IsChildOf(visibleAtlas.transform), Is.True);
            yield return new WaitForEndOfFrame();
            Texture2D capture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Assert.That(capture.GetPixels32().Count(pixel =>
                    Math.Abs(pixel.r - 218) < 6 && Math.Abs(pixel.g - 166) < 6 && Math.Abs(pixel.b - 67) < 6),
                    Is.GreaterThan(100), "The selected route must be rendered on screen, not only enabled in an invisible canvas.");
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-atlas-opening-" + animations + ".png"), capture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(capture); }

            Assert.That((bool)Invoke(manager, "ExitToMenu"), Is.True);
            yield return SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 10;
            while ((SceneManager.GetActiveScene().name != "Menu" || GameObject.Find("ContinueBttn") == null) &&
                Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Menu"));
            Assert.That(GameObject.Find("ContinueBttn"), Is.Not.Null);
            Button continueButton = GameObject.Find("ContinueBttn").GetComponent<Button>();
            Assert.That(continueButton.IsInteractable(), Is.True);
            continueButton.Select();
            GameObject.Find("MainMenuPanel").GetComponent<KeyboardMenuFocus>().HandleKey(KeyCode.Return);
            deadline = Time.realtimeSinceStartup + 10;
            while (SceneManager.GetActiveScene().name != "Main" && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main"));
            manager = (Component)GetStaticField(gameManagerType, "instance");
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
            visibleAtlas = manager.GetComponentsInChildren<MonoBehaviour>().Single(component =>
                component.GetType().FullName == "HallowBlaze.Presentation.Atlas.AtlasScreenController");
            Assert.That(GetProperty<bool>(visibleAtlas, "IsOpen"), Is.True);
            Assert.That(GetProperty<int>(visibleAtlas, "RouteButtonCount"), Is.EqualTo(2));
            Assert.That(GetProperty<GameSession>(manager, "Session").ActiveRun.ActiveLeg, Is.Null);
        }

        /// <summary>Every opening entry restores its pinned board and supplies without replaying Day or offering an early choice.</summary>
        [UnityTest]
        public IEnumerator EveryOpeningBoundaryContinuesWithoutRerollRefillOrEarlyChoice()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            var store = new FileSystemSaveStore(persistenceRoot);
            int choicesPublished = 0;
            Action<IReadOnlyList<WorldMapExitOption>> countChoices = choices => { if (choices.Count > 0) choicesPublished++; };
            gameManagerType.GetEvent("OnRouteChoicesChanged").AddEventHandler(manager, countChoices);
            try
            {
                for (int index = 0; index < 3; index++)
                {
                    GameSession session = GetProperty<GameSession>(manager, "Session");
                    RunState checkpoint = store.LoadRun().Data;
                    BoardRequest request = GetProperty<BoardRequest>(manager, "ActiveBoardRequest");
                    Component adapter = GetField<Component>(manager, "boardScript");
                    string hash = GetProperty<BoardBlueprint>(adapter, "ActiveBlueprint").ContentHash;
                    Assert.That(session.ActiveRun.ActiveLeg.SegmentIndex, Is.EqualTo(index));
                    Assert.That(session.ActiveRun.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Consumed));
                    Assert.That(session.ActiveRun.CompletedDayCount, Is.Zero);
                    Assert.That(choicesPublished, Is.Zero);
                    session.ConsumeFood(3);
                    session.TakeDamage(2);
                    Assert.That((bool)Invoke(manager, "ExitToMenu"), Is.True);
                    InvokeStatic(gameManagerType, "RequestContinue");
                    yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
                    yield return null;
                    session = GetProperty<GameSession>(manager, "Session");
                    Assert.That(GetProperty<BoardRequest>(manager, "ActiveBoardRequest").Address, Is.EqualTo(request.Address));
                    Assert.That(GetProperty<BoardBlueprint>(GetField<Component>(manager, "boardScript"), "ActiveBlueprint").ContentHash, Is.EqualTo(hash));
                    Assert.That(session.ActiveRun.Food, Is.EqualTo(checkpoint.Food));
                    Assert.That(session.ActiveRun.Health, Is.EqualTo(checkpoint.Health));
                    Assert.That(session.ActiveRun.TraversalSequence, Is.EqualTo(1));
                    Assert.That(GetField<GameObject>(manager, "levelImage").activeSelf, Is.False, "Consumed Day must not replay.");
                    session.ConsumeFood(index + 1);
                    session.RestoreHealth(1);
                    int food = session.ActiveRun.Food;
                    int health = session.ActiveRun.Health;
                    BoardOutcome outcome = BoardOutcome.ExitReached(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"));
                    Assert.That((bool)Invoke(manager, "HandleBoardOutcome", outcome), Is.True);
                    yield return null;
                    yield return null;
                    Assert.That((bool)Invoke(manager, "HandleBoardOutcome", outcome), Is.False);
                    Assert.That(session.ActiveRun.Food, Is.EqualTo(food));
                    Assert.That(session.ActiveRun.Health, Is.EqualTo(health));
                    Assert.That(store.LoadRun().Data.Food, Is.EqualTo(food));
                    Assert.That(session.ActiveRun.CompletedDayCount, Is.EqualTo(index == 2 ? 1 : 0));
                    Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.EqualTo(index == 2));
                    Component atlas = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                        .SingleOrDefault(component => component.GetType().FullName ==
                            "HallowBlaze.Presentation.Atlas.AtlasScreenController");
                    Assert.That(atlas, Is.Not.Null, "The production scene must retain the atlas through every opening boundary.");
                    Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.EqualTo(index == 2),
                        "The atlas must actually become visible after the third opening board, not only enter route-choice state.");
                    if (index == 2)
                    {
                        CanvasGroup atlasGroup = atlas.GetComponent<CanvasGroup>();
                        Assert.That(atlasGroup.interactable && atlasGroup.blocksRaycasts && atlas.gameObject.activeInHierarchy, Is.True);
                        Assert.That(GetProperty<int>(atlas, "RouteButtonCount"), Is.EqualTo(2));
                    }
                }
                Assert.That(choicesPublished, Is.EqualTo(1));
                Assert.That(GetProperty<GameSession>(manager, "Session").ActiveRun.TravelHistory, Has.Count.EqualTo(1));
            }
            finally { gameManagerType.GetEvent("OnRouteChoicesChanged").RemoveEventHandler(manager, countChoices); }
        }

        /// <summary>The old one-board opening finishes once through the real scene and is never extended by new content.</summary>
        [UnityTest]
        public IEnumerator OldOneBoardOpeningContinuesAndFinishesWithoutExtraBoards()
        {
            var session = new GameSession(new ProfileState("old-opening-profile", "world-1", 1));
            session.Profile.DiscoverFact("old-opening-knowledge");
            RunState run = session.StartNewRun("old-one-board-opening", 133,
                new RunStateConfiguration(73, 61, 0, "shelter.warm-up"));
            run.StartLeg("road.shelter-warm-up", "shelter.warm-up", "forest.start",
                new[] { new RouteSegmentState("segment.shelter-warm-up.day1", "ordinary.board.v1", 3) });
            Assert.That(run.TryConsumeIntro(run.ActiveLeg.LegId), Is.True);
            BoardAddress address = BoardAddressFactory.CreateFromState(run);
            var store = new FileSystemSaveStore(persistenceRoot);
            Assert.That(store.SaveProfile(session.Profile).IsSuccess, Is.True);
            Assert.That(store.SaveRun(run).IsSuccess, Is.True);
            InvokeStatic(gameManagerType, "RequestContinue");
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            session = GetProperty<GameSession>(manager, "Session");
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveBoardRequest").Address, Is.EqualTo(address));
            Assert.That(session.ActiveRun.ActiveLeg.Segments, Has.Count.EqualTo(1));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(61));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(73));
            yield return PresentExit(manager);
            Assert.That(session.ActiveRun.CompletedDayCount, Is.EqualTo(1));
            Assert.That(session.ActiveRun.TravelHistory, Has.Count.EqualTo(1));
            Assert.That(session.ActiveRun.TravelHistory[0].CompletedSegmentCount, Is.EqualTo(1));
            Assert.That(session.Profile.DiscoveredFactIds, Does.Contain("old-opening-knowledge"));
            Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-west-trail"), Is.True);
            yield return null;
            yield return null;
            Assert.That(session.ActiveRun.CurrentDay, Is.EqualTo(2));
        }

        /// <summary>Failed opening checkpoints never authorize a route choice, a board, or a second day on retry.</summary>
        /// <summary>A continued route checkpoint retains atlas ownership despite the inactive terminal actions in Main.</summary>
        [UnityTest]
        public IEnumerator KeyboardContinueRouteChoiceCannotBeStolenByEmptyTerminalPanel()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            session.StartNewRun("keyboard-route-checkpoint", 48, new RunStateConfiguration(100, 100, 0, "forest.start"));
            var store = new FileSystemSaveStore(persistenceRoot);
            Assert.That(store.SaveRun(session.ActiveRun).IsSuccess, Is.True);
            Assert.That(store.SaveProfile(session.Profile).IsSuccess, Is.True);
            Assert.That((bool)Invoke(manager, "ExitToMenu"), Is.True);
            InvokeStatic(gameManagerType, "RequestContinue");
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
            GameObject selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected.name, Does.StartWith("AtlasRouteButton-"));
            KeyboardMenuFocus focus = selected.GetComponentInParent<KeyboardMenuFocus>();
            focus.HandleKey(KeyCode.RightArrow);
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
            focus.HandleKey(KeyCode.Return);
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Scene cleanup = SceneManager.CreateScene("Keyboard route cleanup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        [UnityTest]
        public IEnumerator OpeningCheckpointFailuresRetryWithoutSkippingOrRepeatingDay()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            AssertOpeningWarmUp(manager);
            Invoke(manager, "BlockBoardStartup");
            Invoke(manager, "StartNewRun");
            RunState run = GetProperty<GameSession>(manager, "Session").ActiveRun;
            string runPath = Path.Combine(persistenceRoot, "run.json");
            File.Delete(runPath);
            Directory.CreateDirectory(runPath);

            LogAssert.Expect(LogType.Error, "Opening warm-up checkpoint failed: IoError");
            Assert.That((bool)Invoke(manager, "TryStartOpeningWarmUp"), Is.False);
            Assert.That(run.ActiveLeg, Is.Null);
            Assert.That(run.CurrentDay, Is.Zero);
            Assert.That(run.TraversalSequence, Is.Zero);
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.False);
            Assert.That((bool)Invoke(manager, "ChooseRoute", "road.shelter-warm-up"), Is.False);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Null);

            Directory.Delete(runPath);
            Assert.That((bool)Invoke(manager, "RetryTravelCheckpoint"), Is.True);
            AssertOpeningWarmUp(manager);
            Assert.That(run.TraversalSequence, Is.EqualTo(1));
            Assert.That((bool)Invoke(manager, "RetryTravelCheckpoint"), Is.False);

            Invoke(manager, "BlockBoardStartup");
            Invoke(manager, "StartNewRun");
            run = GetProperty<GameSession>(manager, "Session").ActiveRun;
            Assert.That((bool)Invoke(manager, "TryStartOpeningWarmUp"), Is.True);
            BoardAddress pendingAddress = BoardAddressFactory.CreateFromState(run);
            File.Delete(runPath);
            Directory.CreateDirectory(runPath);
            LogAssert.Expect(LogType.Error, "Day intro checkpoint failed: IoError");
            Invoke(manager, "InitGame");
            Assert.That(run.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Pending));
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Null);
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);

            Directory.Delete(runPath);
            Assert.That((bool)Invoke(manager, "RetryTravelCheckpoint"), Is.True);
            AssertOpeningWarmUp(manager);
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveBoardRequest").Address, Is.EqualTo(pendingAddress));
            Assert.That(run.TraversalSequence, Is.EqualTo(1));

            yield return AdvanceOpeningToFinalSegment(manager);
            BoardOutcome completion = BoardOutcome.ExitReached(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"));
            File.Delete(runPath);
            Directory.CreateDirectory(runPath);
            LogAssert.Expect(LogType.Error, "Segment checkpoint failed: IoError");
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome", completion), Is.False);
            Assert.That(run.ActiveLeg, Is.Not.Null);
            Assert.That(run.CompletedDayCount, Is.Zero);
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);

            Directory.Delete(runPath);
            Assert.That((bool)Invoke(manager, "RetryTravelCheckpoint"), Is.True);
            Assert.That(run.ActiveLeg, Is.Null);
            Assert.That(run.CurrentDay, Is.EqualTo(1));
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.TravelHistory, Has.Count.EqualTo(1));
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome", completion), Is.False);
            Assert.That((bool)Invoke(manager, "RetryTravelCheckpoint"), Is.False);
            Assert.That(new FileSystemSaveStore(persistenceRoot).LoadRun().Data.CompletedDayCount, Is.EqualTo(1));
        }

        /// <summary>Retrying exit discovery preserves the completed opening and opens the first choice exactly once.</summary>
        [UnityTest]
        public IEnumerator OpeningExitDiscoveryFailureRetriesWithoutRepeatingArrival()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            AssertOpeningWarmUp(manager);
            yield return AdvanceOpeningToFinalSegment(manager);
            RunState run = GetProperty<GameSession>(manager, "Session").ActiveRun;
            WorldMapService map = GetField<WorldMapService>(manager, "worldMap");
            string profilePath = Path.Combine(persistenceRoot, "profile.json");
            Action<WorldDiscoveryEvent> blockExitDiscovery = discovery =>
            {
                if (discovery.Operation != WorldDiscoveryOperation.TraversedEdge)
                    return;
                File.Delete(profilePath);
                Directory.CreateDirectory(profilePath);
            };
            map.OnDiscovery += blockExitDiscovery;
            BoardOutcome completion = BoardOutcome.ExitReached(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("^Route choice could not begin:"));
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome", completion), Is.False);
            map.OnDiscovery -= blockExitDiscovery;
            Assert.That(Directory.Exists(profilePath), Is.True);
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.TravelHistory, Has.Count.EqualTo(1));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);

            Directory.Delete(profilePath);
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
            Assert.That(GetProperty<IReadOnlyList<WorldMapExitOption>>(manager, "RouteChoices"), Has.Count.EqualTo(2));
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.TravelHistory, Has.Count.EqualTo(1));
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome", completion), Is.False);
            Assert.That((bool)Invoke(manager, "RetryTravelCheckpoint"), Is.True);
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.TravelHistory, Has.Count.EqualTo(1));
            Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-east-creek"), Is.True);
            yield return null;
            yield return null;
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(run.TraversalSequence, Is.EqualTo(2));
            Assert.That(run.ActiveLeg.EdgeId, Is.EqualTo("road.start-east-creek"));
        }

        /// <summary>A scene reload republishes the first committed choice without replaying the opening.</summary>
        [UnityTest]
        public IEnumerator OpeningCompletedChoiceSurvivesSceneReload()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            AssertOpeningWarmUp(manager);
            yield return AdvanceOpeningToFinalSegment(manager);
            RunState run = GetProperty<GameSession>(manager, "Session").ActiveRun;
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome",
                BoardOutcome.ExitReached(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"))), Is.True);
            Assert.That(GetProperty<IReadOnlyList<WorldMapExitOption>>(manager, "RouteChoices"), Has.Count.EqualTo(2));

            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
            Assert.That(GetProperty<IReadOnlyList<WorldMapExitOption>>(manager, "RouteChoices"), Has.Count.EqualTo(2));
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.TravelHistory, Has.Count.EqualTo(1));
            Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-west-trail"), Is.True);
            yield return null;
            yield return null;
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(run.TraversalSequence, Is.EqualTo(2));
        }

        /// <summary>A scene reload retries the exact first selection after its run checkpoint failed.</summary>
        [UnityTest]
        public IEnumerator OpeningSelectedRouteSaveFailureRecoversOnSceneReload()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            AssertOpeningWarmUp(manager);
            yield return AdvanceOpeningToFinalSegment(manager);
            RunState run = GetProperty<GameSession>(manager, "Session").ActiveRun;
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome",
                BoardOutcome.ExitReached(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"))), Is.True);
            string runPath = Path.Combine(persistenceRoot, "run.json");
            File.Delete(runPath);
            Directory.CreateDirectory(runPath);
            LogAssert.Expect(LogType.Error, "Route choice save failed for Run: IoError");
            Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-west-trail"), Is.False);
            Assert.That(run.ActiveLeg, Is.Null);
            Assert.That(run.TraversalSequence, Is.EqualTo(1));
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);

            Directory.Delete(runPath);
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            Assert.That(run.ActiveLeg.EdgeId, Is.EqualTo("road.start-west-trail"));
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(run.TraversalSequence, Is.EqualTo(2));
            Assert.That(run.TravelHistory, Has.Count.EqualTo(1));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Not.Null);
            Assert.That(new FileSystemSaveStore(persistenceRoot).LoadRun().Data.ActiveLeg.EdgeId,
                Is.EqualTo("road.start-west-trail"));
        }

        /// <summary>A real two-enemy day-four entry retains its initial plans and executes the bounded no-hit witness without presentation drift.</summary>
        [UnityTest]
        public IEnumerator EntrySafetyWitnessExecutesOnProductionDayFourBoard()
        {
            var fixtureSession = new GameSession(new ProfileState("entry-safety-profile", "world-1", 1));
            var fixtureLifecycle = new RunLifecycleService(fixtureSession, new FileSystemSaveStore(persistenceRoot));
            Assert.That(fixtureLifecycle.StartNewRun("entry-safety-day-four", 904,
                new RunStateConfiguration(1, 5, 3, "forest.start")).IsSuccess, Is.True);
            InvokeStatic(gameManagerType, "RequestContinue");
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-west-trail"), Is.True);
            yield return null;
            yield return null;
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            Component adapter = manager.GetComponent(RequireType(gameManagerType.Assembly, "BoardManager"));
            BoardBlueprint blueprint = GetProperty<BoardBlueprint>(adapter, "ActiveBlueprint");
            Assert.That(runtime.Request.Address.GenerationVersion, Is.EqualTo(BoardGenerationPipeline.OrdinaryConfigurationVersion));
            Assert.That(runtime.Enemies, Has.Count.EqualTo(2));
            BoardEntrySafetyResult audit = BoardEntrySafety.Check(blueprint, OrdinaryBoardContent.CreateCatalog());
            Assert.That(audit.HasEscape, Is.True, runtime.Request.Address.ToString());
            Invoke(manager, "HideLevelImage");
            yield return null;
            void AssertRetainedIntentViews()
            {
                EnemyIntentPresenter intents = GetField<EnemyIntentPresenter>(manager, "activeIntents");
                foreach (var enemy in runtime.Enemies)
                {
                    Assert.That(runtime.BoardState.TryGetEntity(enemy.ActorId, out BoardEntityState actor), Is.True);
                    EnemyIntentProjection expected = EnemyIntentProjection.FromRetainedIntent(enemy.LockedIntent, actor.Position);
                    Assert.That(intents.Views[enemy.ActorId].Projection, Is.EqualTo(expected));
                }
            }
            AssertRetainedIntentViews();
            foreach (PlayerCommand command in audit.EscapeCommands)
            {
                Assert.That((bool)Invoke(manager, "TrySubmitCommand", new object[] { command, null }), Is.True);
                BoardEventPresenter presenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
                while (presenter.Coordinator.Gate.State != PresentationGateState.Idle) yield return null;
                Assert.That(runtime.RunState.Health, Is.EqualTo(1));
                AssertRetainedIntentViews();
                foreach (BoardEntityState actor in runtime.BoardState.GetEntities())
                    if (actor.Definition.Layer == BoardLayer.Actor)
                        Assert.That(runtime.Views[actor.Id].transform.position,
                            Is.EqualTo(new Vector3(actor.Position.X, actor.Position.Y, runtime.Views[actor.Id].transform.position.z)));
            }
            Assert.That(runtime.BoardState.TryGetEntity(runtime.PlayerId, out BoardEntityState player), Is.True);
            Assert.That(player.Position.ManhattanDistance(blueprint.Start), Is.GreaterThanOrEqualTo(3));
            Assert.That(runtime.RunState.Food, Is.GreaterThan(0));
        }

        /// <summary>Dying in the opening records no completed day and a fresh restart retains profile knowledge.</summary>
        [UnityTest]
        public IEnumerator OpeningDeathNeverOffersFirstChoiceAndRestartRetainsKnowledge()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = (Component)GetStaticField(gameManagerType, "instance");
            AssertOpeningWarmUp(manager);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            profile.DiscoverFact("known-before-opening-death");
            RunState deadRun = session.ActiveRun;
            BoardRequest oldRequest = GetProperty<BoardRequest>(manager, "ActiveBoardRequest");
            yield return PresentHealthDeath(manager);
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(deadRun.CompletedDayCount, Is.Zero);
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-east-creek"), Is.False);
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome", BoardOutcome.ExitReached(oldRequest)), Is.False);

            Invoke(manager, "RestartGame");
            yield return null;
            yield return null;
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(profile.DiscoveredFactIds, Does.Contain("known-before-opening-death"));
            Assert.That(session.ActiveRun, Is.Not.SameAs(deadRun));
            AssertOpeningWarmUp(manager);
        }

        /// <summary>Verifies real scene travel and save boundaries on fixed-identity, combat-free traversal fixtures.</summary>
        [UnityTest]
        public IEnumerator ExplicitRouteChoicesAdvanceAcrossAContinuedSession()
        {
            var fixtureSession = new GameSession(new ProfileState("route-lifecycle-profile", "world-1", 1));
            var fixtureLifecycle = new RunLifecycleService(fixtureSession, new FileSystemSaveStore(persistenceRoot));
            Assert.That(fixtureLifecycle.StartNewRun("route-lifecycle-run", 12346,
                new RunStateConfiguration(100, 100, 0, "forest.start")).IsSuccess, Is.True);
            var tools = ToolContentCatalog.Load();
            ToolInstanceState axe = fixtureSession.ActiveRun.CreateToolInstance(tools["axe.basic"]);
            ToolInstanceState shovel = fixtureSession.ActiveRun.CreateToolInstance(tools["shovel.basic"]);
            fixtureSession.ActiveRun.EquipTool(0, axe);
            fixtureSession.ActiveRun.EquipTool(1, shovel);
            Assert.That(fixtureSession.ActiveRun.TrySpendToolCharge(0, axe.InstanceId), Is.True);
            Assert.That(fixtureLifecycle.SaveBoardBoundary().IsSuccess, Is.True);
            InvokeStatic(gameManagerType, "RequestContinue");
            AsyncOperation firstLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!firstLoad.isDone)
                yield return null;
            yield return null;

            Component firstManager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(firstManager, Is.Not.Null);
            Assert.That(GetField<TextAsset>(firstManager, "worldDefinitionJson"), Is.Not.Null);
            GameSession firstSession = GetProperty<GameSession>(firstManager, "Session");
            RunState firstRun = firstSession.ActiveRun;
            Assert.That(firstRun.RunId, Is.EqualTo("route-lifecycle-run"));
            Assert.That(firstRun.RunSeed, Is.EqualTo(12346));
            Assert.That(firstSession.Profile.GetNodeDiscoveryState("forest.start"),
                Is.EqualTo(NodeDiscoveryState.Visited));
            Component firstPlayer = FindSceneComponent(playerType, "Main");
            Assert.That(firstPlayer, Is.Not.Null);
            yield return PresentExit(firstManager, useTraversalFixture: true);

            IReadOnlyList<WorldMapExitOption> firstChoices =
                GetProperty<IReadOnlyList<WorldMapExitOption>>(firstManager, "RouteChoices");
            Assert.That(GetProperty<bool>(firstManager, "IsRouteChoiceActive"), Is.True);
            Assert.That(firstChoices.Select(choice => choice.EdgeId), Is.EqualTo(new[]
            {
                "road.start-east-creek",
                "road.start-west-trail"
            }));
            Assert.That(firstRun.CurrentDay, Is.Zero);
            Assert.That(firstRun.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(firstRun.Route, Is.Empty);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main"));
            Assert.That(GetProperty<bool>(firstManager, "IsGameplayInputEnabled"), Is.False);

            Assert.That(
                (bool)Invoke(firstManager, "ChooseRoute", "road.start-west-trail"),
                Is.True);
            yield return null;
            yield return null;

            Assert.That(firstRun.CurrentDay, Is.EqualTo(1));
            Assert.That(firstRun.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(firstRun.Route, Is.Empty);
            Assert.That(firstRun.ActiveLeg.ToNodeId, Is.EqualTo("forest.west-trail"));
            int firstSegmentCount = firstRun.ActiveLeg.Segments.Count;
            BoardAddress continuedAddress = BoardAddressFactory.CreateFromState(firstRun);
            ToolHudPresenter firstToolHud = GetProperty<ToolHudPresenter>(firstManager, "ActiveToolHud");
            Assert.That(firstToolHud.GetSlotText(0), Is.EqualTo("Axe  2"));
            Assert.That(firstToolHud.GetSlotText(1), Is.EqualTo("Shovel  3"));
            Assert.That(firstSession.Profile.GetNodeDiscoveryState("forest.west-trail"),
                Is.EqualTo(NodeDiscoveryState.Sighted));
            Assert.That(GetProperty<bool>(firstManager, "IsRouteChoiceActive"), Is.False);
            Assert.That(GetProperty<IReadOnlyList<WorldMapExitOption>>(
                firstManager,
                "RouteChoices"), Is.Empty);

            Assert.That((bool)Invoke(firstManager, "ExitToMenu"), Is.True);
            DestroyGameManagerSingleton();
            Assert.That(firstToolHud.IsDisposed, Is.True);
            InvokeStatic(gameManagerType, "RequestContinue");

            AsyncOperation continuedLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!continuedLoad.isDone)
                yield return null;
            yield return null;

            Component continuedManager = GetStaticField(gameManagerType, "instance") as Component;
            GameSession continuedSession = GetProperty<GameSession>(continuedManager, "Session");
            RunState continuedRun = continuedSession.ActiveRun;
            Assert.That(continuedRun.RunId, Is.EqualTo(firstRun.RunId));
            Assert.That(continuedRun.CurrentDay, Is.EqualTo(1));
            Assert.That(continuedRun.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(continuedRun.Route, Is.Empty);
            Assert.That(continuedRun.ActiveLeg.SegmentIndex, Is.Zero);
            Assert.That(GetProperty<BoardRuntime>(continuedManager, "ActiveBoardRuntime").Request.Address,
                Is.EqualTo(continuedAddress));
            ToolHudPresenter continuedToolHud = GetProperty<ToolHudPresenter>(continuedManager, "ActiveToolHud");
            Assert.That(continuedToolHud, Is.Not.SameAs(firstToolHud));
            Assert.That(continuedToolHud.GetSlotText(0), Is.EqualTo("Axe  2"));
            Assert.That(continuedToolHud.GetSlotText(1), Is.EqualTo("Shovel  3"));
            Assert.That(continuedRun.ToolSlots[0].Instance.InstanceId, Is.EqualTo(axe.InstanceId));
            Assert.That(continuedRun.ToolSlots[1].Instance.InstanceId, Is.EqualTo(shovel.InstanceId));
            Assert.That(continuedSession.Profile.GetNodeDiscoveryState("forest.west-trail"),
                Is.EqualTo(NodeDiscoveryState.Sighted));

            Component continuedPlayer = FindSceneComponent(playerType, "Main");
            Assert.That(continuedPlayer, Is.Not.Null);
            yield return PresentExit(continuedManager, useTraversalFixture: true);

            IReadOnlyList<WorldMapExitOption> secondChoices =
                GetProperty<IReadOnlyList<WorldMapExitOption>>(continuedManager, "RouteChoices");
            Assert.That(secondChoices.Select(choice => choice.EdgeId),
                Is.EqualTo(new[] { "road.west-trail-old-road" }));
            Assert.That(
                (bool)Invoke(continuedManager, "ChooseRoute", "road.west-trail-old-road"),
                Is.True);
            yield return null;
            yield return null;

            Assert.That(continuedRun.WorldNodeId, Is.EqualTo("forest.west-trail"));
            Assert.That(continuedRun.ActiveLeg.DayNumber, Is.EqualTo(2));
            int secondSegmentCount = continuedRun.ActiveLeg.Segments.Count;
            yield return PresentExit(continuedManager, useTraversalFixture: true);
            Assert.That(continuedRun.CurrentDay, Is.EqualTo(2));
            Assert.That(continuedRun.WorldNodeId, Is.EqualTo("forest.old-road"));
            Assert.That(continuedRun.Route, Is.EqualTo(new[]
            {
                "forest.west-trail",
                "forest.old-road"
            }));
            Assert.That(continuedRun.CompletedDayCount, Is.EqualTo(2));
            Assert.That(continuedRun.Health, Is.EqualTo(100));
            Assert.That(continuedRun.Food, Is.EqualTo(100 - 4 * (firstSegmentCount + secondSegmentCount)),
                "Every fixture segment must execute four accepted moves through the production controller.");
            Assert.That(continuedSession.Profile.GetNodeDiscoveryState("forest.old-road"),
                Is.EqualTo(NodeDiscoveryState.Visited));
            SaveStoreResult<RunState> checkpoint =
                new FileSystemSaveStore(persistenceRoot).LoadRun();
            Assert.That(checkpoint.IsSuccess, Is.True);
            Assert.That(checkpoint.Data.CurrentDay, Is.EqualTo(2));
            Assert.That(checkpoint.Data.WorldNodeId, Is.EqualTo("forest.old-road"));
            Assert.That(checkpoint.Data.Route, Is.EqualTo(continuedRun.Route));
            Assert.That(checkpoint.Data.Food, Is.EqualTo(continuedRun.Food));
            Assert.That(checkpoint.Data.ToolSlots[0].Instance.InstanceId, Is.EqualTo(axe.InstanceId));
            Assert.That(checkpoint.Data.ToolSlots[0].Instance.Charges, Is.EqualTo(2));

            Scene cleanupScene = SceneManager.CreateScene(
                "M2.5 Route Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        [UnityTest]
        public IEnumerator NewGameButtonStartsWithFreshAtlasHistory()
        {
            AsyncOperation firstLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!firstLoad.isDone)
                yield return null;
            yield return null;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(manager, Is.Not.Null);
            AssertOpeningWarmUp(manager);
            GameSession firstSession = GetProperty<GameSession>(manager, "Session");
            Assert.That(firstSession.Profile.GetNodeDiscoveryState("shelter.warm-up"),
                Is.EqualTo(NodeDiscoveryState.Visited));
            Assert.That(firstSession.Profile.GetNodeDiscoveryState("forest.start"),
                Is.EqualTo(NodeDiscoveryState.Unknown));

            Component player = FindSceneComponent(playerType, "Main");
            yield return PresentExit(manager, useTraversalFixture: true);
            Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-west-trail"), Is.True);
            yield return null;
            yield return null;
            yield return PresentExit(manager, useTraversalFixture: true);

            Assert.That(firstSession.Profile.GetNodeDiscoveryState("forest.west-trail"),
                Is.EqualTo(NodeDiscoveryState.Visited));
            Assert.That(firstSession.Profile.GetEdgeDiscoveryState("road.start-west-trail"),
                Is.EqualTo(EdgeDiscoveryState.Traversed));

            Assert.That((bool)Invoke(manager, "ExitToMenu"), Is.True);
            AsyncOperation menuLoad = SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);
            while (!menuLoad.isDone)
                yield return null;
            yield return null;

                Component newGameHandler = UnityEngine.Object.FindObjectsByType(
                    startButtonType,
                    FindObjectsSortMode.None)
                .Cast<Component>()
                .Single(button => !GetField<bool>(button, "continueButton"));
                Button newGameButton = newGameHandler.GetComponent<Button>();
                Assert.That(newGameButton, Is.Not.Null);
                newGameButton.onClick.Invoke();
            while (SceneManager.GetActiveScene().name != "Main")
                yield return null;
            yield return null;

            GameSession newGameSession = GetProperty<GameSession>(manager, "Session");
            Assert.That(newGameSession.Profile, Is.Not.SameAs(firstSession.Profile));
            Assert.That(newGameSession.Profile.GetNodeDiscoveryState("shelter.warm-up"),
                Is.EqualTo(NodeDiscoveryState.Visited));
            Assert.That(newGameSession.Profile.GetNodeDiscoveryState("forest.start"),
                Is.EqualTo(NodeDiscoveryState.Unknown));
            Assert.That(newGameSession.Profile.GetNodeDiscoveryState("forest.old-road"),
                Is.EqualTo(NodeDiscoveryState.Unknown));
            Assert.That(newGameSession.Profile.GetEdgeDiscoveryState("road.west-trail-old-road"),
                Is.EqualTo(EdgeDiscoveryState.Unknown));

            Component newGamePlayer = FindSceneComponent(playerType, "Main");
            yield return PresentExit(manager, useTraversalFixture: true);

            Assert.That(newGameSession.Profile.GetNodeDiscoveryState("forest.start"),
                Is.EqualTo(NodeDiscoveryState.Visited));
            Assert.That(newGameSession.Profile.GetNodeDiscoveryState("forest.west-trail"),
                Is.EqualTo(NodeDiscoveryState.Sighted));
            Assert.That(newGameSession.Profile.GetEdgeDiscoveryState("road.start-west-trail"),
                Is.EqualTo(EdgeDiscoveryState.Sighted));
            Assert.That(newGameSession.Profile.GetNodeDiscoveryState("forest.old-road"),
                Is.EqualTo(NodeDiscoveryState.Unknown));
            Assert.That(newGameSession.Profile.GetEdgeDiscoveryState("road.west-trail-old-road"),
                Is.EqualTo(EdgeDiscoveryState.Unknown));
        }

        [UnityTest]
        public IEnumerator BoardRequestRejectsStaleAndDuplicateExitOutcomes()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(manager, Is.Not.Null);
            yield return StartFirstRoute(manager);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            RunState run = session.ActiveRun;
            BoardRequest request = GetProperty<BoardRequest>(manager, "ActiveBoardRequest");
            Assert.That(request, Is.Not.Null);
            Assert.That(request.RunId, Is.EqualTo(run.RunId));
            Assert.That(request.WorldNodeId, Is.EqualTo(run.WorldNodeId));
            Assert.That(request.CurrentDay, Is.EqualTo(run.CurrentDay));
            Assert.That(request.Address, Is.EqualTo(BoardAddressFactory.CreateFromState(run)));

            BoardRequest staleRequest = new BoardRequest(
                new BoardAddress(request.RunId, request.Address.LegId, request.Address.EdgeId,
                    request.Address.SegmentId, request.CurrentDay, request.Address.GenerationConfigurationId,
                    request.Address.GenerationVersion, unchecked(request.BoardSeed + 1)),
                request.RunSeed,
                request.WorldNodeId,
                request.PlaceKind,
                request.BiomeFamily,
                request.LegacyDifficultyLevel);
            Assert.That(
                (bool)Invoke(manager, "HandleBoardOutcome", BoardOutcome.ExitReached(staleRequest)),
                Is.False);
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(run.CurrentDay, Is.EqualTo(2));

            BoardOutcome exit = BoardOutcome.ExitReached(request);
            int loads = 0;
            SetField(manager, "sceneLoader", new Action<string>(_ => loads++));
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome", exit), Is.True);
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(1));
            Assert.That((bool)Invoke(manager, "HandleBoardOutcome", exit), Is.False);
            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(loads, Is.EqualTo(1));
            Invoke(manager, "HideLevelImage");
            Invoke(manager, "SetGameplayInputBlocked", false);
            yield return null;
            object[] blocked = { new WaitCommand(), null };
            Assert.That((bool)Invoke(FindSceneComponent(playerType, "Main"), "TrySubmitCommand", blocked), Is.False);
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").Coordinator.ResolutionCount, Is.Zero);

            Scene cleanupScene = SceneManager.CreateScene(
                "M2.7 Exit Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        [UnityTest]
        public IEnumerator BoardStartupFailureKeepsGameplayBlockedAndClearsRequest()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(manager, Is.Not.Null);
            yield return StartFirstRoute(manager);
            Component boardManager = GetField<Component>(manager, "boardScript");
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"), Is.Not.Null);
            Assert.That(GetProperty<BoardRequest>(boardManager, "ActiveRequest"), Is.Not.Null);

            SetField(manager, "worldMap", null);
            SetField(manager, "worldDefinition", null);
            SetField(manager, "worldDefinitionJson", null);
            GetProperty<GameSession>(manager, "Session").AbandonRun();
            InvokeStatic(gameManagerType, "RequestNewRun");

            LogAssert.Expect(LogType.Error, "World definition JSON is not assigned on GameManager.");
            AsyncOperation reload = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!reload.isDone)
                yield return null;
            yield return null;

            Assert.That(GetStaticField(gameManagerType, "instance"), Is.SameAs(manager));
            Assert.That(GetProperty<GameSession>(manager, "Session").ActiveRun, Is.Not.Null);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputBlocked"), Is.True);
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"), Is.Null);
            Assert.That(GetProperty<BoardRequest>(boardManager, "ActiveRequest"), Is.Null);

            Scene cleanupScene = SceneManager.CreateScene(
                "M2.7 Startup Failure Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        [UnityTest]
        public IEnumerator ReloadDuringAnimationCancelsOldReplayAndRejectsSameIdentityStaleOutcome()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            yield return StartFirstRoute(manager);
            BoardRuntime oldRuntime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            BoardRequest oldRequest = oldRuntime.Request;
            BoardEventPresenter oldPresenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            Component oldPlayer = oldRuntime.Views[oldRuntime.PlayerId].GetComponent(playerType);
            Invoke(manager, "HideLevelImage");
            yield return null;
            Direction firstStep = FindExitPath(oldRuntime).First();
            object[] arguments = { new MoveCommand(firstStep), null };
            Assert.That((bool)Invoke(oldPlayer, "TrySubmitCommand", arguments), Is.True);
            var pending = (Task<CommandSubmission>)arguments[1];
            Assert.That(pending.IsCompleted, Is.False);
            int resolvedFood = oldRuntime.RunState.Food;
            Invoke(manager, "BlockBoardStartup");
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!pending.IsCompleted)
                yield return null;
            Assert.That(pending.Result.Diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.Canceled));
            Assert.That(oldRuntime.IsDisposed, Is.True);
            Assert.That(oldPresenter.Coordinator.CanSubmit, Is.False);
            BoardRuntime replacement = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(oldRequest.HasSameIdentity(replacement.Request), Is.True);
            Assert.That(replacement.RunState.Food, Is.EqualTo(resolvedFood));
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").Coordinator.ResolutionCount, Is.Zero);
            Assert.That(((IBoardOutcomeSink)manager).TryNotify(oldRequest, new PlayerDiedEvent(replacement.PlayerId)), Is.False);
            Assert.That(replacement.RunState.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
        }

        [UnityTest]
        public IEnumerator StarvationOutcomeClosesRunWithoutRouteChoice()
        {
            yield return AssertDeathOutcome(
                DeathReason.Starvation,
                "starved",
                "M2.7 Starvation Cleanup");
        }

        [UnityTest]
        public IEnumerator HealthOutcomeClosesRunWithoutRouteChoice()
        {
            yield return AssertDeathOutcome(
                DeathReason.HealthDepleted,
                "brain has been eaten",
                "M2.7 Health Cleanup");
        }

        [UnityTest]
        public IEnumerator DeadRunReturnsToMenuOnceWithoutStartingRun()
        {
            Component manager = CreateManager("M1.11 Game Over Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            string runId = session.ActiveRun.RunId;
            int sceneLoadCount = 0;
            string requestedScene = null;
            SetField(manager, "sceneLoader", (Action<string>)(sceneName =>
            {
                sceneLoadCount++;
                requestedScene = sceneName;
            }));

            Invoke(manager, "GameOver", false);
            Time.timeScale = 0f;
            Invoke(manager, "SetGameplayInputBlocked", true);

            Invoke(manager, "ReturnToMenuAfterGameOver");
            Invoke(manager, "ReturnToMenuAfterGameOver");

            Assert.That(sceneLoadCount, Is.EqualTo(1));
            Assert.That(requestedScene, Is.EqualTo("Menu"));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(profile.RunSummaries.Count, Is.EqualTo(1));
            Assert.That(profile.RunSummaries[0].RunId, Is.EqualTo(runId));
            Assert.That(profile.RunSummaries[0].Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.json")), Is.False);

            yield return null;
            Assert.That(GetStaticField(gameManagerType, "instance"), Is.SameAs(manager));
            Assert.That(((Behaviour)manager).enabled, Is.True);
        }

        [Test]
        public void GameOverMenuIsBlockedWhenTerminalizationFails()
        {
            Component manager = CreateManager("M1.11 Failed Game Over Manager");
            Invoke(manager, "EnsurePersistence");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            int sceneLoadCount = 0;
            SetField(manager, "sceneLoader", (Action<string>)(_ => sceneLoadCount++));
            string profilePath = Path.Combine(persistenceRoot, "profile.json");
            File.Delete(profilePath);
            Directory.CreateDirectory(profilePath);

            LogAssert.Expect(LogType.Error, "Run completion save failed: IoError");
            Invoke(manager, "GameOver", false);
            Invoke(manager, "ReturnToMenuAfterGameOver");

            Assert.That(sceneLoadCount, Is.Zero);
            Assert.That(session.ActiveRun, Is.Not.Null);
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.json")), Is.True);
        }

        [UnityTest]
        public IEnumerator DeadRunRestartsOnceWithFreshRun()
        {
            Component manager = CreateManager("M1.11 Restart Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            RunState completedRun = session.ActiveRun;
            int sceneLoadCount = 0;
            SetField(manager, "sceneLoader", (Action<string>)(_ => sceneLoadCount++));

            Invoke(manager, "GameOver", false);
            Time.timeScale = 0f;
            Invoke(manager, "SetGameplayInputBlocked", true);

            Invoke(manager, "RestartGame");
            Invoke(manager, "RestartGame");

            Assert.That(sceneLoadCount, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(profile.RunSummaries.Count, Is.EqualTo(1));
            Assert.That(session.ActiveRun, Is.Not.SameAs(completedRun));
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(session.ActiveRun.CurrentDay, Is.Zero);
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(100));

            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledAndRecreatedPlayerRendersSessionWithoutOwningResources()
        {
            Component manager = CreateManager("M1.4 Player Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            session.ConsumeFood(27);
            session.TakeDamage(18);

            Component firstPlayer = CreatePlayer("M1.4 First Player", out Text firstFood, out Text firstHealth);
            Assert.That(firstFood.text, Is.EqualTo("Food: 73"));
            Assert.That(firstHealth.text, Is.EqualTo("Health: 82"));

            ((Behaviour)firstPlayer).enabled = false;
            Assert.That(session.ActiveRun.Food, Is.EqualTo(73));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(82));

            session.ConsumeFood(3);
            session.TakeDamage(2);
            ((Behaviour)firstPlayer).enabled = true;
            Invoke(firstPlayer, "Refresh", session.ActiveRun.Health, session.ActiveRun.Food);

            Assert.That(firstFood.text, Is.EqualTo("Food: 70"));
            Assert.That(firstHealth.text, Is.EqualTo("Health: 80"));
            Assert.That(playerType.GetField("food", InstanceFlags), Is.Null);
            Assert.That(playerType.GetField("health", InstanceFlags), Is.Null);

            UnityEngine.Object.DestroyImmediate(firstPlayer.gameObject);
            Component replacement = CreatePlayer("M1.4 Replacement Player", out Text replacementFood, out Text replacementHealth);
            yield return null;

            Assert.That(GetProperty<GameSession>(manager, "Session"), Is.SameAs(session));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(70));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(80));
            Assert.That(replacementFood.text, Is.EqualTo("Food: 70"));
            Assert.That(replacementHealth.text, Is.EqualTo("Health: 80"));

            UnityEngine.Object.DestroyImmediate(replacement.gameObject);
            UnityEngine.Object.DestroyImmediate(firstFood.gameObject);
            UnityEngine.Object.DestroyImmediate(firstHealth.gameObject);
            UnityEngine.Object.DestroyImmediate(replacementFood.gameObject);
            UnityEngine.Object.DestroyImmediate(replacementHealth.gameObject);
        }

        private Component CreateManager(string name)
        {
            GameObject managerObject = new GameObject(name);
            return managerObject.AddComponent(gameManagerType);
        }

        private IEnumerator AssertDeathOutcome(
            DeathReason deathReason,
            string expectedMessage,
            string cleanupSceneName)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(manager, Is.Not.Null);
            yield return StartFirstRoute(manager);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            RunState completedRun = session.ActiveRun;
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            BoardRequest request = runtime.Request;
            Invoke(manager, "HideLevelImage");
            yield return null;
            Invoke(manager, "ObservePresentedObservation", FactObservation.ShamblerTrappedPresented);
            FieldNotesPresenter knowledge = GetProperty<FieldNotesPresenter>(manager, "FieldNotes");
            Assert.That(knowledge.FeedbackTitle, Is.EqualTo("Held by the ground"));
            if (deathReason == DeathReason.Starvation)
            {
                session.ConsumeFood(completedRun.Food - 1);
                object[] arguments = { new WaitCommand(), null };
                Assert.That((bool)Invoke(FindSceneComponent(playerType, "Main"), "TrySubmitCommand", arguments), Is.True);
                var submission = (Task<CommandSubmission>)arguments[1];
                while (!submission.IsCompleted)
                    yield return null;
                Assert.That(submission.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(completedRun.Food, Is.Zero);
            }
            else
                yield return PresentHealthDeath(manager);
            Assert.That(completedRun.Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(GetField<Text>(manager, "levelText").text, Does.Contain(expectedMessage));
            Assert.That(knowledge.FeedbackTitle, Is.Empty, "The production HandleBoardOutcome death path must clear transient feedback.");
            Assert.That(knowledge.DiscoveryFeedbackCount, Is.EqualTo(1));
            Assert.That(session.Profile.DiscoveredFactIds, Does.Contain("PIT_TRAPS_SHAMBLER"));
            Assert.That(knowledge.SampleInput(key => key == KeyCode.N, -300), Is.True);
            yield return null;
            Assert.That(knowledge.IsOpen, Is.True);
            Assert.That(knowledge.Select("PIT_TRAPS_SHAMBLER"), Is.True);
            Assert.That(knowledge.SelectedText, Does.Contain("causes no damage"));
            Assert.That(knowledge.SampleInput(key => key == KeyCode.N, -299), Is.True);
            yield return null;
            Assert.That(knowledge.FeedbackTitle, Is.Empty);
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(((IBoardOutcomeSink)manager).TryNotify(request,
                new PlayerDiedEvent(runtime.PlayerId)), Is.False);
            Assert.That(session.Profile.RunSummaries, Has.Count.EqualTo(1));

            Scene cleanupScene = SceneManager.CreateScene(
                cleanupSceneName + " " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        private Component CreatePlayer(string name, out Text foodText, out Text healthText)
        {
            GameObject foodObject = new GameObject(name + " Food Text");
            foodText = foodObject.AddComponent<Text>();
            GameObject healthObject = new GameObject(name + " Health Text");
            healthText = healthObject.AddComponent<Text>();
            GameObject playerObject = new GameObject(name);
            playerObject.AddComponent<BoxCollider2D>();
            playerObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Component player = playerObject.AddComponent(playerType);
            SetField(player, "foodText", foodText);
            SetField(player, "healthText", healthText);
            RunState run = GetProperty<GameSession>((Component)GetStaticField(gameManagerType, "instance"), "Session").ActiveRun;
            Invoke(player, "Refresh", run.Health, run.Food);
            return player;
        }

        private static IEnumerator StartFirstRoute(Component manager)
        {
            RunState run = GetProperty<GameSession>(manager, "Session").ActiveRun;
            if (!GetProperty<bool>(manager, "IsRouteChoiceActive") &&
                run != null &&
                string.Equals(run.WorldNodeId, "shelter.warm-up", StringComparison.Ordinal))
            {
                AssertOpeningWarmUp(manager);
                SetField(manager, "levelStartDelay", 100f);
                yield return PresentExit(manager, useTraversalFixture: true);
            }

            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
            SetField(manager, "levelStartDelay", 100f);
            Assert.That((bool)Invoke(manager, "ChooseRoute", "road.start-east-creek"), Is.True);
            float deadline = Time.realtimeSinceStartup + 10;
            while (GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime") == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Not.Null);
        }

        private static IEnumerator AdvanceOpeningToFinalSegment(Component manager)
        {
            RunState run = GetProperty<GameSession>(manager, "Session").ActiveRun;
            while (run.ActiveLeg.SegmentIndex < 2)
            {
                int index = run.ActiveLeg.SegmentIndex;
                BoardOutcome completion = BoardOutcome.ExitReached(GetProperty<BoardRequest>(manager, "ActiveBoardRequest"));
                Assert.That((bool)Invoke(manager, "HandleBoardOutcome", completion), Is.True);
                yield return null;
                yield return null;
                Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(index + 1));
                Assert.That(run.CompletedDayCount, Is.Zero);
                Assert.That(run.CurrentDay, Is.EqualTo(1));
                Assert.That(run.WorldNodeId, Is.EqualTo("shelter.warm-up"));
                Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
                Assert.That((bool)Invoke(manager, "HandleBoardOutcome", completion), Is.False);
            }
        }

        private static void AssertOpeningWarmUp(Component manager)
        {
            RunState run = GetProperty<GameSession>(manager, "Session").ActiveRun;
            BoardRequest opening = GetProperty<BoardRequest>(manager, "ActiveBoardRequest");
            Assert.That(run, Is.Not.Null);
            Assert.That(run.WorldNodeId, Is.EqualTo("shelter.warm-up"));
            Assert.That(run.CurrentDay, Is.EqualTo(1));
            Assert.That(run.CompletedDayCount, Is.Zero);
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.Food, Is.EqualTo(100));
            Assert.That(run.ActiveLeg.EdgeId, Is.EqualTo("road.shelter-warm-up"));
            Assert.That(run.ActiveLeg.DayNumber, Is.EqualTo(1));
            Assert.That(run.ActiveLeg.Segments, Has.Count.EqualTo(3));
            Assert.That(opening, Is.Not.Null);
            Assert.That(opening.Address.EdgeId, Is.EqualTo("road.shelter-warm-up"));
            Assert.That(opening.Address.SegmentId, Is.EqualTo("segment.shelter-warm-up.v2.entry"));
            Assert.That(opening.Address.GenerationConfigurationId, Is.EqualTo("ordinary.board.v1"));
            Assert.That(opening.Address.GenerationVersion, Is.EqualTo(BoardGenerationPipeline.OrdinaryConfigurationVersion));
            Assert.That(opening.CurrentDay, Is.EqualTo(1));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(GetProperty<IReadOnlyList<WorldMapExitOption>>(manager, "RouteChoices"), Is.Empty);

            Component boardScript = GetField<Component>(manager, "boardScript");
            BoardGenerationResult generation =
                GetProperty<BoardGenerationResult>(boardScript, "ActiveGenerationResult");
            Assert.That(generation, Is.Not.Null);
            Assert.That(generation.Blueprint.Config.EnemyCount.Minimum, Is.Zero);
            Assert.That(generation.Blueprint.Config.EnemyCount.Maximum, Is.Zero);
            InitialResourceManifestEntry food = generation.Blueprint.InitialResources
                .Single(resource => resource.ResourceFamilyId == "food");
            Assert.That(food.ItemCount, Is.InRange(3, 4));
            Assert.That(generation.Blueprint.Items.Any(item => item.ResourceFamilyId == "tools"), Is.False);
        }

        /// <summary>A missing or already finalized run produces a useful assertion instead of a null dereference.</summary>
        [Test]
        public void TraversalWithoutActiveRunReportsExplicitFailure()
        {
            Component manager = CreateManager("Traversal without active run");
            IEnumerator traversal = PresentExit(manager);
            AssertionException failure = Assert.Throws<AssertionException>(() => traversal.MoveNext());
            Assert.That(failure.Message, Does.Contain("Traversal requires an active run"));
        }

        private static IEnumerator PresentExit(Component manager, bool useTraversalFixture = false, bool animations = false)
        {
            if (GetProperty<bool>(manager, "IsRouteChoiceActive"))
                yield break;
            GameSession session = GetProperty<GameSession>(manager, "Session");
            RunState run = session.ActiveRun;
            Assert.That(run, Is.Not.Null, "Traversal requires an active run; it must not continue after death or completion.");
            while (run.ActiveLeg != null)
            {
                if (useTraversalFixture)
                    PublishTraversalFixture(manager);
                BoardRuntime previous = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
                yield return PresentSegmentExit(manager, animations);
                Assert.That(session.ActiveRun, Is.SameAs(run), "Traversal ended the run instead of reaching the segment exit.");
                float deadline = Time.realtimeSinceStartup + 10;
                while (!GetProperty<bool>(manager, "IsRouteChoiceActive") &&
                    ReferenceEquals(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), previous) &&
                    Time.realtimeSinceStartup < deadline)
                    yield return null;
                yield return null;
                Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Not.Null);
            }
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
        }

        private static void PublishTraversalFixture(Component manager)
        {
            BoardRuntime generated = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            BoardRequest request = generated.Request;
            RunState run = generated.RunState;
            GameObject player = generated.Views[generated.PlayerId];
            player.transform.position = new Vector3(0, 0, player.transform.position.z);
            var root = new GameObject("Lifecycle traversal fixture");
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 3; horizontal++)
                for (int vertical = 0; vertical < 3; vertical++)
                {
                    var floor = new GameObject("Fixture floor");
                    floor.transform.SetParent(root.transform, false);
                    floor.transform.position = new Vector3(horizontal, vertical, 0);
                    layout.Add(new LegacyBoardView(floor, LegacyBoardContentKind.Floor, new GridPosition(horizontal, vertical)));
                }
            var exit = new GameObject("Fixture exit");
            exit.transform.SetParent(root.transform, false);
            exit.transform.position = new Vector3(2, 2, 0);
            layout.Add(new LegacyBoardView(exit, LegacyBoardContentKind.Exit, new GridPosition(2, 2)));
            layout.Add(new LegacyBoardView(player, LegacyBoardContentKind.Player, new GridPosition(0, 0)));
            BoardRuntime fixture = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 2), layout);
            Invoke(manager, "PublishBoardRuntime", fixture);
            Assert.That(generated.IsDisposed, Is.True);
            Assert.That(fixture.Request, Is.SameAs(request));
            Assert.That(fixture.RunState, Is.SameAs(run));
            Assert.That(fixture.Enemies, Is.Empty);
        }

        private static IEnumerator PresentSegmentExit(Component manager, bool animations = false)
        {
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            BoardEventPresenter presenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            presenter.AnimationsEnabled = animations;
            Invoke(manager, "HideLevelImage");
            yield return null;
            int steps = 0;
            while (!runtime.IsDisposed && !runtime.Controller.IsTerminal)
            {
                Assert.That(++steps, Is.LessThan(100), "The fixed travel fixture must reach its exit.");
                Direction direction = FindExitPath(runtime).First();
                KeyCode key = direction.Equals(Direction.North) ? KeyCode.UpArrow :
                    direction.Equals(Direction.East) ? KeyCode.RightArrow :
                    direction.Equals(Direction.South) ? KeyCode.DownArrow : KeyCode.LeftArrow;
                object[] arguments = { new PcCommandSource(candidate => candidate == key), null };
                Assert.That((bool)Invoke(runtime.Views[runtime.PlayerId].GetComponent(
                    manager.GetType().Assembly.GetType("PlayerScript", true)), "TrySubmitInput", arguments), Is.True);
                var submission = (Task<CommandSubmission>)arguments[1];
                while (!submission.IsCompleted)
                    yield return null;
                Assert.That(submission.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(submission.Result.Result.Events.Any(gameEvent => gameEvent is PlayerDiedEvent || gameEvent is PlayerStarvedEvent),
                    Is.False, "Traversal encountered a fatal action instead of a segment exit.");
                yield return null;
            }
        }

        private static IReadOnlyList<Direction> FindExitPath(BoardRuntime runtime)
        {
            var entities = runtime.BoardState.GetEntities().ToArray();
            GridPosition origin = entities.Single(entity => entity.Id.Equals(runtime.PlayerId)).Position;
            GridPosition destination = entities.Single(entity => entity.Definition.Layer == BoardLayer.Terrain &&
                entity.Definition.Traits.IsExit).Position;
            var walkable = new HashSet<GridPosition>(entities.Where(entity => entity.Definition.Layer == BoardLayer.Terrain &&
                entity.Definition.Traits.IsWalkable).Select(entity => entity.Position));
            foreach (var entity in entities.Where(entity => entity.Definition.Layer == BoardLayer.Obstacle ||
                (entity.Definition.Layer == BoardLayer.Actor && !entity.Id.Equals(runtime.PlayerId))))
                walkable.Remove(entity.Position);
            var queue = new Queue<GridPosition>();
            var previous = new Dictionary<GridPosition, (GridPosition Position, Direction Direction)>();
            queue.Enqueue(origin);
            previous.Add(origin, (origin, default));
            while (queue.Count != 0 && !previous.ContainsKey(destination))
            {
                GridPosition current = queue.Dequeue();
                foreach (Direction direction in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                {
                    var next = new GridPosition(current.X + direction.DeltaX, current.Y + direction.DeltaY);
                    if (!walkable.Contains(next) || previous.ContainsKey(next))
                        continue;
                    previous.Add(next, (current, direction));
                    queue.Enqueue(next);
                }
            }
            Assert.That(previous.ContainsKey(destination), Is.True, "No legal exit path for board seed " + runtime.Request.BoardSeed);
            var path = new List<Direction>();
            for (GridPosition current = destination; !current.Equals(origin); current = previous[current].Position)
                path.Add(previous[current].Direction);
            path.Reverse();
            return path;
        }

        private static IEnumerator PresentHealthDeath(Component manager)
        {
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            runtime.RunState.TakeDamage(runtime.RunState.Health);
            Task<PresentationDiagnostic> replay = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter")
                .Dispatcher.ReplayAsync(new GameEvent[]
                {
                    new ActionCostAppliedEvent(runtime.PlayerId, 1), new PlayerDiedEvent(runtime.PlayerId)
                });
            while (!replay.IsCompleted)
                yield return null;
            Assert.That(replay.Result, Is.Null);
        }

        private static Component FindSceneComponent(Type type, string sceneName)
        {
            return UnityEngine.Object.FindObjectsOfType(type)
                .Cast<Component>()
                .FirstOrDefault(component => component.gameObject.scene.name == sceneName);
        }

        private static void AssertSceneHasNoMissingComponents(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, sceneName + " must be loaded.");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(
                        child.GetComponents<Component>().All(component => component != null),
                        Is.True,
                        child.name + " in " + sceneName + " contains a missing script.");
                }
            }
        }

        private static T GetField<T>(Component target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return (T)field.GetValue(target);
        }
        private static Type RequireType(Assembly assembly, string name)
        {
            Type type = assembly.GetType(name);
            Assert.That(type, Is.Not.Null, name + " must exist in Assembly-CSharp.");
            return type;
        }

        private static object Invoke(Component target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, InstanceFlags);
            Assert.That(method, Is.Not.Null, methodName + " must exist.");
            return method.Invoke(target, arguments);
        }

        private static void SetField(Component target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            field.SetValue(target, value);
        }

        private static T GetProperty<T>(Component target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, InstanceFlags);
            Assert.That(property, Is.Not.Null, propertyName + " must exist.");
            return (T)property.GetValue(target, null);
        }

        private static object GetStaticField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return field.GetValue(null);
        }

        private static void SetStaticField(Type type, string fieldName, object value)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            field.SetValue(null, value);
        }

        private static object InvokeStatic(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, methodName + " must exist.");
            return method.Invoke(null, null);
        }

        private static void DestroySingleton(Type type)
        {
            if (type == null)
                return;

            FieldInfo instanceField = type.GetField("instance", BindingFlags.Public | BindingFlags.Static);
            Component instance = instanceField == null ? null : instanceField.GetValue(null) as Component;
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance.gameObject);
            if (instanceField != null)
                instanceField.SetValue(null, null);
        }
        private void DestroyGameManagerSingleton()
        {
            if (gameManagerType == null)
                return;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            if (manager != null)
                UnityEngine.Object.DestroyImmediate(manager.gameObject);
            gameManagerType.GetField("instance", BindingFlags.Public | BindingFlags.Static).SetValue(null, null);
        }
    }
}