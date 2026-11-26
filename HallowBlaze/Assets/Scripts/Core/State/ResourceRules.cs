using System;

namespace HallowBlaze.Core.State
{
    /// <summary>Immutable, persisted limits and start-of-command satiety costs; no presentation or item ownership.</summary>
    public sealed class ResourceRules
    {
        /// <summary>Current interpretation of the serialized resource configuration.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Gets the accepted initial resource balance, shared safely by independent runs.</summary>
        public static ResourceRules Default { get; } = new ResourceRules();

        /// <summary>Defines positive caps and costs with a normal threshold inside the satiety range.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A cap/cost is not positive or the threshold is outside its range.</exception>
        public ResourceRules(int maximumHealth = 100, int maximumSatiety = 200,
            int normalSatietyThreshold = 100, int normalActionCost = 1, int satedActionCost = 2)
        {
            if (maximumHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            if (maximumSatiety <= 0) throw new ArgumentOutOfRangeException(nameof(maximumSatiety));
            if (normalSatietyThreshold < 0 || normalSatietyThreshold > maximumSatiety)
                throw new ArgumentOutOfRangeException(nameof(normalSatietyThreshold));
            if (normalActionCost <= 0) throw new ArgumentOutOfRangeException(nameof(normalActionCost));
            if (satedActionCost <= 0) throw new ArgumentOutOfRangeException(nameof(satedActionCost));
            MaximumHealth = maximumHealth;
            MaximumSatiety = maximumSatiety;
            NormalSatietyThreshold = normalSatietyThreshold;
            NormalActionCost = normalActionCost;
            SatedActionCost = satedActionCost;
        }

        /// <summary>Gets the inclusive upper health bound; excess healing is wasted.</summary>
        public int MaximumHealth { get; }
        /// <summary>Gets the inclusive upper satiety bound; excess food is wasted.</summary>
        public int MaximumSatiety { get; }
        /// <summary>Gets the inclusive upper threshold for the normal action cost.</summary>
        public int NormalSatietyThreshold { get; }
        /// <summary>Gets the cost selected at or below the normal threshold.</summary>
        public int NormalActionCost { get; }
        /// <summary>Gets the cost selected strictly above the normal threshold.</summary>
        public int SatedActionCost { get; }

        /// <summary>Samples a legal pre-command satiety value without changing state or applying an effect.</summary>
        /// <returns>The already-selected cost, which must not be resampled after direct effects.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Satiety is outside this configuration's range.</exception>
        public int SelectActionCost(int satiety)
        {
            if (satiety < 0 || satiety > MaximumSatiety) throw new ArgumentOutOfRangeException(nameof(satiety));
            return satiety <= NormalSatietyThreshold ? NormalActionCost : SatedActionCost;
        }
    }

    /// <summary>Immutable accounting for one requested restoration; the unused amount is never banked.</summary>
    public readonly struct ResourceGain
    {
        /// <summary>Calculates a capped restoration without overflow, mutation, or choosing a terminal outcome.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The current value, cap, or requested amount is invalid.</exception>
        public ResourceGain(int current, int requested, int maximum)
        {
            if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (current < 0 || current > maximum) throw new ArgumentOutOfRangeException(nameof(current));
            if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
            Requested = requested;
            Applied = Math.Min(requested, maximum - current);
        }

        /// <summary>Gets the requested nonnegative restoration before the cap.</summary>
        public int Requested { get; }
        /// <summary>Gets the amount actually added to the resource.</summary>
        public int Applied { get; }
        /// <summary>Gets the amount discarded at the cap.</summary>
        public int Wasted => Requested - Applied;
    }
}