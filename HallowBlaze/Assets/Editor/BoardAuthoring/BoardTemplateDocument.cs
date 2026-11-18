using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Generation.Landmarks;
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

    /// <summary>Owns one detached template or placement draft, independent of Unity scenes and other documents.</summary>
    public sealed class BoardTemplateDocument
    {
        private JObject content;
        private string savedContent;
        private readonly Stack<JObject> undo = new Stack<JObject>();
        private readonly Stack<JObject> redo = new Stack<JObject>();
        private readonly Func<JObject, IReadOnlyList<BoardAuthoringDiagnostic>> validate;
        private readonly Func<JObject, string> serialize;

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
        public IReadOnlyList<BoardAuthoringDiagnostic> Diagnostics => validate(content);

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

        /// <summary>Returns a complete detached copy; callers cannot mutate a document by retaining a JSON reference.</summary>
        public JObject Snapshot() => (JObject)content.DeepClone();

        internal JObject CaptureEditorState() => new JObject
        {
            ["content"] = content.DeepClone(), ["savedContent"] = savedContent, ["path"] = SourcePath,
            ["fingerprint"] = FileFingerprint, ["readOnly"] = IsReadOnly,
            ["undo"] = new JArray(undo.Reverse().Select(snapshot => snapshot.DeepClone())),
            ["redo"] = new JArray(redo.Reverse().Select(snapshot => snapshot.DeepClone()))
        };

        internal static BoardTemplateDocument RestoreEditorState(JObject state, BoardValidationCatalog catalog)
        {
            var document = Template((JObject)state["content"], catalog);
            document.savedContent = (string)state["savedContent"];
            document.SourcePath = (string)state["path"];
            document.FileFingerprint = (string)state["fingerprint"];
            document.IsReadOnly = (bool)state["readOnly"];
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
            if (IsPlacement) throw new InvalidOperationException("Fork a board template, not its route assignment.");
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

        internal void MarkSaved(string path, string fingerprint, bool readOnly)
        {
            SourcePath = path;
            FileFingerprint = fingerprint;
            savedContent = BoardAuthoringJson.Format(content);
            IsReadOnly = readOnly;
        }

        private static BoardTemplateDocument Template(JObject data, BoardValidationCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            return new BoardTemplateDocument(data, false, snapshot => BoardAuthoringValidation.Template(snapshot, catalog),
                snapshot => BoardAuthoringJson.WriteTemplate(snapshot, catalog));
        }

        private static JObject Range(int minimum, int maximum) => new JObject { ["minimum"] = minimum, ["maximum"] = maximum };

        private static void RequireNewId(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier) || identifier != identifier.Trim())
                throw new ArgumentException("A new nonblank stable ID without surrounding whitespace is required.");
        }
    }
}