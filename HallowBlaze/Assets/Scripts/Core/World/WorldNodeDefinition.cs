using System;

namespace HallowBlaze.Core.World
{
    /// <summary>
    /// Immutable definition of a world node with atlas coordinates, distance layer, and place kind.
    /// </summary>
    public sealed class WorldNodeDefinition
    {
        /// <summary>
        /// Unique identifier for this node.
        /// </summary>
        public string NodeId { get; }

        /// <summary>
        /// Atlas X coordinate.
        /// </summary>
        public int AtlasX { get; }

        /// <summary>
        /// Atlas Y coordinate.
        /// </summary>
        public int AtlasY { get; }

        /// <summary>
        /// Distance layer from start (0 = start layer).
        /// </summary>
        public int DistanceLayer { get; }

        /// <summary>
        /// Place kind identifier.
        /// </summary>
        public string PlaceKind { get; }

        /// <summary>
        /// Biome family identifier.
        /// </summary>
        public string BiomeFamily { get; }

        /// <summary>
        /// Creates a new immutable world node definition.
        /// </summary>
        /// <param name="nodeId">Unique node identifier.</param>
        /// <param name="atlasX">Atlas X coordinate.</param>
        /// <param name="atlasY">Atlas Y coordinate.</param>
        /// <param name="distanceLayer">Distance layer from start.</param>
        /// <param name="placeKind">Place kind identifier.</param>
        /// <param name="biomeFamily">Biome family identifier.</param>
        public WorldNodeDefinition(
            string nodeId,
            int atlasX,
            int atlasY,
            int distanceLayer,
            string placeKind,
            string biomeFamily)
        {
            if (!IsStableValue(nodeId))
                throw new ArgumentException("Node ID must be non-empty and not whitespace.", nameof(nodeId));
            if (!IsStableValue(placeKind))
                throw new ArgumentException("Place kind must be non-empty and not whitespace.", nameof(placeKind));
            if (!IsStableValue(biomeFamily))
                throw new ArgumentException("Biome family must be non-empty and not whitespace.", nameof(biomeFamily));
            if (distanceLayer < 0)
                throw new ArgumentOutOfRangeException(nameof(distanceLayer), "Distance layer must be >= 0.");

            NodeId = nodeId;
            AtlasX = atlasX;
            AtlasY = atlasY;
            DistanceLayer = distanceLayer;
            PlaceKind = placeKind;
            BiomeFamily = biomeFamily;
        }

        private static bool IsStableValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                !char.IsWhiteSpace(value[0]) &&
                !char.IsWhiteSpace(value[value.Length - 1]);
        }
    }
}
