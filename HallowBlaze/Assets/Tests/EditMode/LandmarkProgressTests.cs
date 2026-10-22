using System;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Mapping;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies selected landmark identity, exactly-once completed outcomes and compatible run-boundary persistence.</summary>
    public sealed class LandmarkProgressTests
    {
        /// <summary>Selection survives Continue and completed travel saves retain the exact outcome without duplicating it.</summary>
        [Test]
        public void SelectionAndCompletedOutcomeRoundTripWithoutRerollOrReplay()
        {
            RunState run = Run();
            BoardAddress address = BoardAddressFactory.CreateFromState(run);
            var selection = new LandmarkRecord(address, "landmark.radio-tower", 1, "base");
            Assert.That(run.TrySelectLandmark(selection), Is.True);
            Assert.That(run.TryCompleteLeg(address.LegId, address.SegmentId), Is.False);
            RunState restored = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(run));
            Assert.That(restored.ActiveLandmark.Address, Is.EqualTo(address));
            Assert.That(restored.ActiveLandmark.VariantId, Is.EqualTo("base"));
            var result = new LandmarkRecord(address, selection.TemplateId, 1, "base", "tower.supplies");
            Assert.That(restored.TryCompleteLandmark(result), Is.True);
            Assert.That(restored.TryCompleteLeg(address.LegId, address.SegmentId), Is.True);
            Assert.That(restored.TryCompleteLandmark(result), Is.False);
            RunState completed = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(restored));
            Assert.That(completed.ActiveLandmark, Is.Null);
            Assert.That(completed.CompletedLandmarks.Count, Is.EqualTo(1));
            Assert.That(completed.CompletedLandmarks[0].OutcomeId, Is.EqualTo("tower.supplies"));
            Assert.That(run.ActiveLandmark, Is.SameAs(selection));
            completed.Reset("new-run", 1, new RunStateConfiguration(100, 100, 0, "forest.start"));
            Assert.That(completed.CompletedLandmarks, Is.Empty);
        }

        /// <summary>Previous equipment-aware saves gain empty landmark state without changing resources, travel or RNG.</summary>
        [Test]
        public void VersionFourMigratesWithEmptyLandmarkState()
        {
            RunState run = Run();
            JObject json = JObject.Parse(PersistenceJsonSerializer.SerializeRun(run));
            json["schemaVersion"] = 4;
            json.Remove("activeLandmark"); json.Remove("completedLandmarks");
            RunState restored = PersistenceJsonSerializer.DeserializeRun(json.ToString());
            Assert.That(restored.Food, Is.EqualTo(run.Food));
            Assert.That(BoardAddressFactory.CreateFromState(restored), Is.EqualTo(BoardAddressFactory.CreateFromState(run)));
            Assert.That(restored.ActiveLandmark, Is.Null);
            Assert.That(restored.CompletedLandmarks, Is.Empty);
        }

        /// <summary>Foreign selection fields, contradictory outcomes and missing current fields reject before a run is returned.</summary>
        [TestCase("runId")]
        [TestCase("segmentId")]
        [TestCase("outcomeId")]
        [TestCase("templateVersion")]
        [TestCase("missing")]
        public void InvalidLandmarkBoundaryIsRejected(string field)
        {
            RunState run = Run();
            run.TrySelectLandmark(new LandmarkRecord(BoardAddressFactory.CreateFromState(run), "landmark.radio-tower", 1, "base"));
            JObject json = JObject.Parse(PersistenceJsonSerializer.SerializeRun(run));
            if (field == "missing") json.Remove("activeLandmark");
            else if (field == "templateVersion") json["activeLandmark"][field] = 0;
            else json["activeLandmark"][field] = "foreign";
            Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeRun(json.ToString()));
        }

        /// <summary>Save failure restores both identities; retries commit resources/outcome/arrival once and stale callbacks are free.</summary>
        [Test]
        public void SelectionAndArrivalAreAtomicRetryableBoundaries()
        {
            RunState run = Run();
            var session = new GameSession(new ProfileState("profile.landmark", "world.landmark", 1));
            session.ContinueRun(run);
            var world = new WorldDefinition("world.landmark", 1, "forest.ridge", "landmark.radio-tower",
                new[] { new WorldNodeDefinition("forest.ridge", 0, 0, 0, "ridge", "forest"),
                    new WorldNodeDefinition("landmark.radio-tower", 0, 1, 1, "landmark", "forest") },
                new[] { new WorldEdgeDefinition("road.ridge-radio-tower", "forest.ridge", "landmark.radio-tower", "north", "clue",
                    new[] { new RouteSegmentDefinition("segment.ridge-radio-tower.arrival", "landmark.radio-tower", 1) }) });
            var store = new BoundaryStore();
            var travel = new RouteTravelService(session, world, store);
            BoardRequest request = BoardRequest.CreateFromRun(run, world);
            var selection = new LandmarkRecord(request.Address, "landmark.radio-tower", 1, "base");
            store.Fail = true;
            Assert.That(travel.SelectLandmark(request, selection).IsFailure, Is.True);
            Assert.That(run.ActiveLandmark, Is.Null);
            store.Fail = false;
            Assert.That(travel.SelectLandmark(request, selection).IsSuccess, Is.True);
            int saved = store.Saves;
            Assert.That(travel.SelectLandmark(request, selection).IsSuccess, Is.True);
            Assert.That(store.Saves, Is.EqualTo(saved));
            Assert.That(travel.TryCompleteSegment(request, out _, out _), Is.False);
            var result = new LandmarkRecord(request.Address, selection.TemplateId, 1, "base", "tower.supplies");
            run.RestoreFood(20);
            store.Fail = true;
            Assert.That(travel.TryCompleteSegment(request, out SaveStoreResult failed, out bool failedArrival, result), Is.True);
            Assert.That(failed.IsFailure, Is.True);
            Assert.That(failedArrival, Is.False);
            Assert.That(run.ActiveLandmark, Is.SameAs(selection));
            Assert.That(run.CompletedLandmarks, Is.Empty);
            Assert.That(run.ActiveLeg, Is.Not.Null);
            store.Fail = false;
            Assert.That(travel.TryCompleteSegment(request, out SaveStoreResult committed, out bool arrived, result), Is.True);
            Assert.That(committed.IsSuccess, Is.True);
            Assert.That(arrived, Is.True);
            Assert.That(run.Food, Is.EqualTo(120));
            RunState durable = PersistenceJsonSerializer.DeserializeRun(store.Json);
            Assert.That(durable.CompletedLandmarks.Count, Is.EqualTo(1));
            Assert.That(durable.CompletedDayCount, Is.EqualTo(5));
            Assert.That(durable.Food, Is.EqualTo(120));
            saved = store.Saves;
            Assert.That(travel.TryCompleteSegment(request, out _, out _, result), Is.False);
            Assert.That(store.Saves, Is.EqualTo(saved));
        }

        private sealed class BoundaryStore : ISaveStore
        {
            internal bool Fail;
            internal int Saves;
            internal string Json;
            public SaveStoreResult SaveRun(RunState run)
            {
                Saves++;
                if (Fail) return SaveStoreResult.IoError("Injected landmark checkpoint failure.");
                Json = PersistenceJsonSerializer.SerializeRun(run);
                return SaveStoreResult.Success();
            }
            public SaveStoreResult SaveProfile(ProfileState profile) => throw new NotSupportedException();
            public SaveStoreResult ResetGame(ProfileState profile, RunState run) => throw new NotSupportedException();
            public SaveStoreResult<ProfileState> LoadProfile() => throw new NotSupportedException();
            public SaveStoreResult<RunState> LoadRun() => throw new NotSupportedException();
            public SaveStoreResult DeleteRun() => throw new NotSupportedException();
        }

        private static RunState Run()
        {
            var run = new RunState("landmark-progress", 64, new RunStateConfiguration(100, 100, 4, "forest.ridge"));
            run.StartLeg("road.ridge-radio-tower", "forest.ridge", "landmark.radio-tower",
                new[] { new RouteSegmentState("segment.ridge-radio-tower.arrival", "landmark.radio-tower", 1) });
            run.TryConsumeIntro(run.ActiveLeg.LegId);
            return run;
        }
    }
}