using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime.Interaction
{
    /// <summary>Displays the actual ground tool definition and charges, including the preserved old tool after an exchange.</summary>
    public sealed class GroundToolPresenter : IDisposable
    {
        private readonly BoardRuntime runtime;
        private readonly IReadOnlyDictionary<string, ToolDefinition> catalog;
        private readonly List<GameObject> labels = new List<GameObject>();
        private bool disposed;

        /// <summary>Creates a transient board-owned display; item views and authoritative ownership are borrowed.</summary>
        public GroundToolPresenter(BoardRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            catalog = ToolContentCatalog.Load();
            Root = new GameObject("Ground tool identities") { hideFlags = HideFlags.DontSave };
        }
        /// <summary>Gets the owned labels; source IDs are intentionally not presented as mechanical knowledge.</summary>
        public GameObject Root { get; }
        /// <summary>Gets whether this display lifetime has permanently ended.</summary>
        public bool IsDisposed => disposed;

        /// <summary>Refreshes labels from current concrete instances only, without allocating, equipping, collecting or spending charges.</summary>
        public void Refresh()
        {
            if (disposed) return;
            if (runtime.IsDisposed || runtime.Controller.IsTerminal) { Dispose(); return; }
            foreach (GameObject label in labels) Release(label);
            labels.Clear();
            foreach (BoardEntityState entity in runtime.BoardState.GetEntities())
            {
                if (!runtime.BoardState.TryGetGroundTool(entity.Id, out GroundToolState tool)) continue;
                if (!runtime.Views.TryGetValue(entity.Id, out GameObject view) || view == null)
                    throw new InvalidOperationException("Ground tool has no registered view: " + entity.Id);
                view.SetActive(true);
                var owner = new GameObject("Ground tool " + tool.InstanceId) { hideFlags = HideFlags.DontSave };
                owner.transform.SetParent(Root.transform, false);
                owner.transform.position = new Vector3(entity.Position.X, entity.Position.Y + 0.3f, -0.2f);
                TextMesh text = owner.AddComponent<TextMesh>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 48;
                text.characterSize = 0.035f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = Color.white;
                text.text = (catalog.TryGetValue(tool.DefinitionId, out ToolDefinition definition) ? definition.DisplayName : tool.DefinitionId) +
                    "\n" + tool.Charges + " charges";
                MeshRenderer renderer = owner.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = text.font.material;
                renderer.sortingLayerName = EnemyIntentView.GlyphSortingLayer;
                renderer.sortingOrder = EnemyIntentView.GlyphSortingOrder + 1;
                labels.Add(owner);
            }
        }

        /// <summary>Releases only display geometry; no tool or source is removed from the board.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            labels.Clear();
            Release(Root);
        }

        private static void Release(GameObject owner)
        {
            if (owner == null) return;
            owner.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(owner);
            else UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}