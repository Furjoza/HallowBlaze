using System;

namespace HallowBlaze.Core.State
{
    /// <summary>Distinguishes node-only legacy evidence from a completed addressed traversal.</summary>
    public enum TravelHistoryKind
    {
        /// <summary>No edge, leg, day or segment evidence is known.</summary>
        LegacyCheckpoint,
        /// <summary>The entire identified road traversal reached its destination.</summary>
        CompletedTraversal
    }

    /// <summary>Immutable ordered evidence; legacy entries never invent missing travel identities.</summary>
    public sealed class TravelHistoryEntry : IEquatable<TravelHistoryEntry>
    {
        private TravelHistoryEntry(TravelHistoryKind kind, string toNodeId, string legId = null,
            string edgeId = null, string fromNodeId = null, int? dayNumber = null, int? segmentCount = null)
        {
            Kind = kind;
            ToNodeId = toNodeId;
            LegId = legId;
            EdgeId = edgeId;
            FromNodeId = fromNodeId;
            DayNumber = dayNumber;
            SegmentCount = segmentCount;
        }

        /// <summary>Gets the evidence discriminator.</summary>
        public TravelHistoryKind Kind { get; }
        /// <summary>Gets the reached node, the only evidence present in a legacy entry.</summary>
        public string ToNodeId { get; }
        /// <summary>Gets the run-local traversal ID, absent for legacy entries.</summary>
        public string LegId { get; }
        /// <summary>Gets the actual road ID, absent for legacy entries.</summary>
        public string EdgeId { get; }
        /// <summary>Gets the source checkpoint, absent for legacy entries.</summary>
        public string FromNodeId { get; }
        /// <summary>Gets the completed displayed day, absent for legacy entries.</summary>
        public int? DayNumber { get; }
        /// <summary>Gets the total completed segment count, absent for legacy entries.</summary>
        public int? SegmentCount { get; }
        /// <summary>Gets completed progress; addressed entries always include every segment.</summary>
        public int? CompletedSegmentCount => SegmentCount;

        /// <summary>Records only the node evidence available in the old node-route format.</summary>
        public static TravelHistoryEntry LegacyCheckpoint(string nodeId)
        {
            RequireId(nodeId, nameof(nodeId));
            return new TravelHistoryEntry(TravelHistoryKind.LegacyCheckpoint, nodeId);
        }

        /// <summary>Records one whole traversal, including its actual road and run-local identity.</summary>
        /// <exception cref="ArgumentException">An identifier is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Day or segment count is not positive.</exception>
        public static TravelHistoryEntry Completed(string legId, string edgeId, string fromNodeId,
            string toNodeId, int dayNumber, int segmentCount)
        {
            RequireId(legId, nameof(legId));
            RequireId(edgeId, nameof(edgeId));
            RequireId(fromNodeId, nameof(fromNodeId));
            RequireId(toNodeId, nameof(toNodeId));
            if (dayNumber <= 0 || segmentCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(dayNumber));
            return new TravelHistoryEntry(TravelHistoryKind.CompletedTraversal, toNodeId,
                legId, edgeId, fromNodeId, dayNumber, segmentCount);
        }

        /// <summary>Compares all evidenced fields, including tag and actual road.</summary>
        public bool Equals(TravelHistoryEntry other) => other != null && Kind == other.Kind &&
            ToNodeId == other.ToNodeId && LegId == other.LegId && EdgeId == other.EdgeId &&
            FromNodeId == other.FromNodeId && DayNumber == other.DayNumber && SegmentCount == other.SegmentCount;
        /// <summary>Compares this value with another travel entry.</summary>
        public override bool Equals(object obj) => Equals(obj as TravelHistoryEntry);
        /// <summary>Returns a dictionary hash, not a durable identity or replay hash.</summary>
        public override int GetHashCode() => HashCode.Combine(Kind, ToNodeId, LegId, EdgeId, FromNodeId, DayNumber, SegmentCount);

        internal static void RequireId(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("A stable ID without surrounding whitespace is required.", parameter);
        }
    }
}