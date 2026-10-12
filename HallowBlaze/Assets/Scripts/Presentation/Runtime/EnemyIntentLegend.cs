using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HallowBlaze.Core.Board.Primitives;
using UnityEngine;
using UnityEngine.UI;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Owns a labeled, informational intent legend using copies of the accepted renderer's actual glyph meshes.</summary>
    public sealed class EnemyIntentLegend : IDisposable
    {
        private readonly Dictionary<EnemyIntentSymbol, EnemyIntentLegendGlyph> glyphs = new Dictionary<EnemyIntentSymbol, EnemyIntentLegendGlyph>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<RectTransform> rowTexts = new List<RectTransform>();
        private readonly RectTransform viewport;
        private readonly RectTransform panel;
        private readonly RectTransform scrollViewport;
        private readonly RectTransform content;
        private readonly ScrollRect scroll;
        private bool disposed;

        /// <summary>Gets the owned transient canvas; no scene, runtime, or input source is owned.</summary>
        public GameObject Root { get; }
        /// <summary>Gets the labeled navigation-enabled control whose click only toggles this legend.</summary>
        public Button Control { get; }
        /// <summary>Gets the five owned glyph graphics; meshes are borrowed by the graphics and released by the legend.</summary>
        public IReadOnlyDictionary<EnemyIntentSymbol, EnemyIntentLegendGlyph> Glyphs { get; }
        /// <summary>Gets panel bounds in supplied viewport pixels, excluding the separate always-available control.</summary>
        public Rect PanelBounds { get; private set; }
        /// <summary>Gets the labeled control bounds in supplied viewport pixels.</summary>
        public Rect ControlBounds { get; private set; }
        /// <summary>Gets whether the informational panel is shown.</summary>
        public bool IsVisible => !disposed && panel.gameObject.activeSelf;

        /// <summary>
        /// Builds transient UI using the project's English prototype labels and built-in runtime font.
        /// The caller supplies viewport pixels and the inspected board area, then explicitly relayouts on viewport changes.
        /// No gameplay keys, EventSystem, command source, resolver, or domain state is created or accessed.
        /// </summary>
        /// <param name="parent">Borrowed UI owner, or null for a standalone canvas.</param>
        /// <param name="viewportSize">Finite viewport dimensions, at least 320 by 480 pixels.</param>
        /// <param name="inspectedArea">Board pixels kept clear by selecting a free side, top or bottom margin.</param>
        public EnemyIntentLegend(Transform parent, Vector2 viewportSize, Rect inspectedArea)
        {
            Root = new GameObject("Enemy intent legend", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)) { hideFlags = HideFlags.DontSave };
            Root.transform.SetParent(parent, false);
            Canvas canvas = Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            viewport = Rect("Legend viewport", Root.transform, Vector2.zero, Vector2.zero, Vector2.zero);
            panel = Rect("Intent meanings", viewport, Vector2.zero, Vector2.zero, Vector2.zero);
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color32(28, 41, 35, 250);
            scrollViewport = Rect("Legend scroll viewport", panel, Vector2.zero, Vector2.zero, Vector2.zero);
            Image pointerTarget = scrollViewport.gameObject.AddComponent<Image>();
            pointerTarget.color = Color.clear;
            scrollViewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Legend content", scrollViewport, Vector2.zero, Vector2.zero, Vector2.zero);
            scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = scrollViewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24;
            RectTransform controlRect = Rect("Enemy intents", viewport, Vector2.zero, Vector2.zero, Vector2.zero);
            Image controlImage = controlRect.gameObject.AddComponent<Image>();
            controlImage.color = new Color32(45, 62, 52, 255);
            Control = controlRect.gameObject.AddComponent<Button>();
            Control.targetGraphic = controlImage;
            Text("Control label", controlRect, "Enemy intents", new Vector2(8, 4), new Vector2(156, 32));
            Control.onClick.AddListener(Toggle);
            Glyphs = new ReadOnlyDictionary<EnemyIntentSymbol, EnemyIntentLegendGlyph>(glyphs);
            try
            {
                Resize(viewportSize, inspectedArea);
                BuildGlyphs();
                Hide();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Shows only informational UI; repeated calls do not submit a command or advance resources/cadence.</summary>
        public void Show()
        {
            EnsureOpen();
            panel.gameObject.SetActive(true);
        }

        /// <summary>Hides only the informational panel; the labeled control remains available.</summary>
        public void Hide()
        {
            EnsureOpen();
            panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Selects a free margin for the control and panel without covering inspected board pixels.
        /// Rows grow for wrapped labels; a shorter panel scrolls rather than clipping inaccessible meanings.
        /// Rejects unsupported viewports or a board leaving no usable margin, preserving the previous layout.
        /// </summary>
        /// <param name="size">Finite viewport pixels, at least 320 by 480.</param>
        /// <param name="inspectedArea">Required visible board pixels in the same bottom-left coordinate system.</param>
        /// <exception cref="ArgumentException">Dimensions are unsupported or no free margin fits a readable panel and control.</exception>
        /// <exception cref="ObjectDisposedException">This legend has been disposed.</exception>
        public void Resize(Vector2 size, Rect inspectedArea)
        {
            EnsureOpen();
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x < 320 || size.y < 480)
                throw new ArgumentException("Legend viewport must be at least 320 by 480 pixels.", nameof(size));
            Rect usable = new Rect(8, 8, size.x - 16, size.y - 16);
            var margins = new[]
            {
                UnityEngine.Rect.MinMaxRect(Mathf.Max(usable.xMin, inspectedArea.xMax + 8), usable.yMin, usable.xMax, usable.yMax),
                UnityEngine.Rect.MinMaxRect(usable.xMin, usable.yMin, Mathf.Min(usable.xMax, inspectedArea.xMin - 8), usable.yMax),
                UnityEngine.Rect.MinMaxRect(usable.xMin, Mathf.Max(usable.yMin, inspectedArea.yMax + 8), usable.xMax, usable.yMax),
                UnityEngine.Rect.MinMaxRect(usable.xMin, usable.yMin, usable.xMax, Mathf.Min(usable.yMax, inspectedArea.yMin - 8))
            };
            Rect? available = null;
            foreach (Rect margin in margins)
            {
                if (margin.width >= 196 && margin.height >= 132)
                {
                    available = margin;
                    break;
                }
            }
            if (!available.HasValue)
                throw new ArgumentException("Legend must leave the supplied inspected board area visible.", nameof(inspectedArea));
            Rect area = available.Value;
            float width = Mathf.Min(344, area.width);
            var nextPanel = new Rect(area.xMax - width, area.yMin + 48, width, Mathf.Min(394, area.height - 48));
            var nextControl = new Rect(area.xMax - 172, area.yMin, 172, 40);
            PanelBounds = nextPanel;
            ControlBounds = nextControl;
            viewport.sizeDelta = size;
            panel.anchoredPosition = nextPanel.position;
            panel.sizeDelta = nextPanel.size;
            scrollViewport.sizeDelta = nextPanel.size;
            LayoutRows();
            var control = (RectTransform)Control.transform;
            control.anchoredPosition = nextControl.position;
            control.sizeDelta = nextControl.size;
        }

        /// <summary>Removes the listener and releases all owned UI/meshes; borrowed parents and gameplay remain intact.</summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Control.onClick.RemoveListener(Toggle);
            Root.SetActive(false);
            foreach (Mesh mesh in meshes)
                Release(mesh);
            Release(Root);
        }

        private void Toggle()
        {
            if (IsVisible)
                Hide();
            else
                Show();
        }

        private void BuildGlyphs()
        {
            var prototype = new GameObject("Legend glyph prototype") { hideFlags = HideFlags.DontSave };
            var cameraObject = new GameObject("Legend mesh camera") { hideFlags = HideFlags.DontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 1, -10);
            EnemyIntentView view = prototype.AddComponent<EnemyIntentView>();
            string[] meanings = { "Move\nMove to the marked cell.",
                "Move / attack on entry\nEntering the marked cell makes it attack instead.",
                "Attack fixed cell\nLeaving that cell makes it miss.",
                "Investigate (example)\nAn investigation target.", "Wait\nNo movement or attack." };
            try
            {
                for (int index = 0; index < meanings.Length; index++)
                {
                    var symbol = (EnemyIntentSymbol)(index + 1);
                    view.Show(EnemyIntentProjection.CreateExample(new EntityId(0), new GridPosition(0, 0), symbol,
                        symbol == EnemyIntentSymbol.Wait ? (GridPosition?)null : new GridPosition(0, 1),
                        symbol == EnemyIntentSymbol.ConditionalMove || symbol == EnemyIntentSymbol.Attack ? (EntityId?)new EntityId(-1) : null));
                    Transform sourceGlyph = view.transform.Find("Glyph");
                    var pieces = new List<CombineInstance>();
                    foreach (LineRenderer line in sourceGlyph.GetComponentsInChildren<LineRenderer>())
                    {
                        var piece = new Mesh();
                        line.BakeMesh(piece, camera, true);
                        Vector3[] vertices = piece.vertices;
                        for (int vertex = 0; vertex < vertices.Length; vertex++)
                            vertices[vertex] = sourceGlyph.InverseTransformPoint(line.transform.TransformPoint(vertices[vertex])) * (48 / EnemyIntentView.MarkerSize);
                        piece.vertices = vertices;
                        pieces.Add(new CombineInstance { mesh = piece });
                    }
                    var mesh = new Mesh { name = "Legend " + symbol };
                    mesh.CombineMeshes(pieces.ToArray(), true, false);
                    foreach (CombineInstance piece in pieces)
                        Release(piece.mesh);
                    meshes.Add(mesh);
                    float rowBottom = 12 + (4 - index) * 74;
                    RectTransform icon = Rect(symbol + " glyph", content, new Vector2(12, rowBottom + 12), new Vector2(48, 48), new Vector2(0.5f, 0.5f));
                    icon.anchoredPosition += new Vector2(24, 24);
                    icon.gameObject.AddComponent<CanvasRenderer>();
                    EnemyIntentLegendGlyph graphic = icon.gameObject.AddComponent<EnemyIntentLegendGlyph>();
                    graphic.GlyphMesh = mesh;
                    graphic.raycastTarget = false;
                    glyphs.Add(symbol, graphic);
                    rowTexts.Add(Text(symbol + " meaning", content, meanings[index], new Vector2(72, rowBottom), new Vector2(PanelBounds.width - 84, 70)));
                }
                LayoutRows();
            }
            finally
            {
                prototype.SetActive(false);
                Release(prototype);
                Release(cameraObject);
            }
        }

        private void LayoutRows()
        {
            float bottom = 12;
            for (int index = rowTexts.Count - 1; index >= 0; index--)
            {
                RectTransform row = rowTexts[index];
                row.sizeDelta = new Vector2(PanelBounds.width - 84, 70);
                float height = Mathf.Max(70, Mathf.Ceil(row.GetComponent<UnityEngine.UI.Text>().preferredHeight));
                row.sizeDelta = new Vector2(row.sizeDelta.x, height);
                row.anchoredPosition = new Vector2(72, bottom);
                var icon = (RectTransform)glyphs[(EnemyIntentSymbol)(index + 1)].transform;
                icon.anchoredPosition = new Vector2(36, bottom + height / 2);
                bottom += height + 4;
            }
            content.sizeDelta = new Vector2(PanelBounds.width, bottom + 12);
            content.anchoredPosition = new Vector2(0, Mathf.Min(0, PanelBounds.height - content.sizeDelta.y));
            scroll.vertical = content.sizeDelta.y > PanelBounds.height;
            scroll.verticalNormalizedPosition = 1;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size, Vector2 pivot)
        {
            var instance = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
            var rect = (RectTransform)instance.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Text(string name, RectTransform parent, string value, Vector2 position, Vector2 size)
        {
            RectTransform rect = Rect(name, parent, position, size, Vector2.zero);
            UnityEngine.UI.Text label = rect.gameObject.AddComponent<UnityEngine.UI.Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 14;
            label.text = value;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return rect;
        }

        private void EnsureOpen()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(EnemyIntentLegend));
        }

        private static void Release(UnityEngine.Object instance)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(instance);
            else
                UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    /// <summary>Draws a borrowed baked intent mesh as Canvas geometry; it does not own or alter the source renderer or mesh.</summary>
    public sealed class EnemyIntentLegendGlyph : MaskableGraphic
    {
        /// <summary>Gets the legend-owned copy of the accepted glyph geometry, or null before construction/disposal.</summary>
        public Mesh GlyphMesh { get; internal set; }

        /// <summary>Copies the accepted glyph vertices/triangles to the Canvas, without recreating action shapes.</summary>
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (GlyphMesh == null)
                return;
            foreach (Vector3 vertex in GlyphMesh.vertices)
                helper.AddVert(vertex, color, Vector2.zero);
            int[] triangles = GlyphMesh.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
                helper.AddTriangle(triangles[index], triangles[index + 1], triangles[index + 2]);
        }
    }
}