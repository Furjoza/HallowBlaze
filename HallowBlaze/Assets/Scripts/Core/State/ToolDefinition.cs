using System;

namespace HallowBlaze.Core.State
{
    /// <summary>Immutable tool content shared by runs; remaining charges belong only to instances.</summary>
    public sealed class ToolDefinition
    {
        /// <summary>Creates tool content without allocating an instance or changing a run.</summary>
        /// <param name="id">Stable textual definition ID, never an instance or authored source ID.</param>
        /// <param name="displayName">Player-facing name used by the current prototype HUD.</param>
        /// <param name="maximumCharges">Positive capacity and initial charges of a newly created tool.</param>
        /// <exception cref="ArgumentException">An identifier or name is empty or has surrounding whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Capacity is not positive.</exception>
        public ToolDefinition(string id, string displayName, int maximumCharges)
        {
            RequireId(id, nameof(id));
            RequireId(displayName, nameof(displayName));
            if (maximumCharges <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCharges));
            Id = id;
            DisplayName = displayName;
            MaximumCharges = maximumCharges;
        }

        /// <summary>Gets the shared content ID, distinct from an instance or atlas source.</summary>
        public string Id { get; }
        /// <summary>Gets the prototype display name; definitions never store mutable presentation state.</summary>
        public string DisplayName { get; }
        /// <summary>Gets the initial charge capacity; existing instances are not refilled by observing content.</summary>
        public int MaximumCharges { get; }

        internal static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new ArgumentException("A stable value must be nonempty without surrounding whitespace.", parameterName);
        }
    }
}