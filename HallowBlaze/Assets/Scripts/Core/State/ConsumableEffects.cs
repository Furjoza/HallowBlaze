using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.State
{
    /// <summary>The sole run resource affected by a restoration; no buffs or secondary storage are implied.</summary>
    public enum ConsumableResource
    {
        /// <summary>Bounded health.</summary>
        Health,
        /// <summary>Bounded satiety.</summary>
        Satiety
    }

    /// <summary>A failed use changes neither resource, item ownership, nor the turn budget.</summary>
    public enum ConsumableEffectRejection
    {
        /// <summary>The requested effect was applied, possibly wasting excess.</summary>
        None,
        /// <summary>The exact textual effect ID is not registered.</summary>
        UnknownEffect,
        /// <summary>The run is terminal or already has zero health or satiety.</summary>
        InvalidState
    }

    /// <summary>Immutable restoration data; item transfer and action costs remain owned by the resolver.</summary>
    public sealed class ConsumableEffectDefinition
    {
        /// <summary>Defines one positive restoration without applying it or allocating an item.</summary>
        /// <exception cref="ArgumentException">The stable ID is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The resource or amount is unsupported.</exception>
        public ConsumableEffectDefinition(string id, ConsumableResource resource, int amount)
        {
            ItemDefinition.RequireId(id, nameof(id));
            if (resource != ConsumableResource.Health && resource != ConsumableResource.Satiety)
                throw new ArgumentOutOfRangeException(nameof(resource));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Id = id;
            Resource = resource;
            Amount = amount;
        }

        /// <summary>Gets the stable effect reference used by item definitions and future saves.</summary>
        public string Id { get; }
        /// <summary>Gets the one affected resource.</summary>
        public ConsumableResource Resource { get; }
        /// <summary>Gets the requested restoration before the run's configured cap.</summary>
        public int Amount { get; }

        /// <summary>Applies a validated effect only; it never consumes a turn, transfers an item, or resamples a cost.</summary>
        /// <param name="run">The sole mutable resource owner.</param>
        /// <param name="gain">Applied/wasted accounting on success; zero accounting on rejection.</param>
        /// <returns>A typed rejection without mutation, or success even when the full amount is wasted.</returns>
        /// <exception cref="ArgumentNullException">The run is null.</exception>
        public ConsumableEffectRejection TryApply(RunState run, out ResourceGain gain)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            gain = default;
            if (run.Status != RunStatus.Active || run.Health <= 0 || run.Satiety <= 0)
                return ConsumableEffectRejection.InvalidState;
            gain = Resource == ConsumableResource.Health ? run.RestoreHealth(Amount) : run.RestoreSatiety(Amount);
            return ConsumableEffectRejection.None;
        }
    }

    /// <summary>Immutable ordinal effect registry; unknown references reject instead of substituting content.</summary>
    public sealed class ConsumableEffectCatalog
    {
        private readonly Dictionary<string, ConsumableEffectDefinition> effects =
            new Dictionary<string, ConsumableEffectDefinition>(StringComparer.Ordinal);

        /// <summary>Copies definitions and rejects null or duplicate entries before the catalogue is usable.</summary>
        /// <exception cref="ArgumentNullException">The sequence is null.</exception>
        /// <exception cref="ArgumentException">An entry is null or repeats an ordinal ID.</exception>
        public ConsumableEffectCatalog(IEnumerable<ConsumableEffectDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (ConsumableEffectDefinition definition in definitions)
            {
                if (definition == null || effects.ContainsKey(definition.Id))
                    throw new ArgumentException("Effect definitions must be nonnull with unique IDs.", nameof(definitions));
                effects.Add(definition.Id, definition);
            }
        }

        /// <summary>Looks up an exact immutable effect without using an item's display name or Unity identity.</summary>
        /// <returns>False for null or unknown IDs; no content is invented.</returns>
        public bool TryGetDefinition(string effectId, out ConsumableEffectDefinition definition)
        {
            definition = null;
            return effectId != null && effects.TryGetValue(effectId, out definition);
        }

        /// <summary>Resolves an exact reference and applies its bounded effect without owning turn or item state.</summary>
        /// <returns>Unknown/invalid effects reject without changing any resource.</returns>
        /// <exception cref="ArgumentNullException">The run is null.</exception>
        public ConsumableEffectRejection TryApply(string effectId, RunState run, out ResourceGain gain)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            gain = default;
            return TryGetDefinition(effectId, out ConsumableEffectDefinition effect)
                ? effect.TryApply(run, out gain) : ConsumableEffectRejection.UnknownEffect;
        }
    }
}