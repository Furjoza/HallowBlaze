using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class WorldMapServiceTests
    {
        [Test]
        public void ConstructorRequiresDependenciesAndMatchingValidWorld()
        {
            WorldDefinition world = CreateWorld();
            GameSession session = CreateSession();
            FakeSaveStore store = new FakeSaveStore();

            Assert.DoesNotThrow(() => new WorldMapService(world, session, store));
            Assert.Throws<ArgumentNullException>(() => new WorldMapService(null, session, store));
            Assert.Throws<ArgumentNullException>(() => new WorldMapService(world, null, store));
            Assert.Throws<ArgumentNullException>(() => new WorldMapService(world, session, null));

            GameSession wrongWorldSession = CreateSession(
                new ProfileState("profile", "another-world", 1));
            GameSession wrongVersionSession = CreateSession(
                new ProfileState("profile", WorldId, 2));
            Assert.Throws<InvalidOperationException>(
                () => new WorldMapService(world, wrongWorldSession, store));
            Assert.Throws<InvalidOperationException>(
                () => new WorldMapService(world, wrongVersionSession, store));

            ProfileState invalidNodeProfile = CreateProfile();
            invalidNodeProfile.AdvanceNodeDiscovery("node.missing", NodeDiscoveryState.Sighted);
            Assert.Throws<InvalidOperationException>(
                () => new WorldMapService(
                    world,
                    CreateSession(invalidNodeProfile),
                    store));

            ProfileState invalidEdgeProfile = CreateProfile();
            invalidEdgeProfile.AdvanceEdgeDiscovery("road.missing", EdgeDiscoveryState.Sighted);
            Assert.Throws<InvalidOperationException>(
                () => new WorldMapService(
                    world,
                    CreateSession(invalidEdgeProfile),
                    store));

            WorldDefinition unresolvedWorld = new WorldDefinition(
                WorldId,
                WorldVersion,
                StartNodeId,
                GoalNodeId,
                CreateNodes(),
                new[]
                {
                    new WorldEdgeDefinition(
                        "road.invalid",
                        StartNodeId,
                        "node.missing",
                        "north",
                        "clue.invalid")
                });
            Assert.Throws<InvalidOperationException>(
                () => new WorldMapService(unresolvedWorld, session, store));
        }

        [Test]
        public void AtlasSnapshotHonorsKnowledgeLevelsAndHidesUnknownTopology()
        {
            ProfileState profile = CreateProfile();
            profile.AdvanceNodeDiscovery(StartNodeId, NodeDiscoveryState.Rumored);
            profile.AdvanceNodeDiscovery(WestNodeId, NodeDiscoveryState.Sighted);
            profile.AdvanceNodeDiscovery(EastNodeId, NodeDiscoveryState.Visited);
            profile.AdvanceEdgeDiscovery(StartEastEdgeId, EdgeDiscoveryState.Sighted);
            profile.AdvanceEdgeDiscovery(StartWestEdgeId, EdgeDiscoveryState.Sighted);
            profile.AdvanceEdgeDiscovery(WestGoalEdgeId, EdgeDiscoveryState.Traversed);
            WorldMapService service = CreateService(profile);

            WorldMapSnapshot snapshot = service.GetAtlasSnapshot();

            Assert.That(
                snapshot.Nodes.Select(node => node.NodeId),
                Is.EqualTo(new[] { EastNodeId, StartNodeId, WestNodeId }));
            Assert.That(
                snapshot.Edges.Select(edge => edge.EdgeId),
                Is.EqualTo(new[] { StartEastEdgeId, StartWestEdgeId }));
            Assert.That(snapshot.Nodes.Any(node => node.NodeId == GoalNodeId), Is.False);
            Assert.That(snapshot.Edges.Any(edge => edge.EdgeId == WestGoalEdgeId), Is.False);

            WorldMapNodeView rumored = GetNode(snapshot, StartNodeId);
            Assert.That(rumored.DiscoveryState, Is.EqualTo(NodeDiscoveryState.Rumored));
            Assert.That(rumored.AtlasX, Is.Null);
            Assert.That(rumored.AtlasY, Is.Null);
            Assert.That(rumored.DistanceLayer, Is.Null);
            Assert.That(rumored.PlaceKind, Is.Null);
            Assert.That(rumored.BiomeFamily, Is.Null);

            WorldMapNodeView sighted = GetNode(snapshot, WestNodeId);
            Assert.That(sighted.DiscoveryState, Is.EqualTo(NodeDiscoveryState.Sighted));
            Assert.That(sighted.AtlasX, Is.EqualTo(-1));
            Assert.That(sighted.AtlasY, Is.EqualTo(1));
            Assert.That(sighted.DistanceLayer, Is.EqualTo(1));
            Assert.That(sighted.PlaceKind, Is.Null);
            Assert.That(sighted.BiomeFamily, Is.Null);

            WorldMapNodeView visited = GetNode(snapshot, EastNodeId);
            Assert.That(visited.DiscoveryState, Is.EqualTo(NodeDiscoveryState.Visited));
            Assert.That(visited.PlaceKind, Is.EqualTo("creek"));
            Assert.That(visited.BiomeFamily, Is.EqualTo("wet-forest"));

            Assert.Throws<NotSupportedException>(
                () => ((IList<WorldMapNodeView>)snapshot.Nodes).Clear());
            Assert.Throws<NotSupportedException>(
                () => ((IList<WorldMapEdgeView>)snapshot.Edges).Clear());

            profile.AdvanceNodeDiscovery(GoalNodeId, NodeDiscoveryState.Visited);
            Assert.That(snapshot.Nodes, Has.Count.EqualTo(3));
            Assert.That(service.GetAtlasSnapshot().Nodes, Has.Count.EqualTo(4));
        }

        [Test]
        public void LegalExitsAreUnambiguousAndDoNotMutateDiscovery()
        {
            ProfileState profile = CreateProfile();
            FakeSaveStore store = new FakeSaveStore();
            WorldMapService service = CreateService(profile, store);

            WorldMapExitQueryResult result = service.GetLegalExits();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(
                result.Exits.Select(exit => exit.EdgeId),
                Is.EqualTo(new[] { StartEastEdgeId, StartWestEdgeId }));
            Assert.That(result.Exits.Select(exit => exit.EdgeId), Is.Unique);
            Assert.That(result.Exits[0].WorldDirection, Is.EqualTo("northeast"));
            Assert.That(result.Exits[0].ClueKey, Is.EqualTo("clue.running-water"));
            Assert.Throws<NotSupportedException>(
                () => ((IList<WorldMapExitOption>)result.Exits).Clear());
            Assert.That(profile.NodeDiscoveries, Is.Empty);
            Assert.That(profile.EdgeDiscoveries, Is.Empty);
            Assert.That(store.SaveProfileCallCount, Is.Zero);
        }

        [Test]
        public void EnterCurrentNodeSavesAndPublishesOnceWithoutChangingRun()
        {
            ProfileState profile = CreateProfile();
            profile.AdvanceNodeDiscovery(StartNodeId, NodeDiscoveryState.Rumored);
            FakeSaveStore store = new FakeSaveStore();
            GameSession session = CreateSession(profile);
            WorldMapService service = new WorldMapService(CreateWorld(), session, store);
            List<WorldDiscoveryEvent> events = Subscribe(service);
            RunState run = session.GetCurrentRun();
            string originalNodeId = run.WorldNodeId;
            int originalDay = run.CurrentDay;
            string[] originalRoute = run.Route.ToArray();

            WorldMapCommandResult first = service.EnterCurrentNode();
            WorldMapCommandResult second = service.EnterCurrentNode();

            Assert.That(first.Status, Is.EqualTo(WorldMapCommandStatus.Applied));
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.Status, Is.EqualTo(WorldMapCommandStatus.NoChange));
            Assert.That(profile.GetNodeDiscoveryState(StartNodeId), Is.EqualTo(NodeDiscoveryState.Visited));
            Assert.That(store.SaveProfileCallCount, Is.EqualTo(1));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0], Is.SameAs(first.DiscoveryEvent));
            Assert.That(events[0].Operation, Is.EqualTo(WorldDiscoveryOperation.EnteredNode));
            Assert.That(events[0].OperationId, Is.EqualTo(StartNodeId));
            Assert.That(events[0].Changes, Has.Count.EqualTo(1));
            Assert.That(events[0].Changes[0].NodeId, Is.EqualTo(StartNodeId));
            Assert.That(events[0].Changes[0].NodeState, Is.EqualTo(NodeDiscoveryState.Visited));
            AssertRunUnchanged(run, originalNodeId, originalDay, originalRoute);
        }

        [Test]
        public void ObserveCurrentExitsAggregatesChangesAndIsIdempotent()
        {
            ProfileState profile = CreateProfile();
            profile.AdvanceNodeDiscovery(StartNodeId, NodeDiscoveryState.Visited);
            FakeSaveStore store = new FakeSaveStore();
            GameSession session = CreateSession(profile);
            WorldMapService service = new WorldMapService(CreateWorld(), session, store);
            List<WorldDiscoveryEvent> events = Subscribe(service);
            RunState run = session.GetCurrentRun();

            WorldMapCommandResult first = service.ObserveCurrentExits();
            WorldMapCommandResult second = service.ObserveCurrentExits();

            Assert.That(first.Status, Is.EqualTo(WorldMapCommandStatus.Applied));
            Assert.That(second.Status, Is.EqualTo(WorldMapCommandStatus.NoChange));
            Assert.That(profile.GetEdgeDiscoveryState(StartEastEdgeId), Is.EqualTo(EdgeDiscoveryState.Sighted));
            Assert.That(profile.GetEdgeDiscoveryState(StartWestEdgeId), Is.EqualTo(EdgeDiscoveryState.Sighted));
            Assert.That(profile.GetNodeDiscoveryState(EastNodeId), Is.EqualTo(NodeDiscoveryState.Sighted));
            Assert.That(profile.GetNodeDiscoveryState(WestNodeId), Is.EqualTo(NodeDiscoveryState.Sighted));
            Assert.That(store.SaveProfileCallCount, Is.EqualTo(1));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Operation, Is.EqualTo(WorldDiscoveryOperation.ObservedExits));
            Assert.That(events[0].Changes, Has.Count.EqualTo(4));
            Assert.That(events[0].Changes.Count(change => change.EdgeId != null), Is.EqualTo(2));
            Assert.That(events[0].Changes.Count(change => change.NodeId != null), Is.EqualTo(2));
            Assert.That(service.GetAtlasSnapshot().Edges, Has.Count.EqualTo(2));
            AssertRunUnchanged(run, StartNodeId, 0, Array.Empty<string>());
        }

        [Test]
        public void CompleteTraversalRequiresCurrentOutgoingEdgeAndDoesNotMoveRun()
        {
            ProfileState profile = CreateProfile();
            profile.AdvanceEdgeDiscovery(StartEastEdgeId, EdgeDiscoveryState.Sighted);
            FakeSaveStore store = new FakeSaveStore();
            GameSession session = CreateSession(profile);
            WorldMapService service = new WorldMapService(CreateWorld(), session, store);
            List<WorldDiscoveryEvent> events = Subscribe(service);
            RunState run = session.GetCurrentRun();

            WorldMapCommandResult unknown = service.CompleteTraversal("road.missing");
            WorldMapCommandResult illegal = service.CompleteTraversal(WestGoalEdgeId);
            WorldMapCommandResult first = service.CompleteTraversal(StartEastEdgeId);
            WorldMapCommandResult second = service.CompleteTraversal(StartEastEdgeId);

            Assert.That(unknown.Status, Is.EqualTo(WorldMapCommandStatus.Rejected));
            Assert.That(illegal.Status, Is.EqualTo(WorldMapCommandStatus.Rejected));
            Assert.That(first.Status, Is.EqualTo(WorldMapCommandStatus.Applied));
            Assert.That(second.Status, Is.EqualTo(WorldMapCommandStatus.NoChange));
            Assert.That(profile.GetEdgeDiscoveryState(StartEastEdgeId), Is.EqualTo(EdgeDiscoveryState.Traversed));
            Assert.That(profile.GetEdgeDiscoveryState(WestGoalEdgeId), Is.EqualTo(EdgeDiscoveryState.Unknown));
            Assert.That(store.SaveProfileCallCount, Is.EqualTo(1));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Operation, Is.EqualTo(WorldDiscoveryOperation.TraversedEdge));
            Assert.That(events[0].OperationId, Is.EqualTo(StartEastEdgeId));
            Assert.That(events[0].Changes, Has.Count.EqualTo(1));
            Assert.That(events[0].Changes[0].EdgeState, Is.EqualTo(EdgeDiscoveryState.Traversed));
            AssertRunUnchanged(run, StartNodeId, 0, Array.Empty<string>());
        }

        [Test]
        public void PersistenceFailureRequiresIdenticalRetryAndPublishesAfterSave()
        {
            ProfileState profile = CreateProfile();
            FakeSaveStore store = new FakeSaveStore { FailProfileSave = true };
            WorldMapService service = CreateService(profile, store);
            List<WorldDiscoveryEvent> events = Subscribe(service);

            WorldMapCommandResult failed = service.EnterCurrentNode();
            WorldMapCommandResult blocked = service.ObserveCurrentExits();

            Assert.That(failed.Status, Is.EqualTo(WorldMapCommandStatus.PersistenceFailed));
            Assert.That(failed.PersistenceResultType, Is.EqualTo(SaveStoreResultType.IoError));
            Assert.That(failed.ErrorMessage, Is.EqualTo("Profile save failed."));
            Assert.That(blocked.Status, Is.EqualTo(WorldMapCommandStatus.Rejected));
            Assert.That(profile.GetNodeDiscoveryState(StartNodeId), Is.EqualTo(NodeDiscoveryState.Visited));
            Assert.That(profile.EdgeDiscoveries, Is.Empty);
            Assert.That(store.SaveProfileCallCount, Is.EqualTo(1));
            Assert.That(events, Is.Empty);

            store.FailProfileSave = false;
            WorldMapCommandResult retry = service.EnterCurrentNode();
            WorldMapCommandResult repeated = service.EnterCurrentNode();

            Assert.That(retry.Status, Is.EqualTo(WorldMapCommandStatus.Applied));
            Assert.That(repeated.Status, Is.EqualTo(WorldMapCommandStatus.NoChange));
            Assert.That(store.SaveProfileCallCount, Is.EqualTo(2));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0], Is.SameAs(retry.DiscoveryEvent));
        }

        [Test]
        public void DiscoveriesRemainAvailableAcrossRunsWithoutDuplicateSave()
        {
            ProfileState profile = CreateProfile();
            FakeSaveStore firstStore = new FakeSaveStore();
            GameSession firstSession = CreateSession(profile, "run-first");
            WorldMapService firstService = new WorldMapService(CreateWorld(), firstSession, firstStore);

            Assert.That(firstService.EnterCurrentNode().Status, Is.EqualTo(WorldMapCommandStatus.Applied));
            Assert.That(firstService.ObserveCurrentExits().Status, Is.EqualTo(WorldMapCommandStatus.Applied));
            firstSession.AbandonRun();

            FakeSaveStore secondStore = new FakeSaveStore();
            GameSession secondSession = CreateSession(profile, "run-second");
            WorldMapService secondService = new WorldMapService(CreateWorld(), secondSession, secondStore);
            List<WorldDiscoveryEvent> secondRunEvents = Subscribe(secondService);

            Assert.That(secondService.EnterCurrentNode().Status, Is.EqualTo(WorldMapCommandStatus.NoChange));
            Assert.That(secondService.ObserveCurrentExits().Status, Is.EqualTo(WorldMapCommandStatus.NoChange));
            Assert.That(secondService.GetAtlasSnapshot().Nodes, Has.Count.EqualTo(3));
            Assert.That(secondService.GetAtlasSnapshot().Edges, Has.Count.EqualTo(2));
            Assert.That(secondStore.SaveProfileCallCount, Is.Zero);
            Assert.That(secondRunEvents, Is.Empty);
        }

        [Test]
        public void MissingOrUnknownRunPositionReturnsControlledRejection()
        {
            ProfileState profile = CreateProfile();
            FakeSaveStore store = new FakeSaveStore();
            GameSession session = new GameSession(profile);
            WorldMapService service = new WorldMapService(CreateWorld(), session, store);

            Assert.That(service.GetLegalExits().IsSuccess, Is.False);
            Assert.That(service.EnterCurrentNode().Status, Is.EqualTo(WorldMapCommandStatus.Rejected));
            Assert.That(service.ObserveCurrentExits().Status, Is.EqualTo(WorldMapCommandStatus.Rejected));
            Assert.That(service.CompleteTraversal(StartEastEdgeId).Status, Is.EqualTo(WorldMapCommandStatus.Rejected));

            session.StartNewRun(
                "run-missing-node",
                7,
                new RunStateConfiguration(100, 80, 0, "node.missing"));

            Assert.That(service.GetLegalExits().IsSuccess, Is.False);
            Assert.That(service.EnterCurrentNode().Status, Is.EqualTo(WorldMapCommandStatus.Rejected));
            Assert.That(service.ObserveCurrentExits().Status, Is.EqualTo(WorldMapCommandStatus.Rejected));
            Assert.That(service.CompleteTraversal(StartEastEdgeId).Status, Is.EqualTo(WorldMapCommandStatus.Rejected));
            Assert.That(profile.NodeDiscoveries, Is.Empty);
            Assert.That(profile.EdgeDiscoveries, Is.Empty);
            Assert.That(store.SaveProfileCallCount, Is.Zero);
        }

        [Test]
        public void SessionAssemblyStillHasNoUnityDependency()
        {
            string[] referencedAssemblies = typeof(WorldMapService).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.That(
                referencedAssemblies.Any(
                    name => name.StartsWith("Unity", StringComparison.Ordinal)),
                Is.False);
        }

        private const string WorldId = "world-test";
        private const int WorldVersion = 1;
        private const string StartNodeId = "node.start";
        private const string WestNodeId = "node.west";
        private const string EastNodeId = "node.east";
        private const string GoalNodeId = "node.goal";
        private const string StartEastEdgeId = "road.start-east";
        private const string StartWestEdgeId = "road.start-west";
        private const string WestGoalEdgeId = "road.west-goal";

        private static WorldMapService CreateService(
            ProfileState profile,
            FakeSaveStore store = null)
        {
            return new WorldMapService(
                CreateWorld(),
                CreateSession(profile),
                store ?? new FakeSaveStore());
        }

        private static GameSession CreateSession(
            ProfileState profile = null,
            string runId = "run-test")
        {
            GameSession session = new GameSession(profile ?? CreateProfile());
            session.StartNewRun(
                runId,
                7,
                new RunStateConfiguration(100, 80, 0, StartNodeId));
            return session;
        }

        private static ProfileState CreateProfile()
        {
            return new ProfileState("profile-test", WorldId, WorldVersion);
        }

        private static WorldDefinition CreateWorld()
        {
            return new WorldDefinition(
                WorldId,
                WorldVersion,
                StartNodeId,
                GoalNodeId,
                CreateNodes(),
                new[]
                {
                    new WorldEdgeDefinition(
                        StartWestEdgeId,
                        StartNodeId,
                        WestNodeId,
                        "northwest",
                        "clue.dense-trees"),
                    new WorldEdgeDefinition(
                        StartEastEdgeId,
                        StartNodeId,
                        EastNodeId,
                        "northeast",
                        "clue.running-water"),
                    new WorldEdgeDefinition(
                        WestGoalEdgeId,
                        WestNodeId,
                        GoalNodeId,
                        "northeast",
                        "clue.distant-metal"),
                    new WorldEdgeDefinition(
                        "road.east-goal",
                        EastNodeId,
                        GoalNodeId,
                        "northwest",
                        "clue.cold-wind")
                });
        }

        private static WorldNodeDefinition[] CreateNodes()
        {
            return new[]
            {
                new WorldNodeDefinition(
                    StartNodeId,
                    0,
                    0,
                    0,
                    "shelter",
                    "temperate-forest"),
                new WorldNodeDefinition(
                    WestNodeId,
                    -1,
                    1,
                    1,
                    "trail",
                    "temperate-forest"),
                new WorldNodeDefinition(
                    EastNodeId,
                    1,
                    1,
                    1,
                    "creek",
                    "wet-forest"),
                new WorldNodeDefinition(
                    GoalNodeId,
                    0,
                    2,
                    2,
                    "landmark",
                    "rocky-foothills")
            };
        }

        private static WorldMapNodeView GetNode(
            WorldMapSnapshot snapshot,
            string nodeId)
        {
            return snapshot.Nodes.Single(node => node.NodeId == nodeId);
        }

        private static List<WorldDiscoveryEvent> Subscribe(WorldMapService service)
        {
            List<WorldDiscoveryEvent> events = new List<WorldDiscoveryEvent>();
            service.OnDiscovery += discoveryEvent => events.Add(discoveryEvent);
            return events;
        }

        private static void AssertRunUnchanged(
            RunState run,
            string worldNodeId,
            int currentDay,
            IReadOnlyCollection<string> route)
        {
            Assert.That(run.WorldNodeId, Is.EqualTo(worldNodeId));
            Assert.That(run.CurrentDay, Is.EqualTo(currentDay));
            Assert.That(run.Route, Is.EqualTo(route));
        }

        private sealed class FakeSaveStore : ISaveStore
        {
            public ProfileState Profile { get; private set; }
            public RunState Run { get; private set; }
            public bool FailProfileSave { get; set; }
            public int SaveProfileCallCount { get; private set; }

            public SaveStoreResult SaveProfile(ProfileState profile)
            {
                SaveProfileCallCount++;
                if (FailProfileSave)
                    return SaveStoreResult.IoError("Profile save failed.");

                Profile = profile;
                return SaveStoreResult.Success();
            }

            public SaveStoreResult<ProfileState> LoadProfile()
            {
                return Profile == null
                    ? SaveStoreResultExtensions.Missing<ProfileState>()
                    : SaveStoreResultExtensions.Success(Profile);
            }

            public SaveStoreResult SaveRun(RunState run)
            {
                Run = run;
                return SaveStoreResult.Success();
            }

            public SaveStoreResult<RunState> LoadRun()
            {
                return Run == null
                    ? SaveStoreResultExtensions.Missing<RunState>()
                    : SaveStoreResultExtensions.Success(Run);
            }

            public SaveStoreResult DeleteRun()
            {
                Run = null;
                return SaveStoreResult.Success();
            }
        }
    }
}