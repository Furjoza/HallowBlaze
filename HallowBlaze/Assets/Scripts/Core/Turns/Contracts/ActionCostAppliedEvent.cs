using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Reports the selected satiety cost, retained from command start and paid after direct effects.
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

        /// <summary>Gets the pre-effect satiety that selected the tier; null only for manually constructed legacy events.</summary>
        public int? SatietyAtCommandStart { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCostAppliedEvent"/> class.
        /// </summary>
        /// <param name="entityId">The ID of the entity that incurred the cost.</param>
        /// <param name="costAmount">The amount of cost applied. Must be greater than 0.</param>
        /// <param name="satietyAtCommandStart">Pre-effect resource snapshot used for tier selection.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="costAmount"/> must be greater than 0.</exception>
        public ActionCostAppliedEvent(EntityId entityId, int costAmount, int? satietyAtCommandStart = null)
        {
            if (costAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(costAmount), "Cost must be greater than 0.");
            if (satietyAtCommandStart < 0) throw new ArgumentOutOfRangeException(nameof(satietyAtCommandStart));
            EntityId = entityId;
            CostAmount = costAmount;
            SatietyAtCommandStart = satietyAtCommandStart;
        }

        /// <summary>
        /// Gets the type of the event.
        /// </summary>
        public override string EventType => "ActionCostApplied";
    }
}
