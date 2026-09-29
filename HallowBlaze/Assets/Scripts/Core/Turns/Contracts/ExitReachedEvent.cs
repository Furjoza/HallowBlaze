using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a game event where the player reached an exit.
    /// </summary>
    public sealed class ExitReachedEvent : GameEvent
    {
        /// <summary>Gets the ID of the player that reached the exit.</summary>
        public EntityId PlayerId { get; }

        /// <summary>Gets the board-local ID of the reached exit.</summary>
        public EntityId ExitId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExitReachedEvent"/> class.
        /// </summary>
        /// <param name="playerId">The ID of the player that reached the exit.</param>
        /// <param name="exitId">The board-local ID of the reached exit.</param>
        public ExitReachedEvent(EntityId playerId, EntityId exitId)
        {
            PlayerId = playerId;
            ExitId = exitId;
        }

        /// <summary>
        /// Gets the type of the event.
        /// </summary>
        public override string EventType => "ExitReached";
    }
}
