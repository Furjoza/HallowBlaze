using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.Persistence.Mapping;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.World;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class RouteLegTests
    {
        internal static RunState NewRun(int day = 0, string node = "a") =>
            new RunState("run.test", 1729, new RunStateConfiguration(100, 100, day, node));
        internal static RouteSegmentState[] Segments(int count = 2) => Enumerable.Range(1, count)
            .Select(index => new RouteSegmentState("segment." + index, "legacy.board.v1", 1)).ToArray();

        internal static WorldDefinition World(int count = 2) => new WorldDefinition("world.test", 1, "a", "b",
            new[] { new WorldNodeDefinition("a", 0, 0, 0, "shelter", "forest"),
                new WorldNodeDefinition("b", 0, 1, 1, "trail", "forest") },
            new[] { new WorldEdgeDefinition("edge", "a", "b", "north", "clue",
                Segments(count).Select(segment => new RouteSegmentDefinition(segment.SegmentId,
                    segment.GenerationConfigurationId, segment.GenerationVersion))) });

        internal static BoardAddress[] ForeignAddresses(BoardAddress address) => new[]
        {
            new BoardAddress("foreign", address.LegId, address.EdgeId, address.SegmentId, address.DayNumber, address.GenerationConfigurationId, address.GenerationVersion, address.BoardSeed),
            new BoardAddress(address.RunId, "foreign", address.EdgeId, address.SegmentId, address.DayNumber, address.GenerationConfigurationId, address.GenerationVersion, address.BoardSeed),
            new BoardAddress(address.RunId, address.LegId, "foreign", address.SegmentId, address.DayNumber, address.GenerationConfigurationId, address.GenerationVersion, address.BoardSeed),
            new BoardAddress(address.RunId, address.LegId, address.EdgeId, "foreign", address.DayNumber, address.GenerationConfigurationId, address.GenerationVersion, address.BoardSeed),
            new BoardAddress(address.RunId, address.LegId, address.EdgeId, address.SegmentId, address.DayNumber + 1, address.GenerationConfigurationId, address.GenerationVersion, address.BoardSeed),
            new BoardAddress(address.RunId, address.LegId, address.EdgeId, address.SegmentId, address.DayNumber, "foreign", address.GenerationVersion, address.BoardSeed),
            new BoardAddress(address.RunId, address.LegId, address.EdgeId, address.SegmentId, address.DayNumber, address.GenerationConfigurationId, address.GenerationVersion + 1, address.BoardSeed),
            new BoardAddress(address.RunId, address.LegId, address.EdgeId, address.SegmentId, address.DayNumber, address.GenerationConfigurationId, address.GenerationVersion, address.BoardSeed + 1)
        };

        [Test]
        public void BoardAddress_UsesEveryStableIdentityField()
        {
            BoardAddress address = new BoardAddress("run.test", "leg.1", "edge", "segment.1", 1, "legacy.board.v1", 1, 42);
            Assert.That(address, Is.EqualTo(new BoardAddress("run.test", "leg.1", "edge", "segment.1", 1, "legacy.board.v1", 1, 42)));
            foreach (BoardAddress foreign in ForeignAddresses(address))
                Assert.That(foreign, Is.Not.EqualTo(address), foreign.ToString());
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardAddress("r", "l", "e", "s", 0, "c", 1, 0));
            Assert.Throws<ArgumentException>(() => new BoardAddress("r", " l", "e", "s", 1, "c", 1, 0));
        }

        [Test]
        public void BoardSeed_HasStableSegmentIdentityVectors()
        {
            RunState run = NewRun();
            run.StartLeg("edge", "a", "b", Segments());
            BoardAddress first = BoardAddressFactory.Create(run, World());
            Assert.That(first.BoardSeed, Is.EqualTo(-111777454));
            Assert.That(BoardAddressFactory.Create(run, World()), Is.EqualTo(first));
            WorldDefinition world = World();
            WorldEdgeDefinition edge = world.Edges[0];
            WorldDefinition reordered = new WorldDefinition(world.WorldDefinitionId, 1, "a", "b", world.Nodes.Reverse(),
                new[] { new WorldEdgeDefinition("edge", "a", "b", "north", "clue", edge.Segments.Reverse()) });
            Assert.That(BoardAddressFactory.Create(run, reordered), Is.EqualTo(first));
            run.TryConsumeIntro("leg.1");
            run.TryAdvanceSegment("leg.1", "segment.1");
            BoardAddress second = BoardAddressFactory.Create(run, world);
            Assert.That(second.BoardSeed, Is.EqualTo(1058255485));
            Assert.That(second.BoardSeed, Is.Not.EqualTo(first.BoardSeed));
            Assert.That(second.LegId, Is.EqualTo(first.LegId));
            Assert.That(second.DayNumber, Is.EqualTo(first.DayNumber));
        }

        [Test]
        public void BoardRequestAndOutcome_PreserveFullAddressIdentity()
        {
            RunState run = NewRun();
            run.StartLeg("edge", "a", "b", Segments());
            BoardRequest request = BoardRequest.CreateFromRun(run, World());
            BoardOutcome outcome = BoardOutcome.ExitReached(request);
            Assert.That(outcome.Address, Is.SameAs(request.Address));
            Assert.That(outcome.Request, Is.SameAs(request));
            foreach (BoardAddress foreign in ForeignAddresses(request.Address))
                Assert.That(request.HasSameIdentity(new BoardRequest(foreign, run.RunSeed, "a", "shelter", "forest", 1)), Is.False);
            BoardRequest legacy = new BoardRequest(run.RunId, run.RunSeed, "a", run.CurrentDay,
                request.BoardSeed, "shelter", "forest", 1);
            Assert.That(request.HasSameIdentity(legacy), Is.False);
            Assert.That(legacy.HasSameIdentity(request), Is.False);
        }

        [Test]
        public void OutcomeGuard_RejectsForeignAddressBeforeMutation()
        {
            RunState run = NewRun();
            run.StartLeg("edge", "a", "b", Segments());
            BoardRequest request = BoardRequest.CreateFromRun(run, World());
            Assert.That(BoardOutcomeIdentityGuard.ValidateOutcome(BoardOutcome.ExitReached(request), request, run, out _), Is.True);
            foreach (BoardAddress foreign in ForeignAddresses(request.Address))
            {
                BoardRequest wrong = new BoardRequest(foreign, run.RunSeed, "a", "shelter", "forest", 1);
                Assert.That(BoardOutcomeIdentityGuard.ValidateOutcome(BoardOutcome.ExitReached(wrong), request, run, out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.StartWith("ForeignBoardOutcome:"));
            }
            Assert.That(run.Food, Is.EqualTo(100));
            Assert.That(run.TravelHistory, Is.Empty);
            run.TryConsumeIntro("leg.1");
            run.TryAdvanceSegment("leg.1", "segment.1");
            Assert.That(BoardOutcomeIdentityGuard.ValidateOutcome(BoardOutcome.ExitReached(request), request, run, out _), Is.False);
            Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(1));
        }

        [Test]
        public void TravelEntry_DistinguishesParallelEdgesAndLegacyEvidence()
        {
            TravelHistoryEntry first = TravelHistoryEntry.Completed("leg.1", "east", "a", "b", 1, 2);
            Assert.That(first, Is.Not.EqualTo(TravelHistoryEntry.Completed("leg.1", "west", "a", "b", 1, 2)));
            Assert.That(first, Is.EqualTo(TravelHistoryEntry.Completed("leg.1", "east", "a", "b", 1, 2)));
            TravelHistoryEntry legacy = TravelHistoryEntry.LegacyCheckpoint("b");
            Assert.That(legacy.Kind, Is.EqualTo(TravelHistoryKind.LegacyCheckpoint));
            Assert.That(legacy.LegId, Is.Null);
            Assert.That(legacy.EdgeId, Is.Null);
            Assert.That(legacy.FromNodeId, Is.Null);
            Assert.That(legacy.DayNumber, Is.Null);
            Assert.That(legacy.CompletedSegmentCount, Is.Null);
            Assert.That(legacy, Is.Not.EqualTo(first));
            Assert.Throws<ArgumentException>(() => TravelHistoryEntry.LegacyCheckpoint(" b"));
            Assert.Throws<ArgumentOutOfRangeException>(() => TravelHistoryEntry.Completed("leg.1", "edge", "a", "b", 0, 2));
        }

        [Test]
        public void ActiveLeg_ValidatesDetachedProgressAndIntroState()
        {
            RouteSegmentState[] source = Segments();
            ActiveRouteLegState leg = new ActiveRouteLegState("leg.1", "edge", "a", "b", source, 1);
            source[0] = new RouteSegmentState("replacement", "config", 1);
            Assert.That(leg.CurrentSegment.SegmentId, Is.EqualTo("segment.1"));
            Assert.Throws<NotSupportedException>(() => ((IList<RouteSegmentState>)leg.Segments).Clear());
            Assert.Throws<ArgumentException>(() => new ActiveRouteLegState("leg.1", "edge", "a", "b", Segments(), 1, 1, 0));
            Assert.Throws<ArgumentException>(() => new ActiveRouteLegState("leg.1", "edge", "a", "b", Segments(), 1, 1, 1));
            Assert.Throws<ArgumentException>(() => new ActiveRouteLegState("leg.1", "edge", "a", "b", Segments(), 0));
            Assert.Throws<ArgumentException>(() => new ActiveRouteLegState("leg.1", "edge", "a", "b", Segments(), 1, 0, 0, (DayIntroState)2));
            Assert.Throws<ArgumentException>(() => new ActiveRouteLegState("leg.1", "edge", "a", "b", new RouteSegmentState[0], 1));
            Assert.Throws<ArgumentException>(() => new ActiveRouteLegState("leg.1", "edge", "a", "b", new[] { source[0], source[0] }, 1));
            Assert.That(new ActiveRouteLegState("leg.1", "edge", "a", "b", Segments(), 1, 1, 1, DayIntroState.Consumed).IsFinalSegment, Is.True);
        }

        [Test]
        public void StartLeg_CreatesPendingIdentityWithoutArrival()
        {
            RunState run = NewRun();
            ActiveRouteLegState leg = run.StartLeg("edge", "a", "b", Segments());
            Assert.That(leg.LegId, Is.EqualTo("leg.1"));
            Assert.That(run.TraversalSequence, Is.EqualTo(1));
            Assert.That(run.CompletedDayCount, Is.Zero);
            Assert.That(run.CurrentDay, Is.EqualTo(1));
            Assert.That(run.WorldNodeId, Is.EqualTo("a"));
            Assert.That(run.TravelHistory, Is.Empty);
            Assert.That(leg.IntroState, Is.EqualTo(DayIntroState.Pending));
            Assert.Throws<InvalidOperationException>(() => run.StartLeg("edge", "a", "b", Segments()));
            Assert.Throws<InvalidOperationException>(() => run.AdvanceToWorldNode("b"));
            Assert.That(run.ActiveLeg, Is.SameAs(leg));
            RunState maxDay = NewRun(int.MaxValue);
            Assert.Throws<OverflowException>(() => maxDay.StartLeg("edge", "a", "b", Segments()));
            Assert.That(maxDay.ActiveLeg, Is.Null);
            RunState wrongSource = NewRun();
            Assert.Throws<InvalidOperationException>(() => wrongSource.StartLeg("edge", "x", "b", Segments()));
            Assert.That(wrongSource.TraversalSequence, Is.Zero);
        }

        [Test]
        public void TravelStart_CheckpointsPendingWithoutArrivalAndRollsBackFailedSave()
        {
            GameSession session = new GameSession(new ProfileState("profile.test", "world.test", 1));
            session.ContinueRun(NewRun());
            TravelSaveStore store = new TravelSaveStore { FailRunSave = true };
            RouteTravelService travel = new RouteTravelService(session, World(), store);

            Assert.That(travel.StartRoute("edge").IsFailure, Is.True);
            Assert.That(session.ActiveRun.ActiveLeg, Is.Null);
            Assert.That(session.ActiveRun.TraversalSequence, Is.Zero);
            store.FailRunSave = false;
            Assert.That(travel.StartRoute("edge").IsSuccess, Is.True);
            Assert.That(store.SavedLeg.IntroState, Is.EqualTo(DayIntroState.Pending));
            Assert.That(store.SavedLeg.LegId, Is.EqualTo("leg.1"));
            Assert.That(session.ActiveRun.ReachedCheckpointNodeId, Is.EqualTo("a"));
            Assert.That(session.ActiveRun.CompletedDayCount, Is.Zero);
            Assert.That(session.ActiveRun.CurrentDay, Is.EqualTo(1));
            Assert.That(session.ActiveRun.TravelHistory, Is.Empty);
            Assert.That(session.Profile.GetNodeDiscoveryState("b"), Is.EqualTo(NodeDiscoveryState.Unknown));
            Assert.That(session.Profile.GetEdgeDiscoveryState("edge"), Is.EqualTo(EdgeDiscoveryState.Unknown));
            Assert.That(store.ProfileSaveCount, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => travel.StartRoute("edge"));
            Assert.That(store.RunSaveCount, Is.EqualTo(2));
        }

        private sealed class TravelSaveStore : ISaveStore
        {
            public bool FailRunSave { get; set; }
            public int RunSaveCount { get; private set; }
            public int ProfileSaveCount { get; private set; }
            public ActiveRouteLegState SavedLeg { get; private set; }

            public SaveStoreResult SaveRun(RunState run)
            {
                RunSaveCount++;
                if (FailRunSave)
                    return SaveStoreResult.IoError();
                SavedLeg = run.ActiveLeg;
                return SaveStoreResult.Success();
            }

            public SaveStoreResult SaveProfile(ProfileState profile)
            {
                ProfileSaveCount++;
                return SaveStoreResult.Success();
            }

            public SaveStoreResult ResetGame(ProfileState profile, RunState run) => throw new NotSupportedException();
            public SaveStoreResult<ProfileState> LoadProfile() => throw new NotSupportedException();
            public SaveStoreResult<RunState> LoadRun() => throw new NotSupportedException();
            public SaveStoreResult DeleteRun() => throw new NotSupportedException();
        }

        /// <summary>The ordinary publication upgrade retains the entire saved legacy leg, address and frozen seed.</summary>
        [Test]
        public void OrdinaryPublicationKeepsSavedLegacyBoundaryWithoutAcceptingOtherDrift()
        {
            RunState run = NewRun();
            run.StartLeg("edge", "a", "b", Segments());
            BoardAddress before = BoardAddressFactory.Create(run, World());
            WorldDefinition published = WorldWithGeneration("ordinary.board.v1", 1);
            Assert.That(BoardAddressFactory.Create(run, published), Is.EqualTo(before));
            RunState restored = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(run));
            Assert.That(BoardRequest.CreateFromRun(restored, published).Address, Is.EqualTo(before));
            Assert.That(restored.ActiveLeg.Segments.Select(segment => segment.GenerationConfigurationId),
                Is.All.EqualTo("legacy.board.v1"));
            Assert.That(BoardAddressFactory.Create(restored, WorldWithGeneration("ordinary.board.v1", 2)), Is.EqualTo(before));
            Assert.That(BoardAddressFactory.Create(restored, WorldWithGeneration("ordinary.board.v1", 3)), Is.EqualTo(before));
            Assert.Throws<ArgumentException>(() => BoardAddressFactory.Create(restored, WorldWithGeneration("ordinary.board.v1", 4)));
            Assert.Throws<ArgumentException>(() => BoardAddressFactory.Create(restored, WorldWithGeneration("unknown", 1)));
            restored.TryConsumeIntro(restored.ActiveLeg.LegId);
            restored.TryAdvanceSegment(restored.ActiveLeg.LegId, "segment.1");
            Assert.That(BoardAddressFactory.Create(restored, published).BoardSeed, Is.EqualTo(1058255485));
        }

        /// <summary>New legs persist the ordinary family for current RNG while imported runs retain their explicit legacy family.</summary>
        [TestCase(false, 1)]
        [TestCase(true, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 2)]
        [TestCase(false, 3)]
        [TestCase(true, 3)]
        public void OrdinaryRouteStartPersistsTheCorrectGenerationFamily(bool legacyRandom, int generationVersion)
        {
            var session = new GameSession(new ProfileState("profile.test", "world.test", 1));
            RunState run = legacyRandom ? new RunState("run.test", 1729, new RunStateConfiguration(100, 100, 0, "a"),
                RunState.LegacyRandomAlgorithmId, RunState.LegacyRandomAlgorithmVersion) : NewRun();
            session.ContinueRun(run);
            WorldDefinition published = WorldWithGeneration("ordinary.board.v1", generationVersion);
            var store = new TravelSaveStore();
            Assert.That(new RouteTravelService(session, published, store).StartRoute("edge").IsSuccess, Is.True);
            string expected = legacyRandom ? "legacy.board.v1" : "ordinary.board.v1";
            Assert.That(store.SavedLeg.Segments.Select(segment => segment.GenerationConfigurationId), Is.All.EqualTo(expected));
            Assert.That(BoardAddressFactory.Create(run, published).GenerationConfigurationId, Is.EqualTo(expected));
            Assert.That(BoardAddressFactory.Create(run, published).GenerationVersion, Is.EqualTo(legacyRandom ? 1 : generationVersion));
        }

        /// <summary>Saved ordinary v1 legs keep their exact seed and algorithm against a v2 publication without accepting downgrades.</summary>
        [Test]
        public void BoundedPipelinePublicationPreservesOrdinaryVersionOne()
        {
            RunState run = NewRun();
            run.StartLeg("edge", "a", "b", Segments().Select(segment => new RouteSegmentState(segment.SegmentId, "ordinary.board.v1", 1)));
            BoardAddress original = BoardAddressFactory.Create(run, WorldWithGeneration("ordinary.board.v1", 1));
            RunState restored = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(run));
            Assert.That(BoardRequest.CreateFromRun(restored, WorldWithGeneration("ordinary.board.v1", 2)).Address, Is.EqualTo(original));
            Assert.That(restored.ActiveLeg.CurrentSegment.GenerationVersion, Is.EqualTo(1));
            Assert.That(BoardAddressFactory.Create(restored, WorldWithGeneration("ordinary.board.v1", 3)), Is.EqualTo(original));
            Assert.Throws<ArgumentException>(() => BoardAddressFactory.Create(restored, WorldWithGeneration("ordinary.board.v1", 4)));
            var session = new GameSession(new ProfileState("profile.test", "world.test", 1));
            session.ContinueRun(NewRun());
            new RouteTravelService(session, WorldWithGeneration("ordinary.board.v1", 2), new TravelSaveStore()).StartRoute("edge");
            BoardAddress versionTwo = BoardAddressFactory.Create(session.ActiveRun, WorldWithGeneration("ordinary.board.v1", 2));
            RunState restoredTwo = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(session.ActiveRun));
            Assert.That(BoardAddressFactory.Create(restoredTwo, WorldWithGeneration("ordinary.board.v1", 3)), Is.EqualTo(versionTwo));
            Assert.That(restoredTwo.ActiveLeg.CurrentSegment.GenerationVersion, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => BoardAddressFactory.Create(session.ActiveRun, WorldWithGeneration("ordinary.board.v1", 1)));
        }

        private static WorldDefinition WorldWithGeneration(string identifier, int version)
        {
            WorldDefinition world = World();
            return new WorldDefinition(world.WorldDefinitionId, world.Version, world.StartNodeId, world.GoalNodeId, world.Nodes,
                world.Edges.Select(edge => new WorldEdgeDefinition(edge.EdgeId, edge.FromNodeId, edge.ToNodeId,
                    edge.WorldDirection, edge.ClueKey, edge.Segments.Select(segment =>
                        new RouteSegmentDefinition(segment.SegmentId, identifier, version)))));
        }

        [TestCase(2)]
        [TestCase(5)]
        public void TravelBoundaries_ConsumeBeforeRenderAdvanceOnceAndRollbackFailures(int count)
        {
            GameSession session = new GameSession(new ProfileState("profile.test", "world.test", 1));
            session.ContinueRun(NewRun());
            TravelSaveStore store = new TravelSaveStore();
            WorldDefinition world = World(count);
            RouteTravelService travel = new RouteTravelService(session, world, store);
            travel.StartRoute("edge");
            BoardRequest first = BoardRequest.CreateFromRun(session.ActiveRun, world);
            Assert.That(travel.TryCompleteSegment(first, out _, out _), Is.False);
            store.FailRunSave = true;
            Assert.That(travel.ConsumeIntro(out bool failedIntro).IsFailure, Is.True);
            Assert.That(failedIntro, Is.False);
            Assert.That(session.ActiveRun.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Pending));
            store.FailRunSave = false;
            Assert.That(travel.ConsumeIntro(out bool showIntro).IsSuccess, Is.True);
            Assert.That(showIntro, Is.True);
            Assert.That(store.SavedLeg.IntroState, Is.EqualTo(DayIntroState.Consumed));
            int saves = store.RunSaveCount;
            Assert.That(travel.ConsumeIntro(out bool repeatedIntro).IsSuccess, Is.True);
            Assert.That(repeatedIntro, Is.False);
            Assert.That(store.RunSaveCount, Is.EqualTo(saves));

            for (int index = 0; index < count; index++)
            {
                BoardRequest request = BoardRequest.CreateFromRun(session.ActiveRun, world);
                store.FailRunSave = true;
                Assert.That(travel.TryCompleteSegment(request, out SaveStoreResult failed, out bool failedArrival), Is.True);
                Assert.That(failed.IsFailure, Is.True);
                Assert.That(failedArrival, Is.False);
                Assert.That(BoardRequest.CreateFromRun(session.ActiveRun, world).HasSameIdentity(request), Is.True);
                store.FailRunSave = false;
                Assert.That(travel.TryCompleteSegment(request, out SaveStoreResult saved, out bool arrived), Is.True);
                Assert.That(saved.IsSuccess, Is.True);
                Assert.That(arrived, Is.EqualTo(index == count - 1));
                saves = store.RunSaveCount;
                Assert.That(travel.TryCompleteSegment(request, out _, out _), Is.False);
                Assert.That(store.RunSaveCount, Is.EqualTo(saves));
                Assert.That(session.ActiveRun.CurrentDay, Is.EqualTo(1));
                Assert.That(session.ActiveRun.CompletedDayCount, Is.EqualTo(arrived ? 1 : 0));
                Assert.That(session.ActiveRun.ReachedCheckpointNodeId, Is.EqualTo(arrived ? "b" : "a"));
            }
            Assert.That(session.ActiveRun.TravelHistory, Has.Count.EqualTo(1));
            Assert.That(session.ActiveRun.TravelHistory[0].CompletedSegmentCount, Is.EqualTo(count));
            Assert.That(store.ProfileSaveCount, Is.Zero);
        }

        [Test]
        public void IntroConsumption_IsIdentityGuardedAndIdempotent()
        {
            RunState run = NewRun();
            Assert.That(run.TryConsumeIntro("leg.1"), Is.False);
            run.StartLeg("edge", "a", "b", Segments());
            Assert.That(run.TryConsumeIntro("foreign"), Is.False);
            Assert.That(run.TryConsumeIntro("leg.1"), Is.True);
            ActiveRouteLegState consumed = run.ActiveLeg;
            Assert.That(run.TryConsumeIntro("leg.1"), Is.False);
            Assert.That(run.ActiveLeg, Is.SameAs(consumed));
            Assert.That(run.CompletedDayCount, Is.Zero);
            Assert.That(run.Food, Is.EqualTo(100));
            run.MarkDead();
            Assert.That(run.TryConsumeIntro("leg.1"), Is.False);
        }

        [TestCase(2)]
        [TestCase(5)]
        public void IntermediateSegment_AdvancesOnceWithoutDayOrArrival(int count)
        {
            RunState run = NewRun();
            RouteSegmentState[] segments = Segments(count);
            run.StartLeg("edge", "a", "b", segments);
            Assert.That(run.TryAdvanceSegment("leg.1", segments[0].SegmentId), Is.False);
            run.TryConsumeIntro("leg.1");
            for (int index = 0; index < count - 1; index++)
            {
                Assert.That(run.TryAdvanceSegment("leg.1", segments[index].SegmentId), Is.True);
                Assert.That(run.TryAdvanceSegment("leg.1", segments[index].SegmentId), Is.False);
                Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(index + 1));
                Assert.That(run.ActiveLeg.CompletedSegmentCount, Is.EqualTo(index + 1));
                Assert.That(run.CurrentDay, Is.EqualTo(1));
                Assert.That(run.CompletedDayCount, Is.Zero);
                Assert.That(run.WorldNodeId, Is.EqualTo("a"));
                Assert.That(run.TravelHistory, Is.Empty);
            }
            Assert.That(run.TryAdvanceSegment("leg.1", segments[count - 1].SegmentId), Is.False);
            Assert.That(run.Food, Is.EqualTo(100));
        }

        [Test]
        public void FinalSegment_CommitsOneStructuredArrival()
        {
            RunState run = NewRun();
            run.StartLeg("outbound", "a", "b", Segments());
            run.TryConsumeIntro("leg.1");
            Assert.That(run.TryCompleteLeg("leg.1", "segment.1"), Is.False);
            run.TryAdvanceSegment("leg.1", "segment.1");
            Assert.That(run.TryCompleteLeg("other", "segment.2"), Is.False);
            Assert.That(run.TryCompleteLeg("leg.1", "segment.2"), Is.True);
            Assert.That(run.TryCompleteLeg("leg.1", "segment.2"), Is.False);
            Assert.That(run.WorldNodeId, Is.EqualTo("b"));
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.ActiveLeg, Is.Null);
            Assert.That(run.TravelHistory.Single().EdgeId, Is.EqualTo("outbound"));
            run.StartLeg("return", "b", "a", Segments(1));
            run.TryConsumeIntro("leg.2");
            run.TryCompleteLeg("leg.2", "segment.1");
            ActiveRouteLegState repeated = run.StartLeg("outbound", "a", "b", Segments());
            Assert.That(repeated.LegId, Is.EqualTo("leg.3"));
            Assert.That(repeated.DayNumber, Is.EqualTo(3));
            Assert.That(run.TravelHistory.Select(entry => entry.LegId), Is.EqualTo(new[] { "leg.1", "leg.2" }));
        }

        [TestCase(2)]
        [TestCase(5)]
        public void TravelSequence_PreservesExplicitBoundaryState(int count)
        {
            RunState run = NewRun();
            RunState independent = NewRun();
            run.EquipTool(0, new ToolSlotState("axe", 3));
            ActiveRouteLegState pending = run.StartLeg("edge", "a", "b", Segments(count));
            Assert.That(run.ReachedCheckpointNodeId, Is.EqualTo("a"));
            Assert.That(run.CompletedDayCount, Is.Zero);
            Assert.That(pending.DayNumber, Is.EqualTo(1));
            Assert.That(pending.SegmentIndex, Is.Zero);
            Assert.That(pending.CompletedSegmentCount, Is.Zero);
            Assert.That(pending.IntroState, Is.EqualTo(DayIntroState.Pending));
            Assert.That(run.TryAdvanceSegment("leg.1", "segment.1"), Is.False);
            Assert.That(run.TryConsumeIntro("leg.1"), Is.True);
            Assert.That(run.TryConsumeIntro("leg.1"), Is.False);
            for (int index = 0; index < count - 1; index++)
            {
                Assert.That(run.TryAdvanceSegment("leg.1", "segment." + (index + 1)), Is.True);
                Assert.That(run.TryAdvanceSegment("leg.1", "segment." + (index + 1)), Is.False);
                Assert.That(run.ActiveLeg.CurrentSegment.SegmentId, Is.EqualTo("segment." + (index + 2)));
                Assert.That(run.ActiveLeg.CompletedSegmentCount, Is.EqualTo(index + 1));
                Assert.That(run.ActiveLeg.DayNumber, Is.EqualTo(1));
                Assert.That(run.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Consumed));
                Assert.That(run.ReachedCheckpointNodeId, Is.EqualTo("a"));
                Assert.That(run.CompletedDayCount, Is.Zero);
                Assert.That(run.TravelHistory, Is.Empty);
            }
            Assert.That(run.TryCompleteLeg("leg.1", "segment." + count), Is.True);
            Assert.That(run.TryCompleteLeg("leg.1", "segment." + count), Is.False);
            Assert.That(run.ReachedCheckpointNodeId, Is.EqualTo("b"));
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
            Assert.That(run.TravelHistory.Single(), Is.EqualTo(TravelHistoryEntry.Completed("leg.1", "edge", "a", "b", 1, count)));
            Assert.That(run.ActiveLeg, Is.Null);
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.Food, Is.EqualTo(100));
            Assert.That(run.ToolSlots[0].RemainingUses, Is.EqualTo(3));
            Assert.That(independent.TravelHistory, Is.Empty);
            Assert.That(independent.ActiveLeg, Is.Null);
            Assert.That(independent.ToolSlots[0].IsEmpty, Is.True);
        }

        [Test]
        public void AddressedContexts_PreserveExplicitIdentityAndSeed()
        {
            RunState run = NewRun();
            run.StartLeg("edge", "a", "b", Segments());
            BoardAddress first = BoardAddressFactory.Create(run, World());
            Assert.That(first.BoardSeed, Is.EqualTo(-111777454));
            run.TryConsumeIntro("leg.1");
            run.TryAdvanceSegment("leg.1", "segment.1");
            Assert.That(BoardAddressFactory.Create(run, World()).BoardSeed, Is.EqualTo(1058255485));
            run.TryCompleteLeg("leg.1", "segment.2");
            run.StartLeg("return", "b", "a", new[] { new RouteSegmentState("return.segment", "legacy.board.v1", 1) });
            run.TryConsumeIntro("leg.2");
            run.TryCompleteLeg("leg.2", "return.segment");
            run.StartLeg("edge", "a", "b", Segments());
            BoardAddress repeated = BoardAddressFactory.Create(run, World());
            Assert.That(repeated.LegId, Is.EqualTo("leg.3"));
            Assert.That(repeated.DayNumber, Is.EqualTo(3));
            Assert.That(repeated.BoardSeed, Is.EqualTo(1887943502));
            Assert.That(repeated, Is.Not.EqualTo(first));

            RunState configured = NewRun();
            configured.StartLeg("edge", "a", "b", Segments().Select(segment =>
                new RouteSegmentState(segment.SegmentId, "alternate.board.v2", 2)));
            WorldDefinition world = World();
            WorldDefinition alternate = new WorldDefinition("world.test", 1, "a", "b", world.Nodes.Reverse(),
                new[] { new WorldEdgeDefinition("edge", "a", "b", "north", "clue", configured.ActiveLeg.Segments.Select(segment =>
                    new RouteSegmentDefinition(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion))) });
            BoardAddress configurationAddress = BoardAddressFactory.Create(configured, alternate);
            Assert.That(configurationAddress.BoardSeed, Is.EqualTo(1768038604));
            Assert.That(configurationAddress.GenerationConfigurationId, Is.EqualTo("alternate.board.v2"));
            Assert.That(configurationAddress.GenerationVersion, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => BoardAddressFactory.Create(configured, world));

            RunState parallel = NewRun();
            parallel.StartLeg("parallel", "a", "b", new[] { new RouteSegmentState("parallel.segment", "legacy.board.v1", 1) });
            WorldDefinition parallelWorld = new WorldDefinition("world.test", 1, "a", "b", world.Nodes,
                world.Edges.Concat(new[] { new WorldEdgeDefinition("parallel", "a", "b", "north", "clue",
                    new[] { new RouteSegmentDefinition("parallel.segment", "legacy.board.v1", 1) }) }));
            BoardAddress parallelAddress = BoardAddressFactory.Create(parallel, parallelWorld);
            Assert.That(parallelAddress.EdgeId, Is.EqualTo("parallel"));
            Assert.That(parallelAddress, Is.Not.EqualTo(first));
            BoardAddress equalSeedForeign = new BoardAddress(first.RunId, first.LegId, "parallel", first.SegmentId,
                first.DayNumber, first.GenerationConfigurationId, first.GenerationVersion, first.BoardSeed);
            Assert.That(equalSeedForeign, Is.Not.EqualTo(first));
            Assert.That(BoardRequest.CreateFromRun(NewRun(), world).Address, Is.Null);
        }

        [TestCase("checkpoint")]
        [TestCase("pending")]
        [TestCase("consumed")]
        [TestCase("intermediate")]
        [TestCase("complete")]
        public void SavedTravel_ContinuesFromExplicitBoundary(string boundary)
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M311-continuation-" + Guid.NewGuid().ToString("N"));
            try
            {
                RunState source = PersistenceDtoTests.TravelBoundary(boundary);
                FileSystemSaveStore store = new FileSystemSaveStore(root);
                Assert.That(store.SaveRun(source).IsSuccess, Is.True);
                RunState restored = new FileSystemSaveStore(root).LoadRun().Data;
                PersistenceDtoTests.AssertTravelEquals(source, restored);
                Assert.That(restored, Is.Not.SameAs(source));
                if (restored.ActiveLeg == null)
                {
                    string target = restored.WorldNodeId == "a" ? "b" : "a";
                    ActiveRouteLegState next = restored.StartLeg("next", restored.WorldNodeId, target, Segments());
                    Assert.That(next.LegId, Is.EqualTo(boundary == "complete" ? "leg.2" : "leg.1"));
                    Assert.That(next.DayNumber, Is.EqualTo(boundary == "complete" ? 2 : 1));
                    Assert.That(next.IntroState, Is.EqualTo(DayIntroState.Pending));
                }
                else
                {
                    BoardAddress saved = BoardAddressFactory.CreateFromState(restored);
                    Assert.That(saved, Is.EqualTo(BoardAddressFactory.CreateFromState(source)));
                    if (boundary == "pending")
                        Assert.That(restored.TryConsumeIntro(saved.LegId), Is.True);
                    else
                        Assert.That(restored.TryConsumeIntro(saved.LegId), Is.False);
                    if (boundary == "intermediate")
                        Assert.That(restored.TryCompleteLeg(saved.LegId, saved.SegmentId), Is.True);
                    else
                        Assert.That(restored.TryAdvanceSegment(saved.LegId, saved.SegmentId), Is.True);
                    Assert.That(restored.TryAdvanceSegment(saved.LegId, saved.SegmentId), Is.False);
                }
                Assert.That(source.ActiveLeg?.SegmentIndex, Is.EqualTo(boundary == "intermediate" ? (int?)1 :
                    boundary == "pending" || boundary == "consumed" ? (int?)0 : null));
                Assert.That(restored.Food, Is.EqualTo(100));
                Assert.That(restored.ToolSlots[0].RemainingUses, Is.EqualTo(3));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Test]
        public void ForeignOrStaleOutcome_PreservesCurrentTravel()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M311-stale-" + Guid.NewGuid().ToString("N"));
            try
            {
                RunState run = NewRun();
                run.StartLeg("edge", "a", "b", Segments());
                run.TryConsumeIntro("leg.1");
                BoardRequest expected = BoardRequest.CreateFromRun(run, World());
                FileSystemSaveStore store = new FileSystemSaveStore(root);
                store.SaveRun(run);
                byte[] original = File.ReadAllBytes(Path.Combine(root, "run.json"));
                string before = PersistenceJsonSerializer.SerializeRun(run);
                foreach (BoardAddress foreign in ForeignAddresses(expected.Address))
                {
                    BoardRequest wrong = new BoardRequest(foreign, run.RunSeed, "a", "shelter", "forest", 1);
                    Assert.That(BoardOutcomeIdentityGuard.ValidateOutcome(BoardOutcome.ExitReached(wrong), expected, run,
                        out string diagnostic), Is.False, foreign.ToString());
                    Assert.That(diagnostic, Does.StartWith("ForeignBoardOutcome:"));
                    Assert.That(PersistenceJsonSerializer.SerializeRun(run), Is.EqualTo(before));
                    Assert.That(File.ReadAllBytes(Path.Combine(root, "run.json")), Is.EqualTo(original));
                }
                Assert.That(BoardOutcomeIdentityGuard.ValidateOutcome(BoardOutcome.ExitReached(expected), expected, run, out _), Is.True);
                Assert.That(run.TryAdvanceSegment(expected.Address.LegId, expected.Address.SegmentId), Is.True);
                Assert.That(BoardOutcomeIdentityGuard.ValidateOutcome(BoardOutcome.ExitReached(expected), expected, run, out _), Is.False);
                BoardRequest final = BoardRequest.CreateFromRun(run, World());
                Assert.That(BoardOutcomeIdentityGuard.ValidateOutcome(BoardOutcome.ExitReached(final), final, run, out _), Is.True);
                Assert.That(run.TryCompleteLeg(final.Address.LegId, final.Address.SegmentId), Is.True);
                Assert.That(BoardOutcomeIdentityGuard.ValidateOutcome(BoardOutcome.ExitReached(final), final, run, out _), Is.False);
                Assert.That(run.CompletedDayCount, Is.EqualTo(1));
                Assert.That(run.TravelHistory, Has.Count.EqualTo(1));
                Assert.That(run.Food, Is.EqualTo(100));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [TestCase("pending", false)]
        [TestCase("pending", true)]
        [TestCase("consumed", false)]
        [TestCase("consumed", true)]
        [TestCase("intermediate", false)]
        [TestCase("intermediate", true)]
        [TestCase("parallel", false)]
        [TestCase("parallel", true)]
        [TestCase("repeated", false)]
        [TestCase("repeated", true)]
        [TestCase("legacy", false)]
        [TestCase("legacy", true)]
        public void AddressedTravel_PreservesExplicitTurnOutcomesAfterLoad(string scenario, bool reverseInput)
        {
            RunState source;
            if (scenario == "legacy")
            {
                const string v1 = "{\"schemaVersion\":1,\"runId\":\"run.test\",\"runSeed\":1729,\"currentDay\":0,\"worldNodeId\":\"a\",\"health\":100,\"food\":100,\"status\":\"active\",\"toolSlots\":[{\"toolId\":\"\",\"remainingUses\":0},{\"toolId\":\"\",\"remainingUses\":0}],\"route\":[\"a\",\"a\"]}";
                source = PersistenceJsonSerializer.DeserializeRun(v1);
            }
            else if (scenario == "parallel")
            {
                source = NewRun();
                source.StartLeg("parallel", "a", "b", new[] { new RouteSegmentState("parallel.segment", "legacy.board.v1", 1) });
                source.TryConsumeIntro("leg.1");
            }
            else if (scenario == "repeated")
            {
                source = NewRun();
                source.StartLeg("edge", "a", "b", Segments());
                source.TryConsumeIntro("leg.1");
                source.TryAdvanceSegment("leg.1", "segment.1");
                source.TryCompleteLeg("leg.1", "segment.2");
                source.StartLeg("return", "b", "a", Segments(1));
                source.TryConsumeIntro("leg.2");
                source.TryCompleteLeg("leg.2", "segment.1");
                source.StartLeg("edge", "a", "b", Segments());
                source.TryConsumeIntro("leg.3");
            }
            else
                source = PersistenceDtoTests.TravelBoundary(scenario);
            RunState run = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(source));
            PersistenceDtoTests.AssertTravelEquals(source, run);
            BoardAddress address = run.ActiveLeg == null ? null : BoardAddressFactory.CreateFromState(run);
            if (scenario == "pending" || scenario == "consumed" || scenario == "intermediate" || scenario == "repeated")
            {
                Assert.That(address, Is.EqualTo(BoardAddressFactory.Create(run, World())));
                Assert.That(address.BoardSeed, Is.EqualTo(scenario == "intermediate" ? 1058255485 :
                    scenario == "repeated" ? 1887943502 : -111777454));
            }
            if (scenario == "pending")
                Assert.That(run.TryConsumeIntro(run.ActiveLeg.LegId), Is.True);
            string context = scenario + ";reverse=" + reverseInput + ";address=" + (address?.ToString() ?? "legacy");
            var board = new BoardState(new GridBounds(0, 0, 4, 2));
            IEnumerable<int> columns = Enumerable.Range(0, 5);
            if (reverseInput)
                columns = columns.Reverse();
            foreach (int column in columns)
                for (int row = 0; row < 3; row++)
                    AddScenarioEntity(board, 100 + column * 3 + row, BoardLayer.Terrain, new EntityKind("floor"),
                        new GridPosition(column, row), new BoardEntityTraits(true, false, false));
            AddScenarioEntity(board, 1, BoardLayer.Actor, EntityKind.Player, new GridPosition(1, 1), BoardEntityTraits.Default);
            AddScenarioEntity(board, 2, BoardLayer.Actor, EntityKind.Enemy, new GridPosition(3, 1), BoardEntityTraits.Default);
            AddScenarioEntity(board, 3, BoardLayer.Actor, EntityKind.Enemy, new GridPosition(3, 2), BoardEntityTraits.Default);
            AddScenarioEntity(board, 200, BoardLayer.Obstacle, EntityKind.Obstacle, new GridPosition(0, 1), BoardEntityTraits.Default);
            var first = new ShamblerState(new EntityId(2), new ShamblerDefinition(20));
            var second = new ShamblerState(new EntityId(3), new ShamblerDefinition(10));
            var adapter = new EnemyBatchTurnPhases(reverseInput ? new[] { second, first } : new[] { first, second });
            adapter.PlanInitialIntents(board, new EntityId(1));
            var controller = new TurnController(board, run, new EntityId(1), phases: adapter.Handlers);
            TurnResult move = controller.Resolve(new WaitCommand());
            Assert.That(move.Events.Select(gameEvent => gameEvent.EventType),
                Is.EqualTo(new[] { "EntityWaited", "ActionCostApplied", "EntityMoved", "EntityMoved" }), context);
            Assert.That(run.Food, Is.EqualTo(99), context);
            Assert.That(move.Events.OfType<EntityMovedEvent>().Select(gameEvent => gameEvent.EntityId.Value), Is.EqualTo(new long[] { 2, 3 }), context);
            Assert.That(first.Phase, Is.EqualTo(ShamblerPhase.Rest), context);
            Assert.That(second.Phase, Is.EqualTo(ShamblerPhase.Rest), context);
            EnemyIntent retained = first.LockedIntent;
            TurnResult rejected = controller.Resolve(new MoveCommand(Direction.West));
            Assert.That(rejected.Accepted, Is.False, context);
            Assert.That(rejected.Events, Is.Empty, context);
            Assert.That(run.Food, Is.EqualTo(99), context);
            Assert.That(first.LockedIntent, Is.SameAs(retained), context);
            TurnResult rest = controller.Resolve(new WaitCommand());
            Assert.That(rest.Events.Select(gameEvent => gameEvent.EventType),
                Is.EqualTo(new[] { "EntityWaited", "ActionCostApplied", "EntityWaited", "EntityWaited" }), context);
            Assert.That(run.Food, Is.EqualTo(98), context);
            Assert.That(first.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Attack), context);
            Assert.That(first.LockedIntent.TargetPosition, Is.EqualTo(new GridPosition(1, 1)), context);
            Assert.That(second.LockedIntent.TargetPosition, Is.EqualTo(new GridPosition(1, 2)), context);
            TurnResult escaped = controller.Resolve(new MoveCommand(Direction.North));
            Assert.That(escaped.Events.Select(gameEvent => gameEvent.EventType),
                Is.EqualTo(new[] { "EntityMoved", "ActionCostApplied", "EnemyAttackResolved", "EnemyAttackResolved" }), context);
            EnemyAttackResolvedEvent[] attacks = escaped.Events.OfType<EnemyAttackResolvedEvent>().ToArray();
            Assert.That(attacks[0].IsHit, Is.False, context);
            Assert.That(attacks[0].TargetPosition, Is.EqualTo(new GridPosition(1, 1)), context);
            Assert.That(attacks[0].HealthChange, Is.Zero, context);
            Assert.That(attacks[1].IsHit, Is.True, context);
            Assert.That(attacks[1].TargetPosition, Is.EqualTo(new GridPosition(1, 2)), context);
            Assert.That(attacks[1].HealthChange, Is.EqualTo(-10), context);
            Assert.That(run.Health, Is.EqualTo(90), context);
            Assert.That(run.Food, Is.EqualTo(97), context);
            Assert.That(board.TryGetEntity(new EntityId(1), out BoardEntityState player), Is.True);
            Assert.That(player.Position, Is.EqualTo(new GridPosition(1, 2)), context);
            Assert.That(board.TryGetEntity(new EntityId(2), out BoardEntityState firstActor), Is.True);
            Assert.That(firstActor.Position, Is.EqualTo(new GridPosition(2, 1)), context);
            Assert.That(board.TryGetEntity(new EntityId(3), out BoardEntityState secondActor), Is.True);
            Assert.That(secondActor.Position, Is.EqualTo(new GridPosition(2, 2)), context);
            Assert.That(first.Phase, Is.EqualTo(ShamblerPhase.Rest), context);
            Assert.That(second.Phase, Is.EqualTo(ShamblerPhase.Rest), context);
            Assert.That(run.TravelHistory, Is.EqualTo(source.TravelHistory), context);
            if (address != null)
                Assert.That(BoardAddressFactory.CreateFromState(run), Is.EqualTo(address), context);
            Assert.That(source.Food, Is.EqualTo(100), context);
            Assert.That(source.Health, Is.EqualTo(100), context);
        }

        private static void AddScenarioEntity(BoardState board, long id, BoardLayer layer, EntityKind kind,
            GridPosition position, BoardEntityTraits traits)
        {
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(id),
                new BoardEntityDefinition(layer, kind, "scenario." + id, traits), position)), Is.True);
        }
    }
}