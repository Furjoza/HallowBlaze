using System;

namespace HallowBlaze.Core.State
{
    /// <summary>Initial resources and checkpoint with one immutable resource-rule configuration.</summary>
    public sealed class RunStateConfiguration
    {
        /// <summary>Creates a run boundary; initial resources must already lie inside the configured ranges.</summary>
        /// <param name="initialHealth">Health in zero through the configured maximum.</param>
        /// <param name="initialSatiety">Satiety in zero through the configured maximum; new production runs start at 100.</param>
        /// <param name="initialDay">Nonnegative completed day count.</param>
        /// <param name="initialWorldNodeId">Stable reached-checkpoint identity.</param>
        /// <param name="resourceRules">Persisted limits and costs; null selects the accepted initial balance.</param>
        /// <exception cref="ArgumentOutOfRangeException">Resources or the day are outside their ranges.</exception>
        /// <exception cref="ArgumentException">The checkpoint ID is invalid.</exception>
        public RunStateConfiguration(
            int initialHealth,
            int initialSatiety,
            int initialDay,
            string initialWorldNodeId,
            ResourceRules resourceRules = null)
        {
            ResourceRules = resourceRules ?? ResourceRules.Default;
            if (initialHealth < 0 || initialHealth > ResourceRules.MaximumHealth)
                throw new ArgumentOutOfRangeException(nameof(initialHealth));
            if (initialSatiety < 0 || initialSatiety > ResourceRules.MaximumSatiety)
                throw new ArgumentOutOfRangeException(nameof(initialSatiety));
            if (initialDay < 0)
                throw new ArgumentOutOfRangeException(nameof(initialDay));
            if (string.IsNullOrWhiteSpace(initialWorldNodeId) ||
                char.IsWhiteSpace(initialWorldNodeId[0]) ||
                char.IsWhiteSpace(initialWorldNodeId[initialWorldNodeId.Length - 1]))
                throw new ArgumentException("World node ID cannot be empty.", nameof(initialWorldNodeId));

            InitialHealth = initialHealth;
            InitialSatiety = initialSatiety;
            InitialDay = initialDay;
            InitialWorldNodeId = initialWorldNodeId;
        }

        /// <summary>Gets starting health without inferring a terminal outcome.</summary>
        public int InitialHealth { get; }
        /// <summary>Gets starting satiety, not a second legacy Food resource.</summary>
        public int InitialSatiety { get; }
        /// <summary>Gets the completed day count.</summary>
        public int InitialDay { get; }
        /// <summary>Gets the reached checkpoint.</summary>
        public string InitialWorldNodeId { get; }
        /// <summary>Gets the immutable caps and action-cost thresholds retained by this run.</summary>
        public ResourceRules ResourceRules { get; }
    }
}