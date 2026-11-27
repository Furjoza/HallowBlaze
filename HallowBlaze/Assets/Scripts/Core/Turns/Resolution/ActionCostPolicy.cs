using System;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Samples the run's persisted pre-command cost tiers, shared by previews and command resolution.</summary>
    public sealed class ActionCostPolicy
    {
        private readonly int? fixedCost;

        /// <summary>Uses the active run's resource configuration for every accepted command.</summary>
        public ActionCostPolicy() { }

        /// <summary>Creates an explicit flat policy for isolated scenarios; production uses the run's tiers.</summary>
        /// <param name="satietyCost">Positive cost; rejection still spends nothing.</param>
        /// <exception cref="ArgumentOutOfRangeException">The explicit cost is not positive.</exception>
        public ActionCostPolicy(int satietyCost)
        {
            if (satietyCost <= 0) throw new ArgumentOutOfRangeException(nameof(satietyCost));
            fixedCost = satietyCost;
        }

        /// <summary>Reads the cost before any direct effects; resolution retains this value until commit.</summary>
        /// <exception cref="ArgumentNullException">The run is null.</exception>
        public int GetCost(RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            return fixedCost ?? run.ResourceRules.SelectActionCost(run.Satiety);
        }
    }
}