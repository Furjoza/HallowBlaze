using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using NUnit.Framework;
using UnityEngine;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class WorldDefinitionTests
    {
        [Test]
        public void RouteSegment_ValidatesStableGenerationIdentity()
        {
            RouteSegmentDefinition segment = new RouteSegmentDefinition("segment.test.entry", "legacy.board.v1", 1);
            Assert.That(segment.SegmentId, Is.EqualTo("segment.test.entry"));
            Assert.That(segment.GenerationConfigurationId, Is.EqualTo("legacy.board.v1"));
            Assert.That(segment.GenerationVersion, Is.EqualTo(1));
            foreach (string invalid in new[] { null, "", " ", " segment", "segment " })
            {
                Assert.Throws<ArgumentException>(() => new RouteSegmentDefinition(invalid, "config", 1));
                Assert.Throws<ArgumentException>(() => new RouteSegmentDefinition("segment", invalid, 1));
            }
            foreach (int invalid in new[] { int.MinValue, -1, 0 })
                Assert.Throws<ArgumentOutOfRangeException>(() => new RouteSegmentDefinition("segment", "config", invalid));
            Assert.That(typeof(RouteSegmentDefinition).GetProperties().All(property => property.SetMethod == null), Is.True);
        }

        [Test]
        public void PrototypeFixtureIdentityMatchesCurrentProfileContract()
        {
            WorldDefinition world = LoadPrototypeWorld();
            ProfileState profile = new ProfileState("profile-test", "world-1", 1);

            Assert.That(world.WorldDefinitionId, Is.EqualTo("world-1"));
            Assert.That(world.Version, Is.EqualTo(1));
            Assert.That(world.StartNodeId, Is.EqualTo("shelter.warm-up"));
            Assert.That(world.GoalNodeId, Is.EqualTo("landmark.radio-tower"));
            Assert.That(world.WorldDefinitionId, Is.EqualTo(profile.WorldDefinitionId));
            Assert.That(world.Version, Is.EqualTo(profile.WorldDefinitionVersion));
            Assert.That(world.TryGetNode(world.StartNodeId, out WorldNodeDefinition start), Is.True);
            Assert.That(world.TryGetNode(world.GoalNodeId, out WorldNodeDefinition goal), Is.True);
            Assert.That(start.DistanceLayer, Is.Zero);
            Assert.That(goal.DistanceLayer, Is.EqualTo(6));
            Assert.That(goal.PlaceKind, Is.EqualTo("landmark"));
        }

        [Test]
        public void PrototypeFixtureIdsEndpointsAndTopologyMatchContract()
        {
            WorldDefinition world = LoadPrototypeWorld();

            Assert.That(world.Nodes, Has.Count.EqualTo(9));
            Assert.That(world.Edges, Has.Count.EqualTo(10));
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
                        "road.shelter-warm-up|shelter.warm-up|forest.start|north",
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
            Assert.That(routes.Select(route => route.Count), Is.All.EqualTo(6));
            Assert.That(
                routes.Select(RouteSnapshot).OrderBy(route => route, StringComparer.Ordinal),
                Is.EqualTo(
                    new[]
                    {
                        "road.shelter-warm-up>road.start-east-creek>road.east-creek-old-road>road.old-road-east-quarry>road.east-quarry-ridge>road.ridge-radio-tower",
                        "road.shelter-warm-up>road.start-east-creek>road.east-creek-old-road>road.old-road-west-grove>road.west-grove-ridge>road.ridge-radio-tower",
                        "road.shelter-warm-up>road.start-west-trail>road.west-trail-old-road>road.old-road-east-quarry>road.east-quarry-ridge>road.ridge-radio-tower",
                        "road.shelter-warm-up>road.start-west-trail>road.west-trail-old-road>road.old-road-west-grove>road.west-grove-ridge>road.ridge-radio-tower"
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
            Assert.That(
                world.GetOutgoingEdges(world.StartNodeId).Select(road => road.EdgeId),
                Is.EqualTo(new[] { "road.shelter-warm-up" }));
            WorldEdgeDefinition opening = world.GetOutgoingEdges(world.StartNodeId).Single();
            Assert.That(opening.ToNodeId, Is.EqualTo("forest.start"));
            Assert.That(opening.Segments, Has.Count.EqualTo(1));
            Assert.That(opening.Segments[0].SegmentId, Is.EqualTo("segment.shelter-warm-up.day1"));
            Assert.That(opening.Segments[0].GenerationConfigurationId, Is.EqualTo("ordinary.board.v1"));
            Assert.That(opening.Segments[0].GenerationVersion, Is.EqualTo(2));
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

            Assert.That(world.Nodes, Has.Count.EqualTo(9));
            Assert.That(world.Edges, Has.Count.EqualTo(10));
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

        /// <summary>Checks that the opening is a durable single day and never repeats its arrival on retry or Continue.</summary>
        [Test]
        public void OpeningTravelRoundTripCompletesOnceBeforeSelectedDayTwo()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M71-Travel-" + Guid.NewGuid().ToString("N"));
            try
            {
                WorldDefinition world = LoadPrototypeWorld();
                var session = new GameSession(new ProfileState("opening-profile", world.WorldDefinitionId, world.Version));
                session.Profile.DiscoverFact("known-before-opening");
                session.StartNewRun("opening-run", 12346, new RunStateConfiguration(100, 100, 0, world.StartNodeId));
                var store = new FileSystemSaveStore(root);
                var map = new WorldMapService(world, session, store);
                Assert.That(map.EnterCurrentNode().IsSuccess, Is.True);
                Assert.That(map.Travel.StartRoute("road.shelter-warm-up").IsSuccess, Is.True);
                BoardRequest pending = BoardRequest.CreateFromRun(session.ActiveRun, world);
                Assert.That(session.ActiveRun.CurrentDay, Is.EqualTo(1));
                Assert.That(session.ActiveRun.CompletedDayCount, Is.Zero);
                Assert.That(map.BeginRouteChoice().IsSuccess, Is.False);

                SaveStoreResult<RunState> saved = store.LoadRun();
                Assert.That(saved.IsSuccess, Is.True);
                session.ContinueRun(saved.Data);
                map = new WorldMapService(world, session, store);
                Assert.That(BoardRequest.CreateFromRun(session.ActiveRun, world).Address, Is.EqualTo(pending.Address));
                Assert.That(map.Travel.ConsumeIntro(out bool showIntro).IsSuccess, Is.True);
                Assert.That(showIntro, Is.True);
                Assert.That(map.Travel.ConsumeIntro(out showIntro).IsSuccess, Is.True);
                Assert.That(showIntro, Is.False);

                Assert.That(map.Travel.TryCompleteSegment(pending, out SaveStoreResult checkpoint, out bool arrived), Is.True);
                Assert.That(checkpoint.IsSuccess, Is.True);
                Assert.That(arrived, Is.True);
                Assert.That(map.Travel.TryCompleteSegment(pending, out _, out _), Is.False);
                Assert.That(map.ReconcileCompletedTravel().IsSuccess, Is.True);
                Assert.That(map.BeginRouteChoice().IsSuccess, Is.True);
                Assert.That(session.ActiveRun.CurrentDay, Is.EqualTo(1));
                Assert.That(session.ActiveRun.CompletedDayCount, Is.EqualTo(1));
                Assert.That(session.ActiveRun.TravelHistory, Has.Count.EqualTo(1));
                Assert.That(session.ActiveRun.WorldNodeId, Is.EqualTo("forest.start"));

                Assert.That(store.LoadRun().IsSuccess, Is.True);
                session.ContinueRun(store.LoadRun().Data);
                map = new WorldMapService(world, session, store);
                Assert.That(map.ReconcileCompletedTravel().IsSuccess, Is.True);
                Assert.That(map.BeginRouteChoice().IsSuccess, Is.True);
                Assert.That(map.ChooseRoute("road.start-east-creek").Status, Is.EqualTo(RouteChoiceCommandStatus.Applied));
                Assert.That(session.ActiveRun.ActiveLeg.DayNumber, Is.EqualTo(2));
                Assert.That(session.ActiveRun.CompletedDayCount, Is.EqualTo(1));
                Assert.That(session.ActiveRun.TraversalSequence, Is.EqualTo(2));
                Assert.That(session.Profile.DiscoveredFactIds, Does.Contain("known-before-opening"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static WorldDefinition LoadPrototypeWorld()
        {
            return WorldDefinitionJson.Deserialize(LoadPrototypeJson());
        }

        private static string LoadPrototypeJson()
        {
            string path = Path.Combine(
                Application.dataPath,
                "GameData",
                "World",
                "prototype-world.json");
            return File.ReadAllText(path);
        }

        [Test]
        public void EdgeSegments_AreOrderedDetachedAndNonempty()
        {
            List<RouteSegmentDefinition> source = new List<RouteSegmentDefinition>
            {
                new RouteSegmentDefinition("second", "config", 1),
                new RouteSegmentDefinition("first", "config", 1)
            };
            WorldEdgeDefinition edge = new WorldEdgeDefinition("edge", "a", "b", "north", "clue", source);
            source.Clear();
            Assert.That(edge.Segments.Select(segment => segment.SegmentId), Is.EqualTo(new[] { "second", "first" }));
            Assert.That(edge.SegmentCount, Is.EqualTo(2));
            Assert.Throws<NotSupportedException>(() => ((IList<RouteSegmentDefinition>)edge.Segments).Clear());
            Assert.Throws<ArgumentNullException>(() => new WorldEdgeDefinition("edge", "a", "b", "north", "clue", null));
            Assert.Throws<ArgumentException>(() => new WorldEdgeDefinition("edge", "a", "b", "north", "clue", source));
            Assert.Throws<ArgumentException>(() => new WorldEdgeDefinition("edge", "a", "b", "north", "clue",
                new[] { edge.Segments[0], edge.Segments[0] }));
            Assert.Throws<ArgumentNullException>(() => new WorldEdgeDefinition("edge", "a", "b", "north", "clue",
                new RouteSegmentDefinition[] { null }));
            WorldEdgeDefinition legacy = new WorldEdgeDefinition("edge", "a", "b", "north", "clue");
            Assert.That(legacy.Segments[0].SegmentId, Is.EqualTo("legacy.segment:edge"));
            Assert.That(legacy.Segments[0].GenerationConfigurationId, Is.EqualTo("legacy.board.v1"));
        }

        [Test]
        public void Catalogue_RejectsGlobalSegmentAliases()
        {
            WorldDefinition original = LoadPrototypeWorld();
            WorldEdgeDefinition first = original.Edges[0];
            WorldEdgeDefinition alias = new WorldEdgeDefinition("alias", first.FromNodeId, first.ToNodeId,
                first.WorldDirection, first.ClueKey, first.Segments);
            string message = Assert.Throws<ArgumentException>(() => new WorldDefinition("world-1", 1,
                original.StartNodeId, original.GoalNodeId, original.Nodes, new[] { first, alias })).Message;
            Assert.That(message, Does.Contain(first.Segments[0].SegmentId).And.Contain("alias").And.Contain(first.EdgeId));
            WorldDefinition reversed = new WorldDefinition("world-1", 1, original.StartNodeId,
                original.GoalNodeId, original.Nodes.Reverse(), original.Edges.Reverse());
            foreach (WorldEdgeDefinition edge in original.Edges)
                foreach (RouteSegmentDefinition expected in edge.Segments)
                {
                    Assert.That(reversed.TryGetSegment(expected.SegmentId, out RouteSegmentDefinition segment,
                        out WorldEdgeDefinition owner), Is.True);
                    Assert.That(segment, Is.SameAs(expected));
                    Assert.That(owner.EdgeId, Is.EqualTo(edge.EdgeId));
                }
            Assert.That(reversed.TryGetSegment("missing", out _, out _), Is.False);
        }

        [Test]
        public void WorldJson_ReadsExplicitSegmentsAndRejectsMalformedRows()
        {
            const string prefix = "{\"worldDefinitionId\":\"world-1\",\"version\":1,\"startNodeId\":\"a\",\"goalNodeId\":\"b\",\"nodes\":[],\"edges\":[{\"edgeId\":\"edge\",\"fromNodeId\":\"a\",\"toNodeId\":\"b\",\"direction\":\"north\",\"clueKey\":\"clue\"";
            const string suffix = "}]}";
            Assert.That(WorldDefinitionJson.Deserialize(prefix + suffix).TryGetEdge("edge",
                out WorldEdgeDefinition legacy), Is.True);
            Assert.That(legacy.Segments[0].SegmentId, Is.EqualTo("legacy.segment:" + legacy.EdgeId));
            foreach (string bad in new[] { "null", "[]", "[null]", "[{}]",
                "[{\"segmentId\":\"s\",\"generationConfigurationId\":\"c\",\"generationVersion\":0}]",
                "[{\"segmentId\":\"s\",\"generationConfigurationId\":\"c\",\"generationVersion\":1,\"extra\":true}]" })
            {
                Assert.That(() => WorldDefinitionJson.Deserialize(prefix + ",\"segments\":" + bad + suffix), Throws.Exception, bad);
            }
            WorldDefinition explicitWorld = WorldDefinitionJson.Deserialize(prefix + ",\"segments\":[{\"segmentId\":\"s\",\"generationConfigurationId\":\"c\",\"generationVersion\":1}]" + suffix);
            Assert.That(explicitWorld.TryGetSegment("s", out RouteSegmentDefinition segment, out _), Is.True);
            Assert.That(segment.GenerationConfigurationId, Is.EqualTo("c"));
            Assert.That(() => WorldDefinitionJson.Deserialize("{\"version\":1,\"version\":2}"), Throws.Exception);
        }

        [Test]
        public void PrototypeWorld_HasFrozenExplicitSegmentRegistry()
        {
            WorldDefinition world = LoadPrototypeWorld();
            string[] roads = { "east-creek-old-road", "east-quarry-ridge", "old-road-east-quarry",
                "old-road-west-grove", "ridge-radio-tower", "shelter-warm-up", "start-east-creek", "start-west-trail",
                "west-grove-ridge", "west-trail-old-road" };
            string[] names = { "entry", "stretch", "clearing", "approach", "arrival" };
            Assert.That(world.Edges.Select(edge => edge.EdgeId), Is.EqualTo(roads.Select(road => "road." + road)));
            foreach (string road in roads)
            {
                Assert.That(world.TryGetEdge("road." + road, out WorldEdgeDefinition edge), Is.True);
                Assert.That(edge.Segments.Select(segment => segment.SegmentId),
                    Is.EqualTo((road == "shelter-warm-up" ? new[] { "day1" } : names)
                        .Select(name => "segment." + road + "." + name)));
                foreach (RouteSegmentDefinition segment in edge.Segments)
                    if (segment.SegmentId == "segment.ridge-radio-tower.arrival")
                    {
                        Assert.That(segment.GenerationConfigurationId, Is.EqualTo("landmark.radio-tower.board"));
                        Assert.That(segment.GenerationVersion, Is.EqualTo(1));
                    }
                    else
                    {
                        Assert.That(segment.GenerationConfigurationId, Is.EqualTo("ordinary.board.v1"));
                        Assert.That(segment.GenerationVersion, Is.EqualTo(2));
                    }
            }
            Assert.That(world.Edges.SelectMany(edge => edge.Segments).Select(segment => segment.SegmentId), Is.Unique);
            Assert.That(world.WorldDefinitionId, Is.EqualTo("world-1"));
            Assert.That(world.Version, Is.EqualTo(1));
        }

        [TestCase("123", "\"config\"", "1")]
        [TestCase("\"segment\"", "123", "1")]
        [TestCase("\"segment\"", "\"config\"", "1.25")]
        public void WorldJson_RejectsCoercedSegmentTypes(string segment, string configuration, string version)
        {
            string json = "{\"worldDefinitionId\":\"world\",\"version\":1,\"startNodeId\":\"a\",\"goalNodeId\":\"b\",\"nodes\":[],\"edges\":[{\"edgeId\":\"edge\",\"fromNodeId\":\"a\",\"toNodeId\":\"b\",\"direction\":\"north\",\"clueKey\":\"clue\",\"segments\":[{\"segmentId\":" + segment +
                ",\"generationConfigurationId\":" + configuration + ",\"generationVersion\":" + version + "}]}]}";
            Assert.That(() => WorldDefinitionJson.Deserialize(json), Throws.Exception);
        }

        private static string EdgeSnapshot(WorldEdgeDefinition edge)
        {
            return $"{edge.EdgeId}|{edge.FromNodeId}|{edge.ToNodeId}|{edge.WorldDirection}";
        }

        [TestCase("segmentId", "SegmentId")]
        [TestCase("segments", "Segments")]
        public void WorldJson_RejectsCaseAliases(string field, string alias)
        {
            string json = LoadPrototypeJson().Replace("\"" + field + "\"", "\"" + alias + "\"");
            Assert.That(() => WorldDefinitionJson.Deserialize(json), Throws.Exception);
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