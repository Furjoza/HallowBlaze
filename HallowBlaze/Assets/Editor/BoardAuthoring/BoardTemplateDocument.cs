using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.World;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Names a deliberate dirty-document navigation decision; no implicit save or discard is permitted.</summary>
    public enum BoardDocumentDecision
    {
        /// <summary>Keeps the current document and cancels navigation.</summary>
        Cancel,
        /// <summary>Allows navigation only after a successful save has made the document clean.</summary>
        Save,
        /// <summary>Allows the owner to replace/close the current document without writing its changes.</summary>
        Discard
    }

    /// <summary>Owns one detached template, placement or route-pool draft, independent of scenes and other documents.</summary>
    public sealed class BoardTemplateDocument
    {
        private JObject content;
        private string savedContent;
        private JObject diagnosticContent;
        private IReadOnlyList<BoardAuthoringDiagnostic> cachedDiagnostics;
        private readonly Stack<JObject> undo = new Stack<JObject>();
        private readonly Stack<JObject> redo = new Stack<JObject>();
        private readonly Func<JObject, IReadOnlyList<BoardAuthoringDiagnostic>> validate;
        private readonly Func<JObject, string> serialize;
        private string poolWorld;
        private string[] poolTemplates;
        private JObject poolVersions = new JObject();

        private BoardTemplateDocument(JObject data, bool placement,
            Func<JObject, IReadOnlyList<BoardAuthoringDiagnostic>> validate, Func<JObject, string> serialize)
        {
            content = (JObject)data.DeepClone();
            IsPlacement = placement;
            this.validate = validate;
            this.serialize = serialize;
        }

        /// <summary>Gets whether this is a separate assignment document, never an assignment embedded in a grid.</summary>
        public bool IsPlacement { get; }
        /// <summary>Gets whether this independently saved document contains route sources rather than a board grid.</summary>
        public bool IsPool => poolWorld != null;
        /// <summary>Gets the last successfully opened/saved path, or null for a new/forked draft.</summary>
        public string SourcePath { get; private set; }
        /// <summary>Gets the exact saved file-byte fingerprint, not a gameplay seed or content identity.</summary>
        public string FileFingerprint { get; private set; }
        /// <summary>Gets whether a protected original may only be inspected or forked.</summary>
        public bool IsReadOnly { get; private set; }
        /// <summary>Gets whether document data differs from its last successful save; new documents are dirty.</summary>
        public bool IsDirty => savedContent == null || BoardAuthoringJson.Format(content) != savedContent;
        /// <summary>Gets whether an earlier edit can be restored without writing any file.</summary>
        public bool CanUndo => !IsReadOnly && undo.Count > 0;
        /// <summary>Gets whether an undone edit can be reapplied without allocating replacement IDs.</summary>
        public bool CanRedo => !IsReadOnly && redo.Count > 0;
        /// <summary>Gets current blocking diagnostics; invalid intermediate edits remain in the draft.</summary>
        public IReadOnlyList<BoardAuthoringDiagnostic> Diagnostics
        {
            get
            {
                if (!ReferenceEquals(diagnosticContent, content))
                {
                    cachedDiagnostics = validate(content);
                    diagnosticContent = content;
                }
                return cachedDiagnostics;
            }
        }

        /// <summary>Creates an intentionally incomplete, unsaved 8x8 draft; missing start/outcomes block saves.</summary>
        public static BoardTemplateDocument NewTemplate(string templateId, string configurationId, BoardValidationCatalog catalog)
        {
            RequireNewId(templateId);
            RequireNewId(configurationId);
            var data = new JObject
            {
                ["schemaVersion"] = 1, ["templateId"] = templateId, ["templateVersion"] = 1,
                ["configuration"] = new JObject
                {
                    ["id"] = configurationId, ["version"] = 1,
                    ["pathLength"] = Range(1, 63), ["branchPoints"] = Range(0, 64),
                    ["obstacleCount"] = Range(0, 64), ["enemyCount"] = Range(0, 63),
                    ["maximumConcurrentThreats"] = 63, ["minimumSafeStartMoves"] = 0,
                    ["allowedContentIds"] = new JArray("legacy.floor", "legacy.player", "ordinary.exit-terrain"),
                    ["resourceBudgets"] = new JArray()
                },
                ["variants"] = new JArray(new JObject { ["id"] = "base", ["entities"] = new JArray(), ["items"] = new JArray() }),
                ["outcomes"] = new JArray()
            };
            return Template(data, catalog);
        }

        /// <summary>Creates an unfinished shared board; painting an exit supplies its default completion without a fake landmark.</summary>
        public static BoardTemplateDocument NewBoard(string templateId, string configurationId, BoardValidationCatalog catalog)
        {
            JObject data = NewTemplate(templateId, configurationId, catalog).Snapshot();
            data["schemaVersion"] = 2;
            return Template(data, catalog);
        }

        /// <summary>Creates an independent editable shared snapshot, retaining the exact generated entities and bindings.</summary>
        public static BoardTemplateDocument FromSnapshot(JObject snapshot, BoardValidationCatalog catalog) => Template(snapshot, catalog);

        /// <summary>Opens all supported fields/variants into detached memory; invalid input throws without replacing another document.</summary>
        public static BoardTemplateDocument ReadTemplate(string json, BoardValidationCatalog catalog)
        {
            BoardTemplateDocument document = Template(BoardAuthoringJson.ReadTemplate(json, catalog), catalog);
            BoardAuthoringValidation.RequireValid(document.Diagnostics);
            return document;
        }

        /// <summary>Opens an independent assignment draft against the exact external publications; does not activate it.</summary>
        public static BoardTemplateDocument ReadPlacement(string json, WorldDefinition world, LandmarkTemplate template)
        {
            return new BoardTemplateDocument(BoardAuthoringJson.ReadPlacement(json, world, template), true,
                data => BoardAuthoringValidation.Placement(data, world, template),
                data => BoardAuthoringJson.WritePlacement(data, world, template));
        }

        /// <summary>Creates a separate shared assignment draft validated against exact immutable world/template publications.</summary>
        public static BoardTemplateDocument ReadAssignment(string json, WorldDefinition world, LandmarkTemplate template)
        {
            BoardAssignment.Read(json, world, template);
            return new BoardTemplateDocument(JObject.Parse(json), true, data =>
            {
                try { BoardAssignment.Read(data.ToString(), world, template); return Array.Empty<BoardAuthoringDiagnostic>(); }
                catch (Exception exception) { return new[] { new BoardAuthoringDiagnostic("Assignment", exception.Message) }; }
            }, data => BoardAssignment.Read(data.ToString(), world, template).ToJson());
        }

        /// <summary>Opens an independent pool manifest against detached exact world/template snapshots; all candidates must preflight.</summary>
        public static BoardTemplateDocument ReadPool(string json, string worldJson, IEnumerable<string> templateJsons, BoardValidationCatalog catalog)
        {
            string[] templates = templateJsons?.ToArray() ?? throw new ArgumentNullException(nameof(templateJsons));
            BoardPoolPublication publication = BoardPoolPublication.Read(worldJson, json, templates, catalog);
            return Pool(JObject.Parse(publication.ToJson()), worldJson, templates, catalog);
        }

        /// <summary>Creates an unsaved route manifest with generated sources; membership edits never modify template documents.</summary>
        public static BoardTemplateDocument NewPool(string worldJson, string edgeId, string publicationId,
            IEnumerable<string> templateJsons, BoardValidationCatalog catalog)
        {
            WorldDefinition world = WorldDefinitionJson.Deserialize(worldJson);
            if (!world.TryGetEdge(edgeId, out _)) throw new ArgumentException("Unknown route.", nameof(edgeId));
            RequireNewId(publicationId);
            return Pool(new JObject
            {
                ["schemaVersion"] = 1, ["publicationId"] = publicationId, ["publicationVersion"] = 1,
                ["worldDefinitionId"] = world.WorldDefinitionId, ["worldVersion"] = world.Version,
                ["edgeId"] = edgeId, ["sources"] = new JArray()
            }, worldJson, templateJsons?.ToArray() ?? throw new ArgumentNullException(nameof(templateJsons)), catalog);
        }

        /// <summary>Returns a complete detached copy; callers cannot mutate a document by retaining a JSON reference.</summary>
        public JObject Snapshot() => (JObject)content.DeepClone();

        internal JObject CaptureEditorState() => new JObject
        {
            ["content"] = content.DeepClone(), ["savedContent"] = savedContent, ["path"] = SourcePath,
            ["fingerprint"] = FileFingerprint, ["readOnly"] = IsReadOnly,
            ["poolWorld"] = poolWorld, ["poolTemplates"] = poolTemplates == null ? null : new JArray(poolTemplates),
            ["poolVersions"] = poolVersions.DeepClone(),
            ["undo"] = new JArray(undo.Reverse().Select(snapshot => snapshot.DeepClone())),
            ["redo"] = new JArray(redo.Reverse().Select(snapshot => snapshot.DeepClone()))
        };

        internal static BoardTemplateDocument RestoreEditorState(JObject state, BoardValidationCatalog catalog)
        {
            var document = state["poolWorld"]?.Type == JTokenType.String
                ? Pool((JObject)state["content"], (string)state["poolWorld"], state["poolTemplates"].Values<string>().ToArray(), catalog)
                : Template((JObject)state["content"], catalog);
            document.savedContent = (string)state["savedContent"];
            document.SourcePath = (string)state["path"];
            document.FileFingerprint = (string)state["fingerprint"];
            document.IsReadOnly = (bool)state["readOnly"];
            document.RetainPoolVersions(state["poolVersions"] as JObject);
            foreach (JObject snapshot in state["undo"]) document.undo.Push((JObject)snapshot.DeepClone());
            foreach (JObject snapshot in state["redo"]) document.redo.Push((JObject)snapshot.DeepClone());
            return document;
        }

        /// <summary>Applies one undoable edit atomically in memory; rejected callbacks leave data/history unchanged.</summary>
        public void Edit(Action<JObject> edit)
        {
            if (IsReadOnly) throw new InvalidOperationException("Published originals are read-only; fork a new identity first.");
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            var candidate = Snapshot();
            edit(candidate);
            if (BoardAuthoringJson.Format(candidate) == BoardAuthoringJson.Format(content)) return;
            undo.Push(content);
            content = (JObject)candidate.DeepClone();
            redo.Clear();
        }

        /// <summary>Restores the full preceding document, including stable IDs/bindings and its derived dirty state.</summary>
        public bool Undo()
        {
            if (!CanUndo) return false;
            redo.Push(content);
            content = undo.Pop();
            return true;
        }

        /// <summary>Reapplies the full undone edit without rerunning callbacks or allocating new identities.</summary>
        public bool Redo()
        {
            if (!CanRedo) return false;
            undo.Push(content);
            content = redo.Pop();
            return true;
        }

        /// <summary>Creates a new unpublished template identity; Save As alone never changes IDs or assignments.</summary>
        public BoardTemplateDocument Fork(string templateId, string configurationId, BoardValidationCatalog catalog)
        {
            if (IsPlacement || IsPool) throw new InvalidOperationException("Fork a board template, not its route sources.");
            RequireNewId(templateId);
            RequireNewId(configurationId);
            if (templateId == (string)content["templateId"] || configurationId == (string)content["configuration"]?["id"])
                throw new ArgumentException("Fork requires a new template and configuration identity.");
            JObject candidate = Snapshot();
            candidate["templateId"] = templateId;
            candidate["templateVersion"] = 1;
            candidate["configuration"]["id"] = configurationId;
            candidate["configuration"]["version"] = 1;
            return Template(candidate, catalog);
        }

        /// <summary>Checks explicit navigation consent; failed/cancelled Save never discards or replaces the draft.</summary>
        /// <param name="save">Owner-supplied save operation; success also requires the document to be clean.</param>
        public bool CanLeave(BoardDocumentDecision decision, Func<bool> save = null)
        {
            if (!Enum.IsDefined(typeof(BoardDocumentDecision), decision)) throw new ArgumentOutOfRangeException(nameof(decision));
            if (!IsDirty) return true;
            if (decision == BoardDocumentDecision.Discard) return true;
            return decision == BoardDocumentDecision.Save && save != null && save() && !IsDirty;
        }

        /// <summary>Serializes a validated draft only; no file, publication, seed or other document is modified.</summary>
        public string ToJson()
        {
            BoardAuthoringValidation.RequireValid(Diagnostics);
            return serialize(content);
        }

        internal string ToPoolFileJson() => new JObject
        {
            ["authoringSchemaVersion"] = 2, ["publication"] = JObject.Parse(ToJson()),
            ["worldJson"] = poolWorld, ["templateJsons"] = new JArray(poolTemplates),
            ["poolVersions"] = CapturePoolVersions()
        }.ToString(Newtonsoft.Json.Formatting.Indented);

        internal void MarkSaved(string path, string fingerprint, bool readOnly)
        {
            if (IsPool) poolVersions = CapturePoolVersions();
            SourcePath = path;
            FileFingerprint = fingerprint;
            savedContent = BoardAuthoringJson.Format(content);
            IsReadOnly = readOnly;
        }

        internal JObject SavedSnapshot() => savedContent == null ? null : JObject.Parse(savedContent);

        internal int NextPoolVersion(string id) => checked(((int?)poolVersions[id] ?? 0) + 1);

        internal void RetainPoolVersions(JObject versions)
        {
            if (versions == null) return;
            foreach (JProperty entry in versions.Properties())
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name != entry.Name.Trim() || entry.Value.Type != JTokenType.Integer || (int)entry.Value < 1)
                    throw new ArgumentException("Invalid retained pool version history.");
                poolVersions[entry.Name] = Math.Max((int?)poolVersions[entry.Name] ?? 0, (int)entry.Value);
            }
        }

        private JObject CapturePoolVersions()
        {
            var retained = (JObject)poolVersions.DeepClone();
            foreach (JObject source in content["sources"])
                if (source["poolId"].Type == JTokenType.String)
                {
                    string id = (string)source["poolId"];
                    retained[id] = Math.Max((int?)retained[id] ?? 0, (int)source["poolVersion"]);
                }
            return retained;
        }

        private static BoardTemplateDocument Template(JObject data, BoardValidationCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            return new BoardTemplateDocument(data, false, snapshot => BoardAuthoringValidation.Template(snapshot, catalog),
                snapshot => BoardAuthoringJson.WriteTemplate(snapshot, catalog));
        }

        private static BoardTemplateDocument Pool(JObject data, string worldJson, string[] templates, BoardValidationCatalog catalog)
        {
            var captured = (string[])templates.Clone();
            return new BoardTemplateDocument(data, false, snapshot =>
            {
                try { BoardPoolPublication.Read(worldJson, snapshot.ToString(), captured, catalog); return Array.Empty<BoardAuthoringDiagnostic>(); }
                catch (Exception exception) { return new[] { new BoardAuthoringDiagnostic("Pool", exception.Message) }; }
            }, snapshot => BoardPoolPublication.Read(worldJson, snapshot.ToString(), captured, catalog).ToJson())
            { poolWorld = worldJson, poolTemplates = captured };
        }

        private static JObject Range(int minimum, int maximum) => new JObject { ["minimum"] = minimum, ["maximum"] = maximum };

        private static void RequireNewId(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier) || identifier != identifier.Trim())
                throw new ArgumentException("A new nonblank stable ID without surrounding whitespace is required.");
        }
    }
}