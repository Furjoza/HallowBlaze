using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Reports applied healing and discarded excess committed by one consumed medical item.</summary>
    public sealed class HealthRestoredEvent : GameEvent
    {
        /// <summary>Gets the board-local player that received healing.</summary>
        public EntityId PlayerId { get; }
        /// <summary>Gets the exact consumed board-local medical item.</summary>
        public EntityId SourceItemId { get; }
        /// <summary>Gets Health actually restored after the item's own cap was applied.</summary>
        public int Amount { get; }
        /// <summary>Gets the requested healing discarded by the configured health cap.</summary>
        public int Wasted { get; }
        /// <summary>Gets the original requested healing.</summary>
        public int Requested => Amount + Wasted;

        /// <summary>Captures an already-applied medical effect without owning mutable state.</summary>
        /// <param name="playerId">The healed player.</param>
        /// <param name="sourceItemId">The medical item consumed exactly once.</param>
        /// <param name="amount">The nonnegative applied increase.</param>
        /// <param name="wasted">Nonnegative discarded excess; the requested total must be positive.</param>
        /// <exception cref="ArgumentOutOfRangeException">An amount is invalid or the requested total overflows.</exception>
        public HealthRestoredEvent(EntityId playerId, EntityId sourceItemId, int amount, int wasted = 0)
        {
            if (amount < 0 || wasted < 0 || amount > int.MaxValue - wasted || amount == 0 && wasted == 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            PlayerId = playerId;
            SourceItemId = sourceItemId;
            Amount = amount;
            Wasted = wasted;
        }

        /// <inheritdoc />
        public override string EventType => "HealthRestored";
    }
}