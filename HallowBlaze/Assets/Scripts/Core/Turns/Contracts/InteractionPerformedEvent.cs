using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a game event where an interaction was performed between two entities.
    /// </summary>
    public sealed class InteractionPerformedEvent : GameEvent
    {
        /// <summary>
        /// Gets the ID of the actor (entity performing the interaction).
        /// </summary>
        public EntityId ActorId { get; }

        /// <summary>
        /// Gets the ID of the target entity being interacted with.
        /// </summary>
        public EntityId TargetId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InteractionPerformedEvent"/> class.
        /// </summary>
        /// <param name="actorId">The ID of the actor performing the interaction.</param>
        /// <param name="targetId">The ID of the target entity.</param>
        public InteractionPerformedEvent(EntityId actorId, EntityId targetId)
        {
            ActorId = actorId;
            TargetId = targetId;
        }

        /// <summary>
        /// Gets the type of the event.
        /// </summary>
        public override string EventType => "InteractionPerformed";
    }
}
