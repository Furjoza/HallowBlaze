using System;

namespace HallowBlaze.Core.State
{
    /// <summary>Immutable tool content shared by runs; remaining charges belong only to instances.</summary>
    public sealed class ToolDefinition
    {
        /// <summary>Creates tool content without allocating an instance or changing a run.</summary>
        /// <param name="id">Stable textual definition ID, never an instance or authored source ID.</param>
        /// <param name="displayName">Player-facing name used by the current prototype HUD.</param>
        /// <param name="maximumCharges">Positive initial charge snapshot; for reusable tools it is retained for save compatibility, not a use limit.</param>
        /// <param name="isReusable">Whether accepted tool actions ignore depletion and spend no charges, including for retained zero-charge instances.</param>
        /// <exception cref="ArgumentException">An identifier or name is empty or has surrounding whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Capacity is not positive.</exception>
        public ToolDefinition(string id, string displayName, int maximumCharges, bool isReusable = false)
        {
            RequireId(id, nameof(id));
            RequireId(displayName, nameof(displayName));
            if (maximumCharges <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCharges));
            Id = id;
            DisplayName = displayName;
            MaximumCharges = maximumCharges;
            IsReusable = isReusable;
        }

        /// <summary>Gets the shared content ID, distinct from an instance or atlas source.</summary>
        public string Id { get; }
        /// <summary>Gets the prototype display name; definitions never store mutable presentation state.</summary>
        public string DisplayName { get; }
        /// <summary>Gets the initial compatibility charge snapshot; it is not a use limit for reusable tools and never refills an instance.</summary>
        public int MaximumCharges { get; }
        /// <summary>Gets whether the tool remains usable independently of its retained charge snapshot.</summary>
        public bool IsReusable { get; }
        /// <summary>Gets zero for reusable actions or one for finite-use actions; turn, food and noise costs remain separate.</summary>
        public int UseChargeCost => IsReusable ? 0 : 1;

        internal static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new ArgumentException("A stable value must be nonempty without surrounding whitespace.", parameterName);
        }
    }
}