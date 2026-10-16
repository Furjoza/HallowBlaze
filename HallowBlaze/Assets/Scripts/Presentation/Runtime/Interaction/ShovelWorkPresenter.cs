using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.State;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime.Interaction
{
    /// <summary>Shows authored digging targets, manual cache progress and live pit lifetime/hold without mutating domain state.</summary>
    public sealed class ShovelWorkPresenter : IDisposable
    {
        private readonly BoardRuntime runtime;
        private readonly List<GameObject> visuals = new List<GameObject>();
        private Material pitMaterial;
        private bool disposed;

        /// <summary>Creates one transient board-owned overlay; all positions and numbers come from BoardState.</summary>
        public ShovelWorkPresenter(BoardRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Root = new GameObject("Digging progress and pits") { hideFlags = HideFlags.DontSave };
        }
        /// <summary>Gets the transient owner of non-color labels and pit outlines.</summary>
        public GameObject Root { get; }
        /// <summary>Gets whether the overlay has permanently released its lifetime.</summary>
        public bool IsDisposed => disposed;

        /// <summary>Rebuilds visible targets and current counters after setup, accepted commands or recovery; never replays rewards.</summary>
        public void Refresh()
        {
            if (disposed) return;
            if (runtime.IsDisposed || runtime.Controller.IsTerminal) { Dispose(); return; }
            foreach (GameObject visual in visuals) Release(visual);
            visuals.Clear();
            foreach (BoardEntityState entity in runtime.BoardState.GetEntities())
            {
                if (runtime.DiggingRules.IsCache(entity))
                    Label(entity.Position.X, entity.Position.Y + 0.3f, "CACHE " + runtime.BoardState.GetCacheWork(entity.Id) + "/" + runtime.DiggingRules.CacheWork);
                else if (runtime.DiggingRules.IsDiggable(entity) && !runtime.BoardState.TryGetPit(entity.Position, out _))
                    Label(entity.Position.X, entity.Position.Y - 0.3f, "DIG");
                else if (runtime.BoardState.TryGetCacheReward(entity.Id, out _))
                    Label(entity.Position.X, entity.Position.Y + 0.3f, "RATION");
            }
            foreach (BoardPit pit in runtime.BoardState.GetPits())
            {
                Label(pit.Position.X, pit.Position.Y - 0.3f, "PIT T" + pit.RemainingTurns + (pit.TrappedActorId.HasValue ? "\nWAIT" : ""));
                GameObject owner = Create("Visible pit outline");
                LineRenderer line = owner.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = 12;
                line.startWidth = line.endWidth = 0.045f;
                if (pitMaterial == null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader == null) throw new InvalidOperationException("Visible pits require the sprite shader.");
                    pitMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                line.sharedMaterial = pitMaterial;
                line.startColor = line.endColor = new Color(0.15f, 0.95f, 0.6f);
                line.sortingLayerName = EnemyIntentView.GlyphSortingLayer;
                line.sortingOrder = EnemyIntentView.GlyphSortingOrder;
                for (int point = 0; point < line.positionCount; point++)
                {
                    float angle = point * Mathf.PI * 2 / line.positionCount;
                    line.SetPosition(point, new Vector3(pit.Position.X + Mathf.Cos(angle) * 0.42f,
                        pit.Position.Y + Mathf.Sin(angle) * 0.23f, -0.1f));
                }
            }
        }

        /// <summary>Releases only owned visual objects, preserving pit, progress, tool and profile data.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            visuals.Clear();
            Release(Root);
            Release(pitMaterial);
            pitMaterial = null;
        }

        private void Label(float horizontal, float vertical, string value)
        {
            GameObject owner = Create(value);
            owner.transform.position = new Vector3(horizontal, vertical, -0.2f);
            TextMesh text = owner.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48;
            text.characterSize = 0.035f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.text = value;
            MeshRenderer renderer = owner.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = text.font.material;
            renderer.sortingLayerName = EnemyIntentView.GlyphSortingLayer;
            renderer.sortingOrder = EnemyIntentView.GlyphSortingOrder + 1;
        }

        private GameObject Create(string name)
        {
            var owner = new GameObject(name) { hideFlags = HideFlags.DontSave };
            owner.transform.SetParent(Root.transform, false);
            visuals.Add(owner);
            return owner;
        }

        private static void Release(UnityEngine.Object owner)
        {
            if (owner == null) return;
            if (owner is GameObject gameObject) gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(owner);
            else UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}