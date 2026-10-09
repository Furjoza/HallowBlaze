using System;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Replays an already-resolved fixed-cell attack without movement, damage application, or target selection.</summary>
    public sealed class EnemyAttackPresenter : IDisposable
    {
        private readonly BoardRuntime runtime;
        private readonly IRunHud hud;
        private readonly ITurnFeedback feedback;
        private Material material;
        private bool disposed;

        /// <summary>Gets or sets whether the outcome glyph pulses; both modes retain the same fixed-cell feedback.</summary>
        public bool AnimationsEnabled { get; set; } = true;
        /// <summary>Gets finite nonnegative pulse duration in unscaled seconds.</summary>
        public float Duration { get; }
        /// <summary>Gets the owned current hit/miss feedback, or null before replay, after cancellation, or after disposal.</summary>
        public GameObject TargetFeedback { get; private set; }

        /// <summary>Composes an isolated reusable handler borrowing the runtime and current-value feedback sinks.</summary>
        /// <param name="runtime">Borrowed authoritative state and registered views; never disposed by this helper.</param>
        /// <param name="hud">Receives current health/food, not event deltas.</param>
        /// <param name="feedback">Receives the original immutable attack event after its visual replay.</param>
        /// <param name="duration">Finite nonnegative pulse duration in unscaled seconds.</param>
        public EnemyAttackPresenter(BoardRuntime runtime, IRunHud hud, ITurnFeedback feedback, float duration = 0.1f)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.hud = hud ?? throw new ArgumentNullException(nameof(hud));
            this.feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            Duration = duration;
        }

        /// <summary>
        /// Shows a cross for a recorded hit or an empty ring for a recorded miss at the event's exact cell.
        /// The source link uses its authoritative cell, never an interpolated transform or a replacement target.
        /// Registered source/affected views must exist. Errors and cancellation propagate to the owning dispatcher;
        /// no recovery, resolver call, health change, or domain movement is performed here.
        /// </summary>
        /// <param name="gameEvent">The actual resolved outcome, including clamped damage and the fixed attempted cell.</param>
        /// <param name="token">Cooperative cancellation; partially replayed geometry is cleared on failure.</param>
        /// <returns>Completion after visual replay and current-value HUD/original-event feedback.</returns>
        public async Task ReplayAsync(EnemyAttackResolvedEvent gameEvent, CancellationToken token)
        {
            if (gameEvent == null)
                throw new ArgumentNullException(nameof(gameEvent));
            EnsureActive();
            token.ThrowIfCancellationRequested();
            RequireView(gameEvent.AttackerId);
            if (gameEvent.AffectedTargetId.HasValue)
                RequireView(gameEvent.AffectedTargetId.Value);
            if (!runtime.BoardState.TryGetEntity(gameEvent.AttackerId, out BoardEntityState source))
                throw new InvalidOperationException("Attack source is absent from the authoritative board.");
            try
            {
                ClearFeedback();
                Vector3 target = new Vector3(gameEvent.TargetPosition.X, gameEvent.TargetPosition.Y, EnemyIntentView.MarkerDepth);
                TargetFeedback = new GameObject(gameEvent.IsHit ? "Enemy attack hit" : "Enemy attack miss") { hideFlags = HideFlags.DontSave };
                TargetFeedback.transform.position = target;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    throw new InvalidOperationException("Attack feedback requires the built-in sprite shader.");
                material = new Material(shader) { name = "Enemy attack feedback material", hideFlags = HideFlags.HideAndDontSave };
                var glyph = new GameObject("Outcome") { hideFlags = HideFlags.DontSave };
                glyph.transform.SetParent(TargetFeedback.transform, false);
                if (gameEvent.IsHit)
                {
                    DrawLine(glyph, false, new Vector3(-0.22f, -0.22f), new Vector3(0.22f, 0.22f));
                    DrawLine(glyph, false, new Vector3(-0.22f, 0.22f), new Vector3(0.22f, -0.22f));
                }
                else
                {
                    var ring = new Vector3[16];
                    for (int index = 0; index < ring.Length; index++)
                    {
                        float angle = index * 2f * Mathf.PI / ring.Length;
                        ring[index] = new Vector3(0.23f * Mathf.Cos(angle), 0.23f * Mathf.Sin(angle));
                    }
                    DrawLine(glyph, true, ring);
                }
                Vector3 sourceOffset = new Vector3(source.Position.X, source.Position.Y, EnemyIntentView.MarkerDepth) - target;
                DrawLine(TargetFeedback, false, sourceOffset, sourceOffset.normalized * 0.34f);
                if (AnimationsEnabled && Duration > 0)
                {
                    float elapsed = 0;
                    while (elapsed < Duration)
                    {
                        await Task.Yield();
                        token.ThrowIfCancellationRequested();
                        EnsureActive();
                        if (glyph == null)
                            throw new InvalidOperationException("Attack feedback was removed during replay.");
                        elapsed += Time.unscaledDeltaTime;
                        glyph.transform.localScale = Vector3.one * (1 + 0.1f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(elapsed / Duration)));
                    }
                }
                token.ThrowIfCancellationRequested();
                EnsureActive();
                glyph.transform.localScale = Vector3.one;
                hud.Refresh(runtime.RunState.Health, runtime.RunState.Food);
                feedback.ShowEvent(gameEvent);
            }
            catch
            {
                ClearFeedback();
                throw;
            }
        }

        /// <summary>Releases only owned outcome geometry/materials, preserving all borrowed runtime/state/view references.</summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            ClearFeedback();
        }

        private void EnsureActive()
        {
            if (disposed || runtime.IsDisposed)
                throw new ObjectDisposedException(nameof(EnemyAttackPresenter));
        }

        private void RequireView(EntityId id)
        {
            if (!runtime.Views.TryGetValue(id, out GameObject view) || view == null)
                throw new InvalidOperationException("Registered attack view is missing: " + id);
        }

        private void DrawLine(GameObject parent, bool loop, params Vector3[] positions)
        {
            var instance = new GameObject("Attack feedback stroke") { hideFlags = HideFlags.DontSave };
            instance.transform.SetParent(parent.transform, false);
            LineRenderer line = instance.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.positionCount = positions.Length;
            line.SetPositions(positions);
            line.loop = loop;
            line.widthMultiplier = EnemyIntentView.StrokeWidth;
            line.startColor = Color.white;
            line.endColor = Color.white;
            line.sortingOrder = EnemyIntentView.GlyphSortingOrder + 1;
        }

        private void ClearFeedback()
        {
            if (TargetFeedback != null)
            {
                TargetFeedback.SetActive(false);
                Release(TargetFeedback);
                TargetFeedback = null;
            }
            if (material != null)
            {
                Release(material);
                material = null;
            }
        }

        private static void Release(UnityEngine.Object instance)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(instance);
            else
                UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}