using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Selects one action on a stable board entity. Preview never transfers ownership;
    /// the resolver revalidates the selection before applying any effect or cost.
    /// </summary>
    public sealed class InteractCommand : PlayerCommand
    {
        /// <summary>
        /// Gets the ID of the target entity to interact with.
        /// </summary>
        public EntityId TargetId { get; }

        /// <summary>Gets the stable action identifier; legacy commands select the contextual interaction.</summary>
        public string ActionId { get; }

        /// <summary>Gets the selected position, or null for legacy callers without a query snapshot.</summary>
        public GridPosition? TargetPosition { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InteractCommand"/> class.
        /// </summary>
        /// <param name="targetId">The ID of the target entity.</param>
        public InteractCommand(EntityId targetId)
        {
            TargetId = targetId;
            ActionId = "interact";
        }

        /// <summary>Captures an explicit query selection without changing the target or spending a turn.</summary>
        /// <param name="targetId">The exact board-local entity, not a definition or Unity instance ID.</param>
        /// <param name="targetPosition">The position shown during selection; displacement invalidates the command.</param>
        /// <param name="actionId">The registered action selected by the player.</param>
        /// <exception cref="ArgumentException">The action identifier is empty or has surrounding whitespace.</exception>
        public InteractCommand(EntityId targetId, GridPosition targetPosition, string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId) || actionId != actionId.Trim())
                throw new ArgumentException("An interaction requires a stable action identifier.", nameof(actionId));
            TargetId = targetId;
            TargetPosition = targetPosition;
            ActionId = actionId;
        }

        /// <summary>
        /// Gets the type of the command.
        /// </summary>
        public override string CommandType => "Interact";
    }
}
