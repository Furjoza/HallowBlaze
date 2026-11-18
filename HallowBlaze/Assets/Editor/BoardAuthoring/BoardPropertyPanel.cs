using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Edits supported schema fields with native controls, never arbitrary JSON or runtime definitions.</summary>
    public sealed class BoardPropertyPanel
    {
        private int tab;
        private int selectedOutcome;
        private Vector2 scroll;
        private string message = string.Empty;
        private Action deferredRefresh;
        private readonly Action<Action> schedule;
        private readonly Dictionary<string, Rect> controlRects = new Dictionary<string, Rect>();

        /// <summary>Creates a property model; a host-owned scheduler is required only when drawing or handling deferred UI actions.</summary>
        /// <param name="schedule">Commits pending fields and executes actions only while the originating window/document is active.</param>
        public BoardPropertyPanel(Action<Action> schedule = null) => this.schedule = schedule;

        /// <summary>Gets the field path selected by a diagnostic; selection itself does not mutate the draft.</summary>
        public string SelectedFieldPath { get; private set; } = string.Empty;

        /// <summary>Focuses the appropriate inspector section for a known diagnostic field.</summary>
        public void FocusField(string path)
        {
            SelectedFieldPath = path ?? string.Empty;
            tab = SelectedFieldPath.StartsWith("outcomes", StringComparison.Ordinal) ? 2 :
                SelectedFieldPath.StartsWith("configuration", StringComparison.Ordinal) || SelectedFieldPath.StartsWith("template", StringComparison.Ordinal) ? 1 : 0;
            scroll = Vector2.zero;
        }

        /// <summary>Adds an editable template-wide outcome; incomplete declarations remain invalid until the author supplies valid exit metadata.</summary>
        public bool AddOutcome(BoardTemplateDocument document, string variantId)
        {
            if (document == null || document.IsReadOnly) return false;
            JObject data = document.Snapshot();
            var outcomes = (JArray)data["outcomes"];
            string identifier = "outcome.1";
            while (outcomes.OfType<JObject>().Any(row => (string)row["id"] == identifier)) identifier += ".new";
            JObject variant = BoardGridView.Variant(data, variantId);
            if (variant == null) return false;
            int exit = ((JArray)variant["entities"]).OfType<JObject>().Where(row => (string)row["contentId"] == "ordinary.exit-terrain")
                .Select(row => (int)row["id"]).FirstOrDefault(candidate => !outcomes.OfType<JObject>().Any(row => (int)row["exitEntityId"] == candidate));
            document.Edit(target => ((JArray)target["outcomes"]).Add(new JObject { ["id"] = identifier, ["name"] = "New outcome", ["exitEntityId"] = exit, ["optional"] = false }));
            selectedOutcome = outcomes.Count;
            FocusField("outcomes");
            return true;
        }

        /// <summary>Handles inspector-only commands; ordinary text editing and native field navigation retain their normal key ownership.</summary>
        public bool HandleKeyboard(Event input, BoardTemplateDocument document, string variantId, Func<string, bool> confirm)
        {
            if (input.type != EventType.KeyDown || !input.alt || document == null) return false;
            int count = document.Snapshot()["outcomes"].Count();
            switch (input.keyCode)
            {
                case KeyCode.Insert: Defer(() => AddOutcome(document, variantId)); break;
                case KeyCode.Delete: Defer(() => RemoveOutcome(document, selectedOutcome, confirm)); break;
                case KeyCode.UpArrow: Defer(() => { FocusField("outcomes"); selectedOutcome = Math.Max(0, selectedOutcome - 1); }); break;
                case KeyCode.DownArrow: Defer(() => { FocusField("outcomes"); selectedOutcome = Math.Min(Math.Max(0, count - 1), selectedOutcome + 1); }); break;
                default: return false;
            }
            input.Use();
            return true;
        }

        /// <summary>Atomically moves/renames an entity, preserving bindings unless a referenced rename is explicitly confirmed.</summary>
        public bool SetEntity(BoardTemplateDocument document, string variantId, int entityId, int newId,
            GridPosition position, Func<string, bool> confirmReferences = null)
        {
            message = string.Empty;
            if (document == null || document.IsReadOnly) return false;
            JObject snapshot = document.Snapshot();
            JObject variant = BoardGridView.Variant(snapshot, variantId);
            JObject entity = (variant?["entities"] as JArray)?.OfType<JObject>().FirstOrDefault(row => (int)row["id"] == entityId);
            if (entity == null) return false;
            if (!GridPosition.DefaultBoardBounds.Contains(position)) { message = "Position must be within 0.0-7.7."; return false; }
            if (((JArray)variant["entities"]).OfType<JObject>().Any(row => row != entity &&
                ((int)row["id"] == newId || (string)row["layer"] == (string)entity["layer"] && (int)row["x"] == position.X && (int)row["y"] == position.Y)))
            { message = "The entity ID or target layer/cell is occupied."; return false; }
            JToken[] references = BoardGridView.References(snapshot, variant, entityId);
            if (newId != entityId && references.Length > 0 && (confirmReferences == null ||
                !confirmReferences(string.Join("\n", references.Select(reference => reference.Path))))) return false;
            if (newId == entityId && (int)entity["x"] == position.X && (int)entity["y"] == position.Y) return false;
            document.Edit(data =>
            {
                JObject target = BoardGridView.Variant(data, variantId);
                JObject row = ((JArray)target["entities"]).OfType<JObject>().Single(candidate => (int)candidate["id"] == entityId);
                if (newId != entityId)
                    foreach (JObject reference in BoardGridView.References(data, target, entityId).OfType<JObject>())
                    {
                        string field = reference["entityId"] != null ? "entityId" : reference["terrainId"] != null ? "terrainId" : "exitEntityId";
                        reference[field] = newId;
                    }
                row["id"] = newId;
                row["x"] = position.X;
                row["y"] = position.Y;
            });
            return true;
        }

        /// <summary>Edits one item/pressure binding; referenced textual identity changes require consent and atomically update their references.</summary>
        /// <param name="confirm">Receives the affected field paths; cancellation preserves data and undo history.</param>
        public bool SetBinding(BoardTemplateDocument document, string variantId, string path, JObject replacement, Func<string, bool> confirm = null)
        {
            if (document == null || document.IsReadOnly || replacement == null) return false;
            JObject snapshot = document.Snapshot();
            JObject variant = BoardGridView.Variant(snapshot, variantId);
            JObject binding = snapshot.SelectToken(path) as JObject;
            if (variant == null || binding == null || !new[] { "items", "plates", "gates" }.Any(field => binding.Parent == variant[field])) return false;
            string identity = binding["linkId"] == null ? "instanceId" : "linkId";
            string previous = (string)binding[identity];
            string next = (string)replacement[identity];
            JToken[] references = previous == next ? Array.Empty<JToken>() : BoardGridView.TextReferences(snapshot, variant, identity, previous)
                .Where(reference => reference.Path != path + "." + identity).ToArray();
            if (references.Length > 0 && (confirm == null || !confirm("Renamed binding and updated references:\n" + path + "." + identity + "\n" + string.Join("\n", references.Select(reference => reference.Path))))) return false;
            if (JToken.DeepEquals(binding, replacement)) return false;
            document.Edit(data =>
            {
                JObject target = BoardGridView.Variant(data, variantId);
                if (previous != next)
                    foreach (JToken reference in BoardGridView.TextReferences(data, target, identity, previous).Where(reference => reference.Path != path + "." + identity))
                        reference.Replace(new JValue(next));
                data.SelectToken(path).Replace(replacement.DeepClone());
            });
            return true;
        }

        /// <summary>Draws the selected cell, board configuration and outcome properties; returns true only for a committed document edit.</summary>
        /// <param name="refresh">Refreshes the host's diagnostics and dirty state after an asynchronously committed inspector action.</param>
        public bool Draw(BoardTemplateDocument document, string variantId, BoardGridView grid, Func<string, bool> confirm, Action refresh = null)
        {
            deferredRefresh = refresh;
            bool changed = false;
            int nextTab = GUILayout.Toolbar(tab, new[] { "Cell", "Board", "Outcomes" });
            RecordControl("Tabs");
            if (nextTab != tab) Defer(() => { tab = nextTab; scroll = Vector2.zero; });
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(200));
            if (!string.IsNullOrEmpty(SelectedFieldPath)) EditorGUILayout.LabelField(SelectedFieldPath, EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(document == null))
            {
                if (document != null)
                {
                    JObject data = document.Snapshot();
                    if (tab == 0) changed = DrawCell(document, data, variantId, grid, confirm);
                    else if (tab == 1) changed = DrawBoard(document, data);
                    else changed = DrawOutcomes(document, data, variantId, confirm);
                }
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Warning);
            EditorGUILayout.EndScrollView();
            RecordControl("Viewport");
            return changed;
        }

        private bool DrawCell(BoardTemplateDocument document, JObject data, string variantId, BoardGridView grid, Func<string, bool> confirm)
        {
            EditorGUILayout.LabelField("Cell " + grid.SelectedCell.X + "." + grid.SelectedCell.Y, EditorStyles.boldLabel);
            JObject variant = BoardGridView.Variant(data, variantId);
            if (variant == null) return false;
            foreach (JObject occupant in ((JArray)variant["entities"]).OfType<JObject>().Where(row =>
                (int)row["x"] == grid.SelectedCell.X && (int)row["y"] == grid.SelectedCell.Y))
                if (GUILayout.Toggle((string)occupant["layer"] == grid.Layer.ToString(),
                    new GUIContent(occupant["layer"] + " | " + occupant["id"] + " | " + occupant["contentId"], occupant.Path), EditorStyles.miniButton))
                {
                    var selectedLayer = (HallowBlaze.Core.Board.State.BoardLayer)Enum.Parse(typeof(HallowBlaze.Core.Board.State.BoardLayer), (string)occupant["layer"]);
                    if (selectedLayer != grid.Layer) Defer(() => grid.Layer = selectedLayer);
                }
            JObject entity = BoardGridView.EntityAt(variant, grid.Layer, grid.SelectedCell);
            if (entity == null) { EditorGUILayout.LabelField(grid.Layer + ": empty", EditorStyles.miniLabel); return false; }
            EditorGUILayout.LabelField((string)entity["contentId"], EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(document.IsReadOnly))
            {
            EditorGUI.BeginChangeCheck();
            int identifier = EditorGUILayout.DelayedIntField("Entity ID", (int)entity["id"]);
            int horizontal = EditorGUILayout.DelayedIntField("X", (int)entity["x"]);
            RecordControl("Cell.X");
            int vertical = EditorGUILayout.DelayedIntField("Y", (int)entity["y"]);
            if (EditorGUI.EndChangeCheck())
            {
                bool edited = SetEntity(document, variantId, (int)entity["id"], identifier, new GridPosition(horizontal, vertical), confirm);
                if (edited) grid.SelectEditedEntity(new GridPosition(horizontal, vertical));
                return edited;
            }
            foreach (string field in new[] { "items", "plates", "gates" })
            {
                string key = field == "items" ? "entityId" : "terrainId";
                JObject binding = (variant[field] as JArray)?.OfType<JObject>().FirstOrDefault(row => (int)row[key] == (int)entity["id"]);
                if (binding == null) continue;
                JObject edited = (JObject)binding.DeepClone();
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(field == "items" ? "Item Binding" : "Pressure Binding", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                if (field == "items")
                {
                    edited["instanceId"] = EditorGUILayout.DelayedTextField("Spawn ID", (string)binding["instanceId"]);
                    EditorGUILayout.LabelField("Definition", (string)binding["itemDefinitionId"]);
                    EditorGUILayout.LabelField("Family", (string)binding["resourceFamilyId"]);
                    edited["initiallyObservable"] = EditorGUILayout.Toggle("Observable", (bool)binding["initiallyObservable"]);
                    OptionalText(edited, "sourceId", "Fixed Source", new[] { "source.radio-tower.axe", "source.radio-tower.rations" });
                }
                else
                {
                    edited["linkId"] = EditorGUILayout.DelayedTextField("Link ID", (string)binding["linkId"]);
                    Choice(edited, field == "plates" ? "occupants" : "rule", field == "plates" ? "Occupants" : "Rule",
                        field == "plates" ? new[] { "Actors", "Boulders", "ActorsOrBoulders" } : new[] { "All", "Any" });
                }
                if (EditorGUI.EndChangeCheck()) return SetBinding(document, variantId, binding.Path, edited, confirm);
            }
            return false;
            }
        }

        private static bool DrawBoard(BoardTemplateDocument document, JObject data)
        {
            using (new EditorGUI.DisabledScope(document.IsReadOnly))
            {
            JObject edited = (JObject)data.DeepClone();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Template", EditorStyles.boldLabel);
            edited["templateId"] = EditorGUILayout.DelayedTextField("ID", (string)data["templateId"]);
            edited["templateVersion"] = EditorGUILayout.DelayedIntField("Version", (int)data["templateVersion"]);
            JObject config = (JObject)edited["configuration"];
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            config["id"] = EditorGUILayout.DelayedTextField("ID", (string)config["id"]);
            config["version"] = EditorGUILayout.DelayedIntField("Version", (int)config["version"]);
            foreach (string field in new[] { "pathLength", "branchPoints", "obstacleCount", "enemyCount" })
            {
                EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(field), EditorStyles.miniBoldLabel);
                var range = (JObject)config[field];
                range["minimum"] = EditorGUILayout.DelayedIntField("Minimum", (int)range["minimum"]);
                range["maximum"] = EditorGUILayout.DelayedIntField("Maximum", (int)range["maximum"]);
            }
            config["maximumConcurrentThreats"] = EditorGUILayout.DelayedIntField("Max. Threats", (int)config["maximumConcurrentThreats"]);
            config["minimumSafeStartMoves"] = EditorGUILayout.DelayedIntField("Safe Start Moves", (int)config["minimumSafeStartMoves"]);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Allowed Content", EditorStyles.boldLabel);
            var allowed = (JArray)config["allowedContentIds"];
            foreach (string contentId in BoardGridView.Palette)
            {
                bool present = allowed.Values<string>().Contains(contentId);
                bool selected = EditorGUILayout.ToggleLeft(contentId, present);
                if (present != selected)
                {
                    if (selected) allowed.Add(contentId);
                    else allowed.First(value => (string)value == contentId).Remove();
                }
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Resource Budgets", EditorStyles.boldLabel);
            var budgets = (JArray)config["resourceBudgets"];
            foreach (string family in new[] { "food", "tools" })
            {
                JObject budget = budgets.OfType<JObject>().FirstOrDefault(row => (string)row["resourceFamilyId"] == family);
                bool enabled = EditorGUILayout.ToggleLeft(family, budget != null);
                if (!enabled) { budget?.Remove(); continue; }
                if (budget == null)
                {
                    budget = new JObject { ["resourceFamilyId"] = family, ["itemCount"] = new JObject { ["minimum"] = 0, ["maximum"] = 64 } };
                    budgets.Add(budget);
                }
                budget["itemCount"]["minimum"] = EditorGUILayout.DelayedIntField("Minimum", (int)budget["itemCount"]["minimum"]);
                budget["itemCount"]["maximum"] = EditorGUILayout.DelayedIntField("Maximum", (int)budget["itemCount"]["maximum"]);
            }
            if (!EditorGUI.EndChangeCheck()) return false;
            document.Edit(target => { target["templateId"] = edited["templateId"].DeepClone(); target["templateVersion"] = edited["templateVersion"].DeepClone(); target["configuration"] = config.DeepClone(); });
            return true;
            }
        }

        private bool DrawOutcomes(BoardTemplateDocument document, JObject data, string variantId, Func<string, bool> confirm)
        {
            using (new EditorGUI.DisabledScope(document.IsReadOnly))
            {
            var outcomes = (JArray)data["outcomes"];
            selectedOutcome = Math.Min(selectedOutcome, Math.Max(0, outcomes.Count - 1));
            foreach (JObject outcome in outcomes.OfType<JObject>().ToArray())
            {
                EditorGUILayout.LabelField((string)outcome["id"], outcomes.IndexOf(outcome) == selectedOutcome ? EditorStyles.boldLabel : EditorStyles.label);
                var edited = (JObject)outcome.DeepClone();
                EditorGUI.BeginChangeCheck();
                edited["id"] = EditorGUILayout.DelayedTextField("ID", (string)outcome["id"]);
                edited["name"] = EditorGUILayout.DelayedTextField("Name", (string)outcome["name"]);
                edited["exitEntityId"] = EditorGUILayout.DelayedIntField("Exit Entity ID", (int)outcome["exitEntityId"]);
                edited["optional"] = EditorGUILayout.Toggle("Optional", (bool)outcome["optional"]);
                OptionalText(edited, "requiredToolDefinitionId", "Required Tool", new[] { "axe.basic" });
                OptionalText(edited, "guaranteedToolInstanceId", "Guaranteed Spawn", null);
                if (EditorGUI.EndChangeCheck()) { Replace(document, outcome.Path, edited); return true; }
                if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Minus", "Remove outcome"), GUILayout.Width(28)))
                {
                    int index = outcomes.IndexOf(outcome);
                    Defer(() => RemoveOutcome(document, index, confirm));
                }
                EditorGUILayout.Space(8);
            }
            if (GUILayout.Button(new GUIContent("Add Outcome", EditorGUIUtility.IconContent("Toolbar Plus").image)))
                Defer(() => AddOutcome(document, variantId));
            RecordControl("Outcomes.Add");
            return false;
            }
        }

        private static void OptionalText(JObject row, string field, string label, string[] choices)
        {
            int mode = row.Property(field) == null ? 0 : row[field].Type == JTokenType.Null ? 1 : 2;
            int next = EditorGUILayout.Popup(label, mode, new[] { "Absent", "None", "Value" });
            if (next == 0) row.Remove(field);
            else if (next == 1) row[field] = JValue.CreateNull();
            else
            {
                string value = mode == 2 ? (string)row[field] : choices?.FirstOrDefault() ?? "";
                if (choices == null) row[field] = EditorGUILayout.DelayedTextField("Value", value);
                else
                {
                    string[] available = choices.Contains(value) ? choices : choices.Concat(new[] { value }).ToArray();
                    row[field] = available[EditorGUILayout.Popup("Value", Array.IndexOf(available, value), available)];
                }
            }
        }

        private static void Choice(JObject row, string field, string label, string[] values)
        {
            int selected = Math.Max(0, Array.IndexOf(values, (string)row[field]));
            row[field] = values[EditorGUILayout.Popup(label, selected, values)];
        }

        private static void Replace(BoardTemplateDocument document, string path, JToken value) => document.Edit(data => data.SelectToken(path).Replace(value.DeepClone()));

        private static bool RemoveOutcome(BoardTemplateDocument document, int index, Func<string, bool> confirm)
        {
            if (document == null || document.IsReadOnly) return false;
            var outcomes = (JArray)document.Snapshot()["outcomes"];
            if (index < 0 || index >= outcomes.Count || confirm == null || !confirm(outcomes[index].Path)) return false;
            document.Edit(data => ((JArray)data["outcomes"])[index].Remove());
            return true;
        }

        private void RecordControl(string name)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Rect rect = GUILayoutUtility.GetLastRect();
                controlRects[name] = new Rect(GUIUtility.GUIToScreenPoint(rect.position), rect.size);
            }
        }

        private void Defer(Action action)
        {
            if (schedule == null) throw new InvalidOperationException("Deferred inspector actions require an active window scheduler.");
            schedule(() => { action(); deferredRefresh?.Invoke(); });
        }
    }
}