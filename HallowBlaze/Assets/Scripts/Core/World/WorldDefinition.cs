using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HallowBlaze.Core.World
{
    /// <summary>
    /// Provides immutable, order-independent access to one versioned world catalogue.
    /// </summary>
    public sealed class WorldDefinition
    {
        private static readonly IReadOnlyList<WorldEdgeDefinition> NoEdges =
            Array.AsReadOnly(new WorldEdgeDefinition[0]);

        private readonly Dictionary<string, WorldNodeDefinition> nodesById;
        private readonly Dictionary<string, WorldEdgeDefinition> edgesById;
        private readonly Dictionary<string, IReadOnlyList<WorldEdgeDefinition>> outgoingEdgesByNodeId;
        private readonly ReadOnlyCollection<WorldNodeDefinition> nodes;
        private readonly ReadOnlyCollection<WorldEdgeDefinition> edges;

        /// <summary>
        /// Creates a catalogue whose public collections and lookup results cannot be mutated.
        /// </summary>
        /// <param name="worldDefinitionId">Stable identifier compared with profile data.</param>
        /// <param name="version">Positive version of this world definition.</param>
        /// <param name="startNodeId">Stable identifier of the prototype start.</param>
        /// <param name="goalNodeId">Stable identifier of the explicit prototype goal.</param>
        /// <param name="nodeDefinitions">Nodes to copy into the catalogue.</param>
        /// <param name="edgeDefinitions">Directed roads to copy into the catalogue.</param>
        /// <exception cref="ArgumentException">
        /// A stable identifier is invalid or a node or edge identifier is duplicated.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// A definition collection or one of its elements is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="version"/> is not positive.
        /// </exception>
        public WorldDefinition(
            string worldDefinitionId,
            int version,
            string startNodeId,
            string goalNodeId,
            IEnumerable<WorldNodeDefinition> nodeDefinitions,
            IEnumerable<WorldEdgeDefinition> edgeDefinitions)
        {
            ValidateStableId(worldDefinitionId, nameof(worldDefinitionId));
            ValidateStableId(startNodeId, nameof(startNodeId));
            ValidateStableId(goalNodeId, nameof(goalNodeId));
            if (version <= 0)
                throw new ArgumentOutOfRangeException(nameof(version), "World definition version must be positive.");
            if (nodeDefinitions == null)
                throw new ArgumentNullException(nameof(nodeDefinitions));
            if (edgeDefinitions == null)
                throw new ArgumentNullException(nameof(edgeDefinitions));

            List<WorldNodeDefinition> nodeList = new List<WorldNodeDefinition>(nodeDefinitions);
            List<WorldEdgeDefinition> edgeList = new List<WorldEdgeDefinition>(edgeDefinitions);
            nodeList.Sort((left, right) => CompareNodeIds(left, right));
            edgeList.Sort((left, right) => CompareEdgeIds(left, right));

            nodesById = BuildNodeLookup(nodeList);
            edgesById = BuildEdgeLookup(edgeList);
            outgoingEdgesByNodeId = BuildOutgoingLookup(edgeList);
            nodes = nodeList.AsReadOnly();
            edges = edgeList.AsReadOnly();

            WorldDefinitionId = worldDefinitionId;
            Version = version;
            StartNodeId = startNodeId;
            GoalNodeId = goalNodeId;
        }

        /// <summary>
        /// Gets the stable identifier compared with <c>ProfileState.WorldDefinitionId</c>.
        /// </summary>
        public string WorldDefinitionId { get; }

        /// <summary>
        /// Gets the positive version compared with <c>ProfileState.WorldDefinitionVersion</c>.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Gets the stable identifier of the prototype start node.
        /// </summary>
        public string StartNodeId { get; }

        /// <summary>
        /// Gets the stable identifier of the explicit prototype goal node.
        /// </summary>
        public string GoalNodeId { get; }

        /// <summary>
        /// Gets nodes in stable ordinal identifier order through a read-only view.
        /// </summary>
        public IReadOnlyList<WorldNodeDefinition> Nodes => nodes;

        /// <summary>
        /// Gets directed roads in stable ordinal identifier order through a read-only view.
        /// </summary>
        public IReadOnlyList<WorldEdgeDefinition> Edges => edges;

        /// <summary>
        /// Attempts to resolve a node by its stable identifier.
        /// </summary>
        /// <param name="nodeId">Stable node identifier.</param>
        /// <param name="node">Resolved node, or null when the identifier is unknown.</param>
        /// <returns><see langword="true"/> when the node exists; otherwise <see langword="false"/>.</returns>
        public bool TryGetNode(string nodeId, out WorldNodeDefinition node)
        {
            if (nodeId == null)
            {
                node = null;
                return false;
            }

            return nodesById.TryGetValue(nodeId, out node);
        }

        /// <summary>
        /// Attempts to resolve a directed road by its stable identifier.
        /// </summary>
        /// <param name="edgeId">Stable edge identifier.</param>
        /// <param name="edge">Resolved edge, or null when the identifier is unknown.</param>
        /// <returns><see langword="true"/> when the edge exists; otherwise <see langword="false"/>.</returns>
        public bool TryGetEdge(string edgeId, out WorldEdgeDefinition edge)
        {
            if (edgeId == null)
            {
                edge = null;
                return false;
            }

            return edgesById.TryGetValue(edgeId, out edge);
        }

        /// <summary>
        /// Gets outgoing roads in stable ordinal edge-ID order.
        /// </summary>
        /// <param name="nodeId">Stable identifier of an existing source node.</param>
        /// <returns>A read-only list, empty when the known node has no outgoing roads.</returns>
        /// <exception cref="KeyNotFoundException">
        /// <paramref name="nodeId"/> is not present in this catalogue.
        /// </exception>
        public IReadOnlyList<WorldEdgeDefinition> GetOutgoingEdges(string nodeId)
        {
            if (!nodesById.ContainsKey(nodeId))
                throw new KeyNotFoundException($"World node '{nodeId}' does not exist.");

            return outgoingEdgesByNodeId.TryGetValue(nodeId, out IReadOnlyList<WorldEdgeDefinition> outgoingEdges)
                ? outgoingEdges
                : NoEdges;
        }

        private static int CompareNodeIds(WorldNodeDefinition left, WorldNodeDefinition right)
        {
            if (left == null || right == null)
                throw new ArgumentNullException(nameof(left), "World node definitions cannot contain null values.");

            return StringComparer.Ordinal.Compare(left.NodeId, right.NodeId);
        }

        private static int CompareEdgeIds(WorldEdgeDefinition left, WorldEdgeDefinition right)
        {
            if (left == null || right == null)
                throw new ArgumentNullException(nameof(left), "World edge definitions cannot contain null values.");

            return StringComparer.Ordinal.Compare(left.EdgeId, right.EdgeId);
        }

        private static Dictionary<string, WorldNodeDefinition> BuildNodeLookup(
            IEnumerable<WorldNodeDefinition> nodeDefinitions)
        {
            Dictionary<string, WorldNodeDefinition> lookup =
                new Dictionary<string, WorldNodeDefinition>(StringComparer.Ordinal);
            foreach (WorldNodeDefinition node in nodeDefinitions)
            {
                if (node == null)
                    throw new ArgumentNullException(nameof(nodeDefinitions), "World node definitions cannot contain null values.");
                if (!lookup.TryAdd(node.NodeId, node))
                    throw new ArgumentException($"Duplicate world node ID '{node.NodeId}'.", nameof(nodeDefinitions));
            }

            return lookup;
        }

        private static Dictionary<string, WorldEdgeDefinition> BuildEdgeLookup(
            IEnumerable<WorldEdgeDefinition> edgeDefinitions)
        {
            Dictionary<string, WorldEdgeDefinition> lookup =
                new Dictionary<string, WorldEdgeDefinition>(StringComparer.Ordinal);
            foreach (WorldEdgeDefinition edge in edgeDefinitions)
            {
                if (edge == null)
                    throw new ArgumentNullException(nameof(edgeDefinitions), "World edge definitions cannot contain null values.");
                if (!lookup.TryAdd(edge.EdgeId, edge))
                    throw new ArgumentException($"Duplicate world edge ID '{edge.EdgeId}'.", nameof(edgeDefinitions));
            }

            return lookup;
        }

        private static Dictionary<string, IReadOnlyList<WorldEdgeDefinition>> BuildOutgoingLookup(
            IEnumerable<WorldEdgeDefinition> edgeDefinitions)
        {
            Dictionary<string, List<WorldEdgeDefinition>> mutableLookup =
                new Dictionary<string, List<WorldEdgeDefinition>>(StringComparer.Ordinal);
            foreach (WorldEdgeDefinition edge in edgeDefinitions)
            {
                if (!mutableLookup.TryGetValue(edge.FromNodeId, out List<WorldEdgeDefinition> outgoingEdges))
                {
                    outgoingEdges = new List<WorldEdgeDefinition>();
                    mutableLookup.Add(edge.FromNodeId, outgoingEdges);
                }

                outgoingEdges.Add(edge);
            }

            Dictionary<string, IReadOnlyList<WorldEdgeDefinition>> readOnlyLookup =
                new Dictionary<string, IReadOnlyList<WorldEdgeDefinition>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<WorldEdgeDefinition>> pair in mutableLookup)
                readOnlyLookup.Add(pair.Key, pair.Value.AsReadOnly());

            return readOnlyLookup;
        }

        private static void ValidateStableId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                char.IsWhiteSpace(value[0]) ||
                char.IsWhiteSpace(value[value.Length - 1]))
            {
                throw new ArgumentException(
                    "Stable IDs cannot be empty or have surrounding whitespace.",
                    parameterName);
            }
        }
    }
}