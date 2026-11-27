using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Reports capped satiety and wasted excess from the transitional automatic-food adapter.</summary>
    public sealed class FoodRestoredEvent : GameEvent
    {
        /// <summary>Gets the board-local player identifier.</summary>
        public EntityId PlayerId { get; }

        /// <summary>Gets the board-local source item identifier.</summary>
        public EntityId SourceItemId { get; }

        /// <summary>Gets satiety actually restored, including zero when the entire reward is wasted.</summary>
        public int Amount { get; }

        /// <summary>Gets the discarded excess, never stored in another resource.</summary>
        public int Wasted { get; }
        /// <summary>Gets the requested reward before applying the configured cap.</summary>
        public int Requested => Amount + Wasted;

        /// <summary>Captures a completed satiety restoration; historical type name is retained for the adapter.</summary>
        /// <param name="playerId">The player receiving satiety.</param>
        /// <param name="sourceItemId">The collected item providing satiety.</param>
        /// <param name="amount">The nonnegative applied amount.</param>
        /// <param name="wasted">Nonnegative discarded excess; the total requested reward must be positive.</param>
        /// <exception cref="ArgumentOutOfRangeException">An amount is invalid or the requested total would overflow.</exception>
        public FoodRestoredEvent(EntityId playerId, EntityId sourceItemId, int amount, int wasted = 0)
        {
            if (amount < 0 || wasted < 0 || amount > int.MaxValue - wasted || amount == 0 && wasted == 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            PlayerId = playerId;
            SourceItemId = sourceItemId;
            Amount = amount;
            Wasted = wasted;
        }

        /// <inheritdoc />
        public override string EventType => "FoodRestored";
    }
}