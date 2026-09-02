using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class ProfileStateTests
    {
        [Test]
        public void NewProfileUsesCallerProvidedIdentityAndStartsEmpty()
        {
            ProfileState profile = new ProfileState("profile-001", "world.default", 3);

            Assert.That(profile.ProfileId, Is.EqualTo("profile-001"));
            Assert.That(profile.WorldDefinitionId, Is.EqualTo("world.default"));
            Assert.That(profile.WorldDefinitionVersion, Is.EqualTo(3));
            Assert.That(profile.DiscoveredNodeIds, Is.Empty);
            Assert.That(profile.DiscoveredEdgeIds, Is.Empty);
            Assert.That(profile.DiscoveredFactIds, Is.Empty);
            Assert.That(profile.PersistentNoteIds, Is.Empty);
            Assert.That(profile.RunSummaries, Is.Empty);
            Assert.That(profile.CompletedRunCount, Is.Zero);
            Assert.That(profile.WonRunCount, Is.Zero);
            Assert.That(profile.LostRunCount, Is.Zero);
            Assert.That(profile.TotalDaysSurvived, Is.Zero);
        }

        [Test]
        public void ProfileIdentityRequiresStableIdsAndPositiveWorldVersion()
        {
            AssertInvalidStableId(value => new ProfileState(value, "world.default", 1));
            AssertInvalidStableId(value => new ProfileState("profile-001", value, 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ProfileState("profile-001", "world.default", 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ProfileState("profile-001", "world.default", -1));
        }

        [Test]
        public void ProfileIdentityIsImmutable()
        {
            Assert.That(
                typeof(ProfileState).GetProperty(nameof(ProfileState.ProfileId)).CanWrite,
                Is.False);
            Assert.That(
                typeof(ProfileState).GetProperty(nameof(ProfileState.WorldDefinitionId)).CanWrite,
                Is.False);
            Assert.That(
                typeof(ProfileState).GetProperty(nameof(ProfileState.WorldDefinitionVersion)).CanWrite,
                Is.False);
            Assert.That(typeof(ProfileState).GetMethod("SetWorldDefinition"), Is.Null);
        }

        [Test]
        public void DiscoveryOperationsAreIdempotentAndKeepInsertionOrder()
        {
            ProfileState profile = CreateProfile();

            Assert.That(profile.DiscoverNode("node.b"), Is.True);
            Assert.That(profile.DiscoverNode("node.a"), Is.True);
            Assert.That(profile.DiscoverNode("node.b"), Is.False);
            Assert.That(profile.DiscoverEdge("edge.b-a"), Is.True);
            Assert.That(profile.DiscoverEdge("edge.a-b"), Is.True);
            Assert.That(profile.DiscoverEdge("edge.b-a"), Is.False);
            Assert.That(profile.DiscoverFact("fact.weather"), Is.True);
            Assert.That(profile.DiscoverFact("fact.food"), Is.True);
            Assert.That(profile.DiscoverFact("fact.weather"), Is.False);
            Assert.That(profile.AddPersistentNote("note.route"), Is.True);
            Assert.That(profile.AddPersistentNote("note.shelter"), Is.True);
            Assert.That(profile.AddPersistentNote("note.route"), Is.False);

            Assert.That(profile.DiscoveredNodeIds, Is.EqualTo(new[] { "node.b", "node.a" }));
            Assert.That(profile.DiscoveredEdgeIds, Is.EqualTo(new[] { "edge.b-a", "edge.a-b" }));
            Assert.That(profile.DiscoveredFactIds, Is.EqualTo(new[] { "fact.weather", "fact.food" }));
            Assert.That(profile.PersistentNoteIds, Is.EqualTo(new[] { "note.route", "note.shelter" }));
        }

        [Test]
        public void DiscoveryOperationsRequireStableIds()
        {
            ProfileState profile = CreateProfile();

            AssertInvalidStableId(value => profile.DiscoverNode(value));
            AssertInvalidStableId(value => profile.DiscoverEdge(value));
            AssertInvalidStableId(value => profile.DiscoverFact(value));
            AssertInvalidStableId(value => profile.AddPersistentNote(value));

            Assert.That(profile.DiscoveredNodeIds, Is.Empty);
            Assert.That(profile.DiscoveredEdgeIds, Is.Empty);
            Assert.That(profile.DiscoveredFactIds, Is.Empty);
            Assert.That(profile.PersistentNoteIds, Is.Empty);
        }

        [Test]
        public void PublicViewsStayLiveAndCannotBeModifiedOutsideProfile()
        {
            ProfileState profile = CreateProfile();
            IReadOnlyList<string> nodeIds = profile.DiscoveredNodeIds;
            IReadOnlyList<string> edgeIds = profile.DiscoveredEdgeIds;
            IReadOnlyList<string> factIds = profile.DiscoveredFactIds;
            IReadOnlyList<string> noteIds = profile.PersistentNoteIds;
            IReadOnlyList<ProfileRunSummary> summaries = profile.RunSummaries;

            profile.DiscoverNode("node.start");
            profile.DiscoverEdge("edge.start-end");
            profile.DiscoverFact("fact.shelter");
            profile.AddPersistentNote("note.safe-path");
            profile.RecordRunSummary(new ProfileRunSummary("run-001", 4, RunStatus.Won));

            Assert.That(nodeIds, Is.EqualTo(new[] { "node.start" }));
            Assert.That(edgeIds, Is.EqualTo(new[] { "edge.start-end" }));
            Assert.That(factIds, Is.EqualTo(new[] { "fact.shelter" }));
            Assert.That(noteIds, Is.EqualTo(new[] { "note.safe-path" }));
            Assert.That(summaries, Has.Count.EqualTo(1));

            AssertReadOnly((IList<string>)nodeIds, "injected.node");
            AssertReadOnly((IList<string>)edgeIds, "injected.edge");
            AssertReadOnly((IList<string>)factIds, "injected.fact");
            AssertReadOnly((IList<string>)noteIds, "injected.note");
            AssertReadOnly(
                (IList<ProfileRunSummary>)summaries,
                new ProfileRunSummary("injected.run", 1, RunStatus.Dead));
        }

        [Test]
        public void ProfileRunSummaryRequiresStableIdentityNonNegativeDaysAndTerminalStatus()
        {
            AssertInvalidStableId(value => new ProfileRunSummary(value, 0, RunStatus.Dead));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ProfileRunSummary("run-001", -1, RunStatus.Dead));
            Assert.Throws<ArgumentException>(
                () => new ProfileRunSummary("run-001", 0, RunStatus.Active));
            Assert.Throws<ArgumentException>(
                () => new ProfileRunSummary("run-001", 0, (RunStatus)999));

            ProfileRunSummary summary = new ProfileRunSummary("run-001", 7, RunStatus.Won);

            Assert.That(summary.RunId, Is.EqualTo("run-001"));
            Assert.That(summary.DaysSurvived, Is.EqualTo(7));
            Assert.That(summary.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(typeof(ProfileRunSummary).IsSealed, Is.True);
            Assert.That(
                typeof(ProfileRunSummary).GetProperty(nameof(ProfileRunSummary.RunId)).CanWrite,
                Is.False);
            Assert.That(
                typeof(ProfileRunSummary).GetProperty(nameof(ProfileRunSummary.DaysSurvived)).CanWrite,
                Is.False);
            Assert.That(
                typeof(ProfileRunSummary).GetProperty(nameof(ProfileRunSummary.Status)).CanWrite,
                Is.False);
        }

        [Test]
        public void RecordingWonAndDeadRunsUpdatesAggregates()
        {
            ProfileState profile = CreateProfile();
            ProfileRunSummary won = new ProfileRunSummary("run-won", 5, RunStatus.Won);
            ProfileRunSummary lost = new ProfileRunSummary("run-lost", 3, RunStatus.Dead);

            Assert.That(profile.RecordRunSummary(won), Is.True);
            Assert.That(profile.RecordRunSummary(lost), Is.True);

            Assert.That(profile.RunSummaries, Is.EqualTo(new[] { won, lost }));
            Assert.That(profile.CompletedRunCount, Is.EqualTo(2));
            Assert.That(profile.WonRunCount, Is.EqualTo(1));
            Assert.That(profile.LostRunCount, Is.EqualTo(1));
            Assert.That(profile.TotalDaysSurvived, Is.EqualTo(8));
        }

        [Test]
        public void RecordingIdenticalSummaryTwiceIsIdempotent()
        {
            ProfileState profile = CreateProfile();
            ProfileRunSummary first = new ProfileRunSummary("run-001", 5, RunStatus.Won);
            ProfileRunSummary duplicate = new ProfileRunSummary("run-001", 5, RunStatus.Won);

            Assert.That(profile.RecordRunSummary(first), Is.True);
            Assert.That(profile.RecordRunSummary(duplicate), Is.False);

            Assert.That(profile.RunSummaries, Has.Count.EqualTo(1));
            Assert.That(profile.RunSummaries[0], Is.SameAs(first));
            Assert.That(profile.CompletedRunCount, Is.EqualTo(1));
            Assert.That(profile.WonRunCount, Is.EqualTo(1));
            Assert.That(profile.LostRunCount, Is.Zero);
            Assert.That(profile.TotalDaysSurvived, Is.EqualTo(5));
        }

        [Test]
        public void ConflictingSummaryWithSameRunIdThrowsWithoutMutation()
        {
            ProfileState profile = CreateProfile();
            ProfileRunSummary original = new ProfileRunSummary("run-001", 5, RunStatus.Won);
            profile.RecordRunSummary(original);

            Assert.Throws<InvalidOperationException>(
                () => profile.RecordRunSummary(
                    new ProfileRunSummary("run-001", 4, RunStatus.Won)));
            Assert.Throws<InvalidOperationException>(
                () => profile.RecordRunSummary(
                    new ProfileRunSummary("run-001", 5, RunStatus.Dead)));

            Assert.That(profile.RunSummaries, Is.EqualTo(new[] { original }));
            Assert.That(profile.CompletedRunCount, Is.EqualTo(1));
            Assert.That(profile.WonRunCount, Is.EqualTo(1));
            Assert.That(profile.LostRunCount, Is.Zero);
            Assert.That(profile.TotalDaysSurvived, Is.EqualTo(5));
        }

        [Test]
        public void AggregateOverflowDoesNotAddSummaryOrPartiallyChangeCounters()
        {
            ProfileState profile = CreateProfile();
            ProfileRunSummary maximum = new ProfileRunSummary(
                "run-maximum",
                int.MaxValue,
                RunStatus.Won);
            profile.RecordRunSummary(maximum);

            Assert.Throws<OverflowException>(
                () => profile.RecordRunSummary(
                    new ProfileRunSummary("run-overflow", 1, RunStatus.Dead)));

            Assert.That(profile.RunSummaries, Is.EqualTo(new[] { maximum }));
            Assert.That(profile.CompletedRunCount, Is.EqualTo(1));
            Assert.That(profile.WonRunCount, Is.EqualTo(1));
            Assert.That(profile.LostRunCount, Is.Zero);
            Assert.That(profile.TotalDaysSurvived, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void RecordRunSummaryRejectsNull()
        {
            ProfileState profile = CreateProfile();

            Assert.Throws<ArgumentNullException>(() => profile.RecordRunSummary(null));
            Assert.That(profile.RunSummaries, Is.Empty);
        }

        [Test]
        public void SeparateProfilesDoNotShareCollectionsOrAggregates()
        {
            ProfileState first = new ProfileState("profile-first", "world.default", 1);
            ProfileState second = new ProfileState("profile-second", "world.default", 1);

            first.DiscoverNode("node.first");
            first.DiscoverEdge("edge.first");
            first.DiscoverFact("fact.first");
            first.AddPersistentNote("note.first");
            first.RecordRunSummary(new ProfileRunSummary("run-first", 4, RunStatus.Won));
            second.DiscoverNode("node.second");

            Assert.That(first.DiscoveredNodeIds, Is.EqualTo(new[] { "node.first" }));
            Assert.That(second.DiscoveredNodeIds, Is.EqualTo(new[] { "node.second" }));
            Assert.That(second.DiscoveredEdgeIds, Is.Empty);
            Assert.That(second.DiscoveredFactIds, Is.Empty);
            Assert.That(second.PersistentNoteIds, Is.Empty);
            Assert.That(second.RunSummaries, Is.Empty);
            Assert.That(second.CompletedRunCount, Is.Zero);
            Assert.That(second.WonRunCount, Is.Zero);
            Assert.That(second.LostRunCount, Is.Zero);
            Assert.That(second.TotalDaysSurvived, Is.Zero);
        }

        [Test]
        public void CreatingMutatingAndResettingRunsDoesNotChangeProfile()
        {
            ProfileState profile = CreateProfile();
            ProfileRunSummary summary = new ProfileRunSummary("run-profile", 6, RunStatus.Won);
            profile.DiscoverNode("node.known");
            profile.DiscoverEdge("edge.known");
            profile.DiscoverFact("fact.known");
            profile.AddPersistentNote("note.known");
            profile.RecordRunSummary(summary);

            RunStateConfiguration configuration = CreateRunConfiguration();
            RunState firstRun = new RunState("run-first", 11, configuration);
            RunState secondRun = new RunState("run-second", 22, configuration);
            firstRun.TakeDamage(25);
            firstRun.AdvanceDay();
            firstRun.RecordRouteNode("forest.start");
            firstRun.EquipTool(0, new ToolSlotState("axe.basic", 2));
            firstRun.MarkDead();
            secondRun.ConsumeFood(30);
            secondRun.SetCurrentWorldNode("forest.clearing");
            secondRun.RecordRouteNode("forest.clearing");
            secondRun.MarkWon();

            RunStateConfiguration resetConfiguration = new RunStateConfiguration(
                70,
                60,
                2,
                "snow.start");
            firstRun.Reset("run-first-reset", 33, resetConfiguration);
            secondRun.Reset("run-second-reset", 44, resetConfiguration);
            firstRun.RecordRouteNode("snow.start");
            secondRun.EquipTool(1, new ToolSlotState("shovel.basic", 3));

            Assert.That(profile.ProfileId, Is.EqualTo("profile-001"));
            Assert.That(profile.WorldDefinitionId, Is.EqualTo("world.default"));
            Assert.That(profile.WorldDefinitionVersion, Is.EqualTo(1));
            Assert.That(profile.DiscoveredNodeIds, Is.EqualTo(new[] { "node.known" }));
            Assert.That(profile.DiscoveredEdgeIds, Is.EqualTo(new[] { "edge.known" }));
            Assert.That(profile.DiscoveredFactIds, Is.EqualTo(new[] { "fact.known" }));
            Assert.That(profile.PersistentNoteIds, Is.EqualTo(new[] { "note.known" }));
            Assert.That(profile.RunSummaries, Is.EqualTo(new[] { summary }));
            Assert.That(profile.CompletedRunCount, Is.EqualTo(1));
            Assert.That(profile.WonRunCount, Is.EqualTo(1));
            Assert.That(profile.LostRunCount, Is.Zero);
            Assert.That(profile.TotalDaysSurvived, Is.EqualTo(6));
        }

        [Test]
        public void DomainAssemblyDoesNotReferenceUnityEngine()
        {
            string[] references = typeof(ProfileState).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.That(typeof(ProfileState).BaseType, Is.EqualTo(typeof(object)));
            Assert.That(typeof(ProfileRunSummary).BaseType, Is.EqualTo(typeof(object)));
            Assert.That(
                references.Any(name => name.StartsWith("UnityEngine", StringComparison.Ordinal)),
                Is.False);
        }

        private static ProfileState CreateProfile()
        {
            return new ProfileState("profile-001", "world.default", 1);
        }

        private static RunStateConfiguration CreateRunConfiguration()
        {
            return new RunStateConfiguration(100, 100, 1, "forest.start");
        }

        private static void AssertInvalidStableId(Action<string> action)
        {
            Assert.Throws<ArgumentException>(() => action(null));
            Assert.Throws<ArgumentException>(() => action(string.Empty));
            Assert.Throws<ArgumentException>(() => action(" "));
            Assert.Throws<ArgumentException>(() => action(" leading"));
            Assert.Throws<ArgumentException>(() => action("trailing "));
        }

        private static void AssertReadOnly<T>(IList<T> view, T value)
        {
            Assert.That(view.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => view.Add(value));
        }
    }
}