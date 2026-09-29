using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a game event where the player starved (ran out of food).
    /// </summary>
    public sealed class PlayerStarvedEvent : GameEvent
    {
        /// <summary>
        /// Gets the ID of the entity that starved.
        /// </summary>
        public EntityId EntityId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PlayerStarvedEvent"/> class.
        /// </summary>
        /// <param name="entityId">The ID of the entity that starved.</param>
        public PlayerStarvedEvent(EntityId entityId)
        {
            EntityId = entityId;
        }

        /// <summary>
        /// Gets the type of the event.
        /// </summary>
        public override string EventType => "PlayerStarved";
    }
}
