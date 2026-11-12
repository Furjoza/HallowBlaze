using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Reports actual positive healing committed by one consumed medical item, never a requested or wasted amount.</summary>
    public sealed class HealthRestoredEvent : GameEvent
    {
        /// <summary>Gets the board-local player that received healing.</summary>
        public EntityId PlayerId { get; }
        /// <summary>Gets the exact consumed board-local medical item.</summary>
        public EntityId SourceItemId { get; }
        /// <summary>Gets Health actually restored after the item's own cap was applied.</summary>
        public int Amount { get; }

        /// <summary>Captures an already-applied medical effect without owning mutable state.</summary>
        /// <param name="playerId">The healed player.</param>
        /// <param name="sourceItemId">The medical item consumed exactly once.</param>
        /// <param name="amount">The positive applied increase, excluding wasted excess.</param>
        /// <exception cref="ArgumentOutOfRangeException">The applied increase is not positive.</exception>
        public HealthRestoredEvent(EntityId playerId, EntityId sourceItemId, int amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            PlayerId = playerId;
            SourceItemId = sourceItemId;
            Amount = amount;
        }

        /// <inheritdoc />
        public override string EventType => "HealthRestored";
    }
}