using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a game event where an action cost (e.g., food) was applied.
    /// </summary>
    public sealed class ActionCostAppliedEvent : GameEvent
    {
        /// <summary>
        /// Gets the ID of the entity that incurred the cost.
        /// </summary>
        public EntityId EntityId { get; }

        /// <summary>
        /// Gets the amount of cost applied.
        /// </summary>
        public int CostAmount { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCostAppliedEvent"/> class.
        /// </summary>
        /// <param name="entityId">The ID of the entity that incurred the cost.</param>
        /// <param name="costAmount">The amount of cost applied. Must be greater than 0.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="costAmount"/> must be greater than 0.</exception>
        public ActionCostAppliedEvent(EntityId entityId, int costAmount)
        {
            if (costAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(costAmount), "Cost must be greater than 0.");
            EntityId = entityId;
            CostAmount = costAmount;
        }

        /// <summary>
        /// Gets the type of the event.
        /// </summary>
        public override string EventType => "ActionCostApplied";
    }
}
