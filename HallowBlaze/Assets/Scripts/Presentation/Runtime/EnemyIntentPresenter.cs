using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Resolution;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>
    /// Publishes detached locked-intent display batches only on explicit calls, never by polling or planning.
    /// Owns transient child views; the board and enemy states are borrowed only during publication.
    /// </summary>
    public sealed class EnemyIntentPresenter : MonoBehaviour, IDisposable
    {
        private GameObject displayRoot;
        private BoardState boundBoard;
        private Func<BoardState> currentBoard;
        private bool isDisposed;

        /// <summary>Gets whether explicit disposal, stale publication, or component destruction permanently ended this display lifetime.</summary>
        public bool IsDisposed => isDisposed;
        /// <summary>Gets the last complete immutable display batch in stable ascending source-ID order.</summary>
        public IReadOnlyList<EnemyIntentProjection> Projections { get; private set; } = Array.Empty<EnemyIntentProjection>();
        /// <summary>Gets a read-only map of owned current markers; callers must not destroy or reconfigure them.</summary>
        public IReadOnlyDictionary<EntityId, EnemyIntentView> Views { get; private set; } =
            new ReadOnlyDictionary<EntityId, EnemyIntentView>(new Dictionary<EntityId, EnemyIntentView>());

        /// <summary>
        /// Binds this owner once to one exact board instance and an explicit active-board identity provider.
        /// Identity is checked on publication, not polled. A closed or already bound presenter cannot be reused.
        /// The board/provider are borrowed and released, never disposed or mutated, when this owner ends.
        /// </summary>
        /// <param name="board">The one authoritative board instance this presenter may display.</param>
        /// <param name="activeBoard">Returns the current board instance, or null after terminal/disposal ownership ends.</param>
        /// <exception cref="ArgumentNullException">The board or identity provider is null.</exception>
        /// <exception cref="ObjectDisposedException">This display lifetime has ended.</exception>
        /// <exception cref="InvalidOperationException">This presenter was already bound.</exception>
        public void Bind(BoardState board, Func<BoardState> activeBoard)
        {
            EnsureOpen();
            if (board == null)
                throw new ArgumentNullException(nameof(board));
            if (activeBoard == null)
                throw new ArgumentNullException(nameof(activeBoard));
            if (boundBoard != null)
                throw new InvalidOperationException("An intent presenter belongs to exactly one board lifetime.");
            boundBoard = board;
            currentBoard = activeBoard;
        }

        /// <summary>
        /// Copies the supplied retained plans and authoritative source cells, prepares the whole hidden batch,
        /// then replaces the display synchronously. Invalid input or geometry preparation preserves the old batch.
        /// Later domain changes have no display effect until another explicit call. Empty input publishes no markers.
        /// No intent is consumed, replaced, executed, or planned, and no board or run state is mutated.
        /// </summary>
        /// <param name="board">Borrowed board used only to copy the current enemy source cells.</param>
        /// <param name="enemies">Unique non-null states with retained intents; enumeration order is irrelevant.</param>
        /// <exception cref="ArgumentNullException">The board or enemy collection is null.</exception>
        /// <exception cref="ArgumentException">States are duplicated, missing a retained plan/source, or cannot use shared-cell geometry.</exception>
        /// <exception cref="ObjectDisposedException">This display lifetime has ended.</exception>
        /// <exception cref="InvalidOperationException">The presenter is unbound/stale or required glyph rendering is unavailable.</exception>
        public void Publish(BoardState board, IEnumerable<ShamblerState> enemies)
        {
            EnsureOpen();
            if (board == null)
                throw new ArgumentNullException(nameof(board));
            if (boundBoard == null)
                throw new InvalidOperationException("Bind an active board before publishing intents.");
            if (!ReferenceEquals(currentBoard(), boundBoard))
            {
                Dispose();
                throw new InvalidOperationException("The owning board is no longer active; this presenter is closed.");
            }
            if (!ReferenceEquals(board, boundBoard))
                throw new ArgumentException("Publication must use the exact bound board instance.", nameof(board));
            IReadOnlyList<ShamblerState> ordered = EnemyInitiativeOrder.Create(enemies);
            var projections = new List<EnemyIntentProjection>(ordered.Count);
            var targets = new Dictionary<GridPosition, int>();
            foreach (ShamblerState enemy in ordered)
            {
                if (enemy.LockedIntent == null || !board.TryGetEntity(enemy.ActorId, out BoardEntityState entity) ||
                    entity.Definition.Layer != BoardLayer.Actor || !entity.Definition.Kind.Equals(EntityKind.Enemy))
                    throw new ArgumentException("Every source must be a board enemy with one retained intent.", nameof(enemies));
                EnemyIntentProjection projection = EnemyIntentProjection.FromRetainedIntent(enemy.LockedIntent, entity.Position);
                projections.Add(projection);
                if (projection.TargetPosition.HasValue)
                {
                    GridPosition target = projection.TargetPosition.Value;
                    targets.TryGetValue(target, out int count);
                    targets[target] = count + 1;
                }
            }

            var nextRoot = new GameObject("Locked intent display") { hideFlags = HideFlags.DontSave };
            nextRoot.SetActive(false);
            nextRoot.transform.SetParent(transform, false);
            var nextViews = new Dictionary<EntityId, EnemyIntentView>();
            try
            {
                foreach (EnemyIntentProjection projection in projections)
                {
                    var instance = new GameObject("Intent source " + projection.SourceId) { hideFlags = HideFlags.DontSave };
                    instance.transform.SetParent(nextRoot.transform, false);
                    EnemyIntentView view = instance.AddComponent<EnemyIntentView>();
                    bool shared = projection.TargetPosition.HasValue && targets[projection.TargetPosition.Value] > 1;
                    view.Show(projection, shared);
                    nextViews.Add(projection.SourceId, view);
                }
            }
            catch
            {
                ReleaseMaterials(nextRoot);
                Release(nextRoot);
                throw;
            }

            GameObject previous = displayRoot;
            displayRoot = nextRoot;
            Projections = projections.AsReadOnly();
            Views = new ReadOnlyDictionary<EntityId, EnemyIntentView>(nextViews);
            if (previous != null)
                previous.SetActive(false);
            nextRoot.SetActive(true);
            if (previous != null)
            {
                ReleaseMaterials(previous);
                Release(previous);
            }
        }

        /// <summary>
        /// Hides and releases only owned markers/materials synchronously, with deferred Unity destruction in PlayMode.
        /// Repeated calls are harmless, including after disposal. A still-active bound owner may explicitly republish.
        /// </summary>
        public void Clear()
        {
            if (displayRoot != null)
            {
                displayRoot.SetActive(false);
                ReleaseMaterials(displayRoot);
                Release(displayRoot);
                displayRoot = null;
            }
            Projections = Array.Empty<EnemyIntentProjection>();
            Views = new ReadOnlyDictionary<EntityId, EnemyIntentView>(new Dictionary<EntityId, EnemyIntentView>());
        }

        /// <summary>
        /// Permanently closes this board's display, clears owned UI, and rejects future publication or rebinding.
        /// Call at terminal/replacement/disposal ownership boundaries; borrowed runtime, state, and views remain intact.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
                return;
            isDisposed = true;
            Clear();
            boundBoard = null;
            currentBoard = null;
        }

        private void EnsureOpen()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(EnemyIntentPresenter));
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private static void ReleaseMaterials(GameObject root)
        {
            var materials = new HashSet<Material>();
            foreach (LineRenderer line in root.GetComponentsInChildren<LineRenderer>(true))
                if (line.sharedMaterial != null)
                    materials.Add(line.sharedMaterial);
            foreach (Material material in materials)
                if (Application.isPlaying)
                    Destroy(material);
                else
                    DestroyImmediate(material);
        }

        private static void Release(GameObject instance)
        {
            if (Application.isPlaying)
                Destroy(instance);
            else
                DestroyImmediate(instance);
        }
    }
}