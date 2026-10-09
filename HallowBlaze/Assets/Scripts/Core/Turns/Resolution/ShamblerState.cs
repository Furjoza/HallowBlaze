using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Owns one enemy's board-local cadence and locked intent, independently of presentation.
    /// Position belongs to BoardState, not this state. Only consumption advances cadence;
    /// planning, reads, and rejected commands do not. This state is not persisted between boards.
    /// </summary>
    public sealed class ShamblerState
    {
        /// <summary>
        /// Gets the stable board-local identity whose intents this state accepts.
        /// </summary>
        public EntityId ActorId { get; }

        /// <summary>
        /// Gets the immutable rule configuration associated with this enemy.
        /// </summary>
        public ShamblerDefinition Definition { get; }

        /// <summary>
        /// Gets the current opportunity, initially active and unchanged until an intent is consumed.
        /// </summary>
        public ShamblerPhase Phase { get; private set; }

        /// <summary>
        /// Gets the exact retained intent, or null before planning, after consumption, or after reset.
        /// A locked intent cannot be replaced, including by another reference to the same intent.
        /// </summary>
        public EnemyIntent LockedIntent { get; private set; }

        /// <summary>
        /// Creates fresh board-local state with the definition's initial phase and no locked intent.
        /// No board or run state is read or mutated.
        /// </summary>
        /// <param name="actorId">The owning enemy identity; zero and negative values remain valid.</param>
        /// <param name="definition">Immutable configuration retained for this enemy.</param>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
        public ShamblerState(EntityId actorId, ShamblerDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            ActorId = actorId;
            Phase = definition.InitialPhase;
        }

        /// <summary>
        /// Retains one intent unchanged without advancing cadence or checking board legality.
        /// The actor must match this state, and a rest phase accepts only wait.
        /// Every rejected lock preserves the existing intent and phase.
        /// </summary>
        /// <param name="intent">The immutable plan to retain until the enemy phase executes.</param>
        /// <exception cref="ArgumentNullException"><paramref name="intent"/> is null.</exception>
        /// <exception cref="ArgumentException">The intent belongs to another actor or is not wait during rest.</exception>
        /// <exception cref="InvalidOperationException">An intent is already locked, even if it is the same instance.</exception>
        public void LockIntent(EnemyIntent intent)
        {
            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }
            if (!intent.ActorId.Equals(ActorId))
            {
                throw new ArgumentException("The intent must belong to this enemy.", nameof(intent));
            }
            if (LockedIntent != null)
            {
                throw new InvalidOperationException("The locked intent must be consumed before another is retained.");
            }
            if (Phase == ShamblerPhase.Rest && intent.Kind != EnemyIntentKind.Wait)
            {
                throw new ArgumentException("A resting Shambler can retain only a wait intent.", nameof(intent));
            }

            LockedIntent = intent;
        }

        /// <summary>
        /// Consumes the locked intent and advances the active/rest cycle exactly once.
        /// Call only when the enemy phase executes, including blocked actions, misses, and waits;
        /// rejected commands or terminal outcomes before that phase must not consume the intent.
        /// This operation performs no movement, damage, replanning, or event emission.
        /// </summary>
        /// <returns>The exact intent removed from this state.</returns>
        /// <exception cref="InvalidOperationException">
        /// No intent is locked, including duplicate consumption; the phase remains unchanged.
        /// </exception>
        public EnemyIntent ConsumeIntent()
        {
            if (LockedIntent == null)
            {
                throw new InvalidOperationException("There is no locked intent to consume.");
            }

            var intent = LockedIntent;
            LockedIntent = null;
            Phase = Phase == ShamblerPhase.Active ? ShamblerPhase.Rest : ShamblerPhase.Active;
            return intent;
        }

        /// <summary>
        /// Starts a fresh board lifecycle for the same actor and definition, discarding any old intent.
        /// Restores the initial active phase without executing an opportunity or mutating a board/run.
        /// Repeated resets have the same result; do not use reset as a mid-board planning operation.
        /// </summary>
        public void ResetForBoard()
        {
            LockedIntent = null;
            Phase = Definition.InitialPhase;
        }
    }
}