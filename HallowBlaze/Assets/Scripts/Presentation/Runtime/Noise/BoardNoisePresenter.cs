using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime.Noise
{
    /// <summary>Board-owned debug display of immutable hearing footprints, without audio, planning, or gameplay mutation.</summary>
    public sealed class BoardNoisePresenter : MonoBehaviour, IDisposable
    {
        private BoardState board;
        private Func<BoardState> currentBoard;
        private GameObject displayRoot;
        private Material material;

        /// <summary>Gets whether this display lifetime has permanently ended.</summary>
        public bool IsDisposed { get; private set; }
        /// <summary>Gets the exact immutable authoritative snapshots currently drawn.</summary>
        public IReadOnlyList<BoardNoise> Projections { get; private set; } = Array.Empty<BoardNoise>();

        /// <summary>Binds once to one board and its lifecycle identity provider; neither is owned or mutated.</summary>
        /// <param name="board">The authoritative board whose hearing snapshots are displayed.</param>
        /// <param name="activeBoard">Returns that same board while active, or null after replacement/terminal/disposal.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">Already bound or permanently disposed.</exception>
        public void Bind(BoardState board, Func<BoardState> activeBoard)
        {
            if (IsDisposed || this.board != null)
                throw new InvalidOperationException("A noise display belongs to exactly one board lifetime.");
            this.board = board ?? throw new ArgumentNullException(nameof(board));
            currentBoard = activeBoard ?? throw new ArgumentNullException(nameof(activeBoard));
        }

        /// <summary>Replaces geometry from current noise snapshots; stale ownership closes the display without reading a replacement board.</summary>
        /// <exception cref="InvalidOperationException">This open presenter has not been bound or rendering resources are unavailable.</exception>
        public void Refresh()
        {
            if (IsDisposed)
                return;
            if (board == null)
                throw new InvalidOperationException("Bind a noise display before publication.");
            if (!ReferenceEquals(currentBoard(), board))
            {
                Dispose();
                return;
            }
            Clear();
            IReadOnlyList<BoardNoise> noises = board.GetActiveNoises();
            if (noises.Count == 0)
                return;
            if (material == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    throw new InvalidOperationException("Noise markers require the sprite shader.");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            displayRoot = new GameObject("Audible footprints") { hideFlags = HideFlags.DontSave };
            displayRoot.transform.SetParent(transform, false);
            var sources = new Dictionary<GridPosition, int>();
            foreach (BoardNoise noise in noises)
            {
                var signal = new GameObject("Noise " + noise.Id) { hideFlags = HideFlags.DontSave };
                signal.transform.SetParent(displayRoot.transform, false);
                Color color = noise.Strength <= 2 ? new Color(0.2f, 1f, 0.85f, 0.85f) : new Color(1f, 0.85f, 0.2f, 0.85f);
                foreach (GridPosition cell in noise.AudibleCells)
                    DrawCell(signal.transform, cell, color);
                sources.TryGetValue(noise.Position, out int sourceSlot);
                sources[noise.Position] = sourceSlot + 1;
                var label = new GameObject("Strength and lifetime") { hideFlags = HideFlags.DontSave };
                label.transform.SetParent(signal.transform, false);
                label.transform.localPosition = new Vector3(noise.Position.X, noise.Position.Y + 0.3f - sourceSlot * 0.28f, -0.15f);
                TextMesh text = label.AddComponent<TextMesh>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 48;
                text.characterSize = 0.06f;
                text.anchor = TextAnchor.MiddleCenter;
                text.text = "S" + noise.Strength + " T" + noise.RemainingTurns;
                text.color = Color.white;
                MeshRenderer renderer = label.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = text.font.material;
                renderer.sortingLayerName = EnemyIntentView.GlyphSortingLayer;
                renderer.sortingOrder = EnemyIntentView.GlyphSortingOrder - 1;
            }
            Projections = noises;
        }

        private void DrawCell(Transform parent, GridPosition cell, Color color)
        {
            var instance = new GameObject("Audible " + cell) { hideFlags = HideFlags.DontSave };
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = new Vector3(cell.X, cell.Y, -0.12f);
            LineRenderer line = instance.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 4;
            line.SetPositions(new[] { new Vector3(-0.42f, -0.42f), new Vector3(0.42f, -0.42f),
                new Vector3(0.42f, 0.42f), new Vector3(-0.42f, 0.42f) });
            line.widthMultiplier = 0.025f;
            line.startColor = color;
            line.endColor = color;
            line.sortingLayerName = EnemyIntentView.GlyphSortingLayer;
            line.sortingOrder = EnemyIntentView.GlyphSortingOrder - 2;
        }

        /// <summary>Hides and releases only owned geometry, preserving the bound board and its noise.</summary>
        public void Clear()
        {
            if (displayRoot != null)
            {
                displayRoot.SetActive(false);
                Release(displayRoot);
                displayRoot = null;
            }
            Projections = Array.Empty<BoardNoise>();
        }

        /// <summary>Permanently closes the display, clearing geometry and material without changing gameplay state.</summary>
        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            Clear();
            if (material != null)
                Release(material);
            material = null;
            board = null;
            currentBoard = null;
        }

        private void OnDestroy() => Dispose();

        private static void Release(UnityEngine.Object instance)
        {
            if (Application.isPlaying)
                Destroy(instance);
            else
                DestroyImmediate(instance);
        }
    }
}