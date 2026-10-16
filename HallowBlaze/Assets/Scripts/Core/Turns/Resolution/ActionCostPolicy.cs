using System;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Immutable current board-action cost shared by command resolution and consequence previews.</summary>
    public sealed class ActionCostPolicy
    {
        /// <summary>Creates the active flat-cost policy; future cost tiers belong to their own contract.</summary>
        /// <param name="foodCost">Positive food cost for one accepted board command; the transitional default is one.</param>
        public ActionCostPolicy(int foodCost = 1)
        {
            if (foodCost <= 0) throw new ArgumentOutOfRangeException(nameof(foodCost));
            FoodCost = foodCost;
        }

        /// <summary>Gets the cost applied once after an accepted action's direct effects.</summary>
        public int FoodCost { get; }
    }
}