using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using HallowBlaze.Core.Board.Primitives;
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
    internal sealed class BoardRuntimeStartupTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();
        private readonly List<UnityEngine.Object> createdAssets = new List<UnityEngine.Object>();
        private Type boardManagerType;
        private Type gameManagerType;
        private object previousPersistenceRoot;
        private string persistenceRoot;
        private float previousTimeScale;
        private bool hadHighScore;
        private int previousHighScore;

        [SetUp]
        public void SetUp()
        {
            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(
                assembly => assembly.GetName().Name == "Assembly-CSharp");
            boardManagerType = gameAssembly.GetType("BoardManager", true);
            gameManagerType = gameAssembly.GetType("GameManager", true);
            previousPersistenceRoot = gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            previousTimeScale = Time.timeScale;
            hadHighScore = PlayerPrefs.HasKey("HighScore");
            previousHighScore = PlayerPrefs.GetInt("HighScore");
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--)
            {
                if (createdObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[index]);
            }
            createdObjects.Clear();
            foreach (UnityEngine.Object asset in createdAssets)
                if (asset != null)
                    UnityEngine.Object.DestroyImmediate(asset);
            createdAssets.Clear();
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousPersistenceRoot);
            if (persistenceRoot != null && Directory.Exists(persistenceRoot))
                Directory.Delete(persistenceRoot, true);
            persistenceRoot = null;
            Time.timeScale = previousTimeScale;
            if (hadHighScore)
                PlayerPrefs.SetInt("HighScore", previousHighScore);
            else
                PlayerPrefs.DeleteKey("HighScore");
        }

        [UnityTest]
        public IEnumerator SuccessfulSetupPublishesCompleteRuntimeWithModelViewParity()
        {
            Component manager = CreateBoardManager();
            GameObject player = CreateObject("Player", 0, 0);
            RunState run = CreateRun();
            BoardRequest request = CreateRequest(run);

            BoardRuntime runtime = InvokeSetup(manager, request, run, player);

            Assert.That(runtime, Is.SameAs(GetProperty<BoardRuntime>(manager, "ActiveRuntime")));
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.SameAs(request));
            Assert.That(runtime.RunState, Is.SameAs(run));
            Assert.That(runtime.Controller, Is.Not.Null);
            Assert.That(runtime.BoardState.Count, Is.EqualTo(6));
            Assert.That(runtime.Views, Has.Count.EqualTo(runtime.BoardState.Count));
            foreach (var entity in runtime.BoardState.GetEntities())
            {
                Vector3 viewPosition = runtime.Views[entity.Id].transform.position;
                Assert.That(viewPosition.x, Is.EqualTo(entity.Position.X));
                Assert.That(viewPosition.y, Is.EqualTo(entity.Position.Y));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ProductionEnemyBatch_InitializesBeforeInputAndUsesSoleController()
        {
            foreach (int damage in new[] { 10, 20 })
            {
                Component manager = CreateBoardManager();
                SetField(manager, "columns", 4);
                SetField(manager, "rows", 4);
                GameObject prefab = CreateObject("Enemy " + damage + " Prefab", 0, 0);
                Component artwork = prefab.AddComponent(boardManagerType.Assembly.GetType("Enemy", true));
                SetField(artwork, "playerDamage", damage);
                SetField(manager, "enemyTiles", new[] { prefab });
                GameObject player = CreateObject("Player " + damage, 0, 0);
                RunState run = CreateRun();
                BoardRequest request = CreateRequest(run, 2);
                BoardRuntime runtime = InvokeSetup(manager, request, run, player);
                TurnController controller = runtime.Controller;
                IReadOnlyList<ShamblerState> oldRoster = runtime.Enemies;
                Assert.That(oldRoster, Has.Count.EqualTo(1));
                ShamblerState enemy = oldRoster[0];
                Assert.That(enemy.Definition.Damage, Is.EqualTo(damage));
                Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
                Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Move));
                Assert.That(enemy.LockedIntent.ActorId, Is.EqualTo(enemy.ActorId));
                Assert.That(runtime.Views[enemy.ActorId].activeInHierarchy, Is.True);
                GridPosition source = runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(enemy.ActorId)).Position;
                GridPosition target = enemy.LockedIntent.TargetPosition.Value;
                EnemyIntent initialPlan = enemy.LockedIntent;
                yield return null;
                Assert.That(enemy.LockedIntent, Is.SameAs(initialPlan));
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(100));

                Assert.That(controller.Resolve(new MoveCommand(Direction.West)).Accepted, Is.False);
                Assert.That(enemy.LockedIntent, Is.SameAs(initialPlan));
                TurnResult turn = controller.Resolve(new WaitCommand());
                Assert.That(turn.Accepted, Is.True);
                Assert.That(turn.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
                Assert.That(runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(enemy.ActorId)).Position,
                    Is.EqualTo(target));
                Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
                Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
                Assert.That(run.Food, Is.EqualTo(99));
                Assert.That(runtime.Controller, Is.SameAs(controller));
                Assert.That(runtime.Views[enemy.ActorId].transform.position,
                    Is.EqualTo(new Vector3(source.X, source.Y, 0f)), "Legacy artwork must not execute its own movement.");

                BoardRuntime replacement = InvokeSetup(manager, request, run, player);
                Assert.That(runtime.IsDisposed, Is.True);
                Assert.That(oldRoster, Is.Empty);
                Assert.That(replacement.Controller, Is.Not.SameAs(controller));
                Assert.That(replacement.Enemies[0], Is.Not.SameAs(enemy));
                Assert.That(replacement.Enemies[0].ActorId, Is.EqualTo(enemy.ActorId));
                Assert.That(replacement.Enemies[0].Phase, Is.EqualTo(ShamblerPhase.Active));
                Assert.That(replacement.Enemies[0].LockedIntent, Is.Not.Null);
                Assert.That(player, Is.Not.Null);
                TestContext.WriteLine("Production enemy damage=" + damage + "; initial plan before input; one controller; fresh replacement.");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator InvalidEnemyVariant_DoesNotPublishPartialRuntime()
        {
            foreach (bool missing in new[] { true, false })
            {
                Component manager = CreateBoardManager();
                SetField(manager, "columns", 4);
                SetField(manager, "rows", 4);
                GameObject prefab = CreateObject("Invalid Enemy Prefab", 0, 0);
                if (!missing)
                    SetField(prefab.AddComponent(boardManagerType.Assembly.GetType("Enemy", true)), "playerDamage", 15);
                SetField(manager, "enemyTiles", new[] { prefab });
                RunState run = CreateRun();
                GameObject player = CreateObject("Player", 0, 0);
                TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(() =>
                    InvokeSetup(manager, CreateRequest(run, 2), run, player));
                var diagnostic = invocation.InnerException as BoardRuntimeCompositionException;
                Assert.That(diagnostic, Is.Not.Null);
                Assert.That(diagnostic.Code, Is.EqualTo(BoardRuntimeDiagnosticCode.InvalidInput));
                Assert.That(diagnostic.Context, Does.StartWith("prefab:Enemy:"));
                Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.Null);
                Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.Null);
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(100));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator FailedSetupPublishesNeitherRequestNorPartialRuntime()
        {
            Component manager = CreateBoardManager();
            RunState run = CreateRun();

            TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(() =>
                InvokeSetup(manager, CreateRequest(run), run, null));
            var diagnostic = invocation.InnerException as BoardRuntimeCompositionException;

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.Code, Is.EqualTo(BoardRuntimeDiagnosticCode.MissingPlayer));
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.Null);
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReloadDisposesOldRuntimeAndDoesNotLeakGeneratedViews()
        {
            Component manager = CreateBoardManager();
            GameObject player = CreateObject("Player", 0, 0);
            RunState run = CreateRun();
            BoardRequest request = CreateRequest(run);

            BoardRuntime first = InvokeSetup(manager, request, run, player);
            IReadOnlyDictionary<EntityId, GameObject> firstRegistry = first.Views;
            List<GameObject> firstGeneratedViews = firstRegistry.Values
                .Where(view => view != player)
                .ToList();
            object firstController = first.Controller;

            BoardRuntime second = InvokeSetup(manager, request, run, player);

            Assert.That(first.IsDisposed, Is.True);
            Assert.That(firstRegistry, Is.Empty);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Controller, Is.Not.SameAs(firstController));
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.SameAs(second));
            Assert.That(
                second.Views.Values.Where(view => view != player),
                Has.None.Matches<GameObject>(view => firstGeneratedViews.Contains(view)));

            yield return null;
            Assert.That(
                firstGeneratedViews,
                Has.All.Matches<GameObject>(view => view == null));
        }

        [Test]
        public void GameManagerExposesTheAtomicRuntimePublicationSeam()
        {
            Assembly gameAssembly = boardManagerType.Assembly;
            Type gameManagerType = gameAssembly.GetType("GameManager", true);
            PropertyInfo property = gameManagerType.GetProperty(
                "ActiveBoardRuntime",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(property, Is.Not.Null);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(BoardRuntime)));
            Assert.That(property.CanWrite, Is.False);
        }

        /// <summary>Production setup prepares hidden intents during the day transition, reveals them before input and preserves one command path.</summary>
        [UnityTest]
        public IEnumerator ProductionIntents_AppearBeforeInputAndRespectBoardLifetime()
        {
            persistenceRoot = Path.Combine(Path.GetTempPath(), "HB-M3912-" + Guid.NewGuid().ToString("N"));
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, persistenceRoot);
            Time.timeScale = 1;
            Component board = CreateBoardManager();
            SetField(board, "columns", 4);
            SetField(board, "rows", 4);
            GameObject enemyPrefab = CreateObject("Production Enemy Prefab", 0, 0);
            SetField(enemyPrefab.AddComponent(boardManagerType.Assembly.GetType("Enemy", true)), "playerDamage", 10);
            SetField(board, "enemyTiles", new[] { enemyPrefab });
            Component manager = board.gameObject.AddComponent(gameManagerType);
            var world = new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            createdAssets.Add(world);
            SetField(manager, "worldDefinitionJson", world);
            SetField(manager, "levelStartDelay", 100f);
            Assert.That((bool)InvokeManager(manager, "TryEnsureWorldMap"), Is.True);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            session.StartNewRun("production-intents", 54321, new RunStateConfiguration(100, 100, 2, "forest.start"));
            GameObject playerObject = CreateObject("Production Player", 0, 0);
            playerObject.tag = "Player";
            Component player = playerObject.AddComponent(boardManagerType.Assembly.GetType("PlayerScript", true));
            SetField(player, "animationsEnabled", false);
            GameObject transition = CreateObject("LevelImage", 0, 0);
            InvokeManager(manager, "InitGame");
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            BoardEventPresenter presenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            EnemyIntentPresenter display = GetManagerField<EnemyIntentPresenter>(manager, "activeIntents");
            EnemyIntentLegend legend = GetManagerField<EnemyIntentLegend>(manager, "activeIntentLegend");
            Assert.That(display.Views.Count, Is.EqualTo(runtime.Enemies.Count).And.EqualTo(1));
            ShamblerState enemy = runtime.Enemies.Single();
            EnemyIntent initial = enemy.LockedIntent;
            EnemyIntentView marker = display.Views[enemy.ActorId];
            Assert.That(marker.gameObject.activeSelf, Is.True, "Retained markers must already be prepared during setup.");
            Assert.That(marker.gameObject.activeInHierarchy, Is.False, "Day transition must hide all intent markers.");
            Assert.That(legend.Root.activeInHierarchy, Is.False, "Day transition must hide the legend canvas.");
            Assert.That(legend.Control.gameObject.activeInHierarchy, Is.False, "Day transition must hide the legend control.");
            Assert.That(transition.activeSelf, Is.True);
            Assert.That(display.Projections.Single(), Is.EqualTo(EnemyIntentProjection.FromRetainedIntent(initial,
                runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(enemy.ActorId)).Position)));
            Assert.That(display.Projections.Single().AttackOnPlayerEntry, Is.True);
            Assert.That(presenter.Coordinator.Gate.Blocks.HasFlag(PresentationInputBlock.Setup), Is.True);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            yield return null;
            Assert.That(transition.activeSelf, Is.True);
            Assert.That(display.gameObject.activeInHierarchy, Is.False);
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            ((Behaviour)manager).enabled = false;
            ((Behaviour)manager).enabled = true;
            Assert.That(display.Views.Count, Is.EqualTo(1));
            Assert.That(display.gameObject.activeInHierarchy, Is.False, "Enable during setup must not reveal the UI.");
            Assert.That(legend.Root.activeInHierarchy, Is.False);
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(100));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.Zero);
            marker = display.Views[enemy.ActorId];
            InvokeManager(manager, "HideLevelImage");
            Assert.That(transition.activeSelf, Is.False);
            Assert.That(marker.gameObject.activeInHierarchy, Is.True);
            Assert.That(legend.Root.activeInHierarchy, Is.True);
            Assert.That(legend.Control.gameObject.activeInHierarchy, Is.True);
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            yield return null;
            Assert.That(TrySubmit(manager, new MoveCommand(Direction.West), out Task<CommandSubmission> rejected), Is.True);
            Assert.That(rejected.Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(display.Views[enemy.ActorId], Is.SameAs(marker));
            int count = presenter.Coordinator.ResolutionCount;
            legend.Control.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.True);
            legend.Control.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.False);
            InvokeManager(manager, "SetGameplayInputBlocked", true);
            Time.timeScale = 0;
            yield return null;
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(count));
            Time.timeScale = 1;
            InvokeManager(manager, "SetGameplayInputBlocked", false);
            yield return null;
            Assert.That(TrySubmit(manager, new WaitCommand(), out Task<CommandSubmission> accepted), Is.True);
            while (!accepted.IsCompleted)
                yield return null;
            Assert.That(accepted.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(99));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(count + 1));
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter"), Is.SameAs(presenter));
            Assert.That(display.Projections.Single().Symbol, Is.EqualTo(EnemyIntentSymbol.Wait));
            EnemyIntent rest = enemy.LockedIntent;
            ((Behaviour)manager).enabled = false;
            Assert.That(display.Views, Is.Empty);
            Assert.That(legend.Root.activeInHierarchy, Is.False);
            ((Behaviour)manager).enabled = true;
            Assert.That(enemy.LockedIntent, Is.SameAs(rest));
            Assert.That(display.Views.Count, Is.EqualTo(1));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(99));

            EntityId oldId = enemy.ActorId;
            var oldControl = legend.Control;
            GameObject oldOwner = display.gameObject;
            InvokeManager(manager, "BlockBoardStartup");
            Assert.That(runtime.IsDisposed, Is.True);
            Assert.That(display.IsDisposed, Is.True);
            Assert.That(display.Views, Is.Empty);
            Assert.That(oldOwner.activeSelf, Is.False);
            oldControl.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.False);
            transition.SetActive(true);
            InvokeManager(manager, "InitGame");
            BoardRuntime replacement = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(replacement.Enemies.Single().ActorId, Is.EqualTo(oldId));
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter"), Is.Not.SameAs(presenter));
            EnemyIntentPresenter nextDisplay = GetManagerField<EnemyIntentPresenter>(manager, "activeIntents");
            EnemyIntentLegend nextLegend = GetManagerField<EnemyIntentLegend>(manager, "activeIntentLegend");
            Assert.That(nextDisplay.Views.Count, Is.EqualTo(1));
            Assert.That(nextDisplay, Is.Not.SameAs(display));
            Assert.That(transition.activeSelf, Is.True);
            Assert.That(nextDisplay.gameObject.activeInHierarchy, Is.False, "Replacement setup must hide the new board UI.");
            Assert.That(nextLegend.Root.activeInHierarchy, Is.False);
            InvokeManager(manager, "HideLevelImage");
            Assert.That(nextDisplay.Views.Values.All(view => view.gameObject.activeInHierarchy), Is.True);
            Assert.That(nextLegend.Control.gameObject.activeInHierarchy, Is.True);
            yield return null;
            Assert.That(oldOwner == null, Is.True);
            session.ConsumeFood(session.ActiveRun.Food - 1);
            var terminalControl = nextLegend.Control;
            Assert.That(TrySubmit(manager, new WaitCommand(), out Task<CommandSubmission> terminal), Is.True);
            while (!terminal.IsCompleted)
                yield return null;
            Assert.That(terminal.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(nextDisplay.IsDisposed, Is.True);
            Assert.That(nextDisplay.Views, Is.Empty);
            Assert.That(nextLegend.Root.activeSelf, Is.False);
            terminalControl.onClick.Invoke();
            Assert.That(nextLegend.IsVisible, Is.False);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            Assert.That(Directory.Exists(persistenceRoot), Is.True, "Terminal checkpoint must stay in the isolated root.");

            ((Behaviour)manager).enabled = true;
            session.StartNewRun("production-intent-failure", 54321, new RunStateConfiguration(100, 100, 2, "forest.start"));
            Camera camera = CreateObject("Intent overlap camera", 0, 0).AddComponent<Camera>();
            camera.gameObject.tag = "MainCamera";
            camera.transform.position = new Vector3(1.5f, 1.5f, -10);
            camera.orthographic = false;
            camera.orthographicSize = 1;
            camera.fieldOfView = 1;
            camera.pixelRect = new Rect(0, 0, Screen.width, Screen.height);
            LogAssert.Expect(LogType.Error, new Regex("Board startup failed: Legend must leave the supplied inspected board area visible"));
            InvokeManager(manager, "InitGame");
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Null);
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter"), Is.Null);
            Assert.That(GetProperty<BoardRuntime>(board, "ActiveRuntime"), Is.Null);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            InvokeManager(manager, "HideLevelImage");
            yield return null;
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            TestContext.WriteLine("P1 production roster; markers/legend hidden during day transition including re-enable/replacement; prepared plans revealed before input without AI or cost; one coordinator; terminal/failure cleanup; isolated save root.");
        }

        /// <summary>Day-one startup with the production camera framing leaves the transition and enables the sole command path.</summary>
        [UnityTest]
        public IEnumerator DayOneStartup_WithCenteredCameraReleasesTransition()
        {
            persistenceRoot = Path.Combine(Path.GetTempPath(), "HB-LegendStartup-" + Guid.NewGuid().ToString("N"));
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, persistenceRoot);
            Time.timeScale = 1;
            Component board = CreateBoardManager();
            SetField(board, "columns", 8);
            SetField(board, "rows", 8);
            Component manager = board.gameObject.AddComponent(gameManagerType);
            var world = new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            createdAssets.Add(world);
            SetField(manager, "worldDefinitionJson", world);
            SetField(manager, "levelStartDelay", 0.05f);
            Assert.That((bool)InvokeManager(manager, "TryEnsureWorldMap"), Is.True);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            session.StartNewRun("day-one-legend-regression", 54321, new RunStateConfiguration(100, 100, 0, "forest.start"));
            Camera camera = CreateObject("Production camera framing", 0, 0).AddComponent<Camera>();
            camera.gameObject.tag = "MainCamera";
            camera.transform.position = new Vector3(3.5f, 3.5f, -10);
            camera.orthographic = true;
            camera.orthographicSize = 5;
            GameObject playerObject = CreateObject("Day-one player", 0, 0);
            playerObject.tag = "Player";
            Component player = playerObject.AddComponent(boardManagerType.Assembly.GetType("PlayerScript", true));
            SetField(player, "animationsEnabled", false);
            GameObject transition = CreateObject("LevelImage", 0, 0);
            GameObject textObject = CreateObject("LevelText", 0, 0);
            textObject.transform.SetParent(transition.transform, false);
            var dayText = textObject.AddComponent<UnityEngine.UI.Text>();

            Rect originalRect = camera.rect;
            float originalSize = camera.orthographicSize;
            TestContext.WriteLine("Day-one test viewport=" + Screen.width + "x" + Screen.height);
            InvokeManager(manager, "InitGame");
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.Enemies, Is.Empty);
            Assert.That(dayText.text, Is.EqualTo("Day: 1"));
            Assert.That(transition.activeSelf, Is.True);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            EnemyIntentLegend legend = GetManagerField<EnemyIntentLegend>(manager, "activeIntentLegend");
            Assert.That(legend, Is.Not.Null);
            Assert.That(legend.Root.activeInHierarchy, Is.False, "Day 1 must not show a legend over its loading overlay.");
            Assert.That(legend.Control.gameObject.activeInHierarchy, Is.False);
            float deadline = Time.realtimeSinceStartup + 2;
            while (transition.activeSelf && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(transition.activeSelf, Is.False, "The completed board must release the Day: 1 overlay.");
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.True);
            Assert.That(legend.Root.activeInHierarchy, Is.True);
            Assert.That(legend.Control.gameObject.activeInHierarchy, Is.True);
            var inspected = (Rect)InvokeManager(manager, "InspectedBoardArea");
            Assert.That(legend.PanelBounds.Overlaps(inspected) || legend.ControlBounds.Overlaps(inspected), Is.False);
            legend.Control.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.True);
            legend.Control.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.False);
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(TrySubmit(manager, new WaitCommand(), out Task<CommandSubmission> pending), Is.True);
            while (!pending.IsCompleted)
                yield return null;
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(99));
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").Coordinator.ResolutionCount, Is.EqualTo(1));
            InvokeManager(manager, "BlockBoardStartup");
            Assert.That(camera.rect, Is.EqualTo(originalRect));
            Assert.That(camera.orthographicSize, Is.EqualTo(originalSize));
            TestContext.WriteLine("Day: 1 released; production camera (3.5,3.5,-10), size5; viewport=" + Screen.width + "x" + Screen.height + "; nonoverlapping legend; one command/cost.");
        }

        private static object InvokeManager(Component manager, string name, params object[] arguments)
        {
            return manager.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(manager, arguments);
        }

        private static T GetManagerField<T>(Component manager, string name)
        {
            return (T)manager.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
        }

        private static bool TrySubmit(Component manager, PlayerCommand command, out Task<CommandSubmission> submission)
        {
            object[] arguments = { command, null };
            bool admitted = (bool)InvokeManager(manager, "TrySubmitCommand", arguments);
            submission = (Task<CommandSubmission>)arguments[1];
            return admitted;
        }

        private Component CreateBoardManager()
        {
            GameObject owner = CreateObject("Runtime Board Manager", 0, 0);
            Component manager = owner.AddComponent(boardManagerType);
            SetField(manager, "columns", 2);
            SetField(manager, "rows", 2);
            SetCount(manager, "wallCount", 0, 0);
            SetCount(manager, "foodCount", 0, 0);
            SetCount(manager, "aidCount", 0, 0);
            SetCount(manager, "buriedCount", 0, 0);

            GameObject floor = CreateObject("Floor Prefab", 0, 0);
            GameObject outer = CreateObject("Outer Prefab", 0, 0);
            GameObject unused = CreateObject("Unused Prefab", 0, 0);
            GameObject exit = CreateObject("Exit Prefab", 0, 0);
            SetField(manager, "floorTiles", new[] { floor });
            SetField(manager, "outerWallTiles", new[] { outer });
            SetField(manager, "wallTiles", new[] { unused });
            SetField(manager, "groundFoodTiles", new[] { unused });
            SetField(manager, "bushFoodTiles", unused);
            SetField(manager, "buriedFoodTiles", new[] { unused });
            SetField(manager, "aidTiles", new[] { unused });
            SetField(manager, "enemyTiles", new[] { unused });
            SetField(manager, "exit", exit);
            return manager;
        }

        private BoardRuntime InvokeSetup(
            Component manager,
            BoardRequest request,
            RunState run,
            GameObject player)
        {
            MethodInfo method = boardManagerType.GetMethod(
                "SetupScene",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(BoardRequest), typeof(RunState), typeof(GameObject) },
                null);
            Assert.That(method, Is.Not.Null);
            return (BoardRuntime)method.Invoke(manager, new object[] { request, run, player });
        }

        private void SetCount(Component manager, string fieldName, int minimum, int maximum)
        {
            Type countType = boardManagerType.GetNestedType("Count", BindingFlags.Public);
            object count = Activator.CreateInstance(countType, minimum, maximum);
            SetField(manager, fieldName, count);
        }

        private static void SetField(Component target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static T GetProperty<T>(Component target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, propertyName);
            return (T)property.GetValue(target, null);
        }

        private GameObject CreateObject(string name, int x, int y)
        {
            var instance = new GameObject(name);
            instance.transform.position = new Vector3(x, y, 0f);
            createdObjects.Add(instance);
            return instance;
        }

        private static RunState CreateRun()
        {
            return new RunState(
                "runtime-playmode-run",
                54321,
                new RunStateConfiguration(100, 100, 0, "forest.start"));
        }

        private static BoardRequest CreateRequest(RunState run, int difficulty = 1)
        {
            return new BoardRequest(
                run.RunId,
                run.RunSeed,
                run.WorldNodeId,
                run.CurrentDay,
                run.GetBoardSeed(),
                "forest",
                "temperate",
                difficulty);
        }
    }
}
