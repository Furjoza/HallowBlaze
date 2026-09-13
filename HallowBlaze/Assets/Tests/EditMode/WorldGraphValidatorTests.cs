using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.World;
using HallowBlaze.Core.World.Validation;
using NUnit.Framework;
using UnityEngine;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class WorldGraphValidatorTests
    {
        [Test]
        public void PrototypeFixturePassesAtExpectedGoalDayFive()
        {
            WorldDefinition world = LoadPrototypeWorld();

            WorldGraphValidationResult result = WorldGraphValidator.Validate(world, 5);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Diagnostics, Is.Empty);
        }

        [Test]
        public void DuplicateNodeIdProducesStableDiagnostic()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("branch", 1), Node("branch", 2), Node("goal", 1) },
                new[] { Edge("road.start-goal", "start", "goal") });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.DuplicateNodeId,
                "branch",
                "Node ID 'branch' is declared 2 times and is ambiguous.");
        }

        [Test]
        public void DuplicateEdgeIdProducesStableDiagnosticWithoutDependentCascades()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-goal", "start", "goal"),
                    Edge("road.start-goal", "start", "goal")
                });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input, 1);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.DuplicateEdgeId,
                "road.start-goal",
                "Edge ID 'road.start-goal' is declared 2 times and is ambiguous.");
        }

        [Test]
        public void DuplicateEdgeReportsItsIndependentMissingEndpointOnce()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-goal", "start", "goal"),
                    Edge("road.broken", "start", "missing.destination"),
                    Edge("road.broken", "start", "missing.destination")
                });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            Assert.That(
                Snapshot(result),
                Is.EqualTo(
                    new[]
                    {
                        "DuplicateEdgeId|road.broken|Edge ID 'road.broken' is declared 2 times and is ambiguous.",
                        "MissingEdgeDestination|road.broken|Edge 'road.broken' destination 'missing.destination' does not resolve to a declared node."
                    }));
        }

        [Test]
        public void MissingStartNodeIdProducesStableDiagnostic()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 1) },
                new[] { Edge("road.start-goal", "start", "goal") },
                null,
                "goal");

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.MissingStartNodeId,
                "startNodeId",
                "Required field 'startNodeId' does not contain a node ID.");
        }

        [Test]
        public void UnresolvedStartNodeIdProducesStableDiagnostic()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 1) },
                new[] { Edge("road.start-goal", "start", "goal") },
                "missing.start",
                "goal");

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.UnresolvedStartNodeId,
                "missing.start",
                "Required startNodeId 'missing.start' does not resolve to a declared node.");
        }

        [Test]
        public void MissingGoalNodeIdProducesStableDiagnostic()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 1) },
                new[] { Edge("road.start-goal", "start", "goal") },
                "start",
                string.Empty);

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.MissingGoalNodeId,
                "goalNodeId",
                "Required field 'goalNodeId' does not contain a node ID.");
        }

        [Test]
        public void UnresolvedGoalNodeIdProducesStableDiagnostic()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 1) },
                new[] { Edge("road.start-goal", "start", "goal") },
                "start",
                "missing.goal");

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.UnresolvedGoalNodeId,
                "missing.goal",
                "Required goalNodeId 'missing.goal' does not resolve to a declared node.");
        }

        [Test]
        public void MissingEdgeSourceProducesStableDiagnostic()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-goal", "start", "goal"),
                    Edge("road.broken-source", "missing.source", "goal")
                });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.MissingEdgeSource,
                "road.broken-source",
                "Edge 'road.broken-source' source 'missing.source' does not resolve to a declared node.");
        }

        [Test]
        public void MissingEdgeDestinationProducesStableDiagnostic()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-goal", "start", "goal"),
                    Edge("road.broken-destination", "start", "missing.destination")
                });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.MissingEdgeDestination,
                "road.broken-destination",
                "Edge 'road.broken-destination' destination 'missing.destination' does not resolve to a declared node.");
        }

        [Test]
        public void UnreachableNodeProducesStableDiagnostic()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("orphan", 1), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-goal", "start", "goal"),
                    Edge("road.orphan-loop", "orphan", "orphan")
                });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.UnreachableNode,
                "orphan",
                "Node 'orphan' is not reachable from the declared start.");
        }

        [Test]
        public void UnexpectedDeadEndProducesStableDiagnostic()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("dead-end", 1), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-dead-end", "start", "dead-end"),
                    Edge("road.start-goal", "start", "goal")
                });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.UnexpectedDeadEnd,
                "dead-end",
                "Node 'dead-end' has no usable outgoing edge and is not the explicit goal.");
        }

        [Test]
        public void DistanceLayerMismatchUsesShortestDirectedDistance()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 2) },
                new[] { Edge("road.start-goal", "start", "goal") });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input);

            AssertSingleDiagnostic(
                result,
                WorldGraphDiagnosticCode.DistanceLayerMismatch,
                "goal",
                "Node 'goal' declares distance layer 2, shortest distance is 1.");
        }

        [Test]
        public void GoalDayIsOptionalAndUsesShortestDirectedDistance()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("middle", 1), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-middle", "start", "middle"),
                    Edge("road.middle-goal", "middle", "goal"),
                    Edge("road.start-goal", "start", "goal")
                });

            Assert.That(WorldGraphValidator.Validate(input).IsValid, Is.True);
            Assert.That(WorldGraphValidator.Validate(input, 1).IsValid, Is.True);

            WorldGraphValidationResult mismatch = WorldGraphValidator.Validate(input, 2);
            AssertSingleDiagnostic(
                mismatch,
                WorldGraphDiagnosticCode.GoalDayMismatch,
                "goal",
                "Goal 'goal' is reached on day 1, expected day 2.");
        }

        [Test]
        public void IndependentDuplicateAndBrokenReferenceAreReportedTogetherWithoutThrowing()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("duplicate", 1), Node("duplicate", 2), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-goal", "start", "goal"),
                    Edge("road.broken", "start", "missing.destination")
                });
            WorldGraphValidationResult result = null;

            Assert.DoesNotThrow(() => result = WorldGraphValidator.Validate(input));

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                Snapshot(result),
                Is.EqualTo(
                    new[]
                    {
                        "DuplicateNodeId|duplicate|Node ID 'duplicate' is declared 2 times and is ambiguous.",
                        "MissingEdgeDestination|road.broken|Edge 'road.broken' destination 'missing.destination' does not resolve to a declared node."
                    }));
        }

        [Test]
        public void InvalidMissingSourceDoesNotHideIndependentGoalDayMismatch()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-goal", "start", "goal"),
                    Edge("road.broken-source", "missing.source", "goal")
                });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input, 2);

            Assert.That(
                Snapshot(result),
                Is.EqualTo(
                    new[]
                    {
                        "MissingEdgeSource|road.broken-source|Edge 'road.broken-source' source 'missing.source' does not resolve to a declared node.",
                        "GoalDayMismatch|goal|Goal 'goal' is reached on day 1, expected day 2."
                    }));
        }

        [Test]
        public void LongerAmbiguousRouteDoesNotHideIndependentGoalDayMismatch()
        {
            WorldGraphValidationInput input = CreateInput(
                new[] { Node("start", 0), Node("detour", 1), Node("goal", 1) },
                new[]
                {
                    Edge("road.start-goal", "start", "goal"),
                    Edge("road.start-detour", "start", "detour"),
                    Edge("road.ambiguous", "detour", "goal"),
                    Edge("road.ambiguous", "detour", "goal")
                });

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input, 2);

            Assert.That(
                Snapshot(result),
                Is.EqualTo(
                    new[]
                    {
                        "DuplicateEdgeId|road.ambiguous|Edge ID 'road.ambiguous' is declared 2 times and is ambiguous.",
                        "GoalDayMismatch|goal|Goal 'goal' is reached on day 1, expected day 2."
                    }));
        }

        [Test]
        public void ReorderingMalformedInputProducesIdenticalOrderedDiagnostics()
        {
            List<WorldNodeDefinition> nodes = new List<WorldNodeDefinition>
            {
                Node("start", 0),
                Node("dead-end", 1),
                Node("duplicate", 1),
                Node("duplicate", 2),
                Node("goal", 2)
            };
            List<WorldEdgeDefinition> edges = new List<WorldEdgeDefinition>
            {
                Edge("road.start-goal", "start", "goal"),
                Edge("road.start-dead-end", "start", "dead-end"),
                Edge("road.broken", "start", "missing.destination")
            };
            WorldGraphValidationResult original = WorldGraphValidator.Validate(
                CreateInput(nodes, edges),
                5);
            WorldGraphValidationResult reversed = WorldGraphValidator.Validate(
                CreateInput(nodes.AsEnumerable().Reverse(), edges.AsEnumerable().Reverse()),
                5);

            Assert.That(Snapshot(reversed), Is.EqualTo(Snapshot(original)));
        }

        [Test]
        public void ValidationPreservesInputAndExposesReadOnlySnapshotsAndResults()
        {
            List<WorldNodeDefinition> sourceNodes = new List<WorldNodeDefinition>
            {
                Node("goal", 1),
                Node("start", 0)
            };
            List<WorldEdgeDefinition> sourceEdges = new List<WorldEdgeDefinition>
            {
                Edge("road.start-goal", "start", "goal")
            };
            WorldGraphValidationInput input = CreateInput(sourceNodes, sourceEdges);
            string[] nodeOrder = input.Nodes.Select(node => node.NodeId).ToArray();
            string[] edgeOrder = input.Edges.Select(edge => edge.EdgeId).ToArray();

            WorldGraphValidationResult result = WorldGraphValidator.Validate(input, 1);

            Assert.That(input.Nodes.Select(node => node.NodeId), Is.EqualTo(nodeOrder));
            Assert.That(input.Edges.Select(edge => edge.EdgeId), Is.EqualTo(edgeOrder));
            Assert.That(sourceNodes.Select(node => node.NodeId), Is.EqualTo(nodeOrder));
            Assert.That(sourceEdges.Select(edge => edge.EdgeId), Is.EqualTo(edgeOrder));
            Assert.Throws<NotSupportedException>(
                () => ((IList<WorldNodeDefinition>)input.Nodes).Clear());
            Assert.Throws<NotSupportedException>(
                () => ((IList<WorldEdgeDefinition>)input.Edges).Clear());
            Assert.Throws<NotSupportedException>(
                () => ((IList<WorldGraphValidationDiagnostic>)result.Diagnostics).Clear());
        }

        [Test]
        public void ValidationRemainsInUnityIndependentWorldAssembly()
        {
            string[] referencedAssemblies = typeof(WorldGraphValidator).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.That(
                referencedAssemblies.Any(
                    name => name.StartsWith("UnityEngine", StringComparison.Ordinal)),
                Is.False);
        }

        private static WorldGraphValidationInput CreateInput(
            IEnumerable<WorldNodeDefinition> nodes,
            IEnumerable<WorldEdgeDefinition> edges,
            string startNodeId = "start",
            string goalNodeId = "goal")
        {
            return new WorldGraphValidationInput(startNodeId, goalNodeId, nodes, edges);
        }

        private static WorldNodeDefinition Node(string nodeId, int distanceLayer)
        {
            return new WorldNodeDefinition(
                nodeId,
                0,
                distanceLayer,
                distanceLayer,
                "test-place",
                "test-biome");
        }

        private static WorldEdgeDefinition Edge(string edgeId, string fromNodeId, string toNodeId)
        {
            return new WorldEdgeDefinition(
                edgeId,
                fromNodeId,
                toNodeId,
                "north",
                "test-clue");
        }

        private static WorldDefinition LoadPrototypeWorld()
        {
            string path = Path.Combine(
                Application.dataPath,
                "GameData",
                "World",
                "prototype-world.json");
            return WorldDefinitionJson.Deserialize(File.ReadAllText(path));
        }

        private static string[] Snapshot(WorldGraphValidationResult result)
        {
            return result.Diagnostics
                .Select(diagnostic => $"{diagnostic.Code}|{diagnostic.Subject}|{diagnostic.Reason}")
                .ToArray();
        }

        private static void AssertSingleDiagnostic(
            WorldGraphValidationResult result,
            WorldGraphDiagnosticCode code,
            string subject,
            string reason)
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].Code, Is.EqualTo(code));
            Assert.That(result.Diagnostics[0].Subject, Is.EqualTo(subject));
            Assert.That(result.Diagnostics[0].Reason, Is.EqualTo(reason));
        }
    }
}