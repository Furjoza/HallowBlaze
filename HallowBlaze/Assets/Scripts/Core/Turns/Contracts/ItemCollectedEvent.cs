using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Reports that a player removed an automatically collected item from the board.</summary>
    public sealed class ItemCollectedEvent : GameEvent
    {
        /// <summary>Gets the board-local player identifier.</summary>
        public EntityId PlayerId { get; }

        /// <summary>Gets the board-local collected item identifier.</summary>
        public EntityId ItemId { get; }

        /// <summary>Creates an automatic item collection event.</summary>
        /// <param name="playerId">The player that collected the item.</param>
        /// <param name="itemId">The item removed from the board.</param>
        public ItemCollectedEvent(EntityId playerId, EntityId itemId)
        {
            PlayerId = playerId;
            ItemId = itemId;
        }

        /// <inheritdoc />
        public override string EventType => "ItemCollected";
    }
}