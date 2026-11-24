using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Edits named shared-board drafts and exact sandbox assignments without publishing campaign content.</summary>
    public sealed class BoardAuthoringWindow : EditorWindow
    {
        private const string PendingRecovery = "HallowBlaze.BoardAuthoring.PendingRecovery";
        [SerializeField] private string draftRoot = "";
        [SerializeField] private string editorState = "";
        [SerializeField] private string variantId = "base";
        [SerializeField] private string newTemplateId = "draft.board";
        [SerializeField] private string newConfigurationId = "draft.board.config";
        [SerializeField] private bool identityPanel;
        [SerializeField] private bool forkRequested;
        private bool focusIdentity;
        [SerializeField] private string recoveryId;
        [SerializeField] private BoardTestPlaySettings testSettings = new BoardTestPlaySettings();
        [SerializeField] private int generationDay = 1;
        [SerializeField] private BoardAssignmentPanel assignmentPanel = new BoardAssignmentPanel();
        [SerializeField] private BoardPoolAuthoring poolAuthoring = new BoardPoolAuthoring();
        [SerializeField] private string boardName = "";
        [SerializeField] private string savedAssignmentPath = "";
        [SerializeField] private string savedAssignmentFingerprint = "";
        [SerializeField] private string savedDetails = "";
        [SerializeField] private bool bindingSavePending;
        [SerializeField] private bool libraryVisible;
        private IReadOnlyList<BoardSavedDraft> savedBoards = Array.Empty<BoardSavedDraft>();
        private IReadOnlyList<string> libraryDiagnostics = Array.Empty<string>();
        private Vector2 libraryScroll;
        [SerializeField] private string solverOutcome = "";
        private string solverMessage = "";
        private BoardSolverReport boardCheck;
        private string boardCheckInputs;
        private Rect checkBoardRect;
        private Rect playVariantRect;
        private Rect playRouteRect;
        private BoardTemplateDocument document;
        private BoardDocumentStore store;
        private BoardGridView grid;
        private BoardPropertyPanel properties;
        private IReadOnlyList<BoardAuthoringDiagnostic> diagnostics = Array.Empty<BoardAuthoringDiagnostic>();
        private string error = "";
        private string rejectedPath = "";
        private Vector2 scroll;
        private Vector2 paletteScroll;
        private Vector2 diagnosticsScroll;
        private Vector2 guiOrigin;
        private Rect workspaceRect;
        private Rect workspaceViewportRect;
        private Rect paletteViewportRect;
        private Rect paletteLastButtonRect;
        private bool allowClose;
        private bool quitting;
        private bool callbacksActive;
        private int callbackEpoch;
        private readonly HashSet<EditorApplication.CallbackFunction> pendingCallbacks = new HashSet<EditorApplication.CallbackFunction>();

        /// <summary>Gets the current board snapshot independently of its optional saved assignments.</summary>
        public BoardTemplateDocument Document => document;
        /// <summary>Gets the active canvas and its read-only selection/gesture state.</summary>
        public BoardGridView Grid => grid ?? (grid = new BoardGridView());
        /// <summary>Gets the selected stable variant ID, never its array index.</summary>
        public string SelectedVariant => variantId;
        /// <summary>Gets the explicit optional sandbox target, independent of the board document's mutable layout.</summary>
        public BoardAssignmentPanel Assignment => assignmentPanel;
        /// <summary>Gets route-source membership and its independent save/undo state; grid edits never implicitly change it.</summary>
        public BoardPoolAuthoring Pools => poolAuthoring;
        /// <summary>Gets the last operation error without discarding the currently edited document.</summary>
        public string LastError => error;
        /// <summary>Gets the last playability result only while the board, variant and starting inputs still match its evidence.</summary>
        public BoardSolverReport BoardCheck => boardCheckInputs == CaptureBoardCheckInputs() ? boardCheck : null;
        /// <summary>Gets the canvas's window-local bounds for deterministic pointer/keyboard smoke tests.</summary>
        public Rect CanvasRect { get; private set; }

        [MenuItem("HallowBlaze/Authoring/Board Editor")]
        private static void OpenWindow() => GetWindow<BoardAuthoringWindow>("Board Editor").Show();

        private void OnEnable()
        {
            callbacksActive = true;
            callbackEpoch++;
            titleContent = new GUIContent("Board Editor");
            minSize = new Vector2(640, 480);
            if (string.IsNullOrEmpty(recoveryId)) recoveryId = Guid.NewGuid().ToString("N");
            grid = new BoardGridView();
            properties = new BoardPropertyPanel(Defer);
            if (!string.IsNullOrEmpty(draftRoot)) TryConfigureRoot(draftRoot);
            if (document == null && !string.IsNullOrEmpty(editorState))
            {
                try { document = BoardTemplateDocument.RestoreEditorState(JObject.Parse(editorState), OrdinaryBoardContent.CreateAuthoringCatalog()); }
                catch (Exception exception) { error = "Draft recovery failed: " + exception.Message; }
            }
            if (string.IsNullOrEmpty(boardName) && document != null)
                boardName = document.SourcePath == null ? (string)document.Snapshot()["templateId"] : Path.GetFileNameWithoutExtension(document.SourcePath);
            RefreshDocument();
            EditorApplication.wantsToQuit += WantsToQuit;
            EditorApplication.quitting += OnQuitting;
        }

        private void OnDisable()
        {
            callbacksActive = false;
            callbackEpoch++;
            foreach (EditorApplication.CallbackFunction callback in pendingCallbacks) EditorApplication.update -= callback;
            pendingCallbacks.Clear();
            Remember();
            EditorApplication.wantsToQuit -= WantsToQuit;
            EditorApplication.quitting -= OnQuitting;
        }

        private void OnDestroy()
        {
            if (allowClose || quitting || !HasPendingChanges()) return;
            Remember();
            SessionState.SetString(PendingRecovery, new JObject
            {
                ["id"] = recoveryId, ["state"] = editorState, ["root"] = draftRoot, ["variant"] = variantId,
                ["details"] = CaptureDetails(), ["assignment"] = JsonUtility.ToJson(assignmentPanel), ["savedDetails"] = savedDetails,
                ["assignmentPath"] = savedAssignmentPath, ["assignmentFingerprint"] = savedAssignmentFingerprint, ["bindingPending"] = bindingSavePending,
                ["pools"] = poolAuthoring.Capture()
            }.ToString(Formatting.None));
            ScheduleRecovery();
        }

        [InitializeOnLoadMethod]
        private static void ScheduleRecovery() => EditorApplication.delayCall += RecoverPending;

        private static void RecoverPending()
        {
            string pending = SessionState.GetString(PendingRecovery, "");
            if (string.IsNullOrEmpty(pending)) return;
            if (EditorApplication.isCompiling) { EditorApplication.delayCall += RecoverPending; return; }
            JObject state = JObject.Parse(pending);
            if (Resources.FindObjectsOfTypeAll<BoardAuthoringWindow>().Any(window => window.recoveryId == (string)state["id"]))
            { SessionState.EraseString(PendingRecovery); return; }
            var recovered = CreateInstance<BoardAuthoringWindow>();
            recovered.recoveryId = (string)state["id"];
            recovered.draftRoot = (string)state["root"];
            if (!string.IsNullOrEmpty(recovered.draftRoot)) recovered.TryConfigureRoot(recovered.draftRoot);
            recovered.document = string.IsNullOrEmpty((string)state["state"]) ? null :
                BoardTemplateDocument.RestoreEditorState(JObject.Parse((string)state["state"]), OrdinaryBoardContent.CreateAuthoringCatalog());
            recovered.variantId = (string)state["variant"];
            if (state["details"] is JObject details) recovered.boardName = (string)details["name"];
            if (state["assignment"] != null) recovered.assignmentPanel = JsonUtility.FromJson<BoardAssignmentPanel>((string)state["assignment"]);
            recovered.savedDetails = (string)state["savedDetails"] ?? "";
            recovered.savedAssignmentPath = (string)state["assignmentPath"] ?? "";
            recovered.savedAssignmentFingerprint = (string)state["assignmentFingerprint"] ?? "";
            recovered.bindingSavePending = (bool?)state["bindingPending"] ?? false;
            recovered.poolAuthoring = BoardPoolAuthoring.Restore((string)state["pools"]);
            recovered.error = "Close did not save or discard the draft. The document was retained.";
            recovered.RefreshDocument();
            recovered.Show();
            SessionState.EraseString(PendingRecovery);
        }

        private bool WantsToQuit() => ResolveAllDirty(AskDecision, SaveAllDrafts);
        private void OnQuitting() => quitting = true;

        /// <summary>Resolves the native Editor dirty-window save action; failed saves keep the draft dirty and recoverable.</summary>
        public override void SaveChanges()
        {
            if (SaveAllDrafts()) { allowClose = true; base.SaveChanges(); }
        }

        /// <summary>Records an explicit native Discard decision; no file or campaign data is changed.</summary>
        public override void DiscardChanges()
        {
            allowClose = true;
            base.DiscardChanges();
        }

        /// <summary>Configures the external draft directory using the verified store path guard; failure leaves the previous store intact.</summary>
        public bool TryConfigureRoot(string path)
        {
            try
            {
                var candidate = new BoardDocumentStore(path, Path.GetDirectoryName(Application.dataPath));
                store = candidate;
                draftRoot = Path.GetFullPath(path);
                error = "";
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        /// <summary>Creates a blank draft only after the current dirty document has been deliberately resolved.</summary>
        public bool TryNew(string templateId, string configurationId, Func<BoardDocumentDecision> decide, Func<bool> save = null)
        {
            try
            {
                BoardTemplateDocument candidate = BoardTemplateDocument.NewBoard(templateId, configurationId, OrdinaryBoardContent.CreateAuthoringCatalog());
                if (!ResolveDirty(decide, save)) return false;
                document = candidate;
                variantId = "base";
                boardName = templateId;
                savedAssignmentPath = savedAssignmentFingerprint = "";
                savedDetails = "";
                bindingSavePending = false;
                ResetView();
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        /// <summary>Reads/validates before replacing the current document; a failed import leaves its data and history intact.</summary>
        public bool TryOpen(string path, Func<BoardDocumentDecision> decide, Func<bool> save = null)
        {
            try
            {
                if (store == null) throw new InvalidOperationException("Choose an external draft folder first.");
                BoardTemplateDocument candidate = store.OpenTemplate(path, OrdinaryBoardContent.CreateAuthoringCatalog());
                if (!ResolveDirty(decide, save)) return false;
                if (candidate.FileFingerprint != store.CaptureFingerprint(path)) candidate = store.OpenTemplate(path, OrdinaryBoardContent.CreateAuthoringCatalog());
                document = candidate;
                variantId = candidate.Snapshot()["variants"][0]["id"].Value<string>();
                boardName = Path.GetFileNameWithoutExtension(path);
                assignmentPanel = new BoardAssignmentPanel();
                savedAssignmentPath = savedAssignmentFingerprint = "";
                savedDetails = CaptureDetails().ToString(Formatting.None);
                bindingSavePending = false;
                ResetView();
                return true;
            }
            catch (Exception exception) { rejectedPath = path; error = exception.Message; return false; }
        }

        /// <summary>Forks all variants into a new shared-board identity; the legacy source publication is not converted or written.</summary>
        public bool TryFork(string templateId, string configurationId, Func<BoardDocumentDecision> decide, Func<bool> save = null)
        {
            try
            {
                if (document == null) return false;
                BoardTemplateDocument candidate = document.Fork(templateId, configurationId, OrdinaryBoardContent.CreateAuthoringCatalog());
                if ((int)candidate.Snapshot()["schemaVersion"] == 1)
                {
                    JObject shared = candidate.Snapshot();
                    shared["schemaVersion"] = 2;
                    candidate = BoardTemplateDocument.FromSnapshot(shared, OrdinaryBoardContent.CreateAuthoringCatalog());
                }
                if (!ResolveDirty(decide, save)) return false;
                document = candidate;
                boardName = templateId;
                savedAssignmentPath = savedAssignmentFingerprint = "";
                savedDetails = "";
                bindingSavePending = false;
                ResetView();
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        /// <summary>Writes exactly one validated draft through the existing store; failure preserves the edited document.</summary>
        public bool TrySave(string path = null, bool overwrite = false, string expectedFingerprint = null)
        {
            if (store == null || document == null) { error = "Choose an external draft folder and document first."; return false; }
            BoardDocumentSaveResult result = store.Save(document, path, overwrite, expectedFingerprint);
            error = result.Succeeded ? "" : result.Message;
            RefreshDocument();
            return result.Succeeded;
        }

        /// <summary>Saves a named board and optional separate binding; conflicts are checked before either file is written.</summary>
        /// <param name="name">The readable filename label; stable template/configuration IDs are not renamed.</param>
        /// <param name="confirmOverwrite">Consent to replace new targets using the exact previewed fingerprints.</param>
        /// <param name="expectedBoardFingerprint">Previewed bytes for an existing board Save As target.</param>
        /// <param name="expectedAssignmentFingerprint">Previewed bytes for an existing binding not already owned by this window.</param>
        /// <returns>True only when all requested files are saved; a late binding I/O failure explicitly reports the saved board.</returns>
        public bool TrySaveNamed(string name, bool confirmOverwrite = false, string expectedBoardFingerprint = null, string expectedAssignmentFingerprint = null)
        {
            try
            {
                if (store == null || document == null) throw new InvalidOperationException("Choose an external draft folder and document first.");
                string path = store.NamedBoardPath(name);
                BoardTemplateDocument binding = null;
                string bindingPath = null;
                string bindingExpected = null;
                if (assignmentPanel.Enabled)
                {
                    var template = BoardTemplateJson.ReadCompatible(document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                    var assignment = assignmentPanel.Create(template, variantId);
                    binding = BoardTemplateDocument.ReadAssignment(assignment.ToJson(), assignmentPanel.World, template);
                    bindingPath = store.NamedAssignmentPath(name, assignment);
                    bool owned = string.Equals(bindingPath, savedAssignmentPath, StringComparison.OrdinalIgnoreCase);
                    bindingExpected = owned ? savedAssignmentFingerprint : expectedAssignmentFingerprint;
                    string actual = store.CaptureFingerprint(bindingPath);
                    if (actual != bindingExpected || actual != null && !owned && !confirmOverwrite)
                        throw new IOException("Assignment changed or requires overwrite confirmation: " + bindingPath);
                }
                var result = store.Save(document, path, confirmOverwrite, expectedBoardFingerprint);
                if (!result.Succeeded) { error = result.Message; return false; }
                boardName = name;
                bindingSavePending = binding != null;
                if (binding != null)
                {
                    var saved = store.Save(binding, bindingPath, bindingExpected != null, bindingExpected);
                    if (!saved.Succeeded)
                    { error = "Board saved, but assignment was not saved: " + saved.Message; RefreshDocument(); return false; }
                    savedAssignmentPath = binding.SourcePath;
                    savedAssignmentFingerprint = binding.FileFingerprint;
                }
                bindingSavePending = false;
                savedDetails = CaptureDetails().ToString(Formatting.None);
                error = "";
                ReloadLibrary();
                RefreshDocument();
                return true;
            }
            catch (Exception exception) { error = exception.Message; RefreshDocument(); return false; }
        }

        /// <summary>Opens a saved board with its exact variant and assignment only after validating both files and resolving unsaved edits.</summary>
        public bool TryOpenSaved(BoardSavedDraft entry, Func<BoardDocumentDecision> decide, Func<bool> save = null)
        {
            try
            {
                if (store == null || entry == null) throw new InvalidOperationException("Choose a saved board from the selected draft folder.");
                var candidate = store.OpenTemplate(entry.Path, OrdinaryBoardContent.CreateAuthoringCatalog());
                var panel = new BoardAssignmentPanel();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(assignmentPanel), panel);
                BoardTemplateDocument binding = null;
                BoardAssignment assignment = null;
                if (entry.AssignmentPath != null)
                {
                    var template = BoardTemplateJson.ReadCompatible(candidate.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                    binding = store.OpenAssignment(entry.AssignmentPath, panel.World, template);
                    assignment = BoardAssignment.Read(binding.ToJson(), panel.World, template);
                    panel.Select(assignment);
                }
                else panel.ClearTarget();
                if (!ResolveDirty(decide, save)) return false;
                if (candidate.FileFingerprint != store.CaptureFingerprint(entry.Path) || binding != null && binding.FileFingerprint != store.CaptureFingerprint(entry.AssignmentPath))
                    throw new IOException("Saved board or assignment changed during navigation; refresh the library and retry.");
                document = candidate;
                assignmentPanel = panel;
                variantId = assignment?.VariantId ?? (string)candidate.Snapshot()["variants"][0]["id"];
                boardName = entry.Name;
                savedAssignmentPath = binding?.SourcePath ?? "";
                savedAssignmentFingerprint = binding?.FileFingerprint ?? "";
                bindingSavePending = false;
                savedDetails = CaptureDetails().ToString(Formatting.None);
                ResetView();
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        /// <summary>Selects a stable variant without altering data or adding an undo record.</summary>
        public bool SelectVariant(string id)
        {
            if (document == null || BoardGridView.Variant(document.Snapshot(), id) == null) return false;
            variantId = id;
            Grid.Mode = BoardPaintMode.Select;
            properties.FocusField("");
            Repaint();
            return true;
        }

        /// <summary>Applies the current paint/erase/select gesture, then refreshes diagnostics and native dirty state.</summary>
        public bool ActivateCell(GridPosition cell, Func<string, bool> confirm = null)
        {
            bool changed = Grid.Activate(document, variantId, cell, confirm);
            if (changed) RefreshDocument();
            error = Grid.Message;
            Repaint();
            return changed;
        }

        /// <summary>Allows close/navigation only after a successful save or explicit discard, never a failed save.</summary>
        public bool ResolveDirty(Func<BoardDocumentDecision> decide, Func<bool> save = null)
        {
            if (!HasBoardChanges()) return true;
            if (decide == null) return false;
            BoardDocumentDecision decision = decide();
            return decision == BoardDocumentDecision.Discard || decision == BoardDocumentDecision.Save && save != null && save() && !HasBoardChanges();
        }

        /// <summary>Discards only this window's in-memory draft after an explicit owner decision; useful for controlled test cleanup.</summary>
        public void CloseAfterDecision(BoardDocumentDecision decision, Func<bool> save = null)
        {
            if (!ResolveAllDirty(() => decision, save)) return;
            allowClose = true;
            hasUnsavedChanges = false;
            Close();
        }

        private void OnGUI()
        {
            guiOrigin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            Event input = Event.current;
            poolAuthoring.ObservePointer(input);
            if (!BoardTestPlayLauncher.HasRecovery)
            {
            if (input.type == EventType.KeyDown && identityPanel && (input.keyCode == KeyCode.Return || input.keyCode == KeyCode.KeypadEnter || input.keyCode == KeyCode.Escape))
            {
                if (input.keyCode == KeyCode.Escape)
                {
                    identityPanel = false;
                    input.Use();
                    Repaint();
                    GUI.FocusControl(null);
                    GUIUtility.ExitGUI();
                }
                Defer(SubmitIdentity);
                input.Use();
            }
            properties.HandleKeyboard(input, document, variantId, ConfirmReferences);
            if (input.type == EventType.KeyDown && (input.control || input.command))
            {
                if (input.keyCode >= KeyCode.Alpha1 && input.keyCode <= KeyCode.Alpha3)
                {
                    string path = input.keyCode == KeyCode.Alpha2 ? "configuration" : input.keyCode == KeyCode.Alpha3 ? "outcomes" : "";
                    Defer(() => { properties.FocusField(path); Repaint(); });
                    input.Use();
                }
                bool saveAs = input.shift;
                Action command = input.keyCode == KeyCode.S ? (poolAuthoring.KeyboardFocus ? (Action)(() => SavePoolDialog(saveAs)) : () => SaveFromDialog(saveAs)) :
                    input.keyCode == KeyCode.O ? OpenFromDialog : input.keyCode == KeyCode.N ? (Action)(() => ShowIdentity(false)) :
                    input.keyCode == KeyCode.D ? () => ShowIdentity(true) : null;
                if (command != null)
                {
                    input.Use();
                    Defer(command);
                }
            }
            if ((document != null || poolAuthoring.Document != null) && input.type == EventType.KeyDown && (input.control || input.command) && !EditorGUIUtility.editingTextField)
            {
                bool undo = input.keyCode == KeyCode.Z && !input.shift;
                bool redo = input.keyCode == KeyCode.Y || input.keyCode == KeyCode.Z && input.shift;
                if (undo || redo)
                {
                    input.Use();
                    BoardTemplateDocument owner = poolAuthoring.KeyboardFocus ? poolAuthoring.Document : document;
                    Defer(() => { if (undo) owner?.Undo(); else owner?.Redo(); });
                }
            }
            }
            EditorGUI.BeginChangeCheck();
            DrawToolbar();
            if (EditorGUI.EndChangeCheck()) RefreshDocument();
            using (new EditorGUI.DisabledScope(BoardTestPlayLauncher.HasRecovery))
                if (identityPanel) DrawIdentity();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (!string.IsNullOrEmpty(rejectedPath))
            {
                EditorGUILayout.LabelField("Import Diagnostic", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(rejectedPath, EditorStyles.wordWrappedLabel, GUILayout.Height(36));
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (BoardTestPlayLauncher.HasRecovery && !string.IsNullOrEmpty(BoardTestPlayLauncher.LastMessage))
                EditorGUILayout.HelpBox(BoardTestPlayLauncher.LastMessage, MessageType.Warning);
            using (new EditorGUI.DisabledScope(BoardTestPlayLauncher.HasRecovery))
                poolAuthoring.Draw(store, assignmentPanel.WorldJson, guiOrigin, Defer, ResolvePoolNavigation,
                    () => SavePoolDialog(), () => SavePoolDialog(true), OpenPoolDialog, LaunchPoolRouteTest);
            if (document != null)
            {
                using (new EditorGUI.DisabledScope(BoardTestPlayLauncher.HasRecovery))
                {
                bool wide = position.width >= 1060;
                float canvasSide = Mathf.Clamp(position.width - (wide ? 570 : 64), 280, 560);
                float workspaceWidth = wide ? 200 + canvasSide + 24 + 310 + 8 : Mathf.Min(position.width - 24, 760);
                JObject data = document.Snapshot();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginVertical(GUILayout.Width(workspaceWidth));
                if (wide) EditorGUILayout.BeginHorizontal();
                if (wide) EditorGUILayout.BeginVertical(GUILayout.Width(200));
                DrawPalette(wide ? 1 : Math.Max(2, (int)workspaceWidth / 190), wide ? canvasSide + 24 : 180);
                if (wide) { EditorGUILayout.EndVertical(); EditorGUILayout.BeginVertical(GUILayout.Width(canvasSide + 24)); }
                DrawCanvas(data, canvasSide);
                if (wide) { EditorGUILayout.EndVertical(); EditorGUILayout.BeginVertical(GUILayout.Width(310)); }
                if (properties.Draw(document, variantId, Grid, ConfirmReferences, RefreshDocument, canvasSide + 24)) RefreshDocument();
                if (wide) { EditorGUILayout.EndVertical(); EditorGUILayout.EndHorizontal(); }
                DrawDiagnostics();
                EditorGUILayout.EndVertical();
                if (Event.current.type == EventType.Repaint)
                {
                    Rect workspace = GUILayoutUtility.GetLastRect();
                    workspaceRect = new Rect(GUIUtility.GUIToScreenPoint(workspace.position) - guiOrigin, workspace.size);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
                workspaceViewportRect = GUILayoutUtility.GetLastRect();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(BoardTestPlayLauncher.HasRecovery))
                {
                Icon("Toolbar Plus", "New document", () => ShowIdentity(false));
                Icon("FolderOpened Icon", "Open template", OpenFromDialog);
                using (new EditorGUI.DisabledScope(document == null || document.IsReadOnly || diagnostics.Count > 0))
                {
                    Icon("SaveActive", "Save draft", () => SaveFromDialog());
                    Icon("SaveAs", "Save draft as", () => SaveFromDialog(true));
                }
                using (new EditorGUI.DisabledScope(document == null))
                    Icon("TreeEditor.Duplicate", "Fork new identity", () => ShowIdentity(true));
                using (new EditorGUI.DisabledScope(document == null || !document.CanUndo)) Icon("back", "Undo", () => { document.Undo(); RefreshDocument(); });
                using (new EditorGUI.DisabledScope(document == null || !document.CanRedo)) Icon("forward", "Redo", () => { document.Redo(); RefreshDocument(); });
                Icon("Refresh", "Validate data in all variants", RefreshDocument);
                Icon("d_TerrainInspector.TerrainToolSplat", "Generate board using current seed and day preset", GenerateBoard);
                GUILayout.FlexibleSpace();
                if (document != null)
                {
                    string[] variants = document.Snapshot()["variants"].Values<JObject>().Select(row => (string)row["id"]).ToArray();
                    int selected = Math.Max(0, Array.IndexOf(variants, variantId));
                    int chosen = EditorGUILayout.Popup(selected, variants, EditorStyles.toolbarPopup, GUILayout.Width(145));
                    if (chosen != selected) Defer(() => SelectVariant(variants[chosen]));
                }
                }
                using (new EditorGUI.DisabledScope(!BoardTestPlayLauncher.HasRecovery))
                    Icon("PreMatQuad", "Stop local test", BoardTestPlayLauncher.Stop, true);
            }
            using (new EditorGUI.DisabledScope(BoardTestPlayLauncher.HasRecovery))
            {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                Icon("Folder Icon", "Choose external draft folder", () => EnsureStore(true));
                GUILayout.Label(new GUIContent(string.IsNullOrEmpty(draftRoot) ? "Draft Folder" : draftRoot, draftRoot), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(document == null ? "No Document" : (document.IsReadOnly ? "Read-only" : document.IsDirty ? "Modified" : "Saved"), EditorStyles.miniBoldLabel);
            }
            testSettings.Seed = EditorGUILayout.IntField("Test Seed", testSettings.Seed);
            boardName = EditorGUILayout.TextField("Board Name", boardName);
            if (document != null)
            {
                JObject identity = document.Snapshot();
                EditorGUILayout.LabelField("Template / Version", (string)identity["templateId"] + " / " + (int)identity["templateVersion"], EditorStyles.miniLabel);
            }
            generationDay = EditorGUILayout.IntSlider("Generation Day", generationDay, 1, 30);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Choose a fresh generation seed"), GUILayout.Width(27)))
                    Defer(() => testSettings.Seed = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0));
            }
            testSettings.Health = EditorGUILayout.IntField("Starting Health", testSettings.Health);
            testSettings.Food = EditorGUILayout.IntField("Starting Food", testSettings.Food);
            testSettings.EquipAxe = EditorGUILayout.Toggle("Equip Axe", testSettings.EquipAxe);
            if (document != null) DrawBoardCheck();
            if (document != null) assignmentPanel.Draw(Defer, SaveAssignment, OpenAssignment);
            bool showLibrary = EditorGUILayout.Foldout(libraryVisible, "Saved Boards", true);
            if (showLibrary && !libraryVisible) Defer(ReloadLibrary);
            libraryVisible = showLibrary;
            if (libraryVisible) DrawLibrary();
            if (document != null)
            {
                string[] outcomes = document.Snapshot()["outcomes"].Values<JObject>().Select(outcome => (string)outcome["id"]).ToArray();
                if (outcomes.Length > 0)
                {
                    int selected = Math.Max(0, Array.IndexOf(outcomes, solverOutcome));
                    solverOutcome = outcomes[EditorGUILayout.Popup("Solver Outcome", selected, outcomes)];
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                    {
                        using (new EditorGUI.DisabledScope(diagnostics.Count > 0))
                            Icon("d_Search Icon", "Solve selected outcome (50000 states / 32 commands / 10 seconds)", SolveSelectedOutcome);
                        GUILayout.Label(solverMessage, EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            }
        }

        /// <summary>Checks the selected variant with current resources without entering Play Mode, changing the draft or writing saves.</summary>
        /// <returns>A verified exit, an inconclusive search limit, or a controlled invalid/unsolved result.</returns>
        public BoardSolverReport CheckBoard()
        {
            RefreshDocument();
            boardCheckInputs = CaptureBoardCheckInputs();
            try
            {
                boardCheck = document == null || diagnostics.Count > 0
                    ? new BoardSolverReport("Invalid", document == null ? "No board document is open." : "Board data contains " + diagnostics.Count + " validation errors.")
                    : BoardSolverAdapter.CheckPlayable(document.ToJson(), variantId, testSettings);
            }
            catch (Exception exception) { boardCheck = new BoardSolverReport("Invalid", exception.Message); }
            Repaint();
            return boardCheck;
        }

        private string CaptureBoardCheckInputs() => new JObject
        {
            ["document"] = document?.Snapshot(), ["variant"] = variantId,
            ["seed"] = testSettings.Seed, ["health"] = testSettings.Health,
            ["food"] = testSettings.Food, ["axe"] = testSettings.EquipAxe
        }.ToString(Formatting.None);

        private void DrawBoardCheck()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Check board", EditorGUIUtility.IconContent("d_Search Icon").image,
                    "Check data and verify at least one exit for the selected variant and starting inputs"), GUILayout.Width(120), GUILayout.Height(24)))
                    Defer(() => CheckBoard());
                if (Event.current.type == EventType.Repaint) checkBoardRect = GUILayoutUtility.GetLastRect();
                using (new EditorGUI.DisabledScope(diagnostics.Count > 0))
                {
                    if (GUILayout.Button(new GUIContent("Play selected variant", EditorGUIUtility.IconContent("PlayButton").image,
                        "Play the selected variant in isolation, retaining the launcher's data and playability guards"), GUILayout.Width(180), GUILayout.Height(24)))
                        Defer(LaunchTest);
                    if (Event.current.type == EventType.Repaint) playVariantRect = GUILayoutUtility.GetLastRect();
                }
                using (new EditorGUI.DisabledScope(!CanPlayRoute))
                {
                    if (GUILayout.Button(new GUIContent("Play route", EditorGUIUtility.IconContent("PlayButton").image,
                        "Preview the saved assignments and play the selected route from its first segment"), GUILayout.Width(120), GUILayout.Height(24)))
                        Defer(LaunchRouteTest);
                    if (Event.current.type == EventType.Repaint) playRouteRect = GUILayoutUtility.GetLastRect();
                }
            }
            BoardSolverReport report = BoardCheck;
            if (report == null)
                EditorGUILayout.LabelField("Board check", boardCheck == null ? "Not checked" : "Out of date");
            else
            {
                bool solved = report.Status == "Solved" && report.Replayed;
                bool incomplete = report.Status == "TimeLimit" || report.Status == "StateLimit" || report.Status == "DepthLimit";
                string summary = solved ? "Playable: at least one exit verified." : incomplete ? "Inconclusive: " + report.Status + "." : "Not verified: " + report.Status + ".";
                EditorGUILayout.HelpBox(summary + "\n" + report.Message, solved ? MessageType.Info : incomplete ? MessageType.Warning : MessageType.Error);
            }
        }

        private void SolveSelectedOutcome()
        {
            try
            {
                BoardSolverReport result = BoardSolverAdapter.Solve(document.ToJson(), variantId, solverOutcome, testSettings);
                solverMessage = result.Status + ": " + result.Message;
            }
            catch (Exception exception) { solverMessage = "Invalid: " + exception.Message; }
        }

        /// <summary>Creates a production snapshot only after explicit dirty-document consent; failure preserves the previous draft and its history.</summary>
        public bool TryGenerate(int seed, int day, string templateId, string configurationId, Func<BoardDocumentDecision> decide, Func<bool> save = null)
        {
            try
            {
                var candidate = BoardGenerationAdapter.Generate(seed, day, templateId, configurationId);
                if (!ResolveDirty(decide, save)) return false;
                document = candidate;
                testSettings.Seed = seed;
                boardName = "Board " + seed + " Day " + day;
                savedAssignmentPath = savedAssignmentFingerprint = "";
                savedDetails = "";
                bindingSavePending = false;
                generationDay = day;
                variantId = "base";
                ResetView();
                return true;
            }
            catch (Exception exception) { error = "Generation failed: " + exception.Message; return false; }
        }

        private void GenerateBoard()
        {
            string identity = "draft.generated." + Guid.NewGuid().ToString("N");
            TryGenerate(testSettings.Seed, generationDay, identity, identity + ".config", AskDecision, SaveFromDialog);
        }

        private void SaveAssignment()
        {
            if (assignmentPanel.Enabled) SaveFromDialog();
        }

        private void OpenAssignment()
        {
            try
            {
                if (document == null || !EnsureStore()) return;
                string path = EditorUtility.OpenFilePanel("Open Board Assignment", draftRoot, "json");
                if (string.IsNullOrEmpty(path)) return;
                var template = BoardTemplateJson.ReadCompatible(document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                var loaded = store.OpenAssignment(path, assignmentPanel.World, template);
                var assignment = BoardAssignment.Read(loaded.ToJson(), assignmentPanel.World, template);
                assignmentPanel.Select(assignment);
                SelectVariant(assignment.VariantId);
                savedAssignmentPath = loaded.SourcePath;
                savedAssignmentFingerprint = loaded.FileFingerprint;
                savedDetails = CaptureDetails().ToString(Formatting.None);
            }
            catch (Exception exception) { error = "Assignment open failed: " + exception.Message; }
        }

        private void LaunchTest()
        {
            BoardTestPlayLauncher.TryLaunch(this, testSettings,
                () => EditorUtility.DisplayDialog("Play Mode", "Stop the current Play Mode session?", "Stop", "Cancel"),
                UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo);
            error = BoardTestPlayLauncher.LastMessage;
        }

        private bool CanPlayRoute => store != null && document != null && diagnostics.Count == 0 && !HasBoardChanges() &&
            !string.IsNullOrEmpty(document.SourcePath) && assignmentPanel.Enabled && !string.IsNullOrEmpty(savedAssignmentPath);

        private void LaunchRouteTest()
        {
            BoardTestPlayLauncher.TryLaunchRoute(this, testSettings,
                preview => EditorUtility.DisplayDialog("Play route", preview, "Start", "Cancel"),
                () => EditorUtility.DisplayDialog("Play Mode", "Stop the current Play Mode session?", "Stop", "Cancel"),
                UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo);
            error = BoardTestPlayLauncher.LastMessage;
        }

        internal BoardRouteTestSnapshot CaptureRouteTest()
        {
            if (!CanPlayRoute)
                throw new InvalidOperationException("Save the board and its selected route assignment before playing the route.");
            if (document.FileFingerprint != store.CaptureFingerprint(document.SourcePath) ||
                savedAssignmentFingerprint != store.CaptureFingerprint(savedAssignmentPath))
                throw new InvalidOperationException("The saved board or assignment changed outside the Editor; reopen it before playing the route.");
            var template = BoardTemplateJson.ReadCompatible(document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
            return BoardRouteTestSnapshot.Capture(store, draftRoot, assignmentPanel.WorldJson,
                assignmentPanel.Create(template, variantId), document.SourcePath);
        }

        internal BoardRouteTestSnapshot CapturePoolRouteTest() => BoardRouteTestSnapshot.CapturePool(poolAuthoring, store);

        private void LaunchPoolRouteTest()
        {
            BoardTestPlayLauncher.TryLaunchPoolRoute(this, testSettings,
                preview => EditorUtility.DisplayDialog("Play route sources", preview, "Start", "Cancel"),
                () => false, UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo);
            error = BoardTestPlayLauncher.LastMessage;
        }

        private bool ResolvePoolNavigation()
        {
            if (poolAuthoring.Document == null || !poolAuthoring.Document.IsDirty) return true;
            int choice = EditorUtility.DisplayDialogComplex("Unsaved Route Sources", "Save route sources before replacing them?", "Save", "Cancel", "Discard");
            return choice == 2 || choice == 0 && SavePoolDialog();
        }

        private void OpenPoolDialog()
        {
            if (!EnsureStore()) return;
            string path = EditorUtility.OpenFilePanel("Open Route Sources", draftRoot, "json");
            if (!string.IsNullOrEmpty(path) && ResolvePoolNavigation()) poolAuthoring.Open(store, path, assignmentPanel.WorldJson);
        }

        /// <summary>Saves the independent pool draft to its current or explicit Save As path; conflicts and failures preserve membership and history.</summary>
        /// <param name="path">Null saves the owned file; a different explicit path performs Save As within the selected guarded root.</param>
        /// <param name="confirmOverwrite">Explicit consent for replacing an existing different target.</param>
        /// <param name="fingerprint">The previewed exact target bytes required with overwrite consent.</param>
        public bool TrySavePool(string path = null, bool confirmOverwrite = false, string fingerprint = null)
        {
            try
            {
                if (store == null) throw new InvalidOperationException("Choose an external draft folder first.");
                var result = poolAuthoring.Save(store, path, confirmOverwrite, fingerprint);
                error = result.Message;
                RefreshDocument();
                return result.Succeeded;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private bool SavePoolDialog(bool saveAs = false)
        {
            if (!EnsureStore() || poolAuthoring.Document == null) return false;
            string path = poolAuthoring.Document.SourcePath;
            bool overwrite = false;
            string fingerprint = null;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel("Save Route Sources", draftRoot, "route.pool", "json");
                if (string.IsNullOrEmpty(path)) return false;
                fingerprint = store.CaptureFingerprint(path);
                overwrite = fingerprint != null;
                if (overwrite && !EditorUtility.DisplayDialog("Replace Route Sources", path, "Replace", "Cancel")) return false;
            }
            return TrySavePool(path, overwrite, fingerprint);
        }

        private bool SaveAllDrafts() => (!HasBoardChanges() || SaveFromDialog()) &&
            (poolAuthoring.Document == null || !poolAuthoring.Document.IsDirty || SavePoolDialog());

        private bool ResolveAllDirty(Func<BoardDocumentDecision> decide, Func<bool> save)
        {
            if (!HasPendingChanges()) return true;
            if (decide == null) return false;
            BoardDocumentDecision decision = decide();
            return decision == BoardDocumentDecision.Discard || decision == BoardDocumentDecision.Save && save != null && save() && !HasPendingChanges();
        }

        internal string CaptureTestRecovery()
        {
            Remember();
            return new JObject
            {
                ["id"] = recoveryId, ["draft"] = document == null ? null : BoardDraftRecovery.Capture(document, variantId, Grid, draftRoot),
                ["root"] = draftRoot,
                ["seed"] = testSettings.Seed, ["health"] = testSettings.Health, ["food"] = testSettings.Food,
                ["axe"] = testSettings.EquipAxe, ["assignment"] = JsonUtility.ToJson(assignmentPanel), ["generationDay"] = generationDay,
                ["boardName"] = boardName, ["assignmentPath"] = savedAssignmentPath, ["assignmentFingerprint"] = savedAssignmentFingerprint,
                ["savedDetails"] = savedDetails, ["bindingPending"] = bindingSavePending,
                ["pools"] = poolAuthoring.Capture()
            }.ToString(Formatting.None);
        }

        internal static BoardAuthoringWindow RestoreTestRecovery(string recovery)
        {
            JObject state = JObject.Parse(recovery);
            var window = Resources.FindObjectsOfTypeAll<BoardAuthoringWindow>().FirstOrDefault(candidate => candidate.recoveryId == (string)state["id"])
                ?? CreateInstance<BoardAuthoringWindow>();
            window.recoveryId = (string)state["id"];
            string root = (string)state["root"];
            window.document = state["draft"]?.Type == JTokenType.String
                ? BoardDraftRecovery.Restore((string)state["draft"], window.Grid, out window.variantId, out root) : null;
            window.store = null;
            window.draftRoot = "";
            if (!string.IsNullOrEmpty(root))
            {
                if (!Directory.Exists(root)) window.error = "Draft folder unavailable: " + root;
                else if (!window.TryConfigureRoot(root)) window.error = "Draft folder unavailable: " + window.LastError;
            }
            window.testSettings = new BoardTestPlaySettings
            { Seed = (int)state["seed"], Health = (int)state["health"], Food = (int)state["food"], EquipAxe = (bool)state["axe"] };
            window.assignmentPanel = state["assignment"] == null ? new BoardAssignmentPanel() : JsonUtility.FromJson<BoardAssignmentPanel>((string)state["assignment"]);
            window.generationDay = (int?)state["generationDay"] ?? 1;
            window.boardName = (string)state["boardName"] ?? "";
            window.savedAssignmentPath = (string)state["assignmentPath"] ?? "";
            window.savedAssignmentFingerprint = (string)state["assignmentFingerprint"] ?? "";
            window.savedDetails = (string)state["savedDetails"] ?? "";
            window.bindingSavePending = (bool?)state["bindingPending"] ?? false;
            window.poolAuthoring = BoardPoolAuthoring.Restore((string)state["pools"]);
            window.RefreshDocument();
            window.Show();
            return window;
        }

        private void DrawIdentity()
        {
            GUI.SetNextControlName("TemplateIdentity");
            newTemplateId = EditorGUILayout.TextField("Template ID", newTemplateId);
            GUI.SetNextControlName("ConfigurationIdentity");
            newConfigurationId = EditorGUILayout.TextField("Configuration ID", newConfigurationId);
            if (focusIdentity) { GUI.FocusControl("TemplateIdentity"); focusIdentity = false; }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(forkRequested ? "Fork" : "Create"))
                {
                    Defer(SubmitIdentity);
                }
                if (GUILayout.Button("Cancel")) Defer(() => identityPanel = false);
            }
        }

        private void ShowIdentity(bool fork)
        {
            identityPanel = true;
            forkRequested = fork;
            focusIdentity = true;
            Repaint();
        }

        private void SubmitIdentity()
        {
            bool accepted = forkRequested ? TryFork(newTemplateId, newConfigurationId, AskDecision, SaveFromDialog) :
                TryNew(newTemplateId, newConfigurationId, AskDecision, SaveFromDialog);
            if (accepted) identityPanel = false;
            Repaint();
        }

        private void DrawPalette(int columns, float height)
        {
            EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);
            paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll, false, true, GUILayout.Height(height));
            for (int offset = 0; offset < BoardGridView.Palette.Count; offset += columns)
                using (new EditorGUILayout.HorizontalScope())
                    for (int index = offset; index < Math.Min(offset + columns, BoardGridView.Palette.Count); index++)
                    {
                        string id = BoardGridView.Palette[index];
                        string label = BoardGridView.ContentName(id);
                        if (GUILayout.Button(new GUIContent("", id),
                            Grid.ContentId == id && Grid.Mode == BoardPaintMode.Paint ? EditorStyles.miniButtonMid : EditorStyles.miniButton,
                            GUILayout.Height(32), GUILayout.MinWidth(110))) Defer(() => Grid.ChooseContent(id));
                        Rect button = GUILayoutUtility.GetLastRect();
                        if (Event.current.type == EventType.Repaint && index == BoardGridView.Palette.Count - 1)
                            paletteLastButtonRect = new Rect(GUIUtility.GUIToScreenPoint(button.position) - guiOrigin, button.size);
                        BoardGridView.DrawSprite(new Rect(button.x + 2, button.y + 4, 24, 24), BoardGridView.Preview(id));
                        GUI.Label(new Rect(button.x + 32, button.y + 7, button.width - 36, 20), label, EditorStyles.miniLabel);
                    }
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
            {
                Rect viewport = GUILayoutUtility.GetLastRect();
                paletteViewportRect = new Rect(GUIUtility.GUIToScreenPoint(viewport.position) - guiOrigin, viewport.size);
            }
        }

        private void DrawCanvas(JObject data, float side)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Grid.Mode = (BoardPaintMode)GUILayout.Toolbar((int)Grid.Mode, new[]
                {
                    EditorGUIUtility.IconContent("d_RectTool", "Select cells"), EditorGUIUtility.IconContent("Grid.PaintTool", "Paint selected content"),
                    EditorGUIUtility.IconContent("Grid.EraserTool", "Erase active layer")
                }, GUILayout.Width(130));
                var chosenLayer = (BoardLayer)EditorGUILayout.EnumPopup(Grid.Layer, GUILayout.Width(100));
                if (chosenLayer != Grid.Layer) Defer(() => Grid.Layer = chosenLayer);
                GUILayout.FlexibleSpace();
                GUILayout.Label(Grid.SelectedCell.X + "." + Grid.SelectedCell.Y, EditorStyles.miniBoldLabel);
            }
            Rect area = GUILayoutUtility.GetRect(side + 24, side + 24, GUILayout.ExpandWidth(true));
            Rect canvas = new Rect(area.x + 24 + Mathf.Max(0, (area.width - side - 24) / 2), area.y, side, side);
            CanvasRect = new Rect(GUIUtility.GUIToScreenPoint(canvas.position) - guiOrigin, canvas.size);
            bool changed = Grid.Draw(canvas, document, variantId, ConfirmReferences, Defer, () => { error = Grid.Message; RefreshDocument(); });
            if (changed) RefreshDocument();
        }

        private void DrawDiagnostics()
        {
            EditorGUILayout.LabelField("Validation | " + diagnostics.Count + " error(s)", EditorStyles.boldLabel);
            diagnosticsScroll = EditorGUILayout.BeginScrollView(diagnosticsScroll, GUILayout.Height(diagnostics.Count == 0 ? 24 : 170));
            foreach (BoardAuthoringDiagnostic diagnostic in diagnostics)
            {
                string cell = diagnostic.Position.HasValue ? " | " + diagnostic.Position.Value.X + "." + diagnostic.Position.Value.Y : "";
                if (GUILayout.Button(new GUIContent(diagnostic.Code + cell + " | " + diagnostic.Message, diagnostic.FieldPath), EditorStyles.wordWrappedMiniLabel))
                {
                    Defer(() => {
                    if (diagnostic.VariantId != null) SelectVariant(diagnostic.VariantId);
                    if (diagnostic.Position.HasValue) Grid.Select(diagnostic.Position.Value);
                    if (diagnostic.EntityId.HasValue)
                    {
                        JObject variant = BoardGridView.Variant(document.Snapshot(), variantId);
                        JObject entity = variant?["entities"].OfType<JObject>().FirstOrDefault(row => (int)row["id"] == diagnostic.EntityId.Value);
                        if (entity != null) Grid.Layer = (BoardLayer)Enum.Parse(typeof(BoardLayer), (string)entity["layer"]);
                    }
                    properties.FocusField(diagnostic.FieldPath);
                    });
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool EnsureStore(bool choose = false)
        {
            if (store != null && !choose) return true;
            string selected = EditorUtility.OpenFolderPanel("External Draft Folder", draftRoot, "");
            return !string.IsNullOrEmpty(selected) && TryConfigureRoot(selected);
        }

        private void OpenFromDialog()
        {
            if (!EnsureStore()) return;
            string path = EditorUtility.OpenFilePanel("Open Board Template", draftRoot, "json");
            if (!string.IsNullOrEmpty(path)) TryOpen(path, AskDecision, SaveFromDialog);
        }

        private bool SaveFromDialog() => SaveFromDialog(false);

        private bool SaveFromDialog(bool saveAs)
        {
            RefreshDocument();
            if (document == null || document.IsReadOnly || diagnostics.Count > 0) return false;
            if (!EnsureStore()) return false;
            try
            {
                if (string.IsNullOrWhiteSpace(boardName)) boardName = (string)document.Snapshot()["templateId"];
                string path = store.NamedBoardPath(boardName);
                if (saveAs)
                {
                    string chosen = EditorUtility.SaveFilePanel("Save Named Board", draftRoot, boardName, "json");
                    if (string.IsNullOrEmpty(chosen)) return false;
                    if (!string.Equals(Path.GetDirectoryName(chosen), draftRoot, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Named boards must use the selected draft folder.");
                    boardName = Path.GetFileNameWithoutExtension(chosen);
                    path = store.NamedBoardPath(boardName);
                }
                string fingerprint = store.CaptureFingerprint(path);
                string bindingFingerprint = null;
                string bindingPath = null;
                if (assignmentPanel.Enabled)
                {
                    var template = BoardTemplateJson.ReadCompatible(document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                    bindingPath = store.NamedAssignmentPath(boardName, assignmentPanel.Create(template, variantId));
                    bindingFingerprint = store.CaptureFingerprint(bindingPath);
                }
                bool replaceBoard = fingerprint != null && !string.Equals(path, document.SourcePath, StringComparison.OrdinalIgnoreCase);
                bool replaceBinding = bindingFingerprint != null && !string.Equals(bindingPath, savedAssignmentPath, StringComparison.OrdinalIgnoreCase);
                if ((replaceBoard || replaceBinding) && !EditorUtility.DisplayDialog("Replace Named Board Files",
                    string.Join("\n", new[] { replaceBoard ? path : null, replaceBinding ? bindingPath : null }.Where(value => value != null)), "Replace", "Cancel")) return false;
                return TrySaveNamed(boardName, replaceBoard || replaceBinding, fingerprint, bindingFingerprint);
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private void ReloadLibrary()
        {
            if (store == null) { savedBoards = Array.Empty<BoardSavedDraft>(); libraryDiagnostics = Array.Empty<string>(); return; }
            try { savedBoards = store.ListBoards(assignmentPanel.World, OrdinaryBoardContent.CreateAuthoringCatalog(), out libraryDiagnostics); }
            catch (Exception exception) { error = exception.Message; }
        }

        private void DrawLibrary()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Refresh saved boards"), GUILayout.Width(27))) Defer(ReloadLibrary);
                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Folder Icon").image, "Choose board library folder"), GUILayout.Width(27)))
                    Defer(() => { if (EnsureStore(true)) ReloadLibrary(); });
                GUILayout.Label(savedBoards.Count + " saved board target(s)", EditorStyles.miniLabel);
            }
            libraryScroll = EditorGUILayout.BeginScrollView(libraryScroll, GUILayout.Height(140));
            foreach (var entry in savedBoards)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("FolderOpened Icon").image, "Open " + entry.Name), GUILayout.Width(27)))
                        Defer(() => TryOpenSaved(entry, AskDecision, SaveFromDialog));
                    string target = entry.Assignment == null ? "Unassigned" : entry.Assignment.EdgeId + " | " + entry.Assignment.SegmentId + " | " + entry.Assignment.VariantId;
                    GUILayout.Label(new GUIContent(entry.Name + "\n" + target, entry.TemplateId + "/" + entry.TemplateVersion + "\n" + entry.Path), EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(34));
                }
            }
            foreach (string diagnostic in libraryDiagnostics) EditorGUILayout.HelpBox(diagnostic, MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        private BoardDocumentDecision AskDecision()
        {
            if (!HasPendingChanges()) return BoardDocumentDecision.Discard;
            if (diagnostics.Count > 0)
                return EditorUtility.DisplayDialog("Unsaved Invalid Draft", "Discard this document's unsaved changes?", "Discard", "Cancel") ? BoardDocumentDecision.Discard : BoardDocumentDecision.Cancel;
            int decision = EditorUtility.DisplayDialogComplex("Unsaved Draft", "Save this document before leaving?", "Save", "Cancel", "Discard");
            return decision == 0 ? BoardDocumentDecision.Save : decision == 2 ? BoardDocumentDecision.Discard : BoardDocumentDecision.Cancel;
        }

        private static bool ConfirmReferences(string paths) => EditorUtility.DisplayDialog("Dependent Data", paths, "Apply With These References", "Cancel");

        private void Icon(string name, string tooltip, Action action, bool duringTest = false)
        {
            GUIContent icon = EditorGUIUtility.IconContent(name);
            if (GUILayout.Button(new GUIContent(icon.image, tooltip), EditorStyles.toolbarButton, GUILayout.Width(27), GUILayout.Height(22)))
            DeferCore(action, duringTest);
        }

        private void Defer(Action action) => DeferCore(action, false);

        private void DeferCore(Action action, bool duringTest)
        {
            BoardTemplateDocument expectedDocument = document;
            BoardTemplateDocument expectedPool = poolAuthoring.Document;
            int expectedEpoch = callbackEpoch;
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                pendingCallbacks.Remove(callback);
                if (this == null || !callbacksActive || expectedEpoch != callbackEpoch || !ReferenceEquals(expectedDocument, document)) return;
                if (!ReferenceEquals(expectedPool, poolAuthoring.Document)) return;
                if (BoardTestPlayLauncher.HasRecovery && !duringTest) return;
                try { action(); }
                catch (Exception exception) { error = exception.Message; }
                if (this != null && callbacksActive && expectedEpoch == callbackEpoch) RefreshDocument();
            };
            pendingCallbacks.Add(callback);
            EditorApplication.update += callback;
            GUI.FocusControl(null);
        }

        private void ResetView()
        {
            Grid.Mode = BoardPaintMode.Select;
            Grid.Select(new GridPosition(0, 0));
            error = "";
            rejectedPath = "";
            properties.FocusField("");
            RefreshDocument();
        }

        private void RefreshDocument()
        {
            if (this == null || !callbacksActive) return;
            diagnostics = document?.Diagnostics ?? Array.Empty<BoardAuthoringDiagnostic>();
            hasUnsavedChanges = HasPendingChanges();
            if (hasUnsavedChanges) allowClose = false;
            saveChangesMessage = "Save the modified board draft?";
            Remember();
            Repaint();
        }

        private void Remember()
        {
            editorState = document?.CaptureEditorState().ToString(Formatting.None) ?? "";
            poolAuthoring.Remember();
        }

        private JObject CaptureDetails() => new JObject
        {
            ["name"] = boardName, ["assigned"] = assignmentPanel.Enabled,
            ["world"] = assignmentPanel.Enabled ? assignmentPanel.World.WorldDefinitionId : "",
            ["worldVersion"] = assignmentPanel.Enabled ? assignmentPanel.World.Version : 0,
            ["segment"] = assignmentPanel.Enabled ? assignmentPanel.SegmentId : "",
            ["variant"] = assignmentPanel.Enabled ? variantId : ""
        };

        private bool HasPendingChanges() => HasBoardChanges() || poolAuthoring.Document?.IsDirty == true;

        private bool HasBoardChanges() => document != null && !document.IsReadOnly && (document.IsDirty || bindingSavePending ||
            !string.IsNullOrEmpty(savedDetails) && savedDetails != CaptureDetails().ToString(Formatting.None));
    }
}