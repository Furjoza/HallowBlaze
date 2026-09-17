using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.World.Validation
{
    /// <summary>
    /// Validates raw directed macrographs without Unity dependencies or input mutation.
    /// </summary>
    public static class WorldGraphValidator
    {
        /// <summary>
        /// Validates a raw graph snapshot and collects all independent failures that can be established safely.
        /// </summary>
        /// <param name="input">Raw graph snapshot to validate.</param>
        /// <param name="expectedGoalDay">
        /// Optional required shortest directed distance from start to goal; omitted to disable that rule.
        /// </param>
        /// <returns>A read-only result whose diagnostics have deterministic order.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedGoalDay"/> is negative.</exception>
        public static WorldGraphValidationResult Validate(
            WorldGraphValidationInput input,
            int? expectedGoalDay = null)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (expectedGoalDay < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedGoalDay),
                    "Expected goal day cannot be negative.");
            }

            List<WorldGraphValidationDiagnostic> diagnostics =
                new List<WorldGraphValidationDiagnostic>();
            Dictionary<string, List<WorldNodeDefinition>> nodeGroups = GroupNodes(input.Nodes);
            Dictionary<string, List<WorldEdgeDefinition>> edgeGroups = GroupEdges(input.Edges);
            HashSet<string> duplicateNodeIds = AddDuplicateNodeDiagnostics(nodeGroups, diagnostics);
            HashSet<string> duplicateEdgeIds = AddDuplicateEdgeDiagnostics(edgeGroups, diagnostics);
            Dictionary<string, WorldNodeDefinition> uniqueNodes = BuildUniqueNodeLookup(
                nodeGroups,
                duplicateNodeIds);

            bool startResolved = ResolveRequiredNode(
                input.StartNodeId,
                "startNodeId",
                WorldGraphDiagnosticCode.MissingStartNodeId,
                WorldGraphDiagnosticCode.UnresolvedStartNodeId,
                uniqueNodes,
                duplicateNodeIds,
                diagnostics);
            bool goalResolved = ResolveRequiredNode(
                input.GoalNodeId,
                "goalNodeId",
                WorldGraphDiagnosticCode.MissingGoalNodeId,
                WorldGraphDiagnosticCode.UnresolvedGoalNodeId,
                uniqueNodes,
                duplicateNodeIds,
                diagnostics);

            List<WorldEdgeDefinition> usableEdges = new List<WorldEdgeDefinition>();
            List<WorldEdgeDefinition> ambiguousEdges = new List<WorldEdgeDefinition>();
            HashSet<string> nodesWithDeclaredOutgoing = new HashSet<string>(StringComparer.Ordinal);
            InspectEdges(
                edgeGroups,
                duplicateEdgeIds,
                uniqueNodes,
                duplicateNodeIds,
                usableEdges,
                ambiguousEdges,
                nodesWithDeclaredOutgoing,
                diagnostics);

            Dictionary<string, List<WorldEdgeDefinition>> outgoingEdges = BuildOutgoingLookup(usableEdges);

            if (goalResolved)
            {
                AddUnexpectedDeadEnds(
                    input.GoalNodeId,
                    uniqueNodes,
                    outgoingEdges,
                    nodesWithDeclaredOutgoing,
                    diagnostics);
            }

            if (startResolved)
            {
                Dictionary<string, int> distances = FindShortestDistances(
                    input.StartNodeId,
                    outgoingEdges);
                List<WorldEdgeDefinition> potentialEdges = new List<WorldEdgeDefinition>(usableEdges);
                potentialEdges.AddRange(ambiguousEdges);
                Dictionary<string, int> potentialDistances = FindShortestDistances(
                    input.StartNodeId,
                    BuildOutgoingLookup(potentialEdges));
                HashSet<string> uncertainDistances = FindUncertainDistances(
                    distances,
                    potentialDistances);
                AddReachabilityAndDistanceDiagnostics(
                    uniqueNodes,
                    distances,
                    uncertainDistances,
                    diagnostics);

                if (goalResolved &&
                    expectedGoalDay.HasValue &&
                    !uncertainDistances.Contains(input.GoalNodeId) &&
                    distances.TryGetValue(input.GoalNodeId, out int actualGoalDay) &&
                    actualGoalDay != expectedGoalDay.Value)
                {
                    diagnostics.Add(
                        CreateDiagnostic(
                            WorldGraphDiagnosticCode.GoalDayMismatch,
                            input.GoalNodeId,
                            $"Goal '{input.GoalNodeId}' is reached on day {actualGoalDay}, expected day {expectedGoalDay.Value}."));
                }
            }

            diagnostics.Sort(CompareDiagnostics);
            return new WorldGraphValidationResult(diagnostics);
        }

        /// <summary>
        /// Validates an already materialized catalogue through the same graph rules.
        /// </summary>
        /// <param name="definition">Valid catalogue to inspect.</param>
        /// <param name="expectedGoalDay">
        /// Optional required shortest directed distance from start to goal; omitted to disable that rule.
        /// </param>
        /// <returns>A read-only result whose diagnostics have deterministic order.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedGoalDay"/> is negative.</exception>
        public static WorldGraphValidationResult Validate(
            WorldDefinition definition,
            int? expectedGoalDay = null)
        {
            return Validate(WorldGraphValidationInput.FromWorldDefinition(definition), expectedGoalDay);
        }

        private static Dictionary<string, List<WorldNodeDefinition>> GroupNodes(
            IEnumerable<WorldNodeDefinition> nodes)
        {
            Dictionary<string, List<WorldNodeDefinition>> groups =
                new Dictionary<string, List<WorldNodeDefinition>>(StringComparer.Ordinal);
            foreach (WorldNodeDefinition node in nodes)
            {
                if (!groups.TryGetValue(node.NodeId, out List<WorldNodeDefinition> group))
                {
                    group = new List<WorldNodeDefinition>();
                    groups.Add(node.NodeId, group);
                }

                group.Add(node);
            }

            return groups;
        }

        private static Dictionary<string, List<WorldEdgeDefinition>> GroupEdges(
            IEnumerable<WorldEdgeDefinition> edges)
        {
            Dictionary<string, List<WorldEdgeDefinition>> groups =
                new Dictionary<string, List<WorldEdgeDefinition>>(StringComparer.Ordinal);
            foreach (WorldEdgeDefinition edge in edges)
            {
                if (!groups.TryGetValue(edge.EdgeId, out List<WorldEdgeDefinition> group))
                {
                    group = new List<WorldEdgeDefinition>();
                    groups.Add(edge.EdgeId, group);
                }

                group.Add(edge);
            }

            return groups;
        }

        private static HashSet<string> AddDuplicateNodeDiagnostics(
            IReadOnlyDictionary<string, List<WorldNodeDefinition>> nodeGroups,
            ICollection<WorldGraphValidationDiagnostic> diagnostics)
        {
            HashSet<string> duplicateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<WorldNodeDefinition>> pair in nodeGroups)
            {
                if (pair.Value.Count < 2)
                    continue;

                duplicateIds.Add(pair.Key);
                diagnostics.Add(
                    CreateDiagnostic(
                        WorldGraphDiagnosticCode.DuplicateNodeId,
                        pair.Key,
                        $"Node ID '{pair.Key}' is declared {pair.Value.Count} times and is ambiguous."));
            }

            return duplicateIds;
        }

        private static HashSet<string> AddDuplicateEdgeDiagnostics(
            IReadOnlyDictionary<string, List<WorldEdgeDefinition>> edgeGroups,
            ICollection<WorldGraphValidationDiagnostic> diagnostics)
        {
            HashSet<string> duplicateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<WorldEdgeDefinition>> pair in edgeGroups)
            {
                if (pair.Value.Count < 2)
                    continue;

                duplicateIds.Add(pair.Key);
                diagnostics.Add(
                    CreateDiagnostic(
                        WorldGraphDiagnosticCode.DuplicateEdgeId,
                        pair.Key,
                        $"Edge ID '{pair.Key}' is declared {pair.Value.Count} times and is ambiguous."));
            }

            return duplicateIds;
        }

        private static Dictionary<string, WorldNodeDefinition> BuildUniqueNodeLookup(
            IReadOnlyDictionary<string, List<WorldNodeDefinition>> nodeGroups,
            ISet<string> duplicateNodeIds)
        {
            Dictionary<string, WorldNodeDefinition> uniqueNodes =
                new Dictionary<string, WorldNodeDefinition>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<WorldNodeDefinition>> pair in nodeGroups)
            {
                if (!duplicateNodeIds.Contains(pair.Key))
                    uniqueNodes.Add(pair.Key, pair.Value[0]);
            }

            return uniqueNodes;
        }

        private static bool ResolveRequiredNode(
            string nodeId,
            string fieldName,
            WorldGraphDiagnosticCode missingCode,
            WorldGraphDiagnosticCode unresolvedCode,
            IReadOnlyDictionary<string, WorldNodeDefinition> uniqueNodes,
            ISet<string> duplicateNodeIds,
            ICollection<WorldGraphValidationDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        missingCode,
                        fieldName,
                        $"Required field '{fieldName}' does not contain a node ID."));
                return false;
            }

            if (duplicateNodeIds.Contains(nodeId))
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        unresolvedCode,
                        nodeId,
                        $"Required {fieldName} '{nodeId}' resolves to duplicate node declarations."));
                return false;
            }

            if (!uniqueNodes.ContainsKey(nodeId))
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        unresolvedCode,
                        nodeId,
                        $"Required {fieldName} '{nodeId}' does not resolve to a declared node."));
                return false;
            }

            return true;
        }

        private static void InspectEdges(
            IReadOnlyDictionary<string, List<WorldEdgeDefinition>> edgeGroups,
            ISet<string> duplicateEdgeIds,
            IReadOnlyDictionary<string, WorldNodeDefinition> uniqueNodes,
            ISet<string> duplicateNodeIds,
            ICollection<WorldEdgeDefinition> usableEdges,
            ICollection<WorldEdgeDefinition> ambiguousEdges,
            ISet<string> nodesWithDeclaredOutgoing,
            ICollection<WorldGraphValidationDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, List<WorldEdgeDefinition>> pair in edgeGroups)
            {
                foreach (WorldEdgeDefinition edge in pair.Value)
                {
                    if (uniqueNodes.ContainsKey(edge.FromNodeId))
                        nodesWithDeclaredOutgoing.Add(edge.FromNodeId);
                }

                if (duplicateEdgeIds.Contains(pair.Key))
                {
                    foreach (WorldEdgeDefinition duplicateEdge in pair.Value)
                    {
                        bool duplicateSourceResolved = ResolveEdgeEndpoint(
                            duplicateEdge,
                            duplicateEdge.FromNodeId,
                            "source",
                            WorldGraphDiagnosticCode.MissingEdgeSource,
                            uniqueNodes,
                            duplicateNodeIds,
                            diagnostics);
                        bool duplicateDestinationResolved = ResolveEdgeEndpoint(
                            duplicateEdge,
                            duplicateEdge.ToNodeId,
                            "destination",
                            WorldGraphDiagnosticCode.MissingEdgeDestination,
                            uniqueNodes,
                            duplicateNodeIds,
                            diagnostics);

                        if (duplicateSourceResolved && duplicateDestinationResolved)
                            ambiguousEdges.Add(duplicateEdge);
                    }

                    continue;
                }

                WorldEdgeDefinition uniqueEdge = pair.Value[0];
                bool sourceResolved = ResolveEdgeEndpoint(
                    uniqueEdge,
                    uniqueEdge.FromNodeId,
                    "source",
                    WorldGraphDiagnosticCode.MissingEdgeSource,
                    uniqueNodes,
                    duplicateNodeIds,
                    diagnostics);
                bool destinationResolved = ResolveEdgeEndpoint(
                    uniqueEdge,
                    uniqueEdge.ToNodeId,
                    "destination",
                    WorldGraphDiagnosticCode.MissingEdgeDestination,
                    uniqueNodes,
                    duplicateNodeIds,
                    diagnostics);

                if (sourceResolved && destinationResolved)
                {
                    usableEdges.Add(uniqueEdge);
                }
            }
        }

        private static bool ResolveEdgeEndpoint(
            WorldEdgeDefinition edge,
            string nodeId,
            string endpointName,
            WorldGraphDiagnosticCode missingCode,
            IReadOnlyDictionary<string, WorldNodeDefinition> uniqueNodes,
            ISet<string> duplicateNodeIds,
            ICollection<WorldGraphValidationDiagnostic> diagnostics)
        {
            if (duplicateNodeIds.Contains(nodeId))
                return false;
            if (uniqueNodes.ContainsKey(nodeId))
                return true;

            AddDiagnosticIfMissing(
                CreateDiagnostic(
                    missingCode,
                    edge.EdgeId,
                    $"Edge '{edge.EdgeId}' {endpointName} '{nodeId}' does not resolve to a declared node."),
                diagnostics);
            return false;
        }

        private static Dictionary<string, List<WorldEdgeDefinition>> BuildOutgoingLookup(
            IEnumerable<WorldEdgeDefinition> edges)
        {
            Dictionary<string, List<WorldEdgeDefinition>> outgoingEdges =
                new Dictionary<string, List<WorldEdgeDefinition>>(StringComparer.Ordinal);
            foreach (WorldEdgeDefinition edge in edges)
            {
                if (!outgoingEdges.TryGetValue(edge.FromNodeId, out List<WorldEdgeDefinition> outgoing))
                {
                    outgoing = new List<WorldEdgeDefinition>();
                    outgoingEdges.Add(edge.FromNodeId, outgoing);
                }

                outgoing.Add(edge);
            }

            return outgoingEdges;
        }

        private static HashSet<string> FindUncertainDistances(
            IReadOnlyDictionary<string, int> distances,
            IReadOnlyDictionary<string, int> potentialDistances)
        {
            HashSet<string> uncertainNodeIds =
                new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in potentialDistances)
            {
                if (!distances.TryGetValue(pair.Key, out int knownDistance) ||
                    pair.Value < knownDistance)
                {
                    uncertainNodeIds.Add(pair.Key);
                }
            }

            return uncertainNodeIds;
        }

        private static void AddUnexpectedDeadEnds(
            string goalNodeId,
            IReadOnlyDictionary<string, WorldNodeDefinition> uniqueNodes,
            IReadOnlyDictionary<string, List<WorldEdgeDefinition>> outgoingEdges,
            ISet<string> nodesWithDeclaredOutgoing,
            ICollection<WorldGraphValidationDiagnostic> diagnostics)
        {
            foreach (string nodeId in uniqueNodes.Keys)
            {
                if (string.Equals(nodeId, goalNodeId, StringComparison.Ordinal) ||
                    outgoingEdges.ContainsKey(nodeId) ||
                    nodesWithDeclaredOutgoing.Contains(nodeId))
                {
                    continue;
                }

                diagnostics.Add(
                    CreateDiagnostic(
                        WorldGraphDiagnosticCode.UnexpectedDeadEnd,
                        nodeId,
                        $"Node '{nodeId}' has no usable outgoing edge and is not the explicit goal."));
            }
        }

        private static Dictionary<string, int> FindShortestDistances(
            string startNodeId,
            IReadOnlyDictionary<string, List<WorldEdgeDefinition>> outgoingEdges)
        {
            Dictionary<string, int> distances = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [startNodeId] = 0
            };
            Queue<string> pendingNodeIds = new Queue<string>();
            pendingNodeIds.Enqueue(startNodeId);
            while (pendingNodeIds.Count > 0)
            {
                string nodeId = pendingNodeIds.Dequeue();
                if (!outgoingEdges.TryGetValue(nodeId, out List<WorldEdgeDefinition> outgoing))
                    continue;

                int nextDistance = distances[nodeId] + 1;
                foreach (WorldEdgeDefinition edge in outgoing)
                {
                    if (distances.ContainsKey(edge.ToNodeId))
                        continue;

                    distances.Add(edge.ToNodeId, nextDistance);
                    pendingNodeIds.Enqueue(edge.ToNodeId);
                }
            }

            return distances;
        }

        private static void AddReachabilityAndDistanceDiagnostics(
            IReadOnlyDictionary<string, WorldNodeDefinition> uniqueNodes,
            IReadOnlyDictionary<string, int> distances,
            ISet<string> uncertainDistances,
            ICollection<WorldGraphValidationDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, WorldNodeDefinition> pair in uniqueNodes)
            {
                if (uncertainDistances.Contains(pair.Key))
                    continue;

                if (!distances.TryGetValue(pair.Key, out int actualDistance))
                {
                    diagnostics.Add(
                        CreateDiagnostic(
                            WorldGraphDiagnosticCode.UnreachableNode,
                            pair.Key,
                            $"Node '{pair.Key}' is not reachable from the declared start."));
                    continue;
                }

                if (pair.Value.DistanceLayer != actualDistance)
                {
                    diagnostics.Add(
                        CreateDiagnostic(
                            WorldGraphDiagnosticCode.DistanceLayerMismatch,
                            pair.Key,
                            $"Node '{pair.Key}' declares distance layer {pair.Value.DistanceLayer}, shortest distance is {actualDistance}."));
                }
            }
        }

        private static WorldGraphValidationDiagnostic CreateDiagnostic(
            WorldGraphDiagnosticCode code,
            string subject,
            string reason)
        {
            return new WorldGraphValidationDiagnostic(code, subject, reason);
        }

        private static void AddDiagnosticIfMissing(
            WorldGraphValidationDiagnostic diagnostic,
            ICollection<WorldGraphValidationDiagnostic> diagnostics)
        {
            foreach (WorldGraphValidationDiagnostic existing in diagnostics)
            {
                if (existing.Code == diagnostic.Code &&
                    string.Equals(existing.Subject, diagnostic.Subject, StringComparison.Ordinal) &&
                    string.Equals(existing.Reason, diagnostic.Reason, StringComparison.Ordinal))
                {
                    return;
                }
            }

            diagnostics.Add(diagnostic);
        }

        private static int CompareDiagnostics(
            WorldGraphValidationDiagnostic left,
            WorldGraphValidationDiagnostic right)
        {
            int codeComparison = left.Code.CompareTo(right.Code);
            if (codeComparison != 0)
                return codeComparison;

            int subjectComparison = StringComparer.Ordinal.Compare(left.Subject, right.Subject);
            return subjectComparison != 0
                ? subjectComparison
                : StringComparer.Ordinal.Compare(left.Reason, right.Reason);
        }
    }
}