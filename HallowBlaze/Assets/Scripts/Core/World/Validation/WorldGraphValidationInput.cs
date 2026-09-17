using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HallowBlaze.Core.World.Validation
{
    /// <summary>
    /// Snapshots raw node and edge collections before fail-fast world catalogue construction.
    /// </summary>
    public sealed class WorldGraphValidationInput
    {
        private readonly ReadOnlyCollection<WorldNodeDefinition> nodes;
        private readonly ReadOnlyCollection<WorldEdgeDefinition> edges;

        /// <summary>
        /// Creates an immutable validation snapshot without requiring a valid <see cref="WorldDefinition"/>.
        /// </summary>
        /// <param name="startNodeId">Declared start node ID, including an invalid null or blank value.</param>
        /// <param name="goalNodeId">Declared goal node ID, including an invalid null or blank value.</param>
        /// <param name="nodeDefinitions">Node definitions to snapshot; duplicate IDs are allowed.</param>
        /// <param name="edgeDefinitions">Edge definitions to snapshot; duplicate IDs and unresolved endpoints are allowed.</param>
        /// <exception cref="ArgumentNullException">A definition collection is null.</exception>
        /// <exception cref="ArgumentException">A definition collection contains a null element.</exception>
        public WorldGraphValidationInput(
            string startNodeId,
            string goalNodeId,
            IEnumerable<WorldNodeDefinition> nodeDefinitions,
            IEnumerable<WorldEdgeDefinition> edgeDefinitions)
        {
            StartNodeId = startNodeId;
            GoalNodeId = goalNodeId;
            nodes = Snapshot(nodeDefinitions, nameof(nodeDefinitions));
            edges = Snapshot(edgeDefinitions, nameof(edgeDefinitions));
        }

        /// <summary>
        /// Gets the declared start node ID exactly as supplied by authoring data.
        /// </summary>
        public string StartNodeId { get; }

        /// <summary>
        /// Gets the declared goal node ID exactly as supplied by authoring data.
        /// </summary>
        public string GoalNodeId { get; }

        /// <summary>
        /// Gets the snapshotted node definitions in their input order.
        /// </summary>
        public IReadOnlyList<WorldNodeDefinition> Nodes => nodes;

        /// <summary>
        /// Gets the snapshotted edge definitions in their input order.
        /// </summary>
        public IReadOnlyList<WorldEdgeDefinition> Edges => edges;

        /// <summary>
        /// Creates a validation snapshot from an already materialized valid catalogue.
        /// </summary>
        /// <param name="definition">Catalogue to snapshot.</param>
        /// <returns>An immutable validation input containing the catalogue values.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
        public static WorldGraphValidationInput FromWorldDefinition(WorldDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            return new WorldGraphValidationInput(
                definition.StartNodeId,
                definition.GoalNodeId,
                definition.Nodes,
                definition.Edges);
        }

        private static ReadOnlyCollection<T> Snapshot<T>(IEnumerable<T> values, string parameterName)
            where T : class
        {
            if (values == null)
                throw new ArgumentNullException(parameterName);

            List<T> snapshot = new List<T>(values);
            if (snapshot.Contains(null))
                throw new ArgumentException("Definition collections cannot contain null values.", parameterName);

            return snapshot.AsReadOnly();
        }
    }
}