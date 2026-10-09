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
    public sealed class EnemyIntentPresenter : MonoBehaviour
    {
        private GameObject displayRoot;

        /// <summary>Gets the last complete immutable display batch in stable ascending source-ID order.</summary>
        public IReadOnlyList<EnemyIntentProjection> Projections { get; private set; } = Array.Empty<EnemyIntentProjection>();
        /// <summary>Gets a read-only map of owned current markers; callers must not destroy or reconfigure them.</summary>
        public IReadOnlyDictionary<EntityId, EnemyIntentView> Views { get; private set; } =
            new ReadOnlyDictionary<EntityId, EnemyIntentView>(new Dictionary<EntityId, EnemyIntentView>());

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
        /// <exception cref="InvalidOperationException">Required runtime glyph rendering is unavailable.</exception>
        public void Publish(BoardState board, IEnumerable<ShamblerState> enemies)
        {
            if (board == null)
                throw new ArgumentNullException(nameof(board));
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
                var preparedMaterials = new HashSet<Material>();
                foreach (LineRenderer line in nextRoot.GetComponentsInChildren<LineRenderer>(true))
                    if (line.sharedMaterial != null)
                        preparedMaterials.Add(line.sharedMaterial);
                foreach (Material prepared in preparedMaterials)
                    if (Application.isPlaying)
                        Destroy(prepared);
                    else
                        DestroyImmediate(prepared);
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
                Release(previous);
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