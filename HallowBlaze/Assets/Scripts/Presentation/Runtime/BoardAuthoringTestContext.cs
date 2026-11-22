#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using UnityEditor;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>
    /// Owns one Editor-only, immutable authoring publication and external save root.
    /// A pending launch survives Play Mode entry; a running sandbox never survives an assembly reload.
    /// </summary>
    [InitializeOnLoad]
    public sealed class BoardAuthoringTestContext
    {
        private const string StateKey = "HallowBlaze.BoardAuthoring.PendingLaunch";
        private const string RootPrefix = "HallowBlaze-BoardTest-";
        private const string MarkerName = ".authoring-owner";
        private static BoardAuthoringTestContext current;
        private readonly LaunchData data;
        private readonly LandmarkPlacement placement;
        private readonly BoardAssignment assignment;
        private readonly Dictionary<string, BoardAssignment> routeAssignments;
        private readonly Func<string, RunState> readRun;
        private object owner;
        private string expectedScene;
        private bool disposed;

        [Serializable]
        private sealed class LaunchData
        {
            public string WorldJson;
            public string TemplateJson;
            public string RunJson;
            public string SegmentId;
            public string VariantId;
            public string Root;
            public string Token;
            public bool Started;
            public bool Local;
            public string RunId;
            public int Seed;
            public int Health;
            public int Food;
            public bool EquipAxe;
            public bool Assigned;
            public string RouteEdgeId;
            public RouteEntry[] RouteEntries;
        }

        [Serializable]
        private sealed class RouteEntry
        {
            public string TemplateJson;
            public string AssignmentJson;
        }

        [Serializable]
        private sealed class LocalWorld
        {
            public string worldDefinitionId = "authoring.world";
            public int version = 1;
            public string startNodeId = "authoring.start";
            public string goalNodeId = "authoring.goal";
            public LocalNode[] nodes = { new LocalNode(), new LocalNode { nodeId = "authoring.goal", atlasY = 1, distanceLayer = 1, placeKind = "landmark" } };
            public LocalEdge[] edges = { new LocalEdge() };
        }

        [Serializable]
        private sealed class LocalNode
        {
            public string nodeId = "authoring.start";
            public int atlasX;
            public int atlasY;
            public int distanceLayer;
            public string placeKind = "shelter";
            public string biomeFamilyKey = "temperate-forest";
        }

        [Serializable]
        private sealed class LocalEdge
        {
            public string edgeId = "authoring.route";
            public string fromNodeId = "authoring.start";
            public string toNodeId = "authoring.goal";
            public string direction = "north";
            public string clueKey = "authoring.clue";
            public LocalSegment[] segments = { new LocalSegment() };
        }

        [Serializable]
        private sealed class LocalSegment
        {
            public string segmentId = "authoring.segment";
            public string generationConfigurationId;
            public int generationVersion;
        }

        static BoardAuthoringTestContext()
        {
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private BoardAuthoringTestContext(LaunchData data, Func<string, RunState> readRun)
        {
            this.data = data;
            this.readRun = readRun;
            if (!data.Local && readRun == null) throw new ArgumentNullException(nameof(readRun));
            World = WorldDefinitionJson.Deserialize(data.WorldJson);
            if (!string.IsNullOrEmpty(data.RouteEdgeId))
            {
                routeAssignments = ValidateRoute();
                Template = BoardTemplateJson.ReadCompatible(data.RouteEntries[0].TemplateJson, OrdinaryBoardContent.CreateAuthoringCatalog());
                return;
            }
            Template = BoardTemplateJson.ReadCompatible(data.TemplateJson, OrdinaryBoardContent.CreateAuthoringCatalog());
            if (Template.IsShared || data.Assigned) assignment = new BoardAssignment(World, data.SegmentId, Template, data.VariantId);
            else placement = new LandmarkPlacement(World, data.SegmentId, Template, data.VariantId);
            RunState run = ReadInitialRun();
            string edgeId = assignment?.EdgeId ?? placement.EdgeId;
            if (run.Status != RunStatus.Active || run.ActiveLeg == null || run.ActiveLeg.EdgeId != edgeId)
                throw new ArgumentException("Authoring launch requires an active leg on the assigned road.");
            BoardAddressFactory.Create(run, World);
            run.TryConsumeIntro(run.ActiveLeg.LegId);
            while (run.ActiveLeg.CurrentSegment.SegmentId != data.SegmentId)
                if (!run.TryAdvanceSegment(run.ActiveLeg.LegId, run.ActiveLeg.CurrentSegment.SegmentId))
                    throw new ArgumentException("Authoring target is not a remaining route segment.");
            var address = BoardAddressFactory.Create(run, World);
            foreach (string variant in Template.VariantIds)
                if (!(assignment == null ? Template.CreateBoard(address, variant) : Template.AsShared().CreateBoard(address, variant))
                    .Validate(OrdinaryBoardContent.CreateAuthoringCatalog()).IsValid)
                    throw new ArgumentException("Authoring template contains an invalid variant: " + variant);
            if (run.ActiveLandmark != null && (run.ActiveLandmark.TemplateId != Template.Id ||
                run.ActiveLandmark.TemplateVersion != Template.Version || run.ActiveLandmark.VariantId != data.VariantId))
                throw new ArgumentException("The saved landmark selection conflicts with the authoring snapshot.");
        }

        /// <summary>Gets whether bootstrap must use the authoring boundary, even if its payload is invalid.</summary>
        public static bool IsRequested => current != null || !string.IsNullOrEmpty(SessionState.GetString(StateKey, string.Empty));

        /// <summary>Gets the live context for Editor-only view composition; bootstrap must claim it before use.</summary>
        public static BoardAuthoringTestContext Active => current;

        /// <summary>Gets the immutable world used for bootstrap and all controlled travel reloads.</summary>
        public WorldDefinition World { get; }

        /// <summary>Gets the detached selected template, or the first captured route template; runtime selection uses the exact segment mapping.</summary>
        public LandmarkTemplate Template { get; }

        /// <summary>Gets the external sandbox path after checking its ownership marker and ancestors.</summary>
        public string SaveRoot
        {
            get { RequireActive(); ValidateRoot(data.Root, true, data.Token); return data.Root; }
        }

        /// <summary>
        /// Gets the still-validated isolated root for menu inspection after revocation.
        /// This never reactivates the context or authorizes another runtime; it fences a retained bootstrap from real storage.
        /// </summary>
        public string RetainedSaveRoot
        {
            get { ValidateRoot(data.Root, true, data.Token); return data.Root; }
        }

        /// <summary>Gets the single-board explicit variant, or null for a route whose variants belong to its exact assignments; no selection RNG is consumed.</summary>
        public string VariantId => data.VariantId;

        /// <summary>Gets whether this publication was revoked; a retained reference cannot resume normal storage.</summary>
        public bool IsDisposed => disposed;

        /// <summary>Gets or sets the sandbox-only high score, never the player's local record.</summary>
        public int HighScore { get; set; }

        /// <summary>Returns a fresh external path; it does not create a directory or access player data.</summary>
        public static string CreateSaveRootPath() => Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Validates the full snapshot before reserving a fresh sandbox root and publishing a pending launch.
        /// Existing roots, unsupported layouts and a second pending/running launch are refused without replacement.
        /// </summary>
        public static BoardAuthoringTestContext Prepare(string worldJson, string templateJson, string runJson,
            string segmentId, string variantId, string saveRoot, Func<string, RunState> readRun)
        {
            if (IsRequested) throw new InvalidOperationException("An authoring launch already owns the Editor context.");
            ValidateRoot(saveRoot, false, null);
            var data = new LaunchData
            {
                WorldJson = worldJson, TemplateJson = templateJson, RunJson = runJson,
                SegmentId = segmentId, VariantId = variantId, Root = Path.GetFullPath(saveRoot),
                Token = Guid.NewGuid().ToString("N")
            };
            var candidate = new BoardAuthoringTestContext(data, readRun);
            return Publish(candidate);
        }

        /// <summary>
        /// Captures a landmark draft into a minimal isolated world with explicit resources, seed and optional axe.
        /// The generated world is Editor recovery data, never a campaign asset or a new authoring schema.
        /// </summary>
        public static BoardAuthoringTestContext PrepareLocal(string templateJson, string variantId, int seed,
            int health, int food, bool equipAxe, string saveRoot, bool includeApproach = false)
        {
            if (IsRequested) throw new InvalidOperationException("An authoring launch already owns the Editor context.");
            ValidateRoot(saveRoot, false, null);
            if (health < 1 || food < 1) throw new ArgumentOutOfRangeException(nameof(health), "Local resources must be positive.");
            LandmarkTemplate template = BoardTemplateJson.ReadCompatible(templateJson, OrdinaryBoardContent.CreateAuthoringCatalog());
            var world = new LocalWorld();
            if (template.IsShared) world.nodes[1].placeKind = "shelter";
            world.edges[0].segments[0].generationConfigurationId = template.Config.Id;
            world.edges[0].segments[0].generationVersion = template.Config.Version;
            if (includeApproach)
                world.edges[0].segments = new[]
                {
                    new LocalSegment { segmentId = "authoring.approach", generationConfigurationId = "ordinary.board.v1", generationVersion = 3 },
                    world.edges[0].segments[0]
                };
            var data = new LaunchData
            {
                Local = true, WorldJson = JsonUtility.ToJson(world), TemplateJson = templateJson,
                SegmentId = "authoring.segment", VariantId = variantId, Root = Path.GetFullPath(saveRoot),
                Token = Guid.NewGuid().ToString("N"), RunId = "authoring-" + Guid.NewGuid().ToString("N"),
                Seed = seed, Health = health, Food = food, EquipAxe = equipAxe
            };
            return Publish(new BoardAuthoringTestContext(data, null));
        }

        /// <summary>Starts at the chosen real segment in an isolated copy of the published world, never rewriting campaign configuration.</summary>
        public static BoardAuthoringTestContext PrepareAssigned(string worldJson, string templateJson, string segmentId, string variantId,
            int seed, int health, int food, bool equipAxe, string saveRoot)
        {
            if (IsRequested) throw new InvalidOperationException("An authoring launch already owns the Editor context.");
            ValidateRoot(saveRoot, false, null);
            if (health < 1 || food < 1) throw new ArgumentException("Starting resources must be positive.");
            var data = new LaunchData { Local = true, Assigned = true, WorldJson = worldJson, TemplateJson = templateJson,
                SegmentId = segmentId, VariantId = variantId, Root = Path.GetFullPath(saveRoot), Token = Guid.NewGuid().ToString("N"),
                RunId = "authoring-" + Guid.NewGuid().ToString("N"), Seed = seed, Health = health, Food = food, EquipAxe = equipAxe };
            return Publish(new BoardAuthoringTestContext(data, null));
        }

        /// <summary>
        /// Captures a complete route mapping before reserving a fresh sandbox root, then starts at its first segment.
        /// Duplicate segments, conflicting exact template/version payloads and unsupported unassigned configurations fail before I/O.
        /// </summary>
        /// <param name="worldJson">The immutable existing world publication; topology is not edited.</param>
        /// <param name="edgeId">The exact directed route to travel from its first segment.</param>
        /// <param name="templateJsons">Detached shared snapshots parallel to the assignments; repeated identities require identical serialized content.</param>
        /// <param name="assignmentJsons">Exact versioned segment assignments, copied independently of the caller's arrays.</param>
        /// <param name="seed">Deterministic run seed, also used by unassigned generated segments.</param>
        /// <param name="health">Positive starting Health.</param>
        /// <param name="food">Positive starting Food.</param>
        /// <param name="equipAxe">Whether the isolated run begins with the existing basic axe.</param>
        /// <param name="saveRoot">A new marked root directly under the system temporary directory, never player storage.</param>
        public static BoardAuthoringTestContext PrepareRoute(string worldJson, string edgeId, string[] templateJsons, string[] assignmentJsons,
            int seed, int health, int food, bool equipAxe, string saveRoot)
        {
            if (IsRequested) throw new InvalidOperationException("An authoring launch already owns the Editor context.");
            ValidateRoot(saveRoot, false, null);
            if (health < 1 || food < 1) throw new ArgumentException("Starting resources must be positive.");
            if (templateJsons == null || assignmentJsons == null || templateJsons.Length == 0 || templateJsons.Length != assignmentJsons.Length)
                throw new ArgumentException("At least one template and an exact parallel assignment set are required.");
            var entries = templateJsons.Select((json, index) => new RouteEntry { TemplateJson = json, AssignmentJson = assignmentJsons[index] }).ToArray();
            var data = new LaunchData { Local = true, WorldJson = worldJson, RouteEdgeId = edgeId, RouteEntries = entries,
                Root = Path.GetFullPath(saveRoot), Token = Guid.NewGuid().ToString("N"), RunId = "authoring-" + Guid.NewGuid().ToString("N"),
                Seed = seed, Health = health, Food = food, EquipAxe = equipAxe };
            return Publish(new BoardAuthoringTestContext(data, null));
        }

        private Dictionary<string, BoardAssignment> ValidateRoute()
        {
            if (!data.Local || data.RouteEntries == null || data.RouteEntries.Length == 0 || data.Health < 1 || data.Food < 1)
                throw new ArgumentException("Invalid captured route inputs.");
            WorldEdgeDefinition edge = World.Edges.SingleOrDefault(candidate => candidate.EdgeId == data.RouteEdgeId)
                ?? throw new ArgumentException("Unknown route.");
            var mappings = new Dictionary<string, BoardAssignment>(StringComparer.Ordinal);
            var publications = new Dictionary<(string, int), string>();
            foreach (RouteEntry entry in data.RouteEntries)
            {
                LandmarkTemplate template = BoardTemplateJson.ReadCompatible(entry.TemplateJson, OrdinaryBoardContent.CreateAuthoringCatalog());
                if (!template.IsShared) throw new ArgumentException("Route snapshots must use the shared board format; legacy single-board testing is unchanged.");
                var key = (template.Id, template.Version);
                if (publications.TryGetValue(key, out string captured) && captured != entry.TemplateJson)
                    throw new ArgumentException("Conflicting captured template/version: " + template.Id + "/" + template.Version);
                publications[key] = entry.TemplateJson;
                BoardAssignment mapping = BoardAssignment.Read(entry.AssignmentJson, World, template);
                if (mapping.EdgeId != edge.EdgeId || mappings.ContainsKey(mapping.SegmentId))
                    throw new ArgumentException("Wrong route or duplicate segment assignment: " + mapping.SegmentId);
                mappings.Add(mapping.SegmentId, mapping);
            }
            RunState run = ReadInitialRun();
            run.TryConsumeIntro(run.ActiveLeg.LegId);
            for (int index = 0; index < edge.SegmentCount; index++)
            {
                var address = BoardAddressFactory.Create(run, World);
                if (mappings.TryGetValue(address.SegmentId, out BoardAssignment mapping))
                {
                    if (!mapping.CreateBoard(address).Validate(OrdinaryBoardContent.CreateAuthoringCatalog()).IsValid)
                        throw new ArgumentException("Invalid captured board for segment: " + address.SegmentId);
                }
                else OrdinaryBoardContent.CreateConfiguration(address);
                if (index + 1 < edge.SegmentCount && !run.TryAdvanceSegment(run.ActiveLeg.LegId, address.SegmentId))
                    throw new ArgumentException("The captured route cannot advance in its published order.");
            }
            return mappings;
        }

        /// <summary>Gets whether this addressed board uses a shared assignment, not a persisted legacy landmark selection.</summary>
        public bool IsSharedBoard(BoardRequest request) => routeAssignments != null
            ? request?.Address?.EdgeId == data.RouteEdgeId && routeAssignments.ContainsKey(request.Address.SegmentId)
            : assignment != null && request?.Address?.SegmentId == data.SegmentId;

        private static BoardAuthoringTestContext Publish(BoardAuthoringTestContext candidate)
        {
            LaunchData data = candidate.data;
            Directory.CreateDirectory(data.Root);
            using (var marker = new StreamWriter(new FileStream(Path.Combine(data.Root, MarkerName), FileMode.CreateNew, FileAccess.Write, FileShare.None)))
                marker.Write(data.Token);
            ValidateRoot(data.Root, true, data.Token);
            current = candidate;
            SessionState.SetString(StateKey, JsonUtility.ToJson(data));
            return candidate;
        }

        /// <summary>
        /// Acquires the pending context before stores exist, or revalidates its existing bootstrap owner.
        /// Invalid recovery fails closed; callers must not retry with normal persistence.
        /// </summary>
        public static BoardAuthoringTestContext Claim(object bootstrapOwner, Func<string, RunState> readRun = null)
        {
            if (bootstrapOwner == null) throw new ArgumentNullException(nameof(bootstrapOwner));
            if (current == null)
            {
                string json = SessionState.GetString(StateKey, string.Empty);
                if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("No authoring launch is pending.");
                LaunchData restored = JsonUtility.FromJson<LaunchData>(json);
                if (restored == null || restored.Started)
                    throw new InvalidOperationException("A running authoring test cannot resume after assembly reload.");
                current = new BoardAuthoringTestContext(restored, readRun);
            }
            current.RequireActive();
            ValidateRoot(current.data.Root, true, current.data.Token);
            if (current.owner != null && !ReferenceEquals(current.owner, bootstrapOwner))
                throw new InvalidOperationException("Another bootstrap owns this authoring session.");
            current.owner = bootstrapOwner;
            current.data.Started = true;
            SessionState.SetString(StateKey, JsonUtility.ToJson(current.data));
            return current;
        }

        /// <summary>Creates a fresh run snapshot; no mutable state is shared with the draft or a previous test.</summary>
        public RunState CreateInitialRun()
        {
            RequireActive();
            return ReadInitialRun();
        }

        private RunState ReadInitialRun()
        {
            if (!data.Local) return readRun(data.RunJson);
            WorldEdgeDefinition edge = World.Edges[0];
            if (!string.IsNullOrEmpty(data.RouteEdgeId)) edge = World.Edges.Single(candidate => candidate.EdgeId == data.RouteEdgeId);
            if (data.Assigned && !World.TryGetSegment(data.SegmentId, out _, out edge)) throw new ArgumentException("Unknown assigned segment.");
            var run = new RunState(data.RunId, data.Seed, new RunStateConfiguration(data.Health, data.Food, 0, edge.FromNodeId));
            var segments = new System.Collections.Generic.List<RouteSegmentState>();
            foreach (RouteSegmentDefinition segment in edge.Segments)
                segments.Add(new RouteSegmentState(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion));
            run.StartLeg(edge.EdgeId, edge.FromNodeId, edge.ToNodeId, segments);
            if (data.Assigned)
            {
                if (run.ActiveLeg.CurrentSegment.SegmentId != data.SegmentId) run.TryConsumeIntro(run.ActiveLeg.LegId);
                while (run.ActiveLeg.CurrentSegment.SegmentId != data.SegmentId)
                    if (!run.TryAdvanceSegment(run.ActiveLeg.LegId, run.ActiveLeg.CurrentSegment.SegmentId)) throw new ArgumentException("Assigned segment not found.");
            }
            if (data.EquipAxe) run.EquipTool(0, run.CreateToolInstance(ToolContentCatalog.Load()["axe.basic"]));
            return run;
        }

        /// <summary>Returns a strict legacy selection, or null for shared/generated boards; route requests must match the captured publication.</summary>
        public LandmarkRecord GetSelection(BoardRequest request)
        {
            RequireActive();
            if (request?.Address == null) throw new ArgumentException("An addressed authoring board is required.");
            if (routeAssignments != null) { ValidateRouteAddress(request.Address); return null; }
            if (assignment != null) return null;
            if (request.Address.SegmentId != placement.SegmentId)
            {
                if (request.Address.GenerationConfigurationId == Template.Config.Id)
                    throw new InvalidOperationException("The snapshot does not assign this landmark segment.");
                return null;
            }
            placement.CreateBoard(request.Address);
            return new LandmarkRecord(request.Address, Template.Id, Template.Version, VariantId);
        }

        /// <summary>Creates fresh state from an exact shared assignment or checkpointed legacy landmark; unassigned route segments return null for normal generation.</summary>
        public LandmarkBoard CreateBoard(BoardRequest request, RunState run)
        {
            RequireActive();
            if (routeAssignments != null)
            {
                ValidateRouteAddress(request?.Address);
                return routeAssignments.TryGetValue(request.Address.SegmentId, out BoardAssignment mapped) ? mapped.CreateBoard(request.Address) : null;
            }
            if (assignment != null) return IsSharedBoard(request) ? assignment.CreateBoard(request.Address) : null;
            LandmarkRecord selection = GetSelection(request);
            if (selection == null) return null;
            LandmarkRecord saved = run.ActiveLandmark;
            if (saved == null || !saved.Address.Equals(selection.Address) || saved.TemplateId != selection.TemplateId ||
                saved.TemplateVersion != selection.TemplateVersion || saved.VariantId != selection.VariantId)
                throw new InvalidOperationException("Authoring selection is not checkpointed.");
            return placement.CreateBoard(request.Address);
        }

        private void ValidateRouteAddress(BoardAddress address)
        {
            if (address == null || address.EdgeId != data.RouteEdgeId || !World.TryGetSegment(address.SegmentId, out var segment, out var edge) ||
                edge.EdgeId != data.RouteEdgeId || segment.GenerationConfigurationId != address.GenerationConfigurationId ||
                segment.GenerationVersion != address.GenerationVersion)
                throw new ArgumentException("Board address does not match the captured route publication.");
        }

        /// <summary>Marks the single expected gameplay scene reload without releasing publication or root ownership.</summary>
        public void ExpectSceneReload(string sceneName)
        {
            RequireActive();
            if (owner == null || string.IsNullOrEmpty(sceneName)) throw new InvalidOperationException("No owned gameplay scene can reload.");
            expectedScene = sceneName;
        }

        /// <summary>Admits initial entry or the exact controlled reload; unexpected scene changes revoke the context.</summary>
        public void EnterScene(string sceneName, bool initialEntry)
        {
            RequireActive();
            if (!initialEntry && !string.Equals(expectedScene, sceneName, StringComparison.Ordinal))
            {
                Cancel();
                throw new InvalidOperationException("Unexpected authoring scene reload.");
            }
            expectedScene = null;
            ValidateRoot(data.Root, true, data.Token);
        }

        /// <summary>Revokes only this bootstrap's context, never a newer test owned by another bootstrap.</summary>
        public void End(object bootstrapOwner)
        {
            if (ReferenceEquals(current, this) && ReferenceEquals(owner, bootstrapOwner)) Cancel();
        }

        /// <summary>Revokes the sandbox without deleting its diagnostic files or changing real player storage.</summary>
        public static void Cancel()
        {
            if (current != null) { current.disposed = true; current.owner = null; }
            current = null;
            SessionState.EraseString(StateKey);
        }

        private void RequireActive()
        {
            if (disposed) throw new InvalidOperationException("This authoring session has ended.");
        }

        private static void ValidateRoot(string path, bool owned, string token)
        {
            ValidateRootAtTemporaryDirectory(path, owned, token, Path.GetTempPath());
        }

        private static void ValidateRootAtTemporaryDirectory(string path, bool owned, string token, string temporaryDirectory)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new ArgumentException("An explicit external authoring root is required.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            string temporary = Path.GetFullPath(temporaryDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string project = Directory.GetParent(Path.GetFullPath(Application.dataPath)).FullName;
            if (Application.platform != RuntimePlatform.WindowsEditor ||
                IsWithin(fullPath, project) || IsWithin(fullPath, Application.persistentDataPath))
                throw new IOException("Authoring storage requires an external Windows Editor temporary directory.");
            string name = Path.GetFileName(fullPath);
            if (!string.Equals(Path.GetDirectoryName(fullPath), temporary, StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith(RootPrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.Substring(RootPrefix.Length), "N", out _))
                throw new ArgumentException("Only a fresh authoring root directly under the system temporary directory is supported.", nameof(path));
            for (DirectoryInfo directory = new DirectoryInfo(fullPath); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Authoring roots cannot traverse reparse points.");
            if (owned)
            {
                if (string.IsNullOrEmpty(token) || !Directory.Exists(fullPath) ||
                    File.ReadAllText(Path.Combine(fullPath, MarkerName)) != token)
                    throw new IOException("Authoring save root ownership is missing or changed.");
            }
            else if (Directory.Exists(fullPath) || File.Exists(fullPath))
                throw new IOException("Authoring launch cannot reuse an existing path.");
        }

        private static bool IsWithin(string path, string root)
        {
            string boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(path, boundary, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void BeforeAssemblyReload()
        {
            if (current?.owner != null)
            {
                Cancel();
                EditorApplication.isPlaying = false;
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
                Cancel();
        }
    }
}
#endif