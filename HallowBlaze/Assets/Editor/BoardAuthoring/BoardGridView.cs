using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Separates inspection from explicit one-cell paint/erase operations.</summary>
    public enum BoardPaintMode
    {
        /// <summary>Selects a cell without changing document data.</summary>
        Select,
        /// <summary>Places the chosen content on its existing catalog layer.</summary>
        Paint,
        /// <summary>Removes only the selected layer after explicit dependent-data confirmation.</summary>
        Erase
    }

    /// <summary>Maps the fixed grid and applies single-gesture edits through the document's own undo history.</summary>
    public sealed class BoardGridView
    {
        private static readonly string[] PaletteIds =
        {
            "legacy.floor", "ordinary.exit-terrain", "landmark.plate", "landmark.gate",
            "legacy.player", "ordinary.shambler.10", "legacy.wall", "landmark.boulder",
            "axe.basic", "landmark.radio-tower.rations", "ordinary.shambler.20", "legacy.food", "legacy.soda",
            "legacy.bush-food", "ordinary.berries", "ordinary.carrot", "legacy.aid"
        };
        private static readonly Dictionary<string, Sprite> Previews = new Dictionary<string, Sprite>();
        private GridPosition inputCell;
        private int inputVersion;
        private int appliedInputVersion;

        /// <summary>Gets one visible brush per supported content type; retained duplicate fruit IDs remain selectable for recovery.</summary>
        public static IReadOnlyList<string> Palette { get; } = Array.AsReadOnly(PaletteIds
            .Where(id => id != "legacy.bush-food" && id != "landmark.radio-tower.rations").ToArray());
        /// <summary>Gets or sets the active occupancy layer; other layers at a cell are never erased implicitly.</summary>
        public BoardLayer Layer { get; set; } = BoardLayer.Terrain;
        /// <summary>Gets or sets whether the next cell activation selects, paints or erases.</summary>
        public BoardPaintMode Mode { get; set; }
        /// <summary>Gets the current inspected cell, in X-east/Y-north coordinates.</summary>
        public GridPosition SelectedCell { get; private set; } = new GridPosition(0, 0);
        /// <summary>Gets the selected supported palette ID.</summary>
        public string ContentId { get; private set; } = "legacy.floor";
        /// <summary>Gets an actionable refusal from the last attempted edit, or an empty string.</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>Selects existing catalog content and its layer, without editing the document.</summary>
        public void ChooseContent(string contentId)
        {
            if (!PaletteIds.Contains(contentId)) throw new ArgumentException("Unsupported palette content.", nameof(contentId));
            ContentId = contentId;
            Layer = LayerOf(contentId);
            Mode = BoardPaintMode.Paint;
        }

        /// <summary>Returns the fixed square for an X.Y cell; screen top is north (Y=7).</summary>
        public static Rect CellRect(Rect grid, GridPosition cell)
        {
            float size = Mathf.Min(grid.width, grid.height) / 8f;
            return new Rect(grid.x + cell.X * size, grid.y + (7 - cell.Y) * size, size, size);
        }

        /// <summary>Hit-tests the eight-by-eight canvas without treating labels or outside pixels as cells.</summary>
        public static bool TryCell(Rect grid, Vector2 pointer, out GridPosition cell)
        {
            float size = Mathf.Min(grid.width, grid.height) / 8f;
            Rect square = new Rect(grid.x, grid.y, size * 8, size * 8);
            if (size <= 0 || !square.Contains(pointer)) { cell = default; return false; }
            cell = new GridPosition(Mathf.FloorToInt((pointer.x - grid.x) / size), 7 - Mathf.FloorToInt((pointer.y - grid.y) / size));
            return true;
        }

        /// <summary>Changes inspection only; selection and keyboard navigation are never undoable edits.</summary>
        public void Select(GridPosition cell)
        {
            if (!GridPosition.DefaultBoardBounds.Contains(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
            SelectedCell = cell;
            inputCell = cell;
            appliedInputVersion = ++inputVersion;
        }

        internal void SelectEditedEntity(GridPosition cell)
        {
            SelectedCell = cell;
            if (inputVersion == appliedInputVersion) inputCell = cell;
        }

        /// <summary>Applies one pointer/keyboard activation, confirming every removed binding/outcome before one atomic undo unit.</summary>
        /// <param name="confirmRemoval">Receives exact dependent JSON paths; false or null cancels their removal.</param>
        public bool Activate(BoardTemplateDocument document, string variantId, GridPosition cell, Func<string, bool> confirmRemoval = null)
        {
            Select(cell);
            return Apply(document, variantId, cell, Mode, Layer, ContentId, confirmRemoval);
        }

        private bool Apply(BoardTemplateDocument document, string variantId, GridPosition cell, BoardPaintMode mode,
            BoardLayer layer, string contentId, Func<string, bool> confirmRemoval)
        {
            Message = string.Empty;
            if (document == null || document.IsPlacement || mode == BoardPaintMode.Select) return false;
            if (document.IsReadOnly) { Message = "Published original is read-only. Fork a new identity first."; return false; }
            JObject snapshot = document.Snapshot();
            JObject variant = Variant(snapshot, variantId);
            if (variant == null) { Message = "Select an existing variant."; return false; }
            if (mode == BoardPaintMode.Paint && LayerOf(contentId) != layer)
            { Message = "Selected content belongs to " + LayerOf(contentId) + ", not the active " + layer + " layer."; return false; }
            JObject existing = EntityAt(variant, layer, cell);
            if (mode == BoardPaintMode.Erase && existing == null) return false;
            if (mode == BoardPaintMode.Paint && (string)existing?["contentId"] == contentId) return false;
            JToken[] references = existing == null ? Array.Empty<JToken>() : References(snapshot, variant, (int)existing["id"]);
            JToken[] textReferences = references.OfType<JObject>().SelectMany(binding =>
                binding["linkId"] != null ? TextReferences(snapshot, variant, "linkId", (string)binding["linkId"]) :
                binding["instanceId"] != null ? TextReferences(snapshot, variant, "instanceId", (string)binding["instanceId"]) : Array.Empty<JToken>())
                .Where(reference => !references.Any(removed => reference.Ancestors().Contains(removed))).Distinct().ToArray();
            JToken[] authoredReferences = references.Where(reference => !IsDefaultSharedExit(snapshot, reference)).ToArray();
            string notice = "Removed data:\n" + string.Join("\n", authoredReferences.Select(reference => reference.Path));
            if (textReferences.Length > 0) notice += "\nAffected retained references (repair may be required):\n" + string.Join("\n", textReferences.Select(reference => reference.Path));
            if ((authoredReferences.Length > 0 || textReferences.Length > 0) && (confirmRemoval == null || !confirmRemoval(notice))) return false;
            document.Edit(data =>
            {
                if (mode == BoardPaintMode.Paint)
                {
                    var allowed = (JArray)data["configuration"]["allowedContentIds"];
                    if (!allowed.Values<string>().Contains(contentId)) allowed.Add(contentId);
                    if (layer == BoardLayer.Item)
                    {
                        string family = contentId == "axe.basic" ? "tools" : contentId == "legacy.aid" ? "medical" : "food";
                        var budgets = (JArray)data["configuration"]["resourceBudgets"];
                        if (!budgets.OfType<JObject>().Any(budget => (string)budget["resourceFamilyId"] == family))
                            budgets.Add(new JObject { ["resourceFamilyId"] = family, ["itemCount"] = new JObject { ["minimum"] = 0, ["maximum"] = 64 } });
                    }
                }
                JObject target = Variant(data, variantId);
                var entities = (JArray)target["entities"];
                JObject old = EntityAt(target, layer, cell);
                int identifier = old == null ? NextId(data) : (int)old["id"];
                if (old != null)
                {
                    foreach (JToken reference in References(data, target, identifier)) reference.Remove();
                    old.Remove();
                }
                if (mode == BoardPaintMode.Erase) return;
                if (contentId == "legacy.player")
                {
                    JObject start = entities.OfType<JObject>().SingleOrDefault(row => (string)row["contentId"] == "legacy.player");
                    if (start != null) { start["x"] = cell.X; start["y"] = cell.Y; return; }
                }
                entities.Add(new JObject { ["id"] = identifier, ["layer"] = layer.ToString(), ["contentId"] = contentId, ["x"] = cell.X, ["y"] = cell.Y });
                if ((int)data["schemaVersion"] == 2 && contentId == "ordinary.exit-terrain")
                {
                    var outcomes = (JArray)data["outcomes"];
                    if (!outcomes.OfType<JObject>().Any(outcome => (int)outcome["exitEntityId"] == identifier))
                        outcomes.Add(new JObject { ["id"] = "exit." + identifier, ["name"] = "Exit", ["exitEntityId"] = identifier,
                            ["optional"] = outcomes.Count > 0 });
                }
                if (layer == BoardLayer.Item)
                    ((JArray)target["items"]).Add(new JObject
                    {
                        ["instanceId"] = NextItemId(target, identifier), ["entityId"] = identifier,
                        ["itemDefinitionId"] = contentId, ["resourceFamilyId"] = contentId == "axe.basic" ? "tools" : contentId == "legacy.aid" ? "medical" : "food",
                        ["initiallyObservable"] = true
                    });
                if (contentId == "landmark.plate" || contentId == "landmark.gate")
                {
                    string field = contentId == "landmark.plate" ? "plates" : "gates";
                    if (target[field] == null) target[field] = new JArray();
                    var binding = new JObject { ["terrainId"] = identifier, ["linkId"] = "link." + identifier };
                    binding[contentId == "landmark.plate" ? "occupants" : "rule"] = contentId == "landmark.plate" ? "ActorsOrBoulders" : "All";
                    ((JArray)target[field]).Add(binding);
                }
            });
            return true;
        }

        private static bool IsDefaultSharedExit(JObject data, JToken reference)
        {
            if ((int)data["schemaVersion"] != 2 || reference.Parent != data["outcomes"] || !(reference is JObject outcome)) return false;
            return outcome.Count == 4 && (string)outcome["id"] == "exit." + (int)outcome["exitEntityId"] &&
                (string)outcome["name"] == "Exit" && outcome["optional"]?.Type == JTokenType.Boolean;
        }

        /// <summary>Returns the existing read-only content sprite; missing art is not substituted with a different gameplay definition.</summary>
        public static Sprite Preview(string contentId)
        {
            if (Previews.TryGetValue(contentId, out Sprite cached)) return cached;
            GameObject prefab = Prefab(contentId);
            Sprite sprite = prefab == null ? null : prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
            if (sprite == null && prefab != null)
            {
                RuntimeAnimatorController controller = prefab.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController;
                foreach (AnimationClip clip in controller == null ? Array.Empty<AnimationClip>() : controller.animationClips.OrderBy(candidate => candidate.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) < 0).ToArray())
                    foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip).Where(candidate => candidate.propertyName == "m_Sprite"))
                    {
                        sprite = AnimationUtility.GetObjectReferenceCurve(clip, binding).Select(frame => frame.value).OfType<Sprite>().FirstOrDefault();
                        if (sprite != null) break;
                    }
            }
            Previews[contentId] = sprite;
            return sprite;
        }

        /// <summary>Uses the actual source prefab name; there is no display-name translation table.</summary>
        public static string ContentName(string contentId) => Prefab(contentId)?.name ?? contentId;

        private static GameObject Prefab(string contentId)
        {
            string path;
            switch (contentId)
            {
                case "legacy.player": path = "Assets/Prefabs/Player.prefab"; break;
                case "ordinary.shambler.10": path = "Assets/Prefabs/Shambler10.prefab"; break;
                case "ordinary.shambler.20": path = "Assets/Prefabs/Shambler20.prefab"; break;
                case "legacy.floor": path = "Assets/Prefabs/Tiles/Floor1.prefab"; break;
                case "legacy.wall": path = "Assets/Prefabs/Tiles/Bush01.prefab"; break;
                case "ordinary.exit-terrain": path = "Assets/Prefabs/Tiles/Exit.prefab"; break;
                case "landmark.boulder": path = "Assets/Prefabs/Tiles/Boulder.prefab"; break;
                case "landmark.plate": path = "Assets/Prefabs/Tiles/PressurePlate.prefab"; break;
                case "landmark.gate": path = "Assets/Prefabs/Tiles/Gate.prefab"; break;
                case "axe.basic": path = "Assets/Prefabs/Tiles/Axe.prefab"; break;
                case "landmark.radio-tower.rations":
                case "legacy.food": path = "Assets/Prefabs/Tiles/Raspberries.prefab"; break;
                case "legacy.soda": path = "Assets/Prefabs/Tiles/Soda.prefab"; break;
                case "legacy.bush-food":
                case "ordinary.berries": path = "Assets/Prefabs/Tiles/Blueberries.prefab"; break;
                case "ordinary.carrot": path = "Assets/Prefabs/Tiles/Carrot.prefab"; break;
                case "legacy.aid": path = "Assets/Prefabs/Tiles/AidKit.prefab"; break;
                default: return null;
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>Draws all occupancy layers and handles equivalent pointer/keyboard gestures only while this canvas has focus.</summary>
        /// <param name="defer">Commits pending fields and schedules a gesture only while its originating window/document is active.</param>
        public bool Draw(Rect canvas, BoardTemplateDocument document, string variantId, Func<string, bool> confirm, Action<Action> defer, Action refresh = null)
        {
            int control = GUIUtility.GetControlID("HallowBlaze.BoardCanvas".GetHashCode(), FocusType.Keyboard, canvas);
            Event current = Event.current;
            bool changed = false;
            if (current.type == EventType.MouseDown && current.button == 0 && TryCell(canvas, current.mousePosition, out GridPosition cell))
            {
                BoardPaintMode acceptedMode = Mode;
                BoardLayer acceptedLayer = Layer;
                string acceptedContent = ContentId;
                inputCell = cell;
                int acceptedVersion = ++inputVersion;
                defer(() => { SelectedCell = cell; appliedInputVersion = acceptedVersion; Apply(document, variantId, cell, acceptedMode, acceptedLayer, acceptedContent, confirm); refresh?.Invoke(); });
                GUIUtility.keyboardControl = control;
                current.Use();
            }
            if (GUIUtility.keyboardControl == control && current.type == EventType.KeyDown)
            {
                GridPosition next = inputCell;
                BoardPaintMode acceptedMode = Mode;
                bool activate = false;
                switch (current.keyCode)
                {
                    case KeyCode.S: Mode = BoardPaintMode.Select; break;
                    case KeyCode.P: Mode = BoardPaintMode.Paint; break;
                    case KeyCode.E: Mode = BoardPaintMode.Erase; break;
                    case KeyCode.L:
                        var layers = (BoardLayer[])Enum.GetValues(typeof(BoardLayer));
                        Layer = layers[(Array.IndexOf(layers, Layer) + 1) % layers.Length];
                        break;
                    case KeyCode.LeftArrow: next = new GridPosition(Math.Max(0, next.X - 1), next.Y); break;
                    case KeyCode.RightArrow: next = new GridPosition(Math.Min(7, next.X + 1), next.Y); break;
                    case KeyCode.UpArrow: next = new GridPosition(next.X, Math.Min(7, next.Y + 1)); break;
                    case KeyCode.DownArrow: next = new GridPosition(next.X, Math.Max(0, next.Y - 1)); break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter: activate = true; break;
                    case KeyCode.Delete:
                    case KeyCode.Backspace:
                        acceptedMode = BoardPaintMode.Erase;
                        activate = true;
                        break;
                    case KeyCode.Escape: Mode = BoardPaintMode.Select; break;
                    default:
                        int number = current.keyCode == KeyCode.Alpha0 ? 9 : (int)current.keyCode - (int)KeyCode.Alpha1;
                        if (number < 0 || number >= PaletteIds.Length) return changed;
                        ChooseContent(PaletteIds[number]);
                        break;
                }
                inputCell = next;
                BoardLayer acceptedLayer = Layer;
                string acceptedContent = ContentId;
                int acceptedVersion = ++inputVersion;
                current.Use();
                defer(() =>
                {
                    SelectedCell = next;
                    appliedInputVersion = acceptedVersion;
                    if (activate) Apply(document, variantId, next, acceptedMode, acceptedLayer, acceptedContent, confirm);
                    refresh?.Invoke();
                });
                GUIUtility.keyboardControl = control;
            }
            JObject variant = Variant(document.Snapshot(), variantId);
            if (variant == null) return changed;
            for (int horizontal = 0; horizontal < 8; horizontal++)
                for (int vertical = 0; vertical < 8; vertical++)
                {
                    var position = new GridPosition(horizontal, vertical);
                    Rect rect = CellRect(canvas, position);
                    if (current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(rect, (horizontal + vertical) % 2 == 0 ? new Color(0.15f, 0.16f, 0.16f) : new Color(0.19f, 0.20f, 0.20f));
                        foreach (JObject entity in ((JArray)variant["entities"]).OfType<JObject>().Where(row => (int)row["x"] == horizontal && (int)row["y"] == vertical)
                            .OrderBy(row => (string)row["layer"] == "Terrain" ? 0 : (string)row["layer"] == "Obstacle" ? 1 : (string)row["layer"] == "Item" ? 2 : 3))
                        {
                            Sprite sprite = Preview((string)entity["contentId"]);
                            float scale = (string)entity["layer"] == "Terrain" ? 1f : (string)entity["layer"] == "Item" ? 0.4f : 0.7f;
                            Rect image = new Rect(rect.center.x - rect.width * scale / 2, rect.center.y - rect.height * scale / 2, rect.width * scale, rect.height * scale);
                            if ((string)entity["layer"] == "Item") image.position += new Vector2(rect.width * 0.22f, rect.height * 0.22f);
                            DrawSprite(image, sprite);
                        }
                        if (position.Equals(SelectedCell))
                        {
                            Color border = new Color(0.25f, 0.88f, 0.72f);
                            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), border);
                            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), border);
                            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), border);
                            EditorGUI.DrawRect(new Rect(rect.xMax - 2, rect.y, 2, rect.height), border);
                        }
                    }
                    string tooltip = horizontal + "." + vertical + "\n" + string.Join("\n", ((JArray)variant["entities"]).OfType<JObject>()
                        .Where(row => (int)row["x"] == horizontal && (int)row["y"] == vertical).Select(row => row["layer"] + " | " + row["id"] + " | " + row["contentId"]));
                    GUI.Label(rect, new GUIContent("", tooltip));
                    if (vertical == 0) GUI.Label(new Rect(rect.x, canvas.yMax + 2, rect.width, 18), horizontal.ToString(), EditorStyles.centeredGreyMiniLabel);
                    if (horizontal == 0) GUI.Label(new Rect(canvas.x - 22, rect.y + rect.height / 2 - 8, 20, 18), vertical.ToString(), EditorStyles.centeredGreyMiniLabel);
                }
            return changed;
        }

        internal static void DrawSprite(Rect rect, Sprite sprite)
        {
            if (sprite == null) return;
            Rect source = sprite.textureRect;
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, new Rect(source.x / sprite.texture.width, source.y / sprite.texture.height,
                source.width / sprite.texture.width, source.height / sprite.texture.height), true);
        }

        internal static JObject Variant(JObject data, string variantId) => (data["variants"] as JArray)?.OfType<JObject>().FirstOrDefault(row => (string)row["id"] == variantId);

        internal static JObject EntityAt(JObject variant, BoardLayer layer, GridPosition cell) => (variant?["entities"] as JArray)?.OfType<JObject>()
            .FirstOrDefault(row => (string)row["layer"] == layer.ToString() && (int)row["x"] == cell.X && (int)row["y"] == cell.Y);

        internal static BoardLayer LayerOf(string contentId)
        {
            if (contentId == "legacy.player" || contentId == "ordinary.shambler.10" || contentId == "ordinary.shambler.20") return BoardLayer.Actor;
            if (contentId == "legacy.wall" || contentId == "landmark.boulder") return BoardLayer.Obstacle;
            if (contentId == "axe.basic" || contentId == "landmark.radio-tower.rations" || contentId == "legacy.food" || contentId == "legacy.soda" ||
                contentId == "legacy.bush-food" || contentId == "ordinary.berries" || contentId == "ordinary.carrot" || contentId == "legacy.aid") return BoardLayer.Item;
            return BoardLayer.Terrain;
        }

        internal static JToken[] References(JObject data, JObject variant, int identifier)
        {
            return new[] { "items", "plates", "gates" }.SelectMany(field => (variant[field] as JArray ?? new JArray()).OfType<JObject>()
                    .Where(row => (int)(row[field == "items" ? "entityId" : "terrainId"]) == identifier).Cast<JToken>())
                .Concat(((JArray)data["outcomes"]).OfType<JObject>().Where(row => (int)row["exitEntityId"] == identifier)).ToArray();
        }

        internal static JToken[] TextReferences(JObject data, JObject variant, string field, string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return Array.Empty<JToken>();
            if (field == "linkId") return new[] { "plates", "gates" }.SelectMany(name => (variant[name] as JArray ?? new JArray()).OfType<JObject>())
                .Where(row => (string)row["linkId"] == identifier).Select(row => row["linkId"]).ToArray();
            return ((JArray)data["outcomes"]).OfType<JObject>().Where(row => (string)row["guaranteedToolInstanceId"] == identifier)
                .Select(row => row["guaranteedToolInstanceId"]).ToArray();
        }

        private static int NextId(JObject data)
        {
            var used = new HashSet<int>(((JArray)data["variants"]).OfType<JObject>().SelectMany(row => ((JArray)row["entities"]).Values<JObject>()).Select(row => (int)row["id"]));
            int candidate = 1;
            while (used.Contains(candidate)) candidate = checked(candidate + 1);
            return candidate;
        }

        private static string NextItemId(JObject variant, int identifier)
        {
            string candidate = "item." + identifier;
            var used = new HashSet<string>(((JArray)variant["items"]).Values<JObject>().Select(row => (string)row["instanceId"]));
            while (used.Contains(candidate)) candidate += ".new";
            return candidate;
        }
    }
}