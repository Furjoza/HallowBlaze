#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HallowBlaze.Tests.PlayMode
{
    public sealed class BoardAuthoringIsolationTests
    {
        /// <summary>Compiles actual Player scripts into external storage and verifies the Editor context type is absent.</summary>
        public static void ValidatePlayerCompilation()
        {
            string output = Path.Combine(Path.GetTempPath(), "HB-M69a-PlayerScripts");
            Directory.CreateDirectory(output);
            var settings = new UnityEditor.Build.Player.ScriptCompilationSettings
            {
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                group = UnityEditor.BuildTargetGroup.Standalone
            };
            var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(settings, output);
            if (result.assemblies == null || result.assemblies.Count == 0)
                throw new InvalidOperationException("Player script compilation returned no assemblies.");
            string runtime = Path.Combine(output, "HallowBlaze.Presentation.Runtime.dll");
            var assembly = Assembly.ReflectionOnlyLoadFrom(runtime);
            if (assembly.GetType("HallowBlaze.Presentation.Runtime.BoardAuthoringTestContext", false) != null)
                throw new InvalidOperationException("Authoring context leaked into Player scripts.");
            Debug.Log("M6.9a Player scripts compiled; Editor context absent.");
        }

        private string root;
        private string sentinelRoot;
        private Type managerType;
        private Type soundType;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            BoardAuthoringTestContext.Cancel();
            root = BoardAuthoringTestContext.CreateSaveRootPath();
            Assembly game = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Assembly-CSharp");
            managerType = game.GetType("GameManager", true);
            soundType = game.GetType("SoundManager", true);
            DestroySingleton(managerType);
            DestroySingleton(soundType);
            sentinelRoot = Path.Combine(Path.GetTempPath(), "HB-M69-sentinel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sentinelRoot);
            File.WriteAllText(Path.Combine(sentinelRoot, "sentinel"), "unchanged");
            managerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, sentinelRoot);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroySingleton(managerType);
            DestroySingleton(soundType);
            BoardAuthoringTestContext.Cancel();
            managerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Assert.That(Directory.GetFileSystemEntries(sentinelRoot).Length, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(sentinelRoot, "sentinel")), Is.EqualTo("unchanged"));
            Directory.Delete(sentinelRoot, true);
            if (SceneManager.GetSceneByName("Main").isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene("Authoring cleanup " + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync("Main");
            }
            Time.timeScale = 1;
            yield return null;
        }

        [Test]
        public void InvalidLaunchDoesNotPublishOrWrite()
        {
            Assert.Throws<ArgumentException>(() => BoardAuthoringTestContext.Prepare("{}", "{}", "{}", "bad", "base", null,
                PersistenceJsonSerializer.DeserializeTravelRun));
            Assert.Throws<IOException>(() => BoardAuthoringTestContext.Prepare("{}", "{}", "{}", "bad", "base", Application.dataPath,
                PersistenceJsonSerializer.DeserializeTravelRun));
            Assert.That(() => BoardAuthoringTestContext.Prepare("{}", "{}", "{}", "bad", "base", root,
                PersistenceJsonSerializer.DeserializeTravelRun), Throws.Exception);
            Assert.That(Directory.Exists(root), Is.False);
            Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
        }

        [Test]
        public void TemporaryDirectoryInsideProjectOrPlayerStorageIsRejectedBeforeWriting()
        {
            MethodInfo validate = typeof(BoardAuthoringTestContext).GetMethod("ValidateRootAtTemporaryDirectory", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string forbidden in new[] { Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages"),
                Application.dataPath, Application.persistentDataPath })
            {
                string candidate = Path.Combine(forbidden, "HallowBlaze-BoardTest-" + Guid.NewGuid().ToString("N"));
                Exception failure = null;
                try { validate.Invoke(null, new object[] { candidate, false, null, forbidden }); }
                catch (Exception exception) { failure = exception.GetBaseException(); }
                Assert.That(failure, Is.InstanceOf<IOException>());
                Assert.That(Directory.Exists(candidate), Is.False);
                Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
            }
        }

        [Test]
        public void SnapshotClaimsOneOwnerAndRevocationInvalidatesRetainedReferences()
        {
            var context = Prepare();
            object owner = new object();
            Assert.That(BoardAuthoringTestContext.Claim(owner), Is.SameAs(context));
            Assert.Throws<InvalidOperationException>(() => BoardAuthoringTestContext.Claim(new object()));
            string initial = PersistenceJsonSerializer.SerializeTravelRun(context.CreateInitialRun());
            context.CreateInitialRun().ConsumeSatiety(5);
            Assert.That(PersistenceJsonSerializer.SerializeTravelRun(context.CreateInitialRun()), Is.EqualTo(initial));
            Assert.That(context.SaveRoot, Is.EqualTo(root));
            BoardAuthoringTestContext.Cancel();
            Assert.That(context.IsDisposed, Is.True);
            Assert.Throws<InvalidOperationException>(() => context.CreateInitialRun());
            Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
        }

        [UnityTest]
        public IEnumerator RealSceneUsesExactSnapshotAndIsolatedPreferencesAndSaves()
        {
            string music = PlayerPrefs.GetString("Music", "<absent>");
            string sound = PlayerPrefs.GetString("Sound", "<absent>");
            int score = PlayerPrefs.GetInt("HighScore", -1);
            BoardAuthoringTestContext context = Prepare();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            Invoke(manager, "HideLevelImage");
            yield return null;
            BoardRuntime runtime = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.RunState.ActiveLandmark.VariantId, Is.EqualTo("base"));
            Assert.That(runtime.Request.Address, Is.EqualTo(BoardAddressFactory.Create(context.CreateInitialRun(), context.World)));
            BoardState expected = context.CreateBoard(runtime.Request, runtime.RunState).CreateState();
            foreach (var entity in expected.GetEntities())
            {
                Assert.That(runtime.BoardState.TryGetEntity(entity.Id, out var actual), Is.True);
                Assert.That(actual.Position, Is.EqualTo(entity.Position));
                Assert.That(actual.Definition.ContentId, Is.EqualTo(entity.Definition.ContentId));
                Assert.That(runtime.Views[entity.Id].transform.position.x, Is.EqualTo(entity.Position.X));
                Assert.That(runtime.Views[entity.Id].transform.position.y, Is.EqualTo(entity.Position.Y));
            }
            BoardEventPresenter presenter = Property<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            presenter.AnimationsEnabled = false;
            int food = runtime.RunState.Satiety;
            var rejected = presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.South));
            while (!rejected.IsCompleted) yield return null;
            Assert.That(rejected.Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(food));
            var accepted = presenter.Coordinator.SubmitAsync(new WaitCommand());
            while (!accepted.IsCompleted) yield return null;
            Assert.That(accepted.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(food - 1));
            Component soundManager = (Component)soundType.GetField("instance").GetValue(null);
            Invoke(soundManager, "SetMusicEnabled", false);
            Invoke(soundManager, "SetSoundEnabled", false);
            Assert.That(PlayerPrefs.GetString("Music", "<absent>"), Is.EqualTo(music));
            Assert.That(PlayerPrefs.GetString("Sound", "<absent>"), Is.EqualTo(sound));
            Assert.That(PlayerPrefs.GetInt("HighScore", -1), Is.EqualTo(score));
            Assert.That(File.Exists(Path.Combine(root, "profile.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(root, "run.json")), Is.True);
            Assert.That((bool)Invoke(manager, "ExitToMenu"), Is.True);
            Assert.That(runtime.IsDisposed, Is.True);
            Assert.That(context.IsDisposed, Is.True);
            Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
        }

        [UnityTest]
        public IEnumerator DeathRevokesContextAndNeverUpdatesRealHighScore()
        {
            int highScore = PlayerPrefs.GetInt("HighScore", -1);
            var context = Prepare();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            BoardRuntime runtime = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            Invoke(manager, "GameOver", false);
            Assert.That(PlayerPrefs.GetInt("HighScore", -1), Is.EqualTo(highScore));
            Assert.That(context.HighScore, Is.EqualTo(4));
            Assert.That(context.IsDisposed, Is.True);
            Assert.That(runtime.IsDisposed, Is.True);
            var exception = Assert.Throws<TargetInvocationException>(() => Invoke(manager, "EnsurePersistence"));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void RootTamperAndUnexpectedReloadFailClosed()
        {
            var context = Prepare();
            BoardAuthoringTestContext.Claim(new object());
            context.EnterScene("Main", true);
            context.ExpectSceneReload("Main");
            context.EnterScene("Main", false);
            File.Delete(Path.Combine(root, ".authoring-owner"));
            Assert.That(() => { string unused = context.SaveRoot; }, Throws.InstanceOf<IOException>());
            Assert.Throws<InvalidOperationException>(() => context.EnterScene("Other", false));
            Assert.That(context.IsDisposed, Is.True);
        }

        [Test]
        public void PendingPayloadRecoversButRunningPayloadCannotBeReclaimedAfterDomainReload()
        {
            Prepare();
            FieldInfo active = typeof(BoardAuthoringTestContext).GetField("current", BindingFlags.NonPublic | BindingFlags.Static);
            active.SetValue(null, null);
            var recovered = BoardAuthoringTestContext.Claim(new object(), PersistenceJsonSerializer.DeserializeTravelRun);
            Assert.That(recovered.SaveRoot, Is.EqualTo(root));
            Assert.That(recovered.CreateInitialRun().RunId, Is.EqualTo("authoring-isolation"));
            active.SetValue(null, null);
            Assert.Throws<InvalidOperationException>(() => BoardAuthoringTestContext.Claim(new object(), PersistenceJsonSerializer.DeserializeTravelRun));
            Assert.That(BoardAuthoringTestContext.IsRequested, Is.True, "Failed recovery must keep normal bootstrap fenced.");
        }

        [UnityTest]
        public IEnumerator SaveFailureRefusesLaunchBeforeRuntimeAndPreservesNormalStorage()
        {
            var context = Prepare();
            Directory.CreateDirectory(Path.Combine(root, "run.json"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Authoring bootstrap failed: Authoring initial checkpoint failed:"));
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Assert.That(Property<BoardRuntime>(Manager(), "ActiveBoardRuntime"), Is.Null);
            Assert.That(context.IsDisposed, Is.True);
            Assert.That(File.Exists(Path.Combine(root, "profile.json")), Is.False);
        }

        [UnityTest]
        public IEnumerator FailedToolAllocationRestoresAllocatorAndLeavesNoPublishedBoard()
        {
            Prepare();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            BoardRuntime runtime = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            Component board = (Component)managerType.GetField("boardScript").GetValue(manager);
            RunState run = runtime.RunState;
            BoardRequest request = runtime.Request;
            GameObject player = runtime.Views[runtime.PlayerId];
            run.RestoreToolState(int.MaxValue, run.ToolSlots.Select(slot => slot.Instance).ToArray());
            string before = PersistenceJsonSerializer.SerializeTravelRun(run);
            Exception failure = null;
            try { Invoke(board, "SetupScene", request, run, player); }
            catch (TargetInvocationException exception) { failure = exception.InnerException; }
            catch (OverflowException exception) { failure = exception; }
            Assert.That(failure, Is.TypeOf<OverflowException>());
            Assert.That(Property<BoardRequest>(board, "ActiveRequest"), Is.Null);
            Assert.That(PersistenceJsonSerializer.SerializeTravelRun(run), Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator ControlledGameplayReloadRetainsSnapshotAndNormalLaunchRestoresAudioDefaults()
        {
            string music = PlayerPrefs.GetString("Music", "On");
            var context = Prepare();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            BoardRuntime first = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            BoardAddress originalAddress = first.Request.Address;
            Component sound = (Component)soundType.GetField("instance").GetValue(null);
            Invoke(sound, "SetMusicEnabled", music == "Off");
            context.ExpectSceneReload("Main");
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            BoardRuntime reloaded = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded, Is.Not.SameAs(first));
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(reloaded.Request.Address, Is.EqualTo(originalAddress));
            Assert.That(context.SaveRoot, Is.EqualTo(root));
            Assert.That(reloaded.RunState.ActiveLandmark.VariantId, Is.EqualTo("base"));
            Invoke(manager, "ExitToMenu");
            DestroySingleton(managerType);
            var normalOwner = new GameObject("Normal launch default probe");
            try
            {
                normalOwner.AddComponent(managerType);
                Assert.That((bool)soundType.GetProperty("MusicEnabled").GetValue(sound), Is.EqualTo(music != "Off"));
                Assert.That((string)managerType.GetMethod("GetPersistenceRoot", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, null), Is.EqualTo(sentinelRoot));
            }
            finally { UnityEngine.Object.DestroyImmediate(normalOwner); }
        }

        [UnityTest]
        public IEnumerator LocalSnapshotRunsRealPuzzleCommandsAndCompletesItsIsolatedGoal()
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json"));
            var context = BoardAuthoringTestContext.PrepareLocal(json, "base", 64, 100, 100, true, root);
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            Invoke(manager, "HideLevelImage");
            yield return null;
            BoardRuntime runtime = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            RunState run = runtime.RunState;
            Assert.That(run.ToolSlots[0].Instance.DefinitionId, Is.EqualTo("axe.basic"));
            Assert.That(run.RunSeed, Is.EqualTo(64));
            Assert.That(run.Satiety, Is.EqualTo(100));
            BoardEventPresenter presenter = Property<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            presenter.AnimationsEnabled = false;
            var landmark = context.CreateBoard(runtime.Request, run);
            string requestedOutcome = landmark.Outcomes.Single(outcome => outcome.IsOptional).Id;
            var solution = new LandmarkSolver(landmark, runtime.BoardState, run, runtime.PlayerId,
                runtime.Enemies, RadioTowerLandmark.CreateCatalog(), runtime.ClearingRules).Solve(requestedOutcome);
            Assert.That(solution.Status, Is.EqualTo(LandmarkSolverStatus.Solved), solution.ToString());
            foreach (var step in solution.SolutionPath)
            {
                var pending = presenter.Coordinator.SubmitAsync(step.CreateCommand(runtime.BoardState, run, runtime.PlayerId));
                while (!pending.IsCompleted) yield return null;
                Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented),
                    step + " " + pending.Result.Diagnostic?.Error);
            }
            Assert.That(run.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(run.CompletedLandmarks.Single().OutcomeId, Is.EqualTo(requestedOutcome));
            Assert.That(context.IsDisposed, Is.True);
            float cleanupDeadline = Time.realtimeSinceStartup + 2;
            while (!runtime.IsDisposed && Time.realtimeSinceStartup < cleanupDeadline) yield return null;
            Assert.That(runtime.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator SharedSingleExitSnapshotUsesRealRuntimeAndCompletesWithoutLandmarkRecord()
        {
            string json = SharedSingleExitJson();
            var context = BoardAuthoringTestContext.PrepareLocal(json, "base", 64, 100, 100, false, root);
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            Invoke(manager, "HideLevelImage");
            yield return null;
            BoardRuntime runtime = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.RunState.ActiveLandmark, Is.Null);
            BoardEventPresenter presenter = Property<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            presenter.AnimationsEnabled = false;
            RunState run = runtime.RunState;
            foreach (Direction direction in Enumerable.Repeat(Direction.East, 7).Concat(Enumerable.Repeat(Direction.North, 7)))
            {
                var pending = presenter.Coordinator.SubmitAsync(new MoveCommand(direction));
                while (!pending.IsCompleted) yield return null;
                Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented), pending.Result.Diagnostic?.Error?.ToString());
            }
            Assert.That(run.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(run.CompletedLandmarks, Is.Empty);
            Assert.That(context.IsDisposed, Is.True);
        }

        /// <summary>All retained fruit IDs compose their species asset and grant the same species reward once, before movement cost.</summary>
        [UnityTest]
        public IEnumerator SharedFruitUsesSpeciesPrefabAndExactReward(
            [Values("legacy.bush-food", "ordinary.berries", "legacy.food", "landmark.radio-tower.rations")] string contentId)
        {
            var data = Newtonsoft.Json.Linq.JObject.Parse(SharedSingleExitJson());
            var allowed = (Newtonsoft.Json.Linq.JArray)data["configuration"]["allowedContentIds"];
            if (!allowed.Values<string>().Contains(contentId)) allowed.Add(contentId);
            ((Newtonsoft.Json.Linq.JArray)data["variants"][0]["entities"]).Add(new Newtonsoft.Json.Linq.JObject
            { ["id"] = 74, ["layer"] = "Item", ["contentId"] = contentId, ["x"] = 1, ["y"] = 0 });
            ((Newtonsoft.Json.Linq.JArray)data["variants"][0]["items"]).Add(new Newtonsoft.Json.Linq.JObject
            { ["instanceId"] = "authoring.item.74", ["entityId"] = 74, ["itemDefinitionId"] = contentId,
                ["resourceFamilyId"] = "food", ["sourceId"] = Newtonsoft.Json.Linq.JValue.CreateNull(), ["initiallyObservable"] = true });
            BoardAuthoringTestContext.PrepareLocal(data.ToString(), "base", 64, 100, 100, false, root);
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            Invoke(manager, "HideLevelImage");
            yield return null;
            BoardRuntime runtime = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            BoardEntityState item = runtime.BoardState.GetEntities().Single(entity => entity.Definition.ContentId == contentId);
            GameObject view = runtime.Views[item.Id];
            bool blueberries = contentId == "legacy.bush-food" || contentId == "ordinary.berries";
            string path = "Assets/Prefabs/Tiles/" + (blueberries ? "Blueberries" : "Raspberries") + ".prefab";
            Sprite expected = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<SpriteRenderer>().sprite;
            Assert.That(view.GetComponent<SpriteRenderer>().sprite, Is.SameAs(expected));
            int reward = item.Definition.AutomaticFoodReward;
            Assert.That(reward, Is.EqualTo(blueberries ? 10 : 5));
            int previousFood = runtime.RunState.Satiety;
            BoardEventPresenter presenter = Property<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            presenter.AnimationsEnabled = false;
            var pending = presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East));
            while (!pending.IsCompleted) yield return null;
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(runtime.RunState.Satiety, Is.EqualTo(previousFood + reward - 1));
            Assert.That(runtime.BoardState.TryGetEntity(item.Id, out _), Is.False);
            Assert.That(view.activeSelf, Is.False);
        }

        private static string SharedSingleExitJson()
        {
            var data = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json")));
            data["schemaVersion"] = 2;
            data["templateId"] = "shared.single-exit";
            var entities = new Newtonsoft.Json.Linq.JArray();
            for (int horizontal = 0; horizontal < 8; horizontal++)
                for (int vertical = 0; vertical < 8; vertical++)
                    entities.Add(new Newtonsoft.Json.Linq.JObject { ["id"] = 10 + horizontal * 8 + vertical, ["layer"] = "Terrain",
                        ["contentId"] = horizontal == 7 && vertical == 7 ? "ordinary.exit-terrain" : "legacy.floor", ["x"] = horizontal, ["y"] = vertical });
            entities.Add(new Newtonsoft.Json.Linq.JObject { ["id"] = 1, ["layer"] = "Actor", ["contentId"] = "legacy.player", ["x"] = 0, ["y"] = 0 });
            data["variants"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject { ["id"] = "base", ["entities"] = entities,
                ["items"] = new Newtonsoft.Json.Linq.JArray() });
            data["outcomes"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject { ["id"] = "exit", ["name"] = "Exit", ["exitEntityId"] = 73, ["optional"] = false });
            return data.ToString();
        }

        /// <summary>All captured assignments and unassigned generator identities are checked before a sandbox can write.</summary>
        [TestCase("duplicate")]
        [TestCase("variant")]
        [TestCase("payload")]
        [TestCase("route")]
        [TestCase("unsupported")]
        [TestCase("version")]
        public void RouteSandboxRejectsInvalidPublicationBeforeWriting(string fault)
        {
            string worldJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
            var world = WorldDefinitionJson.Deserialize(worldJson);
            var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
            string json = SharedSingleExitJson();
            var template = HallowBlaze.Core.Generation.BoardTemplateJson.ReadCompatible(json, OrdinaryBoardContent.CreateAuthoringCatalog());
            string first = new HallowBlaze.Core.Generation.BoardAssignment(world, edge.Segments[1].SegmentId, template, "base").ToJson();
            string last = new HallowBlaze.Core.Generation.BoardAssignment(world, edge.Segments[edge.SegmentCount - 1].SegmentId, template, "base").ToJson();
            string secondJson = json;
            if (fault == "duplicate") last = first;
            if (fault == "variant")
            {
                var assignment = Newtonsoft.Json.Linq.JObject.Parse(last);
                assignment["variantId"] = "missing";
                last = assignment.ToString();
            }
            if (fault == "payload")
            {
                var changed = Newtonsoft.Json.Linq.JObject.Parse(json);
                changed["outcomes"][0]["name"] = "Conflicting destination";
                secondJson = changed.ToString();
            }
            string route = fault == "route" ? world.Edges.First(candidate => candidate.EdgeId != edge.EdgeId).EdgeId : edge.EdgeId;
            if (fault == "version")
            {
                var changed = Newtonsoft.Json.Linq.JObject.Parse(last);
                changed["templateVersion"] = 99;
                last = changed.ToString();
            }
            if (fault == "unsupported")
            {
                var changed = Newtonsoft.Json.Linq.JObject.Parse(worldJson);
                changed["edges"].OfType<Newtonsoft.Json.Linq.JObject>().Single(candidate => (string)candidate["edgeId"] == edge.EdgeId)
                    ["segments"][0]["generationConfigurationId"] = "unsupported.generator";
                worldJson = changed.ToString();
            }
            Assert.That(() => BoardAuthoringTestContext.PrepareRoute(worldJson, route, new[] { json, secondJson }, new[] { first, last },
                64, 100, 100, false, root), Throws.Exception);
            Assert.That(Directory.Exists(root), Is.False);
            Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
        }

        /// <summary>The captured route and all serialized entries survive pending entry recovery independently of caller arrays.</summary>
        [Test]
        public void RouteSandboxCapturesInputsAndStartsAtFirstSegment()
        {
            string worldJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
            var world = WorldDefinitionJson.Deserialize(worldJson);
            var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
            string json = SharedSingleExitJson();
            var template = HallowBlaze.Core.Generation.BoardTemplateJson.ReadCompatible(json, OrdinaryBoardContent.CreateAuthoringCatalog());
            var templates = new[] { json, json };
            var assignments = new[]
            {
                new HallowBlaze.Core.Generation.BoardAssignment(world, edge.Segments[1].SegmentId, template, "base").ToJson(),
                new HallowBlaze.Core.Generation.BoardAssignment(world, edge.Segments[edge.SegmentCount - 1].SegmentId, template, "base").ToJson()
            };
            var context = BoardAuthoringTestContext.PrepareRoute(worldJson, edge.EdgeId, templates, assignments, 64, 100, 100, false, root);
            string before = PersistenceJsonSerializer.SerializeTravelRun(context.CreateInitialRun());
            templates[0] = assignments[0] = "{}";
            typeof(BoardAuthoringTestContext).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            var restored = BoardAuthoringTestContext.Claim(new object());
            var run = restored.CreateInitialRun();
            Assert.That(PersistenceJsonSerializer.SerializeTravelRun(run), Is.EqualTo(before));
            Assert.That(run.ActiveLeg.CurrentSegment.SegmentId, Is.EqualTo(edge.Segments[0].SegmentId));
            Assert.That(restored.SaveRoot, Is.EqualTo(root));
            Assert.That(() => BoardAuthoringTestContext.PrepareRoute(worldJson, edge.EdgeId, templates, assignments, 64, 100, 100, false,
                BoardAuthoringTestContext.CreateSaveRootPath()), Throws.InvalidOperationException);
        }

        /// <summary>Normal commands traverse generated entry and exact captured middle/final boards with one root and one final day commit.</summary>
        [UnityTest]
        public IEnumerator RouteSandboxTravelsGeneratedEntryAndCapturedMiddleAndFinal() => ExerciseRoute(false);

        /// <summary>Normal commands retain the seeded full plan across generated entry, two pooled segments and final arrival.</summary>
        [UnityTest]
        public IEnumerator PoolSandboxTravelsGeneratedEntryAndPooledMiddleAndFinal() => ExerciseRoute(true);

        private IEnumerator ExerciseRoute(bool pooled)
        {
            string worldJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
            var world = WorldDefinitionJson.Deserialize(worldJson);
            var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
            Assert.That(edge.SegmentCount, Is.EqualTo(3), "This fixture exercises the existing three-segment opening route.");
            var middle = Newtonsoft.Json.Linq.JObject.Parse(SharedSingleExitJson());
            var final = Newtonsoft.Json.Linq.JObject.Parse(SharedSingleExitJson());
            middle["templateId"] = "route.middle";
            final["templateId"] = "route.final";
            ((Newtonsoft.Json.Linq.JArray)middle["variants"][0]["entities"]).Add(new Newtonsoft.Json.Linq.JObject
                { ["id"] = 74, ["layer"] = "Obstacle", ["contentId"] = "landmark.boulder", ["x"] = 3, ["y"] = 3 });
            ((Newtonsoft.Json.Linq.JArray)final["variants"][0]["entities"]).Add(new Newtonsoft.Json.Linq.JObject
                { ["id"] = 74, ["layer"] = "Obstacle", ["contentId"] = "landmark.boulder", ["x"] = 4, ["y"] = 4 });
            var templates = new[] { middle.ToString(), final.ToString() };
            var assignments = templates.Select((json, index) => new HallowBlaze.Core.Generation.BoardAssignment(world,
                edge.Segments[index + 1].SegmentId, HallowBlaze.Core.Generation.BoardTemplateJson.ReadCompatible(json,
                    OrdinaryBoardContent.CreateAuthoringCatalog()), "base").ToJson()).ToArray();
            BoardAuthoringTestContext context;
            if (pooled)
            {
                var entries = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject
                    { ["templateId"] = "route.middle", ["templateVersion"] = 1, ["variantId"] = "base" },
                    new Newtonsoft.Json.Linq.JObject { ["templateId"] = "route.final", ["templateVersion"] = 1, ["variantId"] = "base" });
                var manifest = new Newtonsoft.Json.Linq.JObject
                {
                    ["schemaVersion"] = 1, ["publicationId"] = "play.pool-route", ["publicationVersion"] = 1,
                    ["worldDefinitionId"] = world.WorldDefinitionId, ["worldVersion"] = world.Version, ["edgeId"] = edge.EdgeId,
                    ["sources"] = new Newtonsoft.Json.Linq.JArray(edge.Segments.Skip(1).Select(segment => new Newtonsoft.Json.Linq.JObject
                    {
                        ["segmentId"] = segment.SegmentId, ["generationConfigurationId"] = segment.GenerationConfigurationId,
                        ["generationVersion"] = segment.GenerationVersion, ["poolId"] = "play.shared-pool", ["poolVersion"] = 1,
                        ["entries"] = entries.DeepClone()
                    }))
                };
                context = BoardAuthoringTestContext.PreparePoolRoute(worldJson, manifest.ToString(), templates, 64, 100, 100, true, root);
            }
            else context = BoardAuthoringTestContext.PrepareRoute(worldJson, edge.EdgeId, templates, assignments, 64, 100, 100, false, root);
            RouteBoardPlan pinned = context.CreateInitialRun().ActiveLeg.BoardPlan;
            templates[0] = assignments[0] = "{}";
            int reloads = 0;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> loaded = (scene, mode) => { if (scene.name == "Main") reloads++; };
            SceneManager.sceneLoaded += loaded;
            try
            {
                yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
                yield return null;
                Component manager = Manager();
                BoardRuntime previous = null;
                BoardState previousBoard = null;
                RunState run = null;
                var addresses = new HashSet<BoardAddress>();
                for (int index = 0; index < edge.SegmentCount; index++)
                {
                    Invoke(manager, "HideLevelImage");
                    yield return null;
                    BoardRuntime runtime = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
                    Assert.That(runtime, Is.Not.Null, "Missing runtime for segment " + index);
                    BoardState board = runtime.BoardState;
                    if (run == null) run = runtime.RunState;
                    Assert.That(runtime.RunState, Is.SameAs(run));
                    Assert.That(runtime.Request.Address.SegmentId, Is.EqualTo(edge.Segments[index].SegmentId));
                    Assert.That(runtime.Request.Address.GenerationConfigurationId, Is.EqualTo(edge.Segments[index].GenerationConfigurationId));
                    Assert.That(addresses.Add(runtime.Request.Address), Is.True);
                    Assert.That(run.CompletedDayCount, Is.Zero);
                    Assert.That(run.ActiveLandmark, Is.Null);
                    if (pooled)
                    {
                        Assert.That(run.ActiveLeg.BoardPlan.PublicationHash, Is.EqualTo(pinned.PublicationHash));
                        Assert.That(run.ActiveLeg.BoardPlan.Selections.Select(selection => selection.TemplateId), Is.EqualTo(pinned.Selections.Select(selection => selection.TemplateId)));
                        Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(3));
                        Assert.That(run.ToolInstanceSequence, Is.EqualTo(1));
                        var durable = new FileSystemSaveStore(root).LoadRun();
                        Assert.That(durable.IsSuccess, Is.True);
                        Assert.That(durable.Data.ActiveLeg.BoardPlan.Selections.Select(selection => selection.TemplateId), Is.EqualTo(pinned.Selections.Select(selection => selection.TemplateId)));
                        Assert.That(BoardAddressFactory.Create(durable.Data, world), Is.EqualTo(runtime.Request.Address));
                    }
                    Assert.That(context.SaveRoot, Is.EqualTo(root));
                    Assert.That(context.IsSharedBoard(runtime.Request), Is.EqualTo(index > 0));
                    if (previous != null)
                    {
                        Assert.That(previous.IsDisposed, Is.True);
                        Assert.That(board, Is.Not.SameAs(previousBoard));
                        string selected = pooled ? pinned.Selections.Single(selection => selection.SegmentId == edge.Segments[index].SegmentId).TemplateId :
                            index == 1 ? "route.middle" : "route.final";
                        Assert.That(runtime.BoardState.GetEntities().Single(entity => entity.Definition.ContentId == "landmark.boulder").Position,
                            Is.EqualTo(selected == "route.middle" ? new GridPosition(3, 3) : new GridPosition(4, 4)));
                        if (pooled && index == 1)
                            Assert.That(board.TryRemove(new HallowBlaze.Core.Board.Primitives.EntityId(74)), Is.True);
                    }
                    BoardEventPresenter presenter = Property<BoardEventPresenter>(manager, "ActiveBoardPresenter");
                    presenter.AnimationsEnabled = false;
                    if (index > 0)
                    {
                        int food = run.Satiety;
                        var rejected = presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.West));
                        while (!rejected.IsCompleted) yield return null;
                        Assert.That(run.Satiety, Is.EqualTo(food), "Rejected input must remain free on every captured board.");
                    }
                    int foodBeforeExit = run.Satiety;
                    int healthBeforeExit = run.Health;
                    Direction[] path = ExitPath(runtime).ToArray();
                    bool failCheckpoint = pooled && index == 1;
                    string checkpointPath = Path.Combine(root, "run.json");
                    string retainedPath = Path.Combine(root, "test-retained-run.json");
                    if (failCheckpoint)
                    {
                        File.Move(checkpointPath, retainedPath);
                        Directory.CreateDirectory(checkpointPath);
                        LogAssert.Expect(LogType.Error, "Segment checkpoint failed: IoError");
                    }
                    foreach (Direction direction in path)
                    {
                        var pending = presenter.Coordinator.SubmitAsync(new MoveCommand(direction));
                        while (!pending.IsCompleted) yield return null;
                        if (!failCheckpoint || !runtime.Controller.IsTerminal)
                            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented), "Segment " + index + ": " + pending.Result.Diagnostic?.Error);
                    }
                    if (failCheckpoint)
                    {
                        Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(1));
                        Assert.That(run.CompletedDayCount, Is.Zero);
                        Assert.That(run.ActiveLeg.BoardPlan.PublicationHash, Is.EqualTo(pinned.PublicationHash));
                        object pendingOutcome = managerType.GetField("pendingBoardOutcome", Fields).GetValue(manager);
                        Assert.That(pendingOutcome, Is.Not.Null);
                        Directory.Delete(checkpointPath);
                        File.Move(retainedPath, checkpointPath);
                        int chargedFood = run.Satiety;
                        Assert.That((bool)managerType.GetMethod("HandleBoardOutcome", Fields).Invoke(manager, new[] { pendingOutcome }), Is.True);
                        Assert.That(run.Satiety, Is.EqualTo(chargedFood), "Retrying the boundary must not charge another move.");
                        Assert.That(run.ActiveLeg.BoardPlan.Selections.Select(selection => selection.TemplateId), Is.EqualTo(pinned.Selections.Select(selection => selection.TemplateId)));
                    }
                    if (index > 0)
                    {
                        Assert.That(run.Satiety, Is.EqualTo(foodBeforeExit - path.Length), "Each accepted move costs one Food, including exit.");
                        Assert.That(run.Health, Is.EqualTo(healthBeforeExit));
                    }
                    if (index + 1 < edge.SegmentCount)
                    {
                        float deadline = Time.realtimeSinceStartup + 5;
                        while (ReferenceEquals(Property<BoardRuntime>(manager, "ActiveBoardRuntime"), runtime) && Time.realtimeSinceStartup < deadline) yield return null;
                        Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(index + 1));
                        Assert.That(run.CompletedDayCount, Is.Zero);
                    }
                    previous = runtime;
                    previousBoard = board;
                }
                Assert.That(reloads, Is.EqualTo(3), "Each segment must load through the real gameplay scene boundary.");
                Assert.That(run.CompletedDayCount, Is.EqualTo(1));
                Assert.That(run.WorldNodeId, Is.EqualTo(edge.ToNodeId));
                Assert.That(run.ActiveLeg, Is.Null);
                Assert.That(run.CompletedLandmarks, Is.Empty);
                Assert.That(context.IsDisposed, Is.True, "Final arrival must revoke this one-route sandbox.");
                Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
                Assert.That(context.RetainedSaveRoot, Is.EqualTo(root));
                Assert.That(() => context.SaveRoot, Throws.InvalidOperationException);
                Assert.That(new FileSystemSaveStore(root).LoadRun().IsSuccess, Is.True);
            }
            finally { SceneManager.sceneLoaded -= loaded; }
        }

        /// <summary>The chosen middle segment preserves its real full address; completion does not consume a day.</summary>
        [UnityTest]
        public IEnumerator SharedAssignedIntermediateRetainsDayAndContinuesActualRoute() => ExerciseAssignment(0);

        /// <summary>A shared final map at the concrete Radio Tower node completes normal arrival, without a legacy record.</summary>
        [UnityTest]
        public IEnumerator SharedAssignedLandmarkCompletesNormalArrivalWithoutLegacySelection() => ExerciseAssignment(1);

        /// <summary>A non-landmark final board completes the day once and returns to the real route-choice boundary.</summary>
        [UnityTest]
        public IEnumerator SharedAssignedOrdinaryFinalCompletesDayOnce() => ExerciseAssignment(2);

        private IEnumerator ExerciseAssignment(int targetKind)
        {
            string worldJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
            var world = WorldDefinitionJson.Deserialize(worldJson);
            var edge = targetKind == 1 ? world.Edges.First(candidate => candidate.ToNodeId == world.GoalNodeId) : world.Edges[0];
            int index = targetKind == 0 ? 1 : edge.SegmentCount - 1;
            var segment = edge.Segments[index];
            var context = BoardAuthoringTestContext.PrepareAssigned(worldJson, SharedSingleExitJson(), segment.SegmentId, "base", 64, 100, 100, false, root);
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            Invoke(manager, "HideLevelImage");
            yield return null;
            BoardRuntime runtime = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            RunState run = runtime.RunState;
            Assert.That(runtime.Request.Address.EdgeId, Is.EqualTo(edge.EdgeId));
            Assert.That(runtime.Request.Address.SegmentId, Is.EqualTo(segment.SegmentId));
            Assert.That(runtime.Request.Address.GenerationConfigurationId, Is.EqualTo(segment.GenerationConfigurationId));
            Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(index));
            Assert.That(run.ActiveLandmark, Is.Null);
            BoardEventPresenter presenter = Property<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            presenter.AnimationsEnabled = false;
            var rejected = presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.West));
            while (!rejected.IsCompleted) yield return null;
            Assert.That(run.Satiety, Is.EqualTo(100), "Rejected command must not spend food.");
            foreach (Direction direction in Enumerable.Repeat(Direction.East, 7).Concat(Enumerable.Repeat(Direction.North, 7)))
            {
                var pending = presenter.Coordinator.SubmitAsync(new MoveCommand(direction));
                while (!pending.IsCompleted) yield return null;
                Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented), pending.Result.Diagnostic?.Error?.ToString());
            }
            Assert.That(run.Satiety, Is.EqualTo(86));
            Assert.That(run.CompletedLandmarks, Is.Empty);
            if (targetKind == 0)
            {
                float deadline = Time.realtimeSinceStartup + 5;
                while (ReferenceEquals(Property<BoardRuntime>(manager, "ActiveBoardRuntime"), runtime) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(run.CompletedDayCount, Is.Zero);
                Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(index + 1));
                Assert.That(context.SaveRoot, Is.EqualTo(root));
            }
            else
            {
                Assert.That(run.CompletedDayCount, Is.EqualTo(1));
                Assert.That(run.WorldNodeId, Is.EqualTo(edge.ToNodeId));
                Assert.That(run.ActiveLeg, Is.Null);
                Assert.That(run.Status, Is.EqualTo(targetKind == 1 ? RunStatus.Won : RunStatus.Active));
            }
        }

        [UnityTest]
        public IEnumerator ActualIntermediateExitReloadsExactLandmarkWithoutChangingRootOrDay()
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json"));
            var context = BoardAuthoringTestContext.PrepareLocal(json, "east-guard", 64, 100, 100, false, root, true);
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            Invoke(manager, "HideLevelImage");
            yield return null;
            BoardRuntime first = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(first.Request.Address.SegmentId, Is.EqualTo("authoring.approach"));
            RunState run = first.RunState;
            BoardEventPresenter presenter = Property<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            presenter.AnimationsEnabled = false;
            foreach (Direction direction in ExitPath(first))
            {
                var pending = presenter.Coordinator.SubmitAsync(new MoveCommand(direction));
                while (!pending.IsCompleted) yield return null;
                Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented), pending.Result.Diagnostic?.Error?.ToString());
            }
            float deadline = Time.realtimeSinceStartup + 5;
            BoardRuntime next;
            do
            {
                yield return null;
                next = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            } while ((next == null || ReferenceEquals(next, first)) && Time.realtimeSinceStartup < deadline);
            Assert.That(next, Is.Not.Null);
            Assert.That(next, Is.Not.SameAs(first));
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(next.Request.Address.SegmentId, Is.EqualTo("authoring.segment"));
            Assert.That(next.RunState, Is.SameAs(run));
            Assert.That(run.CompletedDayCount, Is.Zero);
            Assert.That(run.CurrentDay, Is.EqualTo(1));
            Assert.That(run.ActiveLandmark.VariantId, Is.EqualTo("east-guard"));
            Assert.That(context.SaveRoot, Is.EqualTo(root));
            Assert.That(context.IsDisposed, Is.False);
        }

        [UnityTest]
        public IEnumerator MenuAfterSandboxInspectsOnlyItsRetainedIsolatedRoot()
        {
            var context = Prepare();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            Assert.That((bool)Invoke(manager, "ExitToMenu"), Is.True);
            Assert.That(context.IsDisposed, Is.True);
            yield return SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);
            yield return null;
            string inspectedRoot = (string)managerType.GetMethod("GetPersistenceRoot", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
            Assert.That(inspectedRoot, Is.EqualTo(root));
            Assert.That(inspectedRoot, Is.Not.EqualTo(sentinelRoot));
            Assert.That(new FileSystemSaveStore(root).LoadRun().IsSuccess, Is.True);
        }

        [UnityTest]
        public IEnumerator ColdFailedLaunchNeverReadsOrWritesAudioPreferences()
        {
            Prepare();
            Directory.CreateDirectory(Path.Combine(root, "run.json"));
            int reads = 0;
            int writes = 0;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> intercept = (scene, mode) =>
            {
                if (scene.name != "Main") return;
                Component sound = (Component)soundType.GetField("instance").GetValue(null);
                soundType.GetField("ReadPreference", Fields).SetValue(sound,
                    (Func<string, string, string>)((key, fallback) => { reads++; return fallback; }));
                soundType.GetField("WritePreference", Fields).SetValue(sound,
                    (Action<string, string>)((key, value) => writes++));
            };
            SceneManager.sceneLoaded += intercept;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Authoring bootstrap failed: Authoring initial checkpoint failed:"));
            try
            {
                yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
                yield return null;
                Component sound = (Component)soundType.GetField("instance").GetValue(null);
                Invoke(sound, "Start");
                Invoke(sound, "SetMusicEnabled", false);
                Invoke(sound, "SetSoundEnabled", false);
                Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
                Assert.That(reads, Is.Zero);
                Assert.That(writes, Is.Zero);
            }
            finally { SceneManager.sceneLoaded -= intercept; }
        }

        [UnityTest]
        public IEnumerator PresentationFailureRevokesContextBeforeReleasingItsRuntime()
        {
            var context = Prepare();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            Invoke(manager, "HideLevelImage");
            yield return null;
            BoardRuntime runtime = Property<BoardRuntime>(manager, "ActiveBoardRuntime");
            BoardEventPresenter presenter = Property<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            presenter.AnimationsEnabled = false;
            UnityEngine.Object.DestroyImmediate(runtime.Views[runtime.PlayerId]);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Board presentation failed:"));
            var pending = presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.North));
            while (!pending.IsCompleted) yield return null;
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.PresentationFailed));
            Assert.That(context.IsDisposed, Is.True);
            float deadline = Time.realtimeSinceStartup + 2;
            while (!runtime.IsDisposed && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(runtime.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator FailedIntroCheckpointRevokesSandboxWithoutPublishingABoard()
        {
            var context = Prepare();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            Component manager = Manager();
            WorldEdgeDefinition edge = context.World.Edges.Single(candidate => candidate.EdgeId == "road.ridge-radio-tower");
            var pending = new RunState("authoring-intro-failure", 64, new RunStateConfiguration(100, 100, 4, edge.FromNodeId));
            pending.StartLeg(edge.EdgeId, edge.FromNodeId, edge.ToNodeId, edge.Segments.Select(segment =>
                new RouteSegmentState(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion)));
            Property<GameSession>(manager, "Session").ContinueRun(pending);
            managerType.GetField("saveStore", Fields).SetValue(manager,
                new FileSystemSaveStore(root, (source, destination, backup) => { throw new IOException("Injected intro checkpoint failure"); }));
            Invoke(manager, "TryEnsureWorldMap");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Day intro checkpoint failed:"));
            Invoke(manager, "InitGame");
            Assert.That(context.IsDisposed, Is.True);
            Assert.That(Property<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Null);
        }

        private static IEnumerable<Direction> ExitPath(BoardRuntime runtime)
        {
            var entities = runtime.BoardState.GetEntities().ToArray();
            GridPosition origin = entities.Single(entity => entity.Id.Equals(runtime.PlayerId)).Position;
            GridPosition destination = entities.Single(entity => entity.Definition.Traits.IsExit).Position;
            var walkable = new HashSet<GridPosition>(entities.Where(entity => entity.Definition.Layer == BoardLayer.Terrain &&
                entity.Definition.Traits.IsWalkable).Select(entity => entity.Position));
            foreach (var entity in entities.Where(entity => entity.Definition.Layer == BoardLayer.Obstacle ||
                entity.Definition.Layer == BoardLayer.Actor && !entity.Id.Equals(runtime.PlayerId)))
                walkable.Remove(entity.Position);
            var queue = new Queue<GridPosition>();
            var previous = new Dictionary<GridPosition, (GridPosition Position, Direction Direction)>();
            queue.Enqueue(origin);
            previous.Add(origin, (origin, default));
            while (queue.Count != 0 && !previous.ContainsKey(destination))
            {
                GridPosition position = queue.Dequeue();
                foreach (Direction direction in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                {
                    var next = new GridPosition(position.X + direction.DeltaX, position.Y + direction.DeltaY);
                    if (!walkable.Contains(next) || previous.ContainsKey(next)) continue;
                    previous.Add(next, (position, direction));
                    queue.Enqueue(next);
                }
            }
            Assert.That(previous.ContainsKey(destination), Is.True, "Static exit path for " + runtime.Request.Address);
            var path = new List<Direction>();
            for (GridPosition position = destination; !position.Equals(origin); position = previous[position].Position)
                path.Add(previous[position].Direction);
            path.Reverse();
            return path;
        }

        private Component Manager() => (Component)managerType.GetField("instance").GetValue(null);

        private static void DestroySingleton(Type type)
        {
            var singleton = (Component)type.GetField("instance").GetValue(null);
            if (singleton != null) UnityEngine.Object.DestroyImmediate(singleton.gameObject);
        }

        private static object Invoke(Component target, string method, params object[] arguments) =>
            target.GetType().GetMethod(method, Fields).Invoke(target, arguments);

        private static T Property<T>(Component target, string name) =>
            (T)target.GetType().GetProperty(name, Fields).GetValue(target);

        private BoardAuthoringTestContext Prepare()
        {
            string worldJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
            WorldDefinition world = WorldDefinitionJson.Deserialize(worldJson);
            world.TryGetEdge("road.ridge-radio-tower", out WorldEdgeDefinition edge);
            var run = new RunState("authoring-isolation", 64, new RunStateConfiguration(100, 100, 4, edge.FromNodeId));
            run.StartLeg(edge.EdgeId, edge.FromNodeId, edge.ToNodeId, edge.Segments.Select(segment =>
                new RouteSegmentState(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion)));
            run.TryConsumeIntro(run.ActiveLeg.LegId);
            while (!run.ActiveLeg.IsFinalSegment)
                run.TryAdvanceSegment(run.ActiveLeg.LegId, run.ActiveLeg.CurrentSegment.SegmentId);
            string templateJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json"));
            return BoardAuthoringTestContext.Prepare(worldJson, templateJson, PersistenceJsonSerializer.SerializeTravelRun(run),
                edge.Segments[edge.SegmentCount - 1].SegmentId, "base", root, PersistenceJsonSerializer.DeserializeTravelRun);
        }
    }
}
#endif