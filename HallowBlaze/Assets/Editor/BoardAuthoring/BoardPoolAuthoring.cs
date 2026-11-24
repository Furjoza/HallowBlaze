using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Owns independent route-source editing; saved boards never become members without an explicit entry command.</summary>
    [Serializable]
    public sealed class BoardPoolAuthoring
    {
        [SerializeField] private string editorState = "";
        [SerializeField] private bool visible;
        [SerializeField] private string routeId = "road.shelter-warm-up";
        [SerializeField] private string segmentId = "";
        [SerializeField] private string candidateKey = "";
        [SerializeField] private string newPublicationId = "draft.route-pools";
        [SerializeField] private bool creating;
        [SerializeField] private string[] availableTemplates;
        [SerializeField] private TemplateName[] templateNames = Array.Empty<TemplateName>();
        private BoardTemplateDocument document;
        private Rect modeRect;
        private Rect addRect;
        private Rect undoRect;
        private Rect redoRect;
        private Rect saveAsRect;
        private GUIContent[] candidateLabels = Array.Empty<GUIContent>();
        private Vector2 entriesScroll;
        private Vector2 guiOrigin;
        private Rect panelRect;
        private bool keyboardFocus;

        [Serializable]
        private sealed class TemplateName
        {
            public string Key;
            public string Name;
        }

        internal bool KeyboardFocus => keyboardFocus;
        internal void ObservePointer(Event input)
        {
            if (input.type == EventType.MouseDown) keyboardFocus = panelRect.Contains(input.mousePosition);
        }

        /// <summary>Gets the independently dirty and undoable manifest, including invalid intermediate membership edits.</summary>
        public BoardTemplateDocument Document
        {
            get
            {
                if (document == null && !string.IsNullOrEmpty(editorState))
                    document = BoardTemplateDocument.RestoreEditorState(JObject.Parse(editorState), OrdinaryBoardContent.CreateAuthoringCatalog());
                return document;
            }
        }

        /// <summary>Creates a new explicit route publication; absent segment sources remain generated, not inferred from old binding files.</summary>
        public void Create(BoardDocumentStore store, string worldJson, string edgeId, string publicationId)
        {
            var candidate = BoardTemplateDocument.NewPool(worldJson, edgeId, publicationId, Library(store, worldJson), OrdinaryBoardContent.CreateAuthoringCatalog());
            document = candidate;
            routeId = edgeId;
            segmentId = WorldDefinitionJson.Deserialize(worldJson).Edges.Single(edge => edge.EdgeId == edgeId).Segments[0].SegmentId;
            visible = true;
            creating = false;
            availableTemplates = Templates.Select(TemplateKey).ToArray();
            Remember();
        }

        /// <summary>Opens a separately saved manifest only after complete library/world preflight; errors preserve the previous draft.</summary>
        public void Open(BoardDocumentStore store, string path, string worldJson)
        {
            var candidate = store.OpenPool(path, worldJson, Library(store, worldJson), OrdinaryBoardContent.CreateAuthoringCatalog());
            document = candidate;
            routeId = (string)candidate.Snapshot()["edgeId"];
            segmentId = WorldDefinitionJson.Deserialize(worldJson).Edges.Single(edge => edge.EdgeId == routeId).Segments[0].SegmentId;
            visible = true;
            creating = false;
            availableTemplates = Library(store, worldJson).Select(TemplateKey).ToArray();
            Remember();
        }

        /// <summary>Saves only this manifest through the existing atomic, physical-path and external-file conflict guards.</summary>
        public BoardDocumentSaveResult Save(BoardDocumentStore store, string path = null, bool confirmOverwrite = false, string fingerprint = null)
        {
            if (Document == null) throw new InvalidOperationException("No route-source document is open.");
            Document.Edit(data => VersionChangedContent(data, Document.SavedSnapshot()));
            RefreshLibrary(store);
            try { ValidateLiveArtifacts(store, out _); }
            catch (ArgumentException exception) { return new BoardDocumentSaveResult(BoardDocumentSaveStatus.Invalid, path ?? Document.SourcePath, exception.Message); }
            var result = store.Save(Document, path, confirmOverwrite, fingerprint);
            Remember();
            return result;
        }

        /// <summary>Changes one stable segment's explicit source mode; configured empty fixed/pool sources remain invalid until an entry is added.</summary>
        public void SetSource(string targetSegment, string mode)
        {
            if (mode != "generated" && mode != "fixed" && mode != "pool") throw new ArgumentException("Unknown source mode.", nameof(mode));
            WorldDefinition world = WorldDefinitionJson.Deserialize(WorldJson);
            if (!world.TryGetSegment(targetSegment, out var segment, out var edge) || edge.EdgeId != (string)Document.Snapshot()["edgeId"])
                throw new ArgumentException("The segment belongs to another route.", nameof(targetSegment));
            Change(data =>
            {
                var sources = (JArray)data["sources"];
                JObject source = Source(data, targetSegment);
                if (mode == "generated") { source?.Remove(); return; }
                if (source == null)
                {
                    source = new JObject { ["segmentId"] = targetSegment, ["generationConfigurationId"] = segment.GenerationConfigurationId,
                        ["generationVersion"] = segment.GenerationVersion, ["poolId"] = JValue.CreateNull(), ["poolVersion"] = 0, ["entries"] = new JArray() };
                    sources.Add(source);
                }
                bool pooled = source["poolId"].Type != JTokenType.Null;
                if (mode == "pool" && !pooled)
                {
                    string id = (string)data["publicationId"] + "." + targetSegment;
                    source["poolId"] = id; source["poolVersion"] = Document.NextPoolVersion(id);
                }
                if (mode == "fixed" && pooled)
                {
                    source["poolId"] = JValue.CreateNull(); source["poolVersion"] = 0;
                    var entries = (JArray)source["entries"];
                    if (entries.Count > 1) entries.RemoveAll();
                }
            });
        }

        /// <summary>Adds an exact compatible tuple, or replaces the single entry of a fixed source; duplicate pooled tuples are rejected.</summary>
        public void AddEntry(string targetSegment, string templateId, int templateVersion, string variantId)
        {
            var template = Templates.Select(json => BoardTemplateJson.ReadCompatible(json, OrdinaryBoardContent.CreateAuthoringCatalog()))
                .FirstOrDefault(candidate => candidate.Id == templateId && candidate.Version == templateVersion)
                ?? throw new ArgumentException("The exact saved template version is missing.");
            new BoardAssignment(WorldDefinitionJson.Deserialize(WorldJson), targetSegment, template, variantId);
            Change(data =>
            {
                JObject source = Source(data, targetSegment) ?? throw new InvalidOperationException("Choose Fixed or Pool first.");
                var entries = (JArray)source["entries"];
                var entry = new JObject { ["templateId"] = templateId, ["templateVersion"] = templateVersion, ["variantId"] = variantId };
                if (source["poolId"].Type == JTokenType.Null) entries.RemoveAll();
                else if (entries.Any(existing => JToken.DeepEquals(existing, entry))) throw new ArgumentException("This exact entry is already a member.");
                entries.Add(entry);
            });
        }

        /// <summary>Removes only the specified tuple; removing the last member never silently changes the source to generation.</summary>
        public void RemoveEntry(string targetSegment, JObject entry)
        {
            JObject retained = (JObject)entry.DeepClone();
            Change(data => Source(data, targetSegment)?["entries"].FirstOrDefault(candidate => JToken.DeepEquals(candidate, retained))?.Remove());
        }

        /// <summary>Refreshes candidate availability while refusing changed content beneath an existing exact template/version key.</summary>
        public void RefreshLibrary(BoardDocumentStore store)
        {
            string[] latest = Library(store, WorldJson);
            foreach (string previous in Templates)
            {
                JObject old = JObject.Parse(previous);
                JObject replacement = latest.Select(JObject.Parse).FirstOrDefault(candidate => (string)candidate["templateId"] == (string)old["templateId"] &&
                    (int)candidate["templateVersion"] == (int)old["templateVersion"]);
                if (replacement != null && !JToken.DeepEquals(old, replacement))
                    throw new InvalidOperationException("Saved map content changed under the same ID/version; bump its version or fork before refreshing.");
            }
            JObject state = Document.CaptureEditorState();
            state["poolTemplates"] = new JArray(Templates.Concat(latest).GroupBy(TemplateKey).Select(group => group.First()));
            document = BoardTemplateDocument.RestoreEditorState(state, OrdinaryBoardContent.CreateAuthoringCatalog());
            availableTemplates = latest.Select(TemplateKey).ToArray();
            Remember();
        }

        /// <summary>Gets detached candidates captured for this draft, not mutable files or automatic membership.</summary>
        public string[] Templates => Document.CaptureEditorState()["poolTemplates"].Values<string>().ToArray();
        /// <summary>Gets the captured exact world; selecting another world never rewrites this publication.</summary>
        public string WorldJson => (string)Document.CaptureEditorState()["poolWorld"];

        /// <summary>Revalidates saved bytes and every current artifact before returning an immutable activation capture.</summary>
        public BoardPoolPublication CaptureSaved(BoardDocumentStore store, out string[] templates)
        {
            if (Document == null || Document.IsDirty || string.IsNullOrEmpty(Document.SourcePath))
                throw new InvalidOperationException("Save the route sources before previewing activation.");
            if (store.CaptureFingerprint(Document.SourcePath) != Document.FileFingerprint)
                throw new InvalidOperationException("The route-source file changed outside the Editor; reopen it.");
            return ValidateLiveArtifacts(store, out templates);
        }

        private BoardPoolPublication ValidateLiveArtifacts(BoardDocumentStore store, out string[] templates)
        {
            templates = Library(store, WorldJson);
            var captured = BoardPoolPublication.Read(WorldJson, Document.ToJson(), templates, OrdinaryBoardContent.CreateAuthoringCatalog());
            var expected = BoardPoolPublication.Read(WorldJson, Document.ToJson(), Templates, OrdinaryBoardContent.CreateAuthoringCatalog());
            if (captured.ContentHash != expected.ContentHash) throw new InvalidOperationException("Saved maps changed; refresh or reopen the route sources before activation.");
            return captured;
        }

        private static string TemplateKey(string json)
        {
            JObject data = JObject.Parse(json);
            return new JArray(data["templateId"].DeepClone(), data["templateVersion"].DeepClone()).ToString(Formatting.None);
        }

        internal GUIContent DescribeEntry(string templateId, int templateVersion, string variantId)
        {
            string key = new JArray(templateId, templateVersion).ToString(Formatting.None);
            string name = templateNames?.FirstOrDefault(candidate => candidate.Key == key)?.Name ?? templateId;
            string text = name + " | v" + templateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + " | " + variantId;
            return new GUIContent(text.Replace('/', '-').Replace('\\', '-'), templateId + "/" + templateVersion + " | " + variantId);
        }

        internal string Capture()
        {
            Remember();
            return JsonUtility.ToJson(this);
        }

        internal static BoardPoolAuthoring Restore(string state) => string.IsNullOrEmpty(state) ? new BoardPoolAuthoring() : JsonUtility.FromJson<BoardPoolAuthoring>(state);

        internal void Draw(BoardDocumentStore store, string currentWorld, Vector2 origin, Action<Action> defer,
            Func<bool> resolve, Action save, Action saveAs, Action open, Action play)
        {
            guiOrigin = origin;
            using (var area = new EditorGUILayout.VerticalScope())
            {
                DrawContent(store, currentWorld, defer, resolve, save, saveAs, open, play);
                if (Event.current.type == EventType.Repaint) panelRect = WindowRect(area.rect);
            }
        }

        private Rect WindowRect(Rect rect) => new Rect(GUIUtility.GUIToScreenPoint(rect.position) - guiOrigin, rect.size);

        private void DrawContent(BoardDocumentStore store, string currentWorld, Action<Action> defer,
            Func<bool> resolve, Action save, Action saveAs, Action open, Action play)
        {
            visible = EditorGUILayout.Foldout(visible, "Route Sources", true);
            if (!visible) return;
            WorldDefinition world = WorldDefinitionJson.Deserialize(Document == null || creating ? currentWorld : WorldJson);
            using (new EditorGUILayout.HorizontalScope())
            {
                Command("Toolbar Plus", "New route sources", () => creating = true, defer);
                Command("FolderOpened Icon", "Open route sources", open, defer);
                using (new EditorGUI.DisabledScope(Document == null))
                {
                    Command("SaveActive", "Save route sources", save, defer);
                    Command("SaveAs", "Save route sources as", saveAs, defer);
                    saveAsRect = WindowRect(GUILayoutUtility.GetLastRect());
                    Command("Refresh", "Refresh available exact maps", () => RefreshLibrary(store), defer);
                    using (new EditorGUI.DisabledScope(Document == null || !Document.CanUndo))
                    { Command("back", "Undo source edit", () => { Document.Undo(); Remember(); }, defer); undoRect = WindowRect(GUILayoutUtility.GetLastRect()); }
                    using (new EditorGUI.DisabledScope(Document == null || !Document.CanRedo))
                    { Command("forward", "Redo source edit", () => { Document.Redo(); Remember(); }, defer); redoRect = WindowRect(GUILayoutUtility.GetLastRect()); }
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(Document == null ? "No sources" : Document.IsDirty ? "Modified" : "Saved", EditorStyles.miniBoldLabel);
            }
            if (Document == null || creating)
            {
                var routes = world.Edges.ToArray();
                int selected = Math.Max(0, Array.FindIndex(routes, edge => edge.EdgeId == routeId));
                routeId = routes[EditorGUILayout.Popup("Route", selected, routes.Select(edge => edge.EdgeId).ToArray())].EdgeId;
                newPublicationId = EditorGUILayout.TextField("Publication ID", newPublicationId);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create")) defer(() => { if (resolve()) Create(store, currentWorld, routeId, newPublicationId); });
                    if (creating && GUILayout.Button("Cancel")) defer(() => creating = false);
                }
                return;
            }
            JObject data = Document.Snapshot();
            var route = world.Edges.Single(edge => edge.EdgeId == (string)data["edgeId"]);
            EditorGUILayout.SelectableLabel((string)data["publicationId"] + "/" + (int)data["publicationVersion"] + " | " + route.EdgeId + "\n" +
                route.FromNodeId + " > " + route.ToNodeId + " | " + world.WorldDefinitionId + "/" + world.Version,
                EditorStyles.wordWrappedMiniLabel, GUILayout.Height(40));
            int current = Math.Max(0, route.Segments.ToList().FindIndex(segment => segment.SegmentId == segmentId));
            current = EditorGUILayout.Popup("Segment", current, route.Segments.Select((segment, index) => (index + 1) + "/" + route.SegmentCount + " | " + segment.SegmentId).ToArray());
            segmentId = route.Segments[current].SegmentId;
            string target = segmentId;
            JObject source = Source(data, target);
            int mode = source == null ? 0 : source["poolId"].Type == JTokenType.Null ? 1 : 2;
            int chosen = GUILayout.Toolbar(mode, new[] { "Generated", "Fixed", "Pool" });
            modeRect = WindowRect(GUILayoutUtility.GetLastRect());
            if (chosen != mode) defer(() => SetSource(target, new[] { "generated", "fixed", "pool" }[chosen]));
            var segmentDefinition = route.Segments[current];
            EditorGUILayout.LabelField(segmentDefinition.GenerationConfigurationId + "/" + segmentDefinition.GenerationVersion + " | " +
                (world.TryGetNode(route.ToNodeId, out var destination) ? destination.BiomeFamily : "Unknown catalog"), EditorStyles.wordWrappedMiniLabel);
            if (source != null)
            {
                var candidates = new List<(string Key, string Id, int Version, string Variant)>();
                foreach (string json in Templates)
                {
                    if (availableTemplates != null && !availableTemplates.Contains(TemplateKey(json))) continue;
                    var template = BoardTemplateJson.ReadCompatible(json, OrdinaryBoardContent.CreateAuthoringCatalog());
                    foreach (string variant in template.VariantIds)
                    {
                        try { new BoardAssignment(world, target, template, variant); }
                        catch (ArgumentException) { continue; }
                        candidates.Add((template.Id + "/" + template.Version + " | " + variant, template.Id, template.Version, variant));
                    }
                }
                candidates = candidates.GroupBy(candidate => candidate.Key).Select(group => group.First()).OrderBy(candidate => candidate.Key, StringComparer.Ordinal).ToList();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (candidates.Count > 0)
                    {
                        int index = Math.Max(0, candidates.FindIndex(candidate => candidate.Key == candidateKey));
                        candidateLabels = candidates.Select(candidate => DescribeEntry(candidate.Id, candidate.Version, candidate.Variant)).ToArray();
                        index = EditorGUILayout.Popup(index, candidateLabels);
                        var candidate = candidates[index];
                        candidateKey = candidate.Key;
                        Command("Toolbar Plus", "Add exact map and variant", () => AddEntry(target, candidate.Id, candidate.Version, candidate.Variant), defer);
                        addRect = WindowRect(GUILayoutUtility.GetLastRect());
                    }
                    else GUILayout.Label("No compatible saved maps", EditorStyles.wordWrappedMiniLabel);
                }
                var entries = (JArray)source["entries"];
                EditorGUILayout.LabelField(entries.Count + " entry(s) | " + (entries.Count == 0 ? "Empty source" :
                    (100m / entries.Count).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "% each"), EditorStyles.miniLabel);
                entriesScroll = EditorGUILayout.BeginScrollView(entriesScroll, GUILayout.Height(Math.Min(140, Math.Max(36, entries.Count * 38))));
                foreach (JObject entry in entries)
                {
                    JObject retained = (JObject)entry.DeepClone();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(DescribeEntry((string)entry["templateId"], (int)entry["templateVersion"], (string)entry["variantId"]),
                            EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(32));
                        Command("Toolbar Minus", "Remove this exact entry", () => RemoveEntry(target, retained), defer);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            foreach (var diagnostic in Document.Diagnostics) EditorGUILayout.HelpBox(diagnostic.Message, MessageType.Error);
            using (new EditorGUI.DisabledScope(Document.IsDirty || Document.Diagnostics.Count > 0 || string.IsNullOrEmpty(Document.SourcePath)))
                Command("PlayButton", "Preview and play saved route sources", play, defer);
        }

        private static void Command(string icon, string tooltip, Action action, Action<Action> defer)
        {
            if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent(icon).image, tooltip), GUILayout.Width(27), GUILayout.Height(22))) defer(action);
        }

        internal void Remember() => editorState = Document?.CaptureEditorState().ToString(Formatting.None) ?? "";
        internal static JObject Source(JObject data, string targetSegment) => data["sources"].OfType<JObject>().SingleOrDefault(source => (string)source["segmentId"] == targetSegment);

        private void Change(Action<JObject> edit)
        {
            JObject before = Document.Snapshot();
            JObject saved = Document.SavedSnapshot();
            Document.Edit(data =>
            {
                edit(data);
                if (JToken.DeepEquals(before, data)) return;
                VersionChangedContent(data, saved);
            });
            Remember();
        }

        private void VersionChangedContent(JObject data, JObject saved)
        {
            if (saved == null || JToken.DeepEquals(data, saved)) return;
            data["publicationVersion"] = Math.Max((int)data["publicationVersion"], checked((int)saved["publicationVersion"] + 1));
            foreach (JObject source in data["sources"])
            {
                JObject old = Source(saved, (string)source["segmentId"]);
                if (source["poolId"].Type == JTokenType.String && (old == null || !JToken.DeepEquals(old, source)))
                    source["poolVersion"] = Math.Max((int)source["poolVersion"], Document.NextPoolVersion((string)source["poolId"]));
            }
        }

        private string[] Library(BoardDocumentStore store, string worldJson)
        {
            if (store == null) throw new InvalidOperationException("Choose an external board library first.");
            var catalog = OrdinaryBoardContent.CreateAuthoringCatalog();
            var boards = store.ListBoards(WorldDefinitionJson.Deserialize(worldJson), catalog, out var diagnostics);
            if (diagnostics.Count > 0) throw new InvalidOperationException("Invalid board library: " + diagnostics[0]);
            templateNames = boards.GroupBy(board => new JArray(board.TemplateId, board.TemplateVersion).ToString(Formatting.None))
                .Select(group => new TemplateName
                {
                    Key = group.Key,
                    Name = string.Join(" + ", group.Select(board => board.Name).Distinct(StringComparer.Ordinal)
                        .OrderBy(name => name, StringComparer.Ordinal))
                }).ToArray();
            return boards.Select(board => board.Path).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => store.OpenTemplate(path, catalog).ToJson())
                .Where(json => BoardTemplateJson.ReadCompatible(json, catalog).IsShared).ToArray();
        }
    }
}