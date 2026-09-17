using System;

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

        /// <summary>
        /// Creates a new immutable world edge definition.
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

            EdgeId = edgeId;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            WorldDirection = worldDirection;
            ClueKey = clueKey;
        }

        private static bool IsStableValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                !char.IsWhiteSpace(value[0]) &&
                !char.IsWhiteSpace(value[value.Length - 1]);
        }
    }
}
