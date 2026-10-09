using System;
using System.Collections.Generic;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>
    /// Draws persistent prototype intent shapes without animation or gameplay access.
    /// Owns transient geometry/material and places glyphs from copied unit-grid cells, not entity transforms.
    /// </summary>
    public sealed class EnemyIntentView : MonoBehaviour
    {
        /// <summary>Fixed glyph footprint in unit-grid world units, independent of the selected action.</summary>
        public const float MarkerSize = 0.6f;
        /// <summary>Fixed line thickness in world units for every shape and conditional badge.</summary>
        public const float StrokeWidth = 0.04f;
        /// <summary>Default-layer sorting order above ordinary board sprites for prototype glyphs.</summary>
        public const int GlyphSortingOrder = 20;
        /// <summary>Presentation-owned world Z in front of board sprites at Z zero.</summary>
        public const float MarkerDepth = -0.1f;

        private readonly Dictionary<EnemyIntentSymbol, GameObject> shapes = new Dictionary<EnemyIntentSymbol, GameObject>();
        private Material material;
        private Transform glyph;
        private LineRenderer sourceCue;
        private LineRenderer sourceLink;

        /// <summary>Gets the immutable display value currently drawn, or null before publication.</summary>
        public EnemyIntentProjection Projection { get; private set; }
        /// <summary>Gets fixed glyph-local layout bounds; source cues and links lie outside this footprint.</summary>
        public Bounds LocalBounds => new Bounds(Vector3.zero, new Vector3(MarkerSize, MarkerSize, 0.1f));

        /// <summary>
        /// Selects a persistent shape synchronously, including the badge before player input.
        /// Places it at the exact copied target, or source for wait, and connects it to the copied source.
        /// Null input fails without replacing the current display; no occupancy or transform is queried.
        /// </summary>
        /// <param name="projection">Immutable display data; no planner or gameplay state is accessed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="projection"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The built-in sprite shader is unavailable.</exception>
        public void Show(EnemyIntentProjection projection)
        {
            if (projection == null)
                throw new ArgumentNullException(nameof(projection));
            InitializeGeometry();
            foreach (var shape in shapes)
                shape.Value.SetActive(shape.Key == projection.Symbol);
            GridPositionToWorld(projection.SourcePosition, out Vector3 source);
            GridPositionToWorld(projection.TargetPosition ?? projection.SourcePosition, out Vector3 target);
            transform.SetPositionAndRotation(target, Quaternion.identity);
            Vector3 sourceOffset = source - target;
            sourceCue.transform.localPosition = sourceOffset;
            sourceCue.enabled = projection.TargetPosition.HasValue;
            sourceLink.enabled = projection.TargetPosition.HasValue;
            sourceLink.SetPosition(0, sourceOffset);
            sourceLink.SetPosition(1, sourceOffset.normalized * (MarkerSize / 2f + StrokeWidth));
            glyph.localRotation = projection.Symbol == EnemyIntentSymbol.Move || projection.Symbol == EnemyIntentSymbol.ConditionalMove
                ? Quaternion.FromToRotation(Vector3.up, target - source) : Quaternion.identity;
            Projection = projection;
        }

        private void InitializeGeometry()
        {
            if (material != null)
                return;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                throw new InvalidOperationException("Intent glyphs require the built-in sprite shader.");
            material = new Material(shader) { name = "Intent glyph material", hideFlags = HideFlags.HideAndDontSave };
            var glyphObject = new GameObject("Glyph") { hideFlags = HideFlags.DontSave };
            glyphObject.transform.SetParent(transform, false);
            glyph = glyphObject.transform;
            sourceCue = DrawLine(gameObject, "SourceCue", true, new Vector3(-0.08f, -0.08f),
                new Vector3(0.08f, -0.08f), new Vector3(0.08f, 0.08f), new Vector3(-0.08f, 0.08f));
            sourceLink = DrawLine(gameObject, "SourceLink", false, Vector3.zero, Vector3.zero);
            sourceCue.enabled = false;
            sourceLink.enabled = false;

            GameObject move = CreateShape(EnemyIntentSymbol.Move);
            DrawArrow(move);
            GameObject conditional = CreateShape(EnemyIntentSymbol.ConditionalMove);
            DrawArrow(conditional);
            var badge = new GameObject("AttackOnPlayerEntry") { hideFlags = HideFlags.DontSave };
            badge.transform.SetParent(conditional.transform, false);
            badge.transform.localPosition = new Vector3(0.18f, -0.14f, 0);
            DrawLine(badge, "BadgeCross1", false, new Vector3(-0.055f, -0.055f), new Vector3(0.055f, 0.055f));
            DrawLine(badge, "BadgeCross2", false, new Vector3(-0.055f, 0.055f), new Vector3(0.055f, -0.055f));

            GameObject attack = CreateShape(EnemyIntentSymbol.Attack);
            DrawLine(attack, "Cross1", false, new Vector3(-0.22f, -0.22f), new Vector3(0.22f, 0.22f));
            DrawLine(attack, "Cross2", false, new Vector3(-0.22f, 0.22f), new Vector3(0.22f, -0.22f));
            GameObject investigate = CreateShape(EnemyIntentSymbol.Investigate);
            var circle = new Vector3[16];
            for (int index = 0; index < circle.Length; index++)
            {
                float angle = index * 2f * Mathf.PI / circle.Length;
                circle[index] = new Vector3(-0.04f + 0.17f * Mathf.Cos(angle), 0.04f + 0.17f * Mathf.Sin(angle));
            }
            DrawLine(investigate, "Lens", true, circle);
            DrawLine(investigate, "Handle", false, new Vector3(0.08f, -0.08f), new Vector3(0.24f, -0.24f));
            GameObject wait = CreateShape(EnemyIntentSymbol.Wait);
            DrawLine(wait, "PauseLeft", false, new Vector3(-0.1f, -0.24f), new Vector3(-0.1f, 0.24f));
            DrawLine(wait, "PauseRight", false, new Vector3(0.1f, -0.24f), new Vector3(0.1f, 0.24f));
        }

        private GameObject CreateShape(EnemyIntentSymbol symbol)
        {
            var shape = new GameObject(symbol.ToString()) { hideFlags = HideFlags.DontSave };
            shape.transform.SetParent(glyph, false);
            shape.SetActive(false);
            shapes.Add(symbol, shape);
            return shape;
        }

        private void DrawArrow(GameObject parent)
        {
            DrawLine(parent, "Shaft", false, new Vector3(0, -0.24f), new Vector3(0, 0.24f));
            DrawLine(parent, "Head", false, new Vector3(-0.18f, 0.06f), new Vector3(0, 0.24f), new Vector3(0.18f, 0.06f));
        }

        private LineRenderer DrawLine(GameObject parent, string name, bool loop, params Vector3[] positions)
        {
            var instance = new GameObject(name) { hideFlags = HideFlags.DontSave };
            instance.transform.SetParent(parent.transform, false);
            LineRenderer line = instance.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = loop;
            line.positionCount = positions.Length;
            line.SetPositions(positions);
            line.widthMultiplier = StrokeWidth;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.startColor = Color.white;
            line.endColor = Color.white;
            line.sortingOrder = GlyphSortingOrder;
            return line;
        }

        private static void GridPositionToWorld(HallowBlaze.Core.Board.Primitives.GridPosition position, out Vector3 world)
        {
            world = new Vector3(position.X, position.Y, MarkerDepth);
        }

        private void OnDestroy()
        {
            if (material == null)
                return;
            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }
    }
}