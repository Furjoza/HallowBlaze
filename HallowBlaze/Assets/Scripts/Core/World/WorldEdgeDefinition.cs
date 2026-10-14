using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.World
{
    /// <summary>
    /// Immutable definition of a directed edge between world nodes with a world direction.
    /// </summary>
    public sealed class WorldEdgeDefinition
    {
        /// <summary>
        /// Unique identifier for this edge.
        /// </summary>
        public string EdgeId { get; }

        /// <summary>
        /// Source node identifier.
        /// </summary>
        public string FromNodeId { get; }

        /// <summary>
        /// Target node identifier.
        /// </summary>
        public string ToNodeId { get; }

        /// <summary>
        /// World direction identifier.
        /// </summary>
        public string WorldDirection { get; }

        /// <summary>
        /// Clue key for this edge.
        /// </summary>
        public string ClueKey { get; }

        /// <summary>Gets the detached, non-empty authored travel order.</summary>
        public IReadOnlyList<RouteSegmentDefinition> Segments { get; }

        /// <summary>Gets the number of local boards, derived from the authored list.</summary>
        public int SegmentCount => Segments.Count;

        /// <summary>
        /// Creates a checkpoint-compatible edge with one segment named legacy.segment:{edgeId},
        /// configuration legacy.board.v1 and generation version 1. This does not create a run leg.
        /// </summary>
        /// <param name="edgeId">Unique edge identifier.</param>
        /// <param name="fromNodeId">Source node identifier.</param>
        /// <param name="toNodeId">Target node identifier.</param>
        /// <param name="worldDirection">World direction identifier.</param>
        /// <param name="clueKey">Clue key for this edge.</param>
        public WorldEdgeDefinition(
            string edgeId,
            string fromNodeId,
            string toNodeId,
            string worldDirection,
            string clueKey)
            : this(edgeId, fromNodeId, toNodeId, worldDirection, clueKey,
                new[] { new RouteSegmentDefinition("legacy.segment:" + edgeId, "legacy.board.v1", 1) })
        {
        }

        /// <summary>Creates an edge with copied, ordered, uniquely identified local-board segments.</summary>
        /// <exception cref="ArgumentException">An ID is invalid or the segment list is empty or duplicated.</exception>
        /// <exception cref="ArgumentNullException">The segment collection or an element is null.</exception>
        public WorldEdgeDefinition(string edgeId, string fromNodeId, string toNodeId,
            string worldDirection, string clueKey, IEnumerable<RouteSegmentDefinition> segments)
        {
            if (!IsStableValue(edgeId))
                throw new ArgumentException("Edge ID must be non-empty and not whitespace.", nameof(edgeId));
            if (!IsStableValue(fromNodeId))
                throw new ArgumentException("From node ID must be non-empty and not whitespace.", nameof(fromNodeId));
            if (!IsStableValue(toNodeId))
                throw new ArgumentException("To node ID must be non-empty and not whitespace.", nameof(toNodeId));
            if (!IsStableValue(worldDirection))
                throw new ArgumentException("World direction must be non-empty and not whitespace.", nameof(worldDirection));
            if (!IsStableValue(clueKey))
                throw new ArgumentException("Clue key must be non-empty and not whitespace.", nameof(clueKey));
            if (segments == null)
                throw new ArgumentNullException(nameof(segments));
            List<RouteSegmentDefinition> copy = new List<RouteSegmentDefinition>(segments);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (copy.Count == 0)
                throw new ArgumentException("An edge requires at least one segment.", nameof(segments));
            foreach (RouteSegmentDefinition segment in copy)
            {
                if (segment == null)
                    throw new ArgumentNullException(nameof(segments));
                if (!ids.Add(segment.SegmentId))
                    throw new ArgumentException($"Duplicate segment '{segment.SegmentId}' on '{edgeId}'.", nameof(segments));
            }

            EdgeId = edgeId;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            WorldDirection = worldDirection;
            ClueKey = clueKey;
            Segments = copy.AsReadOnly();
        }

        private static bool IsStableValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                !char.IsWhiteSpace(value[0]) &&
                !char.IsWhiteSpace(value[value.Length - 1]);
        }
    }
}
