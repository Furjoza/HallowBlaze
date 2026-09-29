using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Reports player death from depleted health or an explicit dead run state.</summary>
    public sealed class PlayerDiedEvent : GameEvent
    {
        /// <summary>Gets the board-local identifier of the player who died.</summary>
        public EntityId EntityId { get; }

        /// <summary>Records a non-starvation death for ordered presentation.</summary>
        /// <param name="entityId">The board-local player identifier.</param>
        public PlayerDiedEvent(EntityId entityId)
        {
            EntityId = entityId;
        }

        /// <inheritdoc />
        public override string EventType => "PlayerDied";
    }
}
