using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a game event where an entity has moved from one position to another.
    /// </summary>
    public sealed class EntityMovedEvent : GameEvent
    {
        /// <summary>
        /// Gets the ID of the entity that moved.
        /// </summary>
        public EntityId EntityId { get; }

        /// <summary>
        /// Gets the position from which the entity moved.
        /// </summary>
        public GridPosition From { get; }

        /// <summary>
        /// Gets the position to which the entity moved.
        /// </summary>
        public GridPosition To { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EntityMovedEvent"/> class.
        /// </summary>
        /// <param name="entityId">The ID of the entity that moved.</param>
        /// <param name="from">The position from which the entity moved.</param>
        /// <param name="to">The position to which the entity moved.</param>
        public EntityMovedEvent(EntityId entityId, GridPosition from, GridPosition to)
        {
            EntityId = entityId;
            From = from;
            To = to;
        }

        /// <summary>
        /// Gets the type of the event.
        /// </summary>
        public override string EventType => "EntityMoved";
    }
}
