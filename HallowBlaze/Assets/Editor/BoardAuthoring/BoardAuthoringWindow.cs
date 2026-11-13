using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Edits schema-1 local board drafts without loading scenes, changing a campaign or starting gameplay.</summary>
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
        private BoardTemplateDocument document;
        private BoardDocumentStore store;
        private BoardGridView grid;
        private BoardPropertyPanel properties;
        private IReadOnlyList<BoardAuthoringDiagnostic> diagnostics = Array.Empty<BoardAuthoringDiagnostic>();
        private string error = "";
        private string rejectedPath = "";
        private Vector2 scroll;
        private Vector2 diagnosticsScroll;
        private Vector2 guiOrigin;
        private bool allowClose;
        private bool quitting;
        private bool callbacksActive;
        private int callbackEpoch;
        private readonly HashSet<EditorApplication.CallbackFunction> pendingCallbacks = new HashSet<EditorApplication.CallbackFunction>();

        /// <summary>Gets the current document; assignment editing and publication are outside this window's stage.</summary>
        public BoardTemplateDocument Document => document;
        /// <summary>Gets the active canvas and its read-only selection/gesture state.</summary>
        public BoardGridView Grid => grid ?? (grid = new BoardGridView());
        /// <summary>Gets the selected stable variant ID, never its array index.</summary>
        public string SelectedVariant => variantId;
        /// <summary>Gets the last operation error without discarding the currently edited document.</summary>
        public string LastError => error;
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
                try { document = BoardTemplateDocument.RestoreEditorState(JObject.Parse(editorState), RadioTowerLandmark.CreateCatalog()); }
                catch (Exception exception) { error = "Draft recovery failed: " + exception.Message; }
            }
            RefreshDocument();
            EditorApplication.wantsToQuit += WantsToQuit;
            EditorApplication.quitting += OnQuitting;
        }

        private void OnDisable()
        {
            callbacksActive = false;
            callbackEpoch++;
            foreach (EditorApplication.CallbackFunction callback in pendingCallbacks) EditorApplication.delayCall -= callback;
            pendingCallbacks.Clear();
            Remember();
            EditorApplication.wantsToQuit -= WantsToQuit;
            EditorApplication.quitting -= OnQuitting;
        }

        private void OnDestroy()
        {
            if (allowClose || quitting || document == null || !document.IsDirty) return;
            Remember();
            SessionState.SetString(PendingRecovery, new JObject
            {
                ["id"] = recoveryId, ["state"] = editorState, ["root"] = draftRoot, ["variant"] = variantId
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
            recovered.document = BoardTemplateDocument.RestoreEditorState(JObject.Parse((string)state["state"]), RadioTowerLandmark.CreateCatalog());
            recovered.variantId = (string)state["variant"];
            recovered.error = "Close did not save or discard the draft. The document was retained.";
            recovered.RefreshDocument();
            recovered.Show();
            SessionState.EraseString(PendingRecovery);
        }

        private bool WantsToQuit() => ResolveDirty(AskDecision, SaveFromDialog);
        private void OnQuitting() => quitting = true;

        /// <summary>Resolves the native Editor dirty-window save action; failed saves keep the draft dirty and recoverable.</summary>
        public override void SaveChanges()
        {
            if (SaveFromDialog()) { allowClose = true; base.SaveChanges(); }
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
                BoardTemplateDocument candidate = BoardTemplateDocument.NewTemplate(templateId, configurationId, RadioTowerLandmark.CreateCatalog());
                if (!ResolveDirty(decide, save)) return false;
                document = candidate;
                variantId = "base";
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
                BoardTemplateDocument candidate = store.OpenTemplate(path, RadioTowerLandmark.CreateCatalog());
                if (!ResolveDirty(decide, save)) return false;
                if (candidate.FileFingerprint != store.CaptureFingerprint(path)) candidate = store.OpenTemplate(path, RadioTowerLandmark.CreateCatalog());
                document = candidate;
                variantId = candidate.Snapshot()["variants"][0]["id"].Value<string>();
                ResetView();
                return true;
            }
            catch (Exception exception) { rejectedPath = path; error = exception.Message; return false; }
        }

        /// <summary>Forks the full template and all variants into a new identity, without assigning or writing it.</summary>
        public bool TryFork(string templateId, string configurationId, Func<BoardDocumentDecision> decide, Func<bool> save = null)
        {
            try
            {
                if (document == null) return false;
                BoardTemplateDocument candidate = document.Fork(templateId, configurationId, RadioTowerLandmark.CreateCatalog());
                if (!ResolveDirty(decide, save)) return false;
                document = candidate;
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
            if (document == null || !document.IsDirty) return true;
            return decide != null && document.CanLeave(decide(), save);
        }

        /// <summary>Discards only this window's in-memory draft after an explicit owner decision; useful for controlled test cleanup.</summary>
        public void CloseAfterDecision(BoardDocumentDecision decision, Func<bool> save = null)
        {
            if (!ResolveDirty(() => decision, save)) return;
            allowClose = true;
            hasUnsavedChanges = false;
            Close();
        }

        private void OnGUI()
        {
            guiOrigin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            Event input = Event.current;
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
                Action command = input.keyCode == KeyCode.S ? (Action)(() => SaveFromDialog(saveAs)) :
                    input.keyCode == KeyCode.O ? OpenFromDialog : input.keyCode == KeyCode.N ? (Action)(() => ShowIdentity(false)) :
                    input.keyCode == KeyCode.D ? () => ShowIdentity(true) : null;
                if (command != null)
                {
                    input.Use();
                    Defer(command);
                }
            }
            if (document != null && input.type == EventType.KeyDown && (input.control || input.command) && !EditorGUIUtility.editingTextField)
            {
                bool undo = input.keyCode == KeyCode.Z && !input.shift;
                bool redo = input.keyCode == KeyCode.Y || input.keyCode == KeyCode.Z && input.shift;
                if (undo || redo)
                {
                    input.Use();
                    Defer(() => { if (undo) document.Undo(); else document.Redo(); });
                }
            }
            DrawToolbar();
            if (identityPanel) DrawIdentity();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (!string.IsNullOrEmpty(rejectedPath))
            {
                EditorGUILayout.LabelField("Import Diagnostic", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(rejectedPath, EditorStyles.wordWrappedLabel, GUILayout.Height(36));
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (document != null)
            {
                bool wide = position.width >= 1060;
                JObject data = document.Snapshot();
                if (wide) EditorGUILayout.BeginHorizontal();
                if (wide) EditorGUILayout.BeginVertical(GUILayout.Width(200));
                DrawPalette(wide ? 1 : Math.Max(2, (int)(position.width - 40) / 190));
                if (wide) { EditorGUILayout.EndVertical(); EditorGUILayout.BeginVertical(GUILayout.MinWidth(300)); }
                DrawCanvas(data);
                if (wide) { EditorGUILayout.EndVertical(); EditorGUILayout.BeginVertical(GUILayout.Width(310)); }
                if (properties.Draw(document, variantId, Grid, ConfirmReferences, RefreshDocument)) RefreshDocument();
                if (wide) { EditorGUILayout.EndVertical(); EditorGUILayout.EndHorizontal(); }
                DrawDiagnostics();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
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
                Icon("Refresh", "Validate all variants", RefreshDocument);
                GUILayout.FlexibleSpace();
                if (document != null)
                {
                    string[] variants = document.Snapshot()["variants"].Values<JObject>().Select(row => (string)row["id"]).ToArray();
                    int selected = Math.Max(0, Array.IndexOf(variants, variantId));
                    int chosen = EditorGUILayout.Popup(selected, variants, EditorStyles.toolbarPopup, GUILayout.Width(145));
                    if (chosen != selected) Defer(() => SelectVariant(variants[chosen]));
                }
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                Icon("Folder Icon", "Choose external draft folder", () => EnsureStore(true));
                GUILayout.Label(new GUIContent(string.IsNullOrEmpty(draftRoot) ? "Draft Folder" : draftRoot, draftRoot), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(document == null ? "No Document" : (document.IsReadOnly ? "Read-only" : document.IsDirty ? "Modified" : "Saved"), EditorStyles.miniBoldLabel);
            }
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

        private void DrawPalette(int columns)
        {
            EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);
            for (int offset = 0; offset < BoardGridView.Palette.Count; offset += columns)
                using (new EditorGUILayout.HorizontalScope())
                    for (int index = offset; index < Math.Min(offset + columns, BoardGridView.Palette.Count); index++)
                    {
                        string id = BoardGridView.Palette[index];
                        string label = id == "ordinary.shambler.10" ? "Shambler" : id == "axe.basic" ? "Axe" :
                            id == "ordinary.exit-terrain" ? "Exit" : ObjectNames.NicifyVariableName(id.Substring(id.LastIndexOf('.') + 1));
                        if (GUILayout.Button(new GUIContent("", id),
                            Grid.ContentId == id && Grid.Mode == BoardPaintMode.Paint ? EditorStyles.miniButtonMid : EditorStyles.miniButton,
                            GUILayout.Height(32), GUILayout.MinWidth(110))) Defer(() => Grid.ChooseContent(id));
                        Rect button = GUILayoutUtility.GetLastRect();
                        BoardGridView.DrawSprite(new Rect(button.x + 2, button.y + 4, 24, 24), BoardGridView.Preview(id));
                        GUI.Label(new Rect(button.x + 32, button.y + 7, button.width - 36, 20), label, EditorStyles.miniLabel);
                    }
        }

        private void DrawCanvas(JObject data)
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
            float available = position.width >= 1060 ? position.width - 565 : position.width - 50;
            float side = Mathf.Clamp(available, 280, 560);
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
            if (!saveAs && document.SourcePath != null) return TrySave();
            string path = EditorUtility.SaveFilePanel("Save Board Draft", draftRoot, "board-template", "json");
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string fingerprint = store.CaptureFingerprint(path);
                if (fingerprint != null && !EditorUtility.DisplayDialog("Replace Draft", path, "Replace", "Cancel")) return false;
                return TrySave(path, fingerprint != null, fingerprint);
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private BoardDocumentDecision AskDecision()
        {
            if (document == null || !document.IsDirty) return BoardDocumentDecision.Discard;
            if (diagnostics.Count > 0)
                return EditorUtility.DisplayDialog("Unsaved Invalid Draft", "Discard this document's unsaved changes?", "Discard", "Cancel") ? BoardDocumentDecision.Discard : BoardDocumentDecision.Cancel;
            int decision = EditorUtility.DisplayDialogComplex("Unsaved Draft", "Save this document before leaving?", "Save", "Cancel", "Discard");
            return decision == 0 ? BoardDocumentDecision.Save : decision == 2 ? BoardDocumentDecision.Discard : BoardDocumentDecision.Cancel;
        }

        private static bool ConfirmReferences(string paths) => EditorUtility.DisplayDialog("Dependent Data", paths, "Apply With These References", "Cancel");

        private void Icon(string name, string tooltip, Action action)
        {
            GUIContent icon = EditorGUIUtility.IconContent(name);
            if (GUILayout.Button(new GUIContent(icon.image, tooltip), EditorStyles.toolbarButton, GUILayout.Width(27), GUILayout.Height(22)))
                Defer(action);
        }

        private void Defer(Action action)
        {
            BoardTemplateDocument expectedDocument = document;
            int expectedEpoch = callbackEpoch;
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                pendingCallbacks.Remove(callback);
                if (this == null || !callbacksActive || expectedEpoch != callbackEpoch || !ReferenceEquals(expectedDocument, document)) return;
                action();
                if (this != null && callbacksActive && expectedEpoch == callbackEpoch) RefreshDocument();
            };
            pendingCallbacks.Add(callback);
            EditorApplication.delayCall += callback;
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
            hasUnsavedChanges = document != null && document.IsDirty && !document.IsReadOnly;
            if (hasUnsavedChanges) allowClose = false;
            saveChangesMessage = "Save the modified board draft?";
            Remember();
            Repaint();
        }

        private void Remember() => editorState = document?.CaptureEditorState().ToString(Formatting.None) ?? "";
    }
}