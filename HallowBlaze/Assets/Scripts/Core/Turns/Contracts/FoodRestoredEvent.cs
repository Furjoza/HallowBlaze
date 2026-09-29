using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Reports Food restored by collecting a board item.</summary>
    public sealed class FoodRestoredEvent : GameEvent
    {
        /// <summary>Gets the board-local player identifier.</summary>
        public EntityId PlayerId { get; }

        /// <summary>Gets the board-local source item identifier.</summary>
        public EntityId SourceItemId { get; }

        /// <summary>Gets the positive amount of Food restored.</summary>
        public int Amount { get; }

        /// <summary>Creates a Food restoration event.</summary>
        /// <param name="playerId">The player receiving Food.</param>
        /// <param name="sourceItemId">The collected item providing Food.</param>
        /// <param name="amount">The positive amount restored.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive.</exception>
        public FoodRestoredEvent(EntityId playerId, EntityId sourceItemId, int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            PlayerId = playerId;
            SourceItemId = sourceItemId;
            Amount = amount;
        }

        /// <inheritdoc />
        public override string EventType => "FoodRestored";
    }
}