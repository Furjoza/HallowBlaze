using System.IO;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Generation;
using HallowBlaze.Editor.BoardAuthoring;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Exercises Editor recovery, explicit local inputs, bounded search and the actual Edit/Play/Edit boundary.</summary>
    public sealed class BoardAuthoringTestPlayTests
    {
        /// <summary>Recovery retains independent undo/redo owners and the exact inspected cell without saving the draft.</summary>
        [Test]
        public void RecoveryPreservesDirtyStateSelectionAndIndependentUndoRedo()
        {
            var document = Draft();
            document.Edit(data => data["templateVersion"] = 2);
            document.Edit(data => data["templateVersion"] = 3);
            document.Undo();
            var grid = new BoardGridView();
            grid.ChooseContent("landmark.boulder");
            grid.Select(new GridPosition(3, 6));
            string capture = BoardDraftRecovery.Capture(document, "base", grid, "external-draft");
            var restoredGrid = new BoardGridView();
            var restored = BoardDraftRecovery.Restore(capture, restoredGrid, out string variant, out string root);
            Assert.That(restored.Snapshot().ToString(), Is.EqualTo(document.Snapshot().ToString()));
            Assert.That(restored.IsDirty, Is.EqualTo(document.IsDirty));
            Assert.That(restoredGrid.SelectedCell, Is.EqualTo(new GridPosition(3, 6)));
            Assert.That(restoredGrid.ContentId, Is.EqualTo("landmark.boulder"));
            Assert.That(variant, Is.EqualTo("base"));
            Assert.That(root, Is.EqualTo("external-draft"));
            Assert.That(restored.Redo(), Is.True);
            Assert.That((int)restored.Snapshot()["templateVersion"], Is.EqualTo(3));
            Assert.That((int)document.Snapshot()["templateVersion"], Is.EqualTo(2));
            Assert.That(restored.Undo(), Is.True);
            Assert.That(restored.Undo(), Is.True);
            Assert.That((int)restored.Snapshot()["templateVersion"], Is.EqualTo(1));
        }

        /// <summary>Search and replay use the production controller for each selected variant, outcome and equipment choice.</summary>
        [TestCase("base", "tower.arrival", false)]
        [TestCase("east-guard", "tower.supplies", true)]
        [TestCase("north-push", "tower.supplies", false)]
        public void SolverRunsExistingRulesThenReplaysRequestedExit(string variant, string outcome, bool axe)
        {
            var document = Draft();
            string before = document.ToJson();
            var result = BoardSolverAdapter.Solve(before, variant, outcome, new BoardTestPlaySettings { EquipAxe = axe });
            Assert.That(result.Status, Is.EqualTo("Solved"), result.Message);
            Assert.That(result.Replayed, Is.True, result.Message);
            Assert.That(document.ToJson(), Is.EqualTo(before));
            Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
        }

        /// <summary>Budget exhaustion is always incomplete evidence rather than an exhaustive NoSolution claim.</summary>
        [TestCase(1, 32, 10000, "StateLimit")]
        [TestCase(50000, 0, 10000, "DepthLimit")]
        [TestCase(50000, 32, 0, "TimeLimit")]
        public void SolverLimitsAreNotNoSolution(int states, int depth, int time, string status)
        {
            var result = BoardSolverAdapter.Solve(Draft().ToJson(), "base", "tower.arrival", new BoardTestPlaySettings(), states, depth, time);
            Assert.That(result.Status, Is.EqualTo(status), result.Message);
            Assert.That(result.Replayed, Is.False);
        }

        /// <summary>A pending launch is immutable and exclusive; Stop restores selection/history and the previous start scene.</summary>
        [Test]
        public void LauncherPreservesSnapshotAndUndoOnStopWithoutEnteringPlayMode()
        {
            WithWindow(window =>
            {
                window.Document.Edit(data => data["templateVersion"] = 2);
                window.Document.Edit(data => data["templateVersion"] = 3);
                window.Document.Undo();
                window.Grid.Select(new GridPosition(4, 5));
                var previous = EditorSceneManager.playModeStartScene;
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings { Seed = -17, EquipAxe = true },
                    () => false, () => true, () => { }), Is.True, BoardTestPlayLauncher.LastMessage);
                var snapshot = BoardAuthoringTestContext.Active;
                Assert.That(snapshot.CreateInitialRun().RunSeed, Is.EqualTo(-17));
                Assert.That(snapshot.Template.Version, Is.EqualTo(2));
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => true, () => true, () => { }), Is.False);
                BoardTestPlayLauncher.Stop();
                Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False, BoardTestPlayLauncher.LastMessage);
                Assert.That(snapshot.IsDisposed, Is.True);
                Assert.That(window.Grid.SelectedCell, Is.EqualTo(new GridPosition(4, 5)));
                Assert.That(window.Document.IsDirty, Is.True);
                Assert.That(window.Document.Redo(), Is.True);
                Assert.That((int)window.Document.Snapshot()["templateVersion"], Is.EqualTo(3));
                Assert.That(EditorSceneManager.playModeStartScene, Is.EqualTo(previous));
            });
        }

        /// <summary>Malformed, unsupported-version and genuinely exhausted searches remain distinguishable.</summary>
        [Test]
        public void InvalidAndExhaustedSolverResultsRemainDistinct()
        {
            var invalid = BoardSolverAdapter.Solve("{}", "base", "tower.arrival", new BoardTestPlaySettings());
            Assert.That(invalid.Status, Is.EqualTo("Invalid"));
            JObject unsupported = Draft().Snapshot();
            unsupported["schemaVersion"] = 3;
            Assert.That(BoardSolverAdapter.Solve(unsupported.ToString(), "base", "tower.arrival", new BoardTestPlaySettings()).Status, Is.EqualTo("Unsupported"));
            var exhausted = BoardSolverAdapter.Solve(Draft().ToJson(), "base", "tower.arrival", new BoardTestPlaySettings { Food = 1 });
            Assert.That(exhausted.Status, Is.EqualTo("NoSolution"), exhausted.Message);
            Assert.That(exhausted.Replayed, Is.False);
        }

        /// <summary>Failed Play entry revokes its context and a later test reserves a different sandbox without replacing the draft.</summary>
        [Test]
        public void FailedLaunchAndRepeatTestDoNotReplaceTheDraftOrLeavePendingContext()
        {
            WithWindow(window =>
            {
                var original = window.Document;
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => true,
                    () => throw new InvalidOperationException("Injected Play entry failure")), Is.False);
                Assert.That(BoardTestPlayLauncher.LastMessage, Does.Contain("Injected Play entry failure"));
                BoardTestPlayLauncher.Stop();
                Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
                Assert.That(window.Document.ToJson(), Is.EqualTo(original.ToJson()));
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => true, () => { }), Is.True);
                string firstRoot = BoardAuthoringTestContext.Active.SaveRoot;
                BoardTestPlayLauncher.Stop();
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => true, () => { }), Is.True);
                Assert.That(BoardAuthoringTestContext.Active.SaveRoot, Is.Not.EqualTo(firstRoot));
                BoardTestPlayLauncher.Stop();
            });
        }

        /// <summary>Declining to resolve a dirty scene neither starts Play Mode nor publishes a context.</summary>
        [Test]
        public void DirtySceneCancellationDoesNotPublishOrEnterPlayMode()
        {
            WithWindow(window =>
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                bool entered = false;
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => false, () => entered = true), Is.False);
                Assert.That(entered, Is.False);
                Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
                Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False);
                Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);
            });
        }

        /// <summary>Cancel leaves the saved draft, scene setup and launcher untouched; Start freezes the shown publication and inputs.</summary>
        [Test]
        public void RoutePreviewCancelAndStartRetainExactCapturedLibrary()
        {
            WithWindow(window =>
            {
                var world = window.Assignment.World;
                var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
                var template = BoardTemplateJson.ReadCompatible(window.Document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                window.Assignment.Select(new BoardAssignment(world, edge.Segments[1].SegmentId, template, window.SelectedVariant));
                Assert.That(window.TrySaveNamed("Route Middle"), Is.True, window.LastError);
                File.Copy(window.Document.SourcePath, Path.Combine(Path.GetDirectoryName(window.Document.SourcePath), "identical-copy.json"));
                string before = (string)typeof(BoardAuthoringWindow).GetMethod("CaptureTestRecovery", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                var scenes = EditorSceneManager.GetSceneManagerSetup();
                bool entered = false;
                var settings = new BoardTestPlaySettings { Seed = 91, Health = 88, Food = 77, EquipAxe = true };
                Assert.That(BoardTestPlayLauncher.TryLaunchRoute(window, settings, preview =>
                {
                    Assert.That(preview, Does.Contain("1/3 | " + edge.Segments[0].SegmentId));
                    Assert.That(preview, Does.Contain("Generated | ordinary.board.v1/6"));
                    Assert.That(preview, Does.Contain("Route Middle | " + template.Id + "/" + template.Version + " | base"));
                    Assert.That(preview, Does.Contain("Seed 91 | Health 88 | Food 77 | Axe Yes"));
                    return false;
                }, () => false, () => true, () => entered = true), Is.False);
                Assert.That(entered, Is.False);
                Assert.That(BoardAuthoringTestContext.IsRequested || BoardTestPlayLauncher.HasRecovery, Is.False);
                Assert.That((string)typeof(BoardAuthoringWindow).GetMethod("CaptureTestRecovery", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null), Is.EqualTo(before));
                Assert.That(EditorSceneManager.GetSceneManagerSetup().Select(scene => scene.path), Is.EqualTo(scenes.Select(scene => scene.path)));
                Assert.That(BoardTestPlayLauncher.TryLaunchRoute(window, settings, _ =>
                {
                    File.WriteAllText(window.Document.SourcePath, "{}");
                    settings.Seed = 999;
                    return true;
                }, () => false, () => true, () => entered = true), Is.True, BoardTestPlayLauncher.LastMessage);
                var context = BoardAuthoringTestContext.Active;
                Assert.That(entered, Is.True);
                Assert.That(context.CreateInitialRun().RunSeed, Is.EqualTo(91));
                Assert.That(context.Template.Id, Is.EqualTo(template.Id));
                BoardTestPlayLauncher.Stop();
                Assert.That(context.IsDisposed, Is.True);
                Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False);
                Assert.That(window.Assignment.SegmentId, Is.EqualTo(edge.Segments[1].SegmentId));
                Assert.That(window.Document.IsDirty, Is.False);
                Assert.That(window.Document.ToJson(), Does.Contain(template.Id));
            });
        }

        /// <summary>Unsaved changes, external drift, missing publications and duplicate segment bindings cannot publish a route context.</summary>
        [TestCase("dirty")]
        [TestCase("external")]
        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("malformed")]
        [TestCase("conflicting")]
        public void RoutePreviewRejectsInvalidSavedLibrary(string fault)
        {
            WithWindow(window =>
            {
                var world = window.Assignment.World;
                var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
                var template = BoardTemplateJson.ReadCompatible(window.Document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                var assignment = new BoardAssignment(world, edge.Segments[1].SegmentId, template, window.SelectedVariant);
                window.Assignment.Select(assignment);
                Assert.That(window.TrySaveNamed("Route Middle"), Is.True, window.LastError);
                string root = Path.GetDirectoryName(window.Document.SourcePath);
                string binding = Directory.GetFiles(root, "*.assignment.json").Single();
                if (fault == "dirty") window.Document.Edit(data => data["templateVersion"] = 2);
                if (fault == "external") File.AppendAllText(window.Document.SourcePath, " ");
                if (fault == "missing")
                {
                    JObject data = JObject.Parse(assignment.ToJson());
                    data["templateId"] = "missing.template";
                    File.WriteAllText(Path.Combine(root, "orphan.json"), data.ToString());
                }
                if (fault == "duplicate") File.Copy(binding, Path.Combine(root, "duplicate.assignment.json"));
                if (fault == "malformed") File.WriteAllText(Path.Combine(root, "incomplete.assignment.json"), "{}");
                if (fault == "conflicting")
                {
                    JObject copy = window.Document.Snapshot();
                    copy["outcomes"][0]["name"] = "Different publication with the same identity";
                    File.WriteAllText(Path.Combine(root, "conflicting-copy.json"), copy.ToString());
                }
                bool previewed = false;
                Assert.That(BoardTestPlayLauncher.TryLaunchRoute(window, new BoardTestPlaySettings(), _ => { previewed = true; return true; },
                    () => false, () => true, () => Assert.Fail("Invalid route must not enter Play Mode.")), Is.False);
                Assert.That(previewed, Is.False);
                Assert.That(BoardTestPlayLauncher.LastMessage, Is.Not.Empty);
                Assert.That(BoardAuthoringTestContext.IsRequested || BoardTestPlayLauncher.HasRecovery, Is.False);
            });
        }

        /// <summary>A preview cannot launch after the selected draft or route target changes while consent is pending.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RoutePreviewRefusesStaleDraftOrTarget(bool target)
        {
            WithWindow(window =>
            {
                var world = window.Assignment.World;
                var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
                var template = BoardTemplateJson.ReadCompatible(window.Document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                window.Assignment.Select(new BoardAssignment(world, edge.Segments[1].SegmentId, template, window.SelectedVariant));
                Assert.That(window.TrySaveNamed("Route Middle"), Is.True, window.LastError);
                Assert.That(BoardTestPlayLauncher.TryLaunchRoute(window, new BoardTestPlaySettings(), _ =>
                {
                    if (target) window.Assignment.Select(new BoardAssignment(world, edge.Segments[2].SegmentId, template, window.SelectedVariant));
                    else window.Document.Edit(data => data["outcomes"][0]["name"] = "Changed after preview");
                    return true;
                }, () => false, () => true, () => Assert.Fail("A stale route preview must not enter Play Mode.")), Is.False);
                Assert.That(BoardTestPlayLauncher.LastMessage, Does.Contain("changed during preview"));
                Assert.That(BoardAuthoringTestContext.IsRequested || BoardTestPlayLauncher.HasRecovery, Is.False);
            });
        }

        /// <summary>The captured route survives real Play entry and Stop restores named board, history, selection and multi-scene setup.</summary>
        [UnityTest]
        public IEnumerator RouteActualPlayAndStopRecoverSavedBoardAndScenes()
        {
            const string expectedKey = "HallowBlaze.Tests.RouteRoundTripExpected";
            string root = Path.Combine(Path.GetTempPath(), "HB-RouteRoundTrip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Scene menu = EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Additive);
            SceneManager.SetActiveScene(menu);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Menu.unity");
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.Show();
            Assert.That(window.TryConfigureRoot(root), Is.True);
            Assert.That(window.TryOpen(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json"), () => BoardDocumentDecision.Discard), Is.True);
            Assert.That(window.TryFork("draft.route-round-trip", "draft.route-round-trip.config", () => BoardDocumentDecision.Discard), Is.True);
            window.Document.Edit(data => data["templateVersion"] = 2);
            window.Document.Undo();
            var edge = window.Assignment.World.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
            var template = BoardTemplateJson.ReadCompatible(window.Document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
            window.Assignment.Select(new BoardAssignment(window.Assignment.World, edge.Segments[1].SegmentId, template, "base"));
            Assert.That(window.TrySaveNamed("Round Trip Middle"), Is.True, window.LastError);
            window.Grid.ChooseContent("landmark.boulder");
            window.Grid.Select(new GridPosition(3, 6));
            SessionState.SetString(expectedKey, new JObject
            {
                ["window"] = (string)typeof(BoardAuthoringWindow).GetMethod("CaptureTestRecovery", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null),
                ["root"] = root
            }.ToString());
            Assert.That(BoardTestPlayLauncher.TryLaunchRoute(window, new BoardTestPlaySettings { Seed = 975, Food = 43, Health = 89, EquipAxe = true },
                _ => true, () => false, () => true, () => { }), Is.True, BoardTestPlayLauncher.LastMessage);
            yield return new EnterPlayMode();
            yield return null;
            yield return null;
            var context = BoardAuthoringTestContext.Active;
            Assert.That(context, Is.Not.Null);
            var managerType = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Assembly-CSharp").GetType("GameManager", true);
            var manager = (Component)managerType.GetField("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            managerType.GetMethod("HideLevelImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(manager, null);
            yield return null;
            var runtime = (BoardRuntime)managerType.GetProperty("ActiveBoardRuntime").GetValue(manager);
            Assert.That(runtime.RunState.RunSeed, Is.EqualTo(975));
            Assert.That(runtime.RunState.Health, Is.EqualTo(89));
            Assert.That(runtime.RunState.Food, Is.EqualTo(43));
            Assert.That(runtime.RunState.CompletedDayCount, Is.Zero);
            Assert.That(runtime.RunState.ActiveLeg.SegmentIndex, Is.Zero);
            Assert.That(runtime.Request.Address.EdgeId, Is.EqualTo("road.shelter-warm-up"));
            Assert.That(context.IsSharedBoard(runtime.Request), Is.False, "The saved middle assignment must not replace the generated entry.");
            yield return new ExitPlayMode();
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (BoardTestPlayLauncher.HasRecovery && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False, BoardTestPlayLauncher.LastMessage);
            Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
            Assert.That(EditorSceneManager.GetSceneManagerSetup().Select(scene => scene.path), Is.EqualTo(new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Main.unity" }));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo("Assets/Scenes/Menu.unity"));
            Assert.That(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), Is.EqualTo("Assets/Scenes/Menu.unity"));
            JObject expected = JObject.Parse(SessionState.GetString(expectedKey, ""));
            window = Resources.FindObjectsOfTypeAll<BoardAuthoringWindow>().Single(candidate => (string)candidate.Document?.Snapshot()["templateId"] == "draft.route-round-trip");
            Assert.That((string)typeof(BoardAuthoringWindow).GetMethod("CaptureTestRecovery", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null), Is.EqualTo((string)expected["window"]));
            Assert.That(window.Document.Redo(), Is.True);
            Assert.That((int)window.Document.Snapshot()["templateVersion"], Is.EqualTo(2));
            window.CloseAfterDecision(BoardDocumentDecision.Discard);
            Directory.Delete((string)expected["root"], true);
            SessionState.EraseString(expectedKey);
            EditorSceneManager.playModeStartScene = null;
        }

        /// <summary>The real gameplay scene uses exact inputs; domain reload restores draft history and the multi-scene Editor setup.</summary>
        [UnityTest]
        public IEnumerator ActualPlayEntryAndExitRecoverDirtyDraftAndSelection()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M69b-play-draft-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Scene menu = EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Additive);
            SceneManager.SetActiveScene(menu);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Menu.unity");
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.Show();
            Assert.That(window.TryConfigureRoot(root), Is.True);
            Assert.That(window.TryOpen(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json"), () => BoardDocumentDecision.Discard), Is.True);
            Assert.That(window.TryFork("draft.actual-play", "draft.actual-play.config", () => BoardDocumentDecision.Discard), Is.True);
            window.Document.Edit(data => data["templateVersion"] = 2);
            window.Grid.ChooseContent("landmark.boulder");
            window.Grid.Select(new GridPosition(3, 6));
            Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings { Seed = 975, Food = 43, Health = 89 },
                () => false, () => true, () => { }), Is.True, BoardTestPlayLauncher.LastMessage);
            yield return new EnterPlayMode();
            yield return null;
            yield return null;
            var context = BoardAuthoringTestContext.Active;
            Assert.That(context, Is.Not.Null);
            Assert.That(context.Template.Id, Is.EqualTo("draft.actual-play"));
            Assert.That(context.Template.Version, Is.EqualTo(2));
            window = Resources.FindObjectsOfTypeAll<BoardAuthoringWindow>().Single(candidate => (string)candidate.Document?.Snapshot()["templateId"] == "draft.actual-play");
            window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, control = true });
            yield return null;
            Assert.That((int)window.Document.Snapshot()["templateVersion"], Is.EqualTo(2));
            var managerType = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Assembly-CSharp").GetType("GameManager", true);
            var manager = (Component)managerType.GetField("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            Assert.That(manager, Is.Not.Null);
            managerType.GetMethod("HideLevelImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(manager, null);
            yield return null;
            var runtime = (BoardRuntime)managerType.GetProperty("ActiveBoardRuntime").GetValue(manager);
            Assert.That(runtime.RunState.Food, Is.EqualTo(43));
            Assert.That(runtime.RunState.Health, Is.EqualTo(89));
            Assert.That(runtime.RunState.RunSeed, Is.EqualTo(975));
            Assert.That(runtime.RunState.ActiveLandmark, Is.Null, "A shared-board fork must not checkpoint a legacy landmark selection.");
            yield return new ExitPlayMode();
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (BoardTestPlayLauncher.HasRecovery && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False, BoardTestPlayLauncher.LastMessage);
            Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
            Assert.That(EditorSceneManager.GetSceneManagerSetup().Select(scene => scene.path), Is.EqualTo(new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Main.unity" }));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo("Assets/Scenes/Menu.unity"));
            Assert.That(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), Is.EqualTo("Assets/Scenes/Menu.unity"));
            EditorSceneManager.playModeStartScene = null;
            window = Resources.FindObjectsOfTypeAll<BoardAuthoringWindow>().Single(candidate => (string)candidate.Document?.Snapshot()["templateId"] == "draft.actual-play");
            Assert.That(window.Document.IsDirty, Is.True);
            Assert.That(window.Grid.SelectedCell, Is.EqualTo(new GridPosition(3, 6)));
            Assert.That(window.Grid.ContentId, Is.EqualTo("landmark.boulder"));
            Assert.That(window.Document.Undo(), Is.True);
            Assert.That((int)window.Document.Snapshot()["templateVersion"], Is.EqualTo(1));
            string restoredRoot = (string)typeof(BoardAuthoringWindow).GetField("draftRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            window.CloseAfterDecision(BoardDocumentDecision.Discard);
            Directory.Delete(restoredRoot, true);
        }

        /// <summary>Recovery cannot discard a scene authored after a pending entry; cancellation retains both scene and recovery.</summary>
        [Test]
        public void PendingEntryRecoveryProtectsNewDirtyAdditiveScene()
        {
            WithWindow(window =>
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => true, () => { }), Is.True);
                Scene extra = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var marker = new GameObject("Unsaved authoring change");
                SceneManager.MoveGameObjectToScene(marker, extra);
                EditorSceneManager.MarkSceneDirty(extra);
                try
                {
                    BoardTestPlayLauncher.Stop(() => false);
                    Assert.That(BoardTestPlayLauncher.HasRecovery, Is.True);
                    Assert.That(extra.IsValid() && extra.isLoaded && extra.isDirty, Is.True);
                    Assert.That(marker != null, Is.True);
                    Assert.That(BoardTestPlayLauncher.LastMessage, Does.Contain("no scenes were closed"));
                    Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
                    BoardTestPlayLauncher.Stop(() =>
                    {
                        EditorSceneManager.CloseScene(extra, true);
                        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Additive);
                        return true;
                    });
                    Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False, BoardTestPlayLauncher.LastMessage);
                    Assert.That(EditorSceneManager.GetSceneManagerSetup().Select(scene => scene.path), Is.EqualTo(new[] { "Assets/Scenes/Menu.unity" }));
                }
                finally
                {
                    if (extra.IsValid()) EditorSceneManager.CloseScene(extra, true);
                    BoardTestPlayLauncher.Stop(() => false);
                }
                Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False, BoardTestPlayLauncher.LastMessage);
            });
        }

        /// <summary>A clean unnamed scene with its default camera can launch and stop without replacing scene objects.</summary>
        [Test]
        public void CleanUnnamedCameraSceneDoesNotBlockTestOrLoseObjectsOnStop()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount != 1 || !string.IsNullOrEmpty(scene.path) || scene.isDirty || scene.rootCount == 0)
                Assert.Ignore("This regression requires a single clean unnamed scene with existing objects.");
            string root = Path.Combine(Path.GetTempPath(), "HB-UnnamedCamera-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            GameObject[] objects = scene.GetRootGameObjects();
            SceneAsset previousStart = EditorSceneManager.playModeStartScene;
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                Assert.That(window.TryOpen(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json"), () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.unnamed-camera", "draft.unnamed-camera.config", () => BoardDocumentDecision.Discard), Is.True);
                bool entered = false;
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => false,
                    () => entered = true), Is.True, BoardTestPlayLauncher.LastMessage);
                Assert.That(entered, Is.True);
                BoardTestPlayLauncher.Stop();
                Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False, BoardTestPlayLauncher.LastMessage);
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(scene));
                Assert.That(scene.GetRootGameObjects(), Is.EqualTo(objects));
                Assert.That(objects.All(owner => owner != null), Is.True);
                Assert.That(EditorSceneManager.playModeStartScene, Is.EqualTo(previousStart));
                Assert.That(scene.isDirty, Is.False);
            }
            finally
            {
                BoardTestPlayLauncher.Stop(() => false);
                window.CloseAfterDecision(BoardDocumentDecision.Discard);
                Directory.Delete(root, true);
            }
        }

        /// <summary>Unity's real Play Mode round trip restores the clean unnamed scene and camera rather than an empty replacement.</summary>
        [UnityTest]
        public IEnumerator CleanUnnamedCameraSceneSurvivesActualPlayRoundTrip()
        {
            Scene scene = SceneManager.GetActiveScene();
            Camera camera = scene.GetRootGameObjects().SelectMany(owner => owner.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            if (SceneManager.sceneCount != 1 || !string.IsNullOrEmpty(scene.path) || scene.isDirty || camera == null)
                Assert.Ignore("This regression requires a single clean unnamed scene containing a camera.");
            string[] roots = scene.GetRootGameObjects().Select(owner => owner.name).OrderBy(name => name).ToArray();
            Vector3 position = camera.transform.position;
            bool orthographic = camera.orthographic;
            float size = camera.orthographicSize;
            string previousStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
            string root = Path.Combine(Path.GetTempPath(), "HB-UnnamedActualPlay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            SessionState.SetString("HallowBlaze.Tests.UnnamedCameraExpected", new JObject
            {
                ["roots"] = new JArray(roots), ["x"] = position.x, ["y"] = position.y, ["z"] = position.z,
                ["orthographic"] = orthographic, ["size"] = size, ["start"] = previousStart, ["root"] = root
            }.ToString());
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            Assert.That(window.TryConfigureRoot(root), Is.True);
            Assert.That(window.TryOpen(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json"), () => BoardDocumentDecision.Discard), Is.True);
            Assert.That(window.TryFork("draft.unnamed-actual-play", "draft.unnamed-actual-play.config", () => BoardDocumentDecision.Discard), Is.True);
            Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => false, () => { }), Is.True,
                BoardTestPlayLauncher.LastMessage);
            yield return new EnterPlayMode();
            yield return null;
            Assert.That(BoardAuthoringTestContext.Active, Is.Not.Null);
            yield return new ExitPlayMode();
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (BoardTestPlayLauncher.HasRecovery && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False, BoardTestPlayLauncher.LastMessage);
            var expected = JObject.Parse(SessionState.GetString("HallowBlaze.Tests.UnnamedCameraExpected", ""));
            scene = SceneManager.GetActiveScene();
            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
            Assert.That(scene.path, Is.Empty);
            Assert.That(scene.isDirty, Is.False);
            Assert.That(scene.GetRootGameObjects().Select(owner => owner.name).OrderBy(name => name), Is.EqualTo(expected["roots"].Values<string>()));
            camera = scene.GetRootGameObjects().SelectMany(owner => owner.GetComponentsInChildren<Camera>(true)).First();
            Assert.That(camera.transform.position, Is.EqualTo(new Vector3((float)expected["x"], (float)expected["y"], (float)expected["z"])));
            Assert.That(camera.orthographic, Is.EqualTo((bool)expected["orthographic"]));
            Assert.That(camera.orthographicSize, Is.EqualTo((float)expected["size"]));
            Assert.That(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), Is.EqualTo((string)expected["start"]));
            window = Resources.FindObjectsOfTypeAll<BoardAuthoringWindow>().Single(candidate =>
                (string)candidate.Document?.Snapshot()["templateId"] == "draft.unnamed-actual-play");
            window.CloseAfterDecision(BoardDocumentDecision.Discard);
            Directory.Delete((string)expected["root"], true);
            SessionState.EraseString("HallowBlaze.Tests.UnnamedCameraExpected");
        }

        /// <summary>The unnamed-camera fix still refuses clean or dirty unnamed additive scenes without changing the original scene.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AdditionalUnnamedSceneStillBlocksLaunchWithoutChangingOriginal(bool dirty)
        {
            Scene original = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(original.path))
                Assert.Ignore("Unity requires a saved base scene before creating an unnamed additive scene; the owner's Untitled scene is not saved by this test.");
            GameObject[] objects = original.GetRootGameObjects();
            Scene extra = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (dirty) EditorSceneManager.MarkSceneDirty(extra);
            string root = Path.Combine(Path.GetTempPath(), "HB-AdditionalUnnamed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                Assert.That(window.TryOpen(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json"), () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.additive-protection", "draft.additive-protection.config", () => BoardDocumentDecision.Discard), Is.True);
                bool entered = false;
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => false, () => entered = true), Is.False);
                Assert.That(entered, Is.False);
                Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False);
                Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
                Assert.That(extra.IsValid() && extra.isLoaded, Is.True);
                Assert.That(extra.isDirty, Is.EqualTo(dirty));
                Assert.That(original.GetRootGameObjects(), Is.EqualTo(objects));
            }
            finally
            {
                window.CloseAfterDecision(BoardDocumentDecision.Discard);
                if (extra.IsValid()) EditorSceneManager.CloseScene(extra, true);
                SceneManager.SetActiveScene(original);
                Directory.Delete(root, true);
            }
        }

        /// <summary>Even an empty unnamed additive scene must not be omitted from the captured Editor setup.</summary>
        [Test]
        public void EmptyUnnamedAdditiveSceneRefusesLaunchWithoutPublishingOrChangingScenes()
        {
            WithWindow(window =>
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);
                Scene extra = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(extra);
                SceneAsset previousStart = EditorSceneManager.playModeStartScene;
                bool entered = false;
                try
                {
                    Assert.That(extra.isDirty, Is.False);
                    Assert.That(extra.rootCount, Is.Zero);
                    Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => false,
                        () => entered = true), Is.False);
                    Assert.That(BoardTestPlayLauncher.LastMessage, Does.Contain("unnamed"));
                    Assert.That(entered, Is.False);
                    Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False);
                    Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
                    Assert.That(EditorSceneManager.playModeStartScene, Is.EqualTo(previousStart));
                    Assert.That(SceneManager.sceneCount, Is.EqualTo(2));
                    Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(extra));
                }
                finally { if (extra.IsValid()) EditorSceneManager.CloseScene(extra, true); }
            });
        }

        /// <summary>A missing external store never blocks in-memory draft/history or independent Editor setting recovery.</summary>
        [Test]
        public void UnavailableDraftFolderRecoversMemoryAndAllowsReplacementFolder()
        {
            WithWindow(window =>
            {
                window.Document.Edit(data => data["templateVersion"] = 2);
                var previousStart = EditorSceneManager.playModeStartScene;
                string root = (string)typeof(BoardAuthoringWindow).GetField("draftRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                string parked = root + ".parked";
                string replacement = root + ".replacement";
                Assert.That(BoardTestPlayLauncher.TryLaunch(window, new BoardTestPlaySettings(), () => false, () => true, () => { }), Is.True);
                Directory.Move(root, parked);
                File.WriteAllText(root, "Injected unavailable draft directory");
                try
                {
                    BoardTestPlayLauncher.Stop();
                    Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False, BoardTestPlayLauncher.LastMessage);
                    Assert.That(EditorSceneManager.playModeStartScene, Is.EqualTo(previousStart));
                    Assert.That(window.LastError, Does.Contain("Draft folder unavailable"));
                    Assert.That(window.Document.IsDirty, Is.True);
                    Assert.That(window.Document.Undo(), Is.True);
                    Assert.That((int)window.Document.Snapshot()["templateVersion"], Is.EqualTo(1));
                    Directory.CreateDirectory(replacement);
                    Assert.That(window.TryConfigureRoot(replacement), Is.True, window.LastError);
                    Assert.That(window.TrySave(Path.Combine(replacement, "recovered.json")), Is.True, window.LastError);
                }
                finally
                {
                    File.Delete(root);
                    Directory.Move(parked, root);
                    if (Directory.Exists(replacement)) Directory.Delete(replacement, true);
                }
            });
        }

        private static void WithWindow(Action<BoardAuthoringWindow> action)
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M69b-draft-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            SceneSetup[] previousScenes = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.Show();
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                Assert.That(window.TryOpen(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json"), () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.test-play", "draft.test-play.config", () => BoardDocumentDecision.Discard), Is.True);
                action(window);
            }
            finally
            {
                BoardTestPlayLauncher.Stop();
                if (window != null) window.CloseAfterDecision(BoardDocumentDecision.Discard);
                if (previousScenes.Length > 0 && !string.IsNullOrEmpty(previousScenes[0].path))
                    EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
                Directory.Delete(root, true);
            }
        }

        private static BoardTemplateDocument Draft() => BoardTemplateDocument.ReadTemplate(
            File.ReadAllText(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json")),
            RadioTowerLandmark.CreateCatalog()).Fork("draft.test-play", "draft.test-play.config", RadioTowerLandmark.CreateCatalog());
    }
}