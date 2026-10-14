using System;

namespace HallowBlaze.Core.World
{
    /// <summary>Identifies one authored board position on a road independently of its list index.</summary>
    public sealed class RouteSegmentDefinition
    {
        /// <summary>Creates a segment with migration-sensitive textual generation identity.</summary>
        /// <exception cref="ArgumentException">An identifier is blank or has surrounding whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The generation version is not positive.</exception>
        public RouteSegmentDefinition(string segmentId, string generationConfigurationId, int generationVersion)
        {
            RequireId(segmentId, nameof(segmentId));
            RequireId(generationConfigurationId, nameof(generationConfigurationId));
            if (generationVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(generationVersion));
            SegmentId = segmentId;
            GenerationConfigurationId = generationConfigurationId;
            GenerationVersion = generationVersion;
        }

        /// <summary>Gets the globally unique published segment ID, never an ordinal cursor.</summary>
        public string SegmentId { get; }
        /// <summary>Gets the stable generator configuration key, not an asset reference.</summary>
        public string GenerationConfigurationId { get; }
        /// <summary>Gets the positive version of the generation contract.</summary>
        public int GenerationVersion { get; }

        private static void RequireId(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("A stable ID without surrounding whitespace is required.", parameter);
        }
    }
}