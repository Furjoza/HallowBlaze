using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Presentation.Runtime.Interaction
{
    /// <summary>Optional view-only audio/sprite adapter; it must not change wall HP, occupancy, charges or other rules.</summary>
    public interface IObstacleWorkFeedback
    {
        /// <summary>Presents the already-resolved manual or axe effect once at its ordered event position.</summary>
        void PresentWork(ObstacleWorkedEvent work);
    }

    /// <summary>Owns non-color progress labels and hides cleared views using domain state, never legacy wall HP.</summary>
    public sealed class ObstacleWorkPresenter : IDisposable
    {
        private readonly BoardRuntime runtime;
        private readonly Dictionary<EntityId, GameObject> labels = new Dictionary<EntityId, GameObject>();
        private bool disposed;

        /// <summary>Creates board-owned transient progress geometry without modifying or owning the borrowed runtime.</summary>
        public ObstacleWorkPresenter(BoardRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Root = new GameObject("Soft obstacle progress") { hideFlags = HideFlags.DontSave };
        }

        /// <summary>Gets the transient label owner; no gameplay data is stored in its children.</summary>
        public GameObject Root { get; }
        /// <summary>Gets whether the display lifetime has permanently ended.</summary>
        public bool IsDisposed => disposed;

        /// <summary>Applies ordered feedback to the matching view and refreshes authoritative progress without applying work again.</summary>
        public void Present(ObstacleWorkedEvent work)
        {
            if (disposed || runtime.IsDisposed) return;
            bool exists = runtime.BoardState.TryGetEntity(work.ObstacleId, out _);
            if (exists == work.IsCleared)
                throw new InvalidOperationException("Obstacle work event disagrees with authoritative occupancy.");
            if (!runtime.Views.TryGetValue(work.ObstacleId, out GameObject view) || view == null)
                throw new InvalidOperationException("The worked obstacle has no registered view.");
            foreach (MonoBehaviour component in view.GetComponents<MonoBehaviour>())
                if (component is IObstacleWorkFeedback feedback) feedback.PresentWork(work);
            view.SetActive(exists);
            Refresh();
        }

        /// <summary>Rebuilds labels from current work counters during recovery, never replaying audio or changing the board.</summary>
        public void Refresh()
        {
            if (disposed) return;
            if (runtime.IsDisposed || runtime.Controller.IsTerminal) { Dispose(); return; }
            foreach (GameObject label in labels.Values) Release(label);
            labels.Clear();
            foreach (BoardEntityState obstacle in runtime.BoardState.GetEntities())
            {
                int completed = runtime.BoardState.GetObstacleWork(obstacle.Id);
                if (completed == 0 || !runtime.ClearingRules.Supports(obstacle)) continue;
                var label = new GameObject("Clearing " + obstacle.Id) { hideFlags = HideFlags.DontSave };
                label.transform.SetParent(Root.transform, false);
                label.transform.position = new Vector3(obstacle.Position.X, obstacle.Position.Y + 0.24f, -0.2f);
                TextMesh text = label.AddComponent<TextMesh>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 48;
                text.characterSize = 0.06f;
                text.anchor = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.text = completed + "/" + runtime.ClearingRules.RequiredWork;
                MeshRenderer renderer = label.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = text.font.material;
                renderer.sortingLayerName = EnemyIntentView.GlyphSortingLayer;
                renderer.sortingOrder = EnemyIntentView.GlyphSortingOrder + 1;
                labels.Add(obstacle.Id, label);
            }
        }

        /// <summary>Releases only owned geometry; no progress or equipment is cleared.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            labels.Clear();
            Release(Root);
        }

        private static void Release(GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}