using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a game event where an entity waited (skipped a turn).
    /// </summary>
    public sealed class EntityWaitedEvent : GameEvent
    {
        /// <summary>
        /// Gets the ID of the entity that waited.
        /// </summary>
        public EntityId EntityId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EntityWaitedEvent"/> class.
        /// </summary>
        /// <param name="entityId">The ID of the entity that waited.</param>
        public EntityWaitedEvent(EntityId entityId)
        {
            EntityId = entityId;
        }

        /// <summary>
        /// Gets the type of the event.
        /// </summary>
        public override string EventType => "EntityWaited";
    }
}
