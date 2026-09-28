using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a player command to interact with a target entity.
    /// </summary>
    public sealed class InteractCommand : PlayerCommand
    {
        /// <summary>
        /// Gets the ID of the target entity to interact with.
        /// </summary>
        public EntityId TargetId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InteractCommand"/> class.
        /// </summary>
        /// <param name="targetId">The ID of the target entity.</param>
        public InteractCommand(EntityId targetId)
        {
            TargetId = targetId;
        }

        /// <summary>
        /// Gets the type of the command.
        /// </summary>
        public override string CommandType => "Interact";
    }
}
