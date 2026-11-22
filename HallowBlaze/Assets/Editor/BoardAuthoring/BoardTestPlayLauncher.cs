using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Explicit local inputs; changing these never changes template data or an already running snapshot.</summary>
    [Serializable]
    public sealed class BoardTestPlaySettings
    {
        /// <summary>Deterministic gameplay root seed for this test.</summary>
        public int Seed = 64;
        /// <summary>Positive starting health, independent of player data.</summary>
        public int Health = 100;
        /// <summary>Positive starting food, independent of player data.</summary>
        public int Food = 100;
        /// <summary>Whether the first temporary slot starts with the existing basic axe.</summary>
        public bool EquipAxe;
    }

    internal sealed class BoardRouteTestSnapshot
    {
        private readonly string worldJson;
        private readonly string edgeId;
        private readonly string[] templates;
        private readonly string[] assignments;
        private readonly string preview;

        private BoardRouteTestSnapshot(string worldJson, string edgeId, string[] templates, string[] assignments, string preview)
        { this.worldJson = worldJson; this.edgeId = edgeId; this.templates = templates; this.assignments = assignments; this.preview = preview; }

        internal static BoardRouteTestSnapshot Capture(BoardDocumentStore store, string root, string worldJson,
            BoardAssignment selected, string selectedPath)
        {
            WorldDefinition world = WorldDefinitionJson.Deserialize(worldJson);
            WorldEdgeDefinition edge = world.Edges.Single(candidate => candidate.EdgeId == selected.EdgeId);
            var catalog = OrdinaryBoardContent.CreateAuthoringCatalog();
            var boards = store.ListBoards(world, catalog, out var diagnostics);
            if (diagnostics.Count > 0) throw new InvalidOperationException("Invalid board library: " + diagnostics[0]);
            foreach (string path in Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            {
                string fingerprint = store.CaptureFingerprint(path);
                JObject data = JObject.Parse(File.ReadAllText(path));
                if (store.CaptureFingerprint(path) != fingerprint) throw new IOException("Library changed during capture: " + path);
                if (data["variants"] != null) continue;
                if (data["segmentId"] == null && data["templateId"] == null && !path.EndsWith(".assignment.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (data["segmentId"] == null || data["templateId"] == null || data["edgeId"] == null)
                    throw new InvalidOperationException("Incomplete saved assignment: " + Path.GetFileName(path));
                bool onRoute = (string)data["edgeId"] == edge.EdgeId ||
                    world.TryGetSegment((string)data["segmentId"], out _, out var owner) && owner.EdgeId == edge.EdgeId;
                if (onRoute && !boards.Any(board => string.Equals(board.AssignmentPath, path, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Missing or incompatible template for route assignment: " + Path.GetFileName(path));
            }
            var captured = new Dictionary<string, (string Template, string Assignment, string Label)>(StringComparer.Ordinal);
            bool includesSelected = false;
            foreach (var group in boards.Where(candidate => candidate.Assignment?.EdgeId == edge.EdgeId)
                .GroupBy(candidate => candidate.AssignmentPath, StringComparer.OrdinalIgnoreCase))
            {
                var board = group.FirstOrDefault(candidate => string.Equals(candidate.Path, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? group.First();
                var document = store.OpenTemplate(board.Path, catalog);
                string json = document.ToJson();
                foreach (var copy in group)
                    if (!JToken.DeepEquals(JObject.Parse(store.OpenTemplate(copy.Path, catalog).ToJson()), JObject.Parse(json)))
                        throw new InvalidOperationException("Conflicting saved template/version: " + board.TemplateId + "/" + board.TemplateVersion);
                var template = BoardTemplateJson.ReadCompatible(json, catalog);
                string binding = store.OpenAssignment(board.AssignmentPath, world, template).ToJson();
                var assignment = BoardAssignment.Read(binding, world, template);
                if (captured.ContainsKey(assignment.SegmentId))
                    throw new InvalidOperationException("More than one saved assignment targets segment: " + assignment.SegmentId);
                captured.Add(assignment.SegmentId, (json, binding, board.Name + " | " + template.Id + "/" + template.Version + " | " + assignment.VariantId));
                includesSelected |= string.Equals(board.Path, selectedPath, StringComparison.OrdinalIgnoreCase) && binding == selected.ToJson();
            }
            if (!includesSelected) throw new InvalidOperationException("The selected saved board assignment is missing from this route library.");
            var ordered = edge.Segments.Where(segment => captured.ContainsKey(segment.SegmentId)).Select(segment => captured[segment.SegmentId]).ToArray();
            string preview = world.WorldDefinitionId + "/" + world.Version + "\n" + edge.EdgeId + "\n" + edge.FromNodeId + " > " + edge.ToNodeId + "\n\n" +
                string.Join("\n\n", edge.Segments.Select((segment, index) => (index + 1) + "/" + edge.SegmentCount + " | " + segment.SegmentId + "\n" +
                    (captured.TryGetValue(segment.SegmentId, out var entry) ? entry.Label : "Generated | " + segment.GenerationConfigurationId + "/" + segment.GenerationVersion)));
            return new BoardRouteTestSnapshot(worldJson, edge.EdgeId, ordered.Select(entry => entry.Template).ToArray(), ordered.Select(entry => entry.Assignment).ToArray(), preview);
        }

        internal string Describe(BoardTestPlaySettings settings) => preview + "\n\nSeed " + settings.Seed + " | Health " + settings.Health +
            " | Food " + settings.Food + " | Axe " + (settings.EquipAxe ? "Yes" : "No");

        internal BoardAuthoringTestContext Prepare(BoardTestPlaySettings settings) => BoardAuthoringTestContext.PrepareRoute(worldJson, edgeId,
            templates, assignments, settings.Seed, settings.Health, settings.Food, settings.EquipAxe, BoardAuthoringTestContext.CreateSaveRootPath());
    }

    /// <summary>Owns a single Editor test and restores its original scene setup/draft after Play Mode ends.</summary>
    [InitializeOnLoad]
    public static class BoardTestPlayLauncher
    {
        private const string RecoveryKey = "HallowBlaze.BoardAuthoring.PlayRecovery";
        private const string ObservedPlayKey = "HallowBlaze.BoardAuthoring.PlayObserved";
        private const string MainScene = "Assets/Scenes/Main.unity";
        private static bool stopping;
        private static bool launchObserved;
        private static double launchDeadline;

        static BoardTestPlayLauncher()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += CheckLifetime;
            if (HasRecovery && !EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += Recover;
        }

        /// <summary>Gets whether a test or an unfinished recovery owns the launcher; another test must not replace it.</summary>
        public static bool HasRecovery => !string.IsNullOrEmpty(SessionState.GetString(RecoveryKey, string.Empty));

        /// <summary>Gets the last controlled launch/recovery refusal; recovery data is retained on failure.</summary>
        public static string LastMessage { get; private set; } = string.Empty;

        /// <summary>
        /// Validates and captures one draft, resolves dirty scenes, then enters Play Mode with an isolated context.
        /// An existing Play Mode requires explicit Stop consent and a later Test after Edit Mode returns.
        /// </summary>
        /// <returns>True only when this call requests a new local test; cancellation changes no document.</returns>
        public static bool TryLaunch(BoardAuthoringWindow window, BoardTestPlaySettings settings,
            Func<bool> stopExisting, Func<bool> resolveDirtyScenes, Action enterPlay = null)
            => TryLaunchCore(window, settings, stopExisting, resolveDirtyScenes, enterPlay, null);

        /// <summary>
        /// Captures saved assignments for the selected route, previews their exact order, then launches that immutable set.
        /// Cancellation creates no context; Start uses the captured files rather than rereading the library.
        /// The existing scene and draft recovery also owns this route session through final arrival or Stop.
        /// </summary>
        /// <param name="confirmPreview">Explicit Start/Cancel decision on the captured route and starting inputs.</param>
        /// <returns>True only after preflight succeeds and Play Mode entry is requested.</returns>
        public static bool TryLaunchRoute(BoardAuthoringWindow window, BoardTestPlaySettings settings,
            Func<string, bool> confirmPreview, Func<bool> stopExisting, Func<bool> resolveDirtyScenes, Action enterPlay = null)
            => TryLaunchCore(window, settings, stopExisting, resolveDirtyScenes, enterPlay, confirmPreview ?? (_ => false));

        private static bool TryLaunchCore(BoardAuthoringWindow window, BoardTestPlaySettings settings,
            Func<bool> stopExisting, Func<bool> resolveDirtyScenes, Action enterPlay, Func<string, bool> confirmRoute)
        {
            bool preparedHere = false;
            try
            {
                if (window == null || window.Document == null || settings == null)
                    throw new InvalidOperationException("A board document and starting inputs are required.");
                if (HasRecovery) throw new InvalidOperationException("A test or recovery already owns the Editor.");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("The Editor is still importing or compiling.");
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    if (stopExisting != null && stopExisting()) EditorApplication.isPlaying = false;
                    LastMessage = "Play Mode is already running.";
                    return false;
                }
                string json = window.Document.ToJson();
                if (settings.Health < 1 || settings.Food < 1)
                    throw new InvalidOperationException("Starting health and food must be positive.");
                settings = new BoardTestPlaySettings { Seed = settings.Seed, Health = settings.Health, Food = settings.Food, EquipAxe = settings.EquipAxe };
                BoardRouteTestSnapshot route = null;
                if (confirmRoute != null)
                {
                    route = window.CaptureRouteTest();
                    string beforePreview = window.CaptureTestRecovery();
                    if (!confirmRoute(route.Describe(settings))) { LastMessage = "Route preview cancelled."; return false; }
                    if (window.CaptureTestRecovery() != beforePreview)
                        throw new InvalidOperationException("The open draft or route selection changed during preview; preview again.");
                }
                if (route == null && (int)JObject.Parse(json)["schemaVersion"] == 2)
                {
                    BoardSolverReport playable = BoardSolverAdapter.CheckPlayable(json, window.SelectedVariant, settings);
                    if (playable.Status != "Solved" || !playable.Replayed)
                        throw new InvalidOperationException("Solution not verified: " + playable.Status + ". " + playable.Message);
                }
                if (!SceneSetupIsClean() && (resolveDirtyScenes == null || !resolveDirtyScenes() || !SceneSetupIsClean()))
                { LastMessage = "Scene changes were not saved; test cancelled."; return false; }
                SceneSetup[] scenes = EditorSceneManager.GetSceneManagerSetup();
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene scene = SceneManager.GetSceneAt(index);
                    if (string.IsNullOrEmpty(scene.path) && SceneManager.sceneCount > 1)
                        throw new InvalidOperationException("Save unnamed additive scenes before a local test; a single clean unnamed scene is supported.");
                }
                if (scenes.Length == 0 && SceneManager.sceneCount == 1 && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                    scenes = new[] { new SceneSetup { path = "", isLoaded = true, isActive = true } };
                if (scenes.Length == 0 || scenes.Any(scene => string.IsNullOrEmpty(scene.path)) && scenes.Length != 1)
                    throw new InvalidOperationException("Save unnamed additive scenes before a local test. Setup=" + scenes.Length +
                        ", loaded=" + SceneManager.sceneCount + ", paths=" + string.Join(",", scenes.Select(scene => scene.path)));
                string recovery = new JObject
                {
                    ["window"] = window.CaptureTestRecovery(),
                    ["startScene"] = EditorSceneManager.playModeStartScene == null ? "" : AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                    ["preserveUnnamedContents"] = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(SceneManager.GetActiveScene().path) &&
                        SceneManager.GetActiveScene().rootCount > 0,
                    ["scenes"] = new JArray(scenes.Select(scene => new JObject
                    { ["path"] = scene.path, ["loaded"] = scene.isLoaded, ["active"] = scene.isActive }))
                }.ToString(Formatting.None);
                var context = route != null ? route.Prepare(settings) : window.Assignment.Enabled
                    ? BoardAuthoringTestContext.PrepareAssigned(window.Assignment.WorldJson, json, window.Assignment.SegmentId, window.SelectedVariant,
                        settings.Seed, settings.Health, settings.Food, settings.EquipAxe, BoardAuthoringTestContext.CreateSaveRootPath())
                    : BoardAuthoringTestContext.PrepareLocal(json, window.SelectedVariant, settings.Seed,
                        settings.Health, settings.Food, settings.EquipAxe, BoardAuthoringTestContext.CreateSaveRootPath());
                preparedHere = true;
                JObject state = JObject.Parse(recovery);
                state["root"] = context.SaveRoot;
                SessionState.SetString(RecoveryKey, state.ToString(Formatting.None));
                SessionState.SetBool(ObservedPlayKey, false);
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScene);
                if (EditorSceneManager.playModeStartScene == null) throw new InvalidOperationException("Gameplay scene is missing.");
                stopping = false;
                launchObserved = false;
                launchDeadline = EditorApplication.timeSinceStartup + 60;
                (enterPlay ?? (() => EditorApplication.isPlaying = true))();
                LastMessage = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                LastMessage = "Test launch failed: " + exception.Message;
                if (preparedHere) BoardAuthoringTestContext.Cancel();
                if (preparedHere && HasRecovery)
                {
                    EditorApplication.delayCall += Recover;
                }
                return false;
            }
        }

        /// <summary>Stops only a launcher-owned test; the preserved recovery is restored on returning to Edit Mode.</summary>
        public static void Stop() => Stop(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo);

        /// <summary>Stops an owned test, retaining recovery if an Edit Mode scene cannot be saved or the owner cancels.</summary>
        /// <param name="resolveDirtyScenes">Explicit save/cancel decision for scenes changed after a pending or failed entry.</param>
        public static void Stop(Func<bool> resolveDirtyScenes)
        {
            if (!HasRecovery) return;
            stopping = true;
            BoardAuthoringTestContext.Cancel();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else RecoverWithDecision(resolveDirtyScenes);
        }

        private static bool SceneSetupIsClean()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
                if (SceneManager.GetSceneAt(index).isDirty) return false;
            return true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!HasRecovery) return;
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
                SessionState.SetBool(ObservedPlayKey, true);
            if (state == PlayModeStateChange.ExitingEditMode) launchObserved = true;
            if (state == PlayModeStateChange.EnteredPlayMode) { launchObserved = true; launchDeadline = 0; }
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Recover;
        }

        private static void CheckLifetime()
        {
            if (!HasRecovery) return;
            if (EditorApplication.isPlaying && !BoardAuthoringTestContext.IsRequested && !stopping)
            { stopping = true; EditorApplication.isPlaying = false; }
            if (!EditorApplication.isPlayingOrWillChangePlaymode && launchDeadline > 0 &&
                EditorApplication.timeSinceStartup > launchDeadline)
            { BoardAuthoringTestContext.Cancel(); launchDeadline = 0; Recover(); }
            if (!EditorApplication.isPlayingOrWillChangePlaymode && (launchObserved || SessionState.GetBool(ObservedPlayKey, false)))
                Recover();
        }

        private static void Recover() => RecoverWithDecision(null);

        private static void RecoverWithDecision(Func<bool> resolveDirtyScenes)
        {
            if (!HasRecovery || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            try
            {
                JObject state = JObject.Parse(SessionState.GetString(RecoveryKey, string.Empty));
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>((string)state["startScene"]);
                var scenes = state["scenes"].Select(scene => new SceneSetup
                { path = (string)scene["path"], isLoaded = (bool)scene["loaded"], isActive = (bool)scene["active"] }).ToArray();
                if (!SceneSetupIsClean() && (resolveDirtyScenes == null || !resolveDirtyScenes() || !SceneSetupIsClean()))
                {
                    LastMessage = "Scene changes block recovery; no scenes were closed.";
                    launchObserved = false;
                    SessionState.SetBool(ObservedPlayKey, false);
                    launchDeadline = 0;
                    return;
                }
                SceneSetup[] current = EditorSceneManager.GetSceneManagerSetup();
                if (current.Length == 0 && SceneManager.sceneCount == 1 && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                    current = new[] { new SceneSetup { path = "", isLoaded = true, isActive = true } };
                bool sameSetup = current.Length == scenes.Length && current.Where((scene, index) => scene.path != scenes[index].path ||
                    scene.isLoaded != scenes[index].isLoaded || scene.isActive != scenes[index].isActive).Any() == false;
                if (!sameSetup)
                {
                    if (scenes.Length == 1 && string.IsNullOrEmpty(scenes[0].path))
                    {
                        if ((bool?)state["preserveUnnamedContents"] == true)
                            throw new InvalidOperationException("The original unnamed scene must be restored by Unity before recovery; no scene was replaced.");
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    }
                    else EditorSceneManager.RestoreSceneManagerSetup(scenes);
                }
                BoardAuthoringWindow.RestoreTestRecovery((string)state["window"]);
                BoardAuthoringTestContext.Cancel();
                SessionState.EraseString(RecoveryKey);
                SessionState.SetBool(ObservedPlayKey, false);
                launchDeadline = 0;
                launchObserved = false;
                stopping = false;
            }
            catch (Exception exception) { LastMessage = "Test recovery failed: " + exception.Message; }
        }
    }
}