using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using NUnit.Framework;
using UnityEngine;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class WorldDefinitionTests
    {
        [Test]
        public void PrototypeFixtureIdentityMatchesCurrentProfileContract()
        {
            WorldDefinition world = LoadPrototypeWorld();
            ProfileState profile = new ProfileState("profile-test", "world-1", 1);

            Assert.That(world.WorldDefinitionId, Is.EqualTo("world-1"));
            Assert.That(world.Version, Is.EqualTo(1));
            Assert.That(world.StartNodeId, Is.EqualTo("forest.start"));
            Assert.That(world.GoalNodeId, Is.EqualTo("landmark.radio-tower"));
            Assert.That(world.WorldDefinitionId, Is.EqualTo(profile.WorldDefinitionId));
            Assert.That(world.Version, Is.EqualTo(profile.WorldDefinitionVersion));
            Assert.That(world.TryGetNode(world.StartNodeId, out WorldNodeDefinition start), Is.True);
            Assert.That(world.TryGetNode(world.GoalNodeId, out WorldNodeDefinition goal), Is.True);
            Assert.That(start.DistanceLayer, Is.Zero);
            Assert.That(goal.DistanceLayer, Is.EqualTo(5));
            Assert.That(goal.PlaceKind, Is.EqualTo("landmark"));
        }

        [Test]
        public void PrototypeFixtureIdsEndpointsAndTopologyMatchContract()
        {
            WorldDefinition world = LoadPrototypeWorld();

            Assert.That(world.Nodes, Has.Count.EqualTo(8));
            Assert.That(world.Edges, Has.Count.EqualTo(9));
            Assert.That(
                world.Nodes.Select(node => node.NodeId),
                Is.Unique.And.All.Not.Empty);
            Assert.That(
                world.Edges.Select(edge => edge.EdgeId),
                Is.Unique.And.All.Not.Empty);

            foreach (WorldEdgeDefinition edge in world.Edges)
            {
                Assert.That(
                    world.TryGetNode(edge.FromNodeId, out _),
                    Is.True,
                    $"Unknown source on {edge.EdgeId}.");
                Assert.That(
                    world.TryGetNode(edge.ToNodeId, out _),
                    Is.True,
                    $"Unknown destination on {edge.EdgeId}.");
            }

            Assert.That(
                world.Edges.Select(EdgeSnapshot),
                Is.EqualTo(
                    new[]
                    {
                        "road.east-creek-old-road|forest.east-creek|forest.old-road|northwest",
                        "road.east-quarry-ridge|forest.east-quarry|forest.ridge|northwest",
                        "road.old-road-east-quarry|forest.old-road|forest.east-quarry|northeast",
                        "road.old-road-west-grove|forest.old-road|forest.west-grove|northwest",
                        "road.ridge-radio-tower|forest.ridge|landmark.radio-tower|north",
                        "road.start-east-creek|forest.start|forest.east-creek|northeast",
                        "road.start-west-trail|forest.start|forest.west-trail|northwest",
                        "road.west-grove-ridge|forest.west-grove|forest.ridge|northeast",
                        "road.west-trail-old-road|forest.west-trail|forest.old-road|northeast"
                    }));

            Assert.That(
                world.Nodes
                    .Where(node => world.GetOutgoingEdges(node.NodeId).Count > 1)
                    .Select(node => node.NodeId),
                Is.EqualTo(new[] { "forest.old-road", "forest.start" }));
            Assert.That(
                world.Edges
                    .GroupBy(edge => edge.ToNodeId, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .OrderBy(nodeId => nodeId, StringComparer.Ordinal),
                Is.EqualTo(new[] { "forest.old-road", "forest.ridge" }));

            IReadOnlyList<IReadOnlyList<WorldEdgeDefinition>> routes = FindRoutes(
                world,
                world.StartNodeId,
                world.GoalNodeId);
            Assert.That(routes, Has.Count.EqualTo(4));
            Assert.That(routes.Select(route => route.Count), Is.All.EqualTo(5));
            Assert.That(
                routes.Select(RouteSnapshot).OrderBy(route => route, StringComparer.Ordinal),
                Is.EqualTo(
                    new[]
                    {
                        "road.start-east-creek>road.east-creek-old-road>road.old-road-east-quarry>road.east-quarry-ridge>road.ridge-radio-tower",
                        "road.start-east-creek>road.east-creek-old-road>road.old-road-west-grove>road.west-grove-ridge>road.ridge-radio-tower",
                        "road.start-west-trail>road.west-trail-old-road>road.old-road-east-quarry>road.east-quarry-ridge>road.ridge-radio-tower",
                        "road.start-west-trail>road.west-trail-old-road>road.old-road-west-grove>road.west-grove-ridge>road.ridge-radio-tower"
                    }));
            Assert.That(routes[0].Count + 1, Is.LessThan(world.Nodes.Count));
            Assert.That(
                world.Nodes.Select(node => node.AtlasY),
                Is.EqualTo(world.Nodes.Select(node => node.DistanceLayer)));
        }

        [Test]
        public void LookupAndOutgoingEdgesHaveControlledUnknownResults()
        {
            WorldDefinition world = LoadPrototypeWorld();

            Assert.That(world.TryGetNode("forest.old-road", out WorldNodeDefinition node), Is.True);
            Assert.That(node.PlaceKind, Is.EqualTo("crossroads"));
            Assert.That(world.TryGetNode("unknown.node", out WorldNodeDefinition unknownNode), Is.False);
            Assert.That(unknownNode, Is.Null);

            Assert.That(
                world.TryGetEdge("road.ridge-radio-tower", out WorldEdgeDefinition edge),
                Is.True);
            Assert.That(edge.ToNodeId, Is.EqualTo(world.GoalNodeId));
            Assert.That(world.TryGetEdge("unknown.edge", out WorldEdgeDefinition unknownEdge), Is.False);
            Assert.That(unknownEdge, Is.Null);

            IReadOnlyList<WorldEdgeDefinition> outgoing = world.GetOutgoingEdges("forest.start");
            Assert.That(
                outgoing.Select(road => road.EdgeId),
                Is.EqualTo(new[] { "road.start-east-creek", "road.start-west-trail" }));
            Assert.That(
                outgoing.Select(road => road.ToNodeId),
                Is.EqualTo(new[] { "forest.east-creek", "forest.west-trail" }));
            Assert.That(world.GetOutgoingEdges(world.GoalNodeId), Is.Empty);
            Assert.Throws<KeyNotFoundException>(() => world.GetOutgoingEdges("unknown.node"));
        }

        [Test]
        public void ReversingInputCollectionsDoesNotChangeCatalogueResults()
        {
            WorldDefinition original = LoadPrototypeWorld();
            WorldDefinition reversed = new WorldDefinition(
                original.WorldDefinitionId,
                original.Version,
                original.StartNodeId,
                original.GoalNodeId,
                original.Nodes.Reverse(),
                original.Edges.Reverse());

            Assert.That(
                reversed.Nodes.Select(node => node.NodeId),
                Is.EqualTo(original.Nodes.Select(node => node.NodeId)));
            Assert.That(
                reversed.Edges.Select(EdgeSnapshot),
                Is.EqualTo(original.Edges.Select(EdgeSnapshot)));

            foreach (WorldNodeDefinition expectedNode in original.Nodes)
            {
                Assert.That(reversed.TryGetNode(expectedNode.NodeId, out WorldNodeDefinition actualNode), Is.True);
                Assert.That(actualNode.NodeId, Is.EqualTo(expectedNode.NodeId));
                Assert.That(
                    reversed.GetOutgoingEdges(expectedNode.NodeId).Select(edge => edge.EdgeId),
                    Is.EqualTo(original.GetOutgoingEdges(expectedNode.NodeId).Select(edge => edge.EdgeId)));
            }

            foreach (WorldEdgeDefinition expectedEdge in original.Edges)
            {
                Assert.That(reversed.TryGetEdge(expectedEdge.EdgeId, out WorldEdgeDefinition actualEdge), Is.True);
                Assert.That(EdgeSnapshot(actualEdge), Is.EqualTo(EdgeSnapshot(expectedEdge)));
            }
        }

        [Test]
        public void CatalogueDefensivelyCopiesInputsAndExposesReadOnlyViews()
        {
            WorldDefinition fixture = LoadPrototypeWorld();
            List<WorldNodeDefinition> sourceNodes = fixture.Nodes.ToList();
            List<WorldEdgeDefinition> sourceEdges = fixture.Edges.ToList();
            WorldDefinition world = new WorldDefinition(
                fixture.WorldDefinitionId,
                fixture.Version,
                fixture.StartNodeId,
                fixture.GoalNodeId,
                sourceNodes,
                sourceEdges);

            sourceNodes.Clear();
            sourceEdges.Clear();

            Assert.That(world.Nodes, Has.Count.EqualTo(8));
            Assert.That(world.Edges, Has.Count.EqualTo(9));
            Assert.Throws<NotSupportedException>(
                () => ((IList<WorldNodeDefinition>)world.Nodes).Clear());
            Assert.Throws<NotSupportedException>(
                () => ((IList<WorldEdgeDefinition>)world.Edges).Clear());
            Assert.Throws<NotSupportedException>(
                () => ((IList<WorldEdgeDefinition>)world.GetOutgoingEdges("forest.start")).Clear());
            Assert.That(world.TryGetNode("forest.start", out _), Is.True);
            Assert.That(world.GetOutgoingEdges("forest.start"), Has.Count.EqualTo(2));
        }

        [Test]
        public void WorldAssemblyHasNoUnityEngineDependency()
        {
            string[] referencedAssemblies = typeof(WorldDefinition).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.That(
                referencedAssemblies.Any(
                    name => name.StartsWith("UnityEngine", StringComparison.Ordinal)),
                Is.False);
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

        private static string EdgeSnapshot(WorldEdgeDefinition edge)
        {
            return $"{edge.EdgeId}|{edge.FromNodeId}|{edge.ToNodeId}|{edge.WorldDirection}";
        }

        private static string RouteSnapshot(IReadOnlyList<WorldEdgeDefinition> route)
        {
            return string.Join(">", route.Select(edge => edge.EdgeId));
        }

        private static IReadOnlyList<IReadOnlyList<WorldEdgeDefinition>> FindRoutes(
            WorldDefinition world,
            string startNodeId,
            string goalNodeId)
        {
            List<IReadOnlyList<WorldEdgeDefinition>> routes =
                new List<IReadOnlyList<WorldEdgeDefinition>>();
            CollectRoutes(
                world,
                startNodeId,
                goalNodeId,
                new HashSet<string>(StringComparer.Ordinal),
                new List<WorldEdgeDefinition>(),
                routes);
            return routes;
        }

        private static void CollectRoutes(
            WorldDefinition world,
            string nodeId,
            string goalNodeId,
            HashSet<string> visitedNodeIds,
            List<WorldEdgeDefinition> currentRoute,
            List<IReadOnlyList<WorldEdgeDefinition>> routes)
        {
            if (string.Equals(nodeId, goalNodeId, StringComparison.Ordinal))
            {
                routes.Add(currentRoute.ToArray());
                return;
            }

            if (!visitedNodeIds.Add(nodeId))
                return;

            foreach (WorldEdgeDefinition edge in world.GetOutgoingEdges(nodeId))
            {
                currentRoute.Add(edge);
                CollectRoutes(
                    world,
                    edge.ToNodeId,
                    goalNodeId,
                    visitedNodeIds,
                    currentRoute,
                    routes);
                currentRoute.RemoveAt(currentRoute.Count - 1);
            }

            visitedNodeIds.Remove(nodeId);
        }
    }
}