using System;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.Persistence.Mapping;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.Random;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class PersistenceDtoTests
    {
        [Test]
        public void RunV2Dto_HasExplicitTravelSchemaWithoutV1Cutover()
        {
            Assert.That(RunStateV2Dto.CurrentSchemaVersion, Is.EqualTo(2));
            Assert.That(RunStateDto.CurrentSchemaVersion, Is.EqualTo(1));
            Assert.That(typeof(RunStateV2Dto).GetProperties().Select(property => property.Name),
                Is.EquivalentTo(new[] { "SchemaVersion", "RunId", "RunSeed", "CompletedDayCount", "ReachedCheckpointNodeId",
                    "TraversalSequence", "Health", "Food", "Status", "ToolSlots", "TravelHistory", "ActiveLeg" }));
            foreach (Type type in new[] { typeof(RunStateV2Dto), typeof(TravelHistoryDto), typeof(ActiveRouteLegDto), typeof(RouteSegmentDto) })
                Assert.That(type.GetProperties().Any(property => typeof(UnityEngine.Object).IsAssignableFrom(property.PropertyType)), Is.False);
        }

        [TestCase("checkpoint")]
        [TestCase("pending")]
        [TestCase("consumed")]
        [TestCase("intermediate")]
        [TestCase("complete")]
        [TestCase("dead")]
        [TestCase("won")]
        public void RunV2Mapper_RoundTripsAllStableTravelBoundaries(string boundary)
        {
            RunState original = TravelBoundary(boundary);
            RunStateV2Dto dto = RunStateV2Mapper.ToDto(original);
            RunState restored = RunStateV2Mapper.FromDto(dto);
            AssertTravelEquals(original, restored);
            dto.TravelHistory = Array.Empty<TravelHistoryDto>();
            if (dto.ActiveLeg != null)
                dto.ActiveLeg.Segments[0].SegmentId = "mutated.dto";
            Assert.That(restored.ActiveLeg?.Segments[0].SegmentId, Is.Not.EqualTo("mutated.dto"));
            if (original.ActiveLeg != null && original.Status == RunStatus.Active)
                Assert.That(BoardAddressFactory.CreateFromState(restored), Is.EqualTo(BoardAddressFactory.CreateFromState(original)));
            if (boundary == "complete")
                Assert.That(restored.StartLeg("return", "b", "a", RouteLegTests.Segments()).LegId, Is.EqualTo("leg.2"));
        }

        [TestCase(RunStatus.Active, 0)]
        [TestCase(RunStatus.Active, int.MaxValue)]
        [TestCase(RunStatus.Dead, 7)]
        [TestCase(RunStatus.Won, 4)]
        public void RunV1Migration_PreservesCheckpointWithoutInventedTravel(RunStatus status, int day)
        {
            RunStateDto legacy = CreateRunDto();
            legacy.CurrentDay = day;
            legacy.Health = int.MaxValue;
            legacy.Food = int.MaxValue;
            legacy.Route = new[] { "node.start", "node.start" };
            legacy.ToolSlots[0] = new ToolSlotDto { ToolId = "axe", RemainingUses = 3 };
            legacy.Status = status == RunStatus.Active ? "active" : status == RunStatus.Dead ? "dead" : "won";
            RunState restored = RunStateV2Mapper.FromDto(RunStateV1Migration.Migrate(legacy));
            AssertRunEquals(RunStateMapper.FromDto(legacy), restored);
            Assert.That(restored.CompletedDayCount, Is.EqualTo(day));
            Assert.That(restored.ActiveLeg, Is.Null);
            Assert.That(restored.TraversalSequence, Is.Zero);
            Assert.That(restored.TravelHistory.All(entry => entry.Kind == TravelHistoryKind.LegacyCheckpoint &&
                entry.LegId == null && entry.EdgeId == null && entry.FromNodeId == null && entry.DayNumber == null), Is.True);
            if (status == RunStatus.Active && day == int.MaxValue)
                Assert.Throws<OverflowException>(() => restored.StartLeg("edge", restored.WorldNodeId, "b", RouteLegTests.Segments()));
            legacy.Route[0] = " invalid ";
            Assert.Throws<PersistenceDataException>(() => RunStateV1Migration.Migrate(legacy));
        }

        [Test]
        public void TravelRunReader_DispatchesSupportedSchemasOrRejects()
        {
            const string v1 = "{\"schemaVersion\":1,\"runId\":\"legacy\",\"runSeed\":7,\"currentDay\":3,\"worldNodeId\":\"a\",\"health\":80,\"food\":90,\"status\":\"active\",\"toolSlots\":[{\"toolId\":\"axe\",\"remainingUses\":2},{\"toolId\":\"\",\"remainingUses\":0}],\"route\":[\"a\",\"a\"]}";
            RunState migrated = PersistenceJsonSerializer.DeserializeTravelRun(v1);
            Assert.That(migrated.CompletedDayCount, Is.EqualTo(3));
            Assert.That(migrated.TravelHistory, Has.Count.EqualTo(2));
            Assert.That(migrated.ActiveLeg, Is.Null);
            foreach (string bad in new[] { "{\"schemaVersion\":3}", "{\"schemaVersion\":0}",
                "{\"schemaVersion\":2,\"schemaVersion\":2}", "{\"schemaVersion\":2} {}", "{\"schemaVersion\":2}", "{}" })
                Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeTravelRun(bad), bad);
        }

        [Test]
        public void TravelRunWriter_EmitsCompleteV4StableState()
        {
            string json = PersistenceJsonSerializer.SerializeTravelRun(TravelBoundary("pending"));
            Assert.That(json, Does.Contain("\"schemaVersion\": 4").And.Contain("\"introState\": \"pending\""));
            Assert.That(json, Does.Not.Contain("\"route\":").And.Not.Contain("\"boardSeed\":").And.Not.Contain("\"currentDay\":"));
            AssertTravelEquals(TravelBoundary("pending"), PersistenceJsonSerializer.DeserializeTravelRun(json));
            Assert.That(RunStateDto.CurrentSchemaVersion, Is.EqualTo(1));
        }

        [TestCase("checkpoint")]
        [TestCase("pending")]
        [TestCase("consumed")]
        [TestCase("intermediate")]
        [TestCase("complete")]
        [TestCase("dead")]
        [TestCase("won")]
        public void TravelPersistence_RoundTripsMigratesAndRejectsCorruption(string boundary)
        {
            RunState original = TravelBoundary(boundary);
            string json = PersistenceJsonSerializer.SerializeTravelRun(original);
            RunState once = PersistenceJsonSerializer.DeserializeTravelRun(json);
            RunState twice = PersistenceJsonSerializer.DeserializeTravelRun(PersistenceJsonSerializer.SerializeTravelRun(once));
            AssertTravelEquals(original, once);
            AssertTravelEquals(original, twice);
            Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeTravelRun(json + " {}"));
            Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeTravelRun(
                json.Insert(json.IndexOf('{') + 1, "\"unknownField\":true,")));
            if (original.ActiveLeg != null)
            {
                Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeTravelRun(
                    json.Replace("\"introState\": \"" + (original.ActiveLeg.IntroState == DayIntroState.Pending ? "pending" : "consumed") + "\"",
                        "\"introState\": \"presented\"")));
                RunStateV2Dto bad = RunStateV2Mapper.ToDto(original);
                bad.ActiveLeg.CompletedSegmentCount++;
                PersistenceDataException error = Assert.Throws<PersistenceDataException>(() => RunStateV2Mapper.FromDto(bad));
                Assert.That(error.FieldPath, Is.EqualTo("activeLeg.segmentIndex"));
                bad = RunStateV2Mapper.ToDto(original);
                bad.TraversalSequence++;
                Assert.Throws<PersistenceDataException>(() => RunStateV2Mapper.FromDto(bad));
                bad = RunStateV2Mapper.ToDto(original);
                bad.TravelHistory[0].LegId = "fabricated";
                Assert.Throws<PersistenceDataException>(() => RunStateV2Mapper.FromDto(bad));
            }
        }

        [Test]
        public void DefaultRunSchema_UsesV4EquipmentWithoutProfileChange()
        {
            ProfileState profile = new ProfileState("profile", "world.test", 1);
            profile.AdvanceNodeDiscovery("a", NodeDiscoveryState.Visited);
            string profileJson = PersistenceJsonSerializer.SerializeProfile(profile);
            RunState run = TravelBoundary("intermediate");
            string runJson = PersistenceJsonSerializer.SerializeRun(run);
            Assert.That(runJson, Does.Contain("\"schemaVersion\": 4"));
            AssertTravelEquals(run, PersistenceJsonSerializer.DeserializeRun(runJson));
            Assert.That(PersistenceJsonSerializer.SerializeProfile(profile), Is.EqualTo(profileJson));
            Assert.That(ProfileStateDto.CurrentSchemaVersion, Is.EqualTo(2));
            Assert.That(RunStateDto.CurrentSchemaVersion, Is.EqualTo(1));
        }

        /// <summary>Equipment identities, depletion, allocator gaps and unknown definitions survive a detached v4 round-trip.</summary>
        [Test]
        public void ToolInstancesRoundTripWithoutAliasingAndRetainUnknownDefinition()
        {
            RunState run = TravelBoundary("consumed");
            run.ClearTool(0);
            run.ClearTool(1);
            ToolInstanceState removed = run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3));
            ToolInstanceState unknown = run.CreateToolInstance(new ToolDefinition("uninstalled.tool", "Unknown", 1));
            run.EquipTool(1, unknown);
            run.TrySpendToolCharge(1, unknown.InstanceId);
            RunStateV4Dto dto = RunStateV4Mapper.ToDto(run);
            RunState restored = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(run));
            Assert.That(restored.ToolSlots[0].IsEmpty, Is.True);
            Assert.That(restored.ToolSlots[1].Instance.DefinitionId, Is.EqualTo("uninstalled.tool"));
            Assert.That(restored.ToolSlots[1].Instance.InstanceId, Is.EqualTo(unknown.InstanceId));
            Assert.That(restored.ToolSlots[1].Instance.Charges, Is.Zero);
            Assert.That(restored.ToolInstanceSequence, Is.EqualTo(run.ToolInstanceSequence));
            Assert.That(BoardAddressFactory.CreateFromState(restored), Is.EqualTo(BoardAddressFactory.CreateFromState(run)));
            dto.ToolSlots[1].Charges = 99;
            Assert.That(restored.ToolSlots[1].Instance.Charges, Is.Zero);
            Assert.That(run.ToolSlots[1].Instance.Charges, Is.Zero);
            Assert.That(restored.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3)).InstanceId,
                Is.Not.EqualTo(removed.InstanceId).And.Not.EqualTo(unknown.InstanceId));
        }

        /// <summary>All historical schemas acquire deterministic instance IDs and preserve existing rewards, RNG and tool values.</summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void LegacyToolSlotsMigrateWithoutLosingChargesAndEmptySlotsStayEmpty(int version)
        {
            RunState original = TravelBoundary("checkpoint");
            original.ClearTool(1);
            object dto = version == 1 ? (object)RunStateMapper.ToDto(original) :
                version == 2 ? RunStateV2Mapper.ToDto(original) : (object)RunStateV3Mapper.ToDto(original);
            RunState migrated = PersistenceJsonSerializer.DeserializeRun(JsonConvert.SerializeObject(dto));
            Assert.That(migrated.ToolSlots[0].Instance.InstanceId, Is.EqualTo("tool.1"));
            Assert.That(migrated.ToolSlots[0].ToolId, Is.EqualTo(original.ToolSlots[0].ToolId));
            Assert.That(migrated.ToolSlots[0].RemainingUses, Is.EqualTo(original.ToolSlots[0].RemainingUses));
            Assert.That(migrated.ToolSlots[1].IsEmpty, Is.True);
            Assert.That(migrated.ToolInstanceSequence, Is.EqualTo(1));
            original.ClearTool(0);
            dto = version == 1 ? (object)RunStateMapper.ToDto(original) :
                version == 2 ? RunStateV2Mapper.ToDto(original) : (object)RunStateV3Mapper.ToDto(original);
            RunState empty = PersistenceJsonSerializer.DeserializeRun(JsonConvert.SerializeObject(dto));
            Assert.That(empty.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(empty.ToolInstanceSequence, Is.Zero);
        }

        /// <summary>Malformed v4 equipment is rejected rather than coerced, partially restored or silently replaced.</summary>
        [TestCase("count")]
        [TestCase("duplicate")]
        [TestCase("unallocated")]
        [TestCase("negative")]
        [TestCase("coerced")]
        [TestCase("missing")]
        [TestCase("source-as-instance")]
        public void ToolPersistenceRejectsMalformedEquipment(string invalid)
        {
            JObject json = JObject.Parse(PersistenceJsonSerializer.SerializeRun(TravelBoundary("consumed")));
            if (invalid == "count") ((JArray)json["toolSlots"]).RemoveAt(1);
            if (invalid == "duplicate") json["toolSlots"][1] = json["toolSlots"][0].DeepClone();
            if (invalid == "unallocated") json["toolInstanceSequence"] = 0;
            if (invalid == "negative") json["toolSlots"][0]["charges"] = -1;
            if (invalid == "coerced") json["toolSlots"][0]["charges"] = "3";
            if (invalid == "missing") ((JObject)json["toolSlots"][0]).Remove("definitionId");
            if (invalid == "source-as-instance") json["toolSlots"][0]["instanceId"] = "atlas.source.axe";
            Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeRun(json.ToString()));
        }

        [TestCase("checkpoint")]
        [TestCase("pending")]
        [TestCase("consumed")]
        [TestCase("intermediate")]
        [TestCase("complete")]
        [TestCase("dead")]
        [TestCase("won")]
        public void RunV3RoundTripAndV2MigrationPreserveTheExplicitRngIdentity(string boundary)
        {
            RunState original = TravelBoundary(boundary);
            RunState current = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(original));
            AssertTravelEquals(original, current);
            Assert.That(current.RandomAlgorithmId, Is.EqualTo(RandomStreamProvider.AlgorithmId));
            Assert.That(current.RandomAlgorithmVersion, Is.EqualTo(RandomStreamProvider.AlgorithmVersion));
            string oldJson = JsonConvert.SerializeObject(RunStateV2Mapper.ToDto(original));
            RunState migrated = PersistenceJsonSerializer.DeserializeRun(oldJson);
            AssertTravelEquals(original, migrated);
            Assert.That(migrated.RandomAlgorithmId, Is.EqualTo(RunState.LegacyRandomAlgorithmId));
            Assert.That(migrated.RandomAlgorithmVersion, Is.EqualTo(RunState.LegacyRandomAlgorithmVersion));
            RunState savedAgain = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(migrated));
            Assert.That(savedAgain.RandomAlgorithmId, Is.EqualTo(migrated.RandomAlgorithmId));
            Assert.That(savedAgain.RandomAlgorithmVersion, Is.EqualTo(migrated.RandomAlgorithmVersion));
            AssertTravelEquals(migrated, savedAgain);
            migrated.Reset("fresh", 4, new RunStateConfiguration(100, 100, 0, "a"));
            Assert.That(migrated.RandomAlgorithmId, Is.EqualTo(RandomStreamProvider.AlgorithmId));
            Assert.That(migrated.RandomAlgorithmVersion, Is.EqualTo(RandomStreamProvider.AlgorithmVersion));
        }

        [Test]
        public void RunV3RejectsMissingCoercedAndUnsupportedRngMetadata()
        {
            JObject document = JObject.Parse(PersistenceJsonSerializer.SerializeRun(TravelBoundary("pending")));
            foreach (string field in new[] { "randomAlgorithmId", "randomAlgorithmVersion" })
            {
                JObject missing = (JObject)document.DeepClone();
                missing.Remove(field);
                Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeRun(missing.ToString()));
                missing[field] = JValue.CreateNull();
                Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeRun(missing.ToString()));
            }
            foreach (JToken invalid in new JToken[] { new JValue(0), new JValue(-1), new JValue(2), new JValue("1"), new JValue(1.25) })
            {
                JObject changed = (JObject)document.DeepClone();
                changed["randomAlgorithmVersion"] = invalid;
                Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeRun(changed.ToString()));
            }
            foreach (JToken invalid in new JToken[] { new JValue("unknown"), new JValue(""), new JValue(1) })
            {
                JObject changed = (JObject)document.DeepClone();
                changed["randomAlgorithmId"] = invalid;
                Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeRun(changed.ToString()));
            }
            Assert.That(RunStateV2Dto.CurrentSchemaVersion, Is.EqualTo(2));
            Assert.That(RunStateV3Dto.CurrentSchemaVersion, Is.EqualTo(3));
        }

        [TestCase("runId", "\"run.test\"", "123")]
        [TestCase("runSeed", "1729", "\"1729\"")]
        [TestCase("completedDayCount", "0", "0.25")]
        [TestCase("dayNumber", "1", "1.25")]
        [TestCase("segmentIndex", "0", "0.25")]
        [TestCase("generationVersion", "1", "\"1\"")]
        [TestCase("generationConfigurationId", "\"legacy.board.v1\"", "123")]
        public void TravelJson_RejectsCoercedScalarTypes(string field, string valid, string invalid)
        {
            string json = PersistenceJsonSerializer.SerializeTravelRun(TravelBoundary("pending"));
            string malformed = json.Replace("\"" + field + "\": " + valid, "\"" + field + "\": " + invalid);
            Assert.That(malformed, Is.Not.EqualTo(json));
            PersistenceDataException error = Assert.Throws<PersistenceDataException>(() =>
                PersistenceJsonSerializer.DeserializeTravelRun(malformed), field);
            Assert.That(error.Error, Is.EqualTo(PersistenceDataError.InvalidValue));
            Assert.That(error.FieldPath, Does.Contain(field));
        }

        [Test]
        public void TravelJson_RejectsCaseAliasesAndDuplicateBindings()
        {
            string json = PersistenceJsonSerializer.SerializeTravelRun(TravelBoundary("pending"));
            string alias = json.Insert(json.IndexOf('{') + 1, "\"RunId\":123,");
            Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeTravelRun(alias));
            Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeTravelRun(json.Replace("\"runId\"", "\"RunId\"")));
            string duplicate = json.Insert(json.IndexOf('{') + 1, "\"runId\":\"foreign\",");
            Assert.Throws<PersistenceDataException>(() => PersistenceJsonSerializer.DeserializeTravelRun(duplicate));
        }

        internal static RunState TravelBoundary(string boundary)
        {
            RunState run = RouteLegTests.NewRun();
            run.RecordRouteNode("a");
            run.EquipTool(0, new ToolSlotState("axe", 3));
            if (boundary == "checkpoint")
                return run;
            run.StartLeg("edge", "a", "b", RouteLegTests.Segments());
            if (boundary != "pending")
                run.TryConsumeIntro("leg.1");
            if (boundary == "intermediate" || boundary == "complete" || boundary == "dead" || boundary == "won")
                run.TryAdvanceSegment("leg.1", "segment.1");
            if (boundary == "complete")
                run.TryCompleteLeg("leg.1", "segment.2");
            if (boundary == "dead")
                run.MarkDead();
            if (boundary == "won")
                run.MarkWon();
            return run;
        }

        internal static void AssertTravelEquals(RunState expected, RunState actual)
        {
            AssertRunEquals(expected, actual);
            Assert.That(actual.CompletedDayCount, Is.EqualTo(expected.CompletedDayCount));
            Assert.That(actual.TraversalSequence, Is.EqualTo(expected.TraversalSequence));
            Assert.That(actual.TravelHistory, Is.EqualTo(expected.TravelHistory));
            Assert.That(actual.ActiveLeg == null, Is.EqualTo(expected.ActiveLeg == null));
            if (actual.ActiveLeg != null)
            {
                Assert.That(actual.ActiveLeg.LegId, Is.EqualTo(expected.ActiveLeg.LegId));
                Assert.That(actual.ActiveLeg.EdgeId, Is.EqualTo(expected.ActiveLeg.EdgeId));
                Assert.That(actual.ActiveLeg.FromNodeId, Is.EqualTo(expected.ActiveLeg.FromNodeId));
                Assert.That(actual.ActiveLeg.ToNodeId, Is.EqualTo(expected.ActiveLeg.ToNodeId));
                Assert.That(actual.ActiveLeg.DayNumber, Is.EqualTo(expected.ActiveLeg.DayNumber));
                Assert.That(actual.ActiveLeg.SegmentIndex, Is.EqualTo(expected.ActiveLeg.SegmentIndex));
                Assert.That(actual.ActiveLeg.CompletedSegmentCount, Is.EqualTo(expected.ActiveLeg.CompletedSegmentCount));
                Assert.That(actual.ActiveLeg.IntroState, Is.EqualTo(expected.ActiveLeg.IntroState));
                Assert.That(actual.ActiveLeg.Segments.Select(segment => segment.SegmentId + "|" + segment.GenerationConfigurationId + "|" + segment.GenerationVersion),
                    Is.EqualTo(expected.ActiveLeg.Segments.Select(segment => segment.SegmentId + "|" + segment.GenerationConfigurationId + "|" + segment.GenerationVersion)));
            }
        }

        [Test]
        public void ProfileRoundTripPreservesCompleteStateAndInsertionOrder()
        {
            ProfileState original = new ProfileState(
                "profile-main",
                "world.default",
                int.MaxValue);
            original.AdvanceNodeDiscovery("node.rumored", NodeDiscoveryState.Rumored);
            original.AdvanceNodeDiscovery("node.visited", NodeDiscoveryState.Visited);
            original.AdvanceNodeDiscovery("node.sighted", NodeDiscoveryState.Sighted);
            original.AdvanceEdgeDiscovery("edge.sighted", EdgeDiscoveryState.Sighted);
            original.AdvanceEdgeDiscovery("edge.traversed", EdgeDiscoveryState.Traversed);
            original.DiscoverFact("fact.weather");
            original.AddPersistentNote("note.safe-route");
            original.RecordRunSummary(
                new ProfileRunSummary("run-won", int.MaxValue, RunStatus.Won));

            string json = PersistenceJsonSerializer.SerializeProfile(original);
            ProfileState restored = PersistenceJsonSerializer.DeserializeProfile(json);

            Assert.That(restored.ProfileId, Is.EqualTo(original.ProfileId));
            Assert.That(restored.WorldDefinitionId, Is.EqualTo(original.WorldDefinitionId));
            Assert.That(
                restored.WorldDefinitionVersion,
                Is.EqualTo(original.WorldDefinitionVersion));
            Assert.That(
                restored.NodeDiscoveries.Select(ToSnapshot),
                Is.EqualTo(original.NodeDiscoveries.Select(ToSnapshot)));
            Assert.That(
                restored.EdgeDiscoveries.Select(ToSnapshot),
                Is.EqualTo(original.EdgeDiscoveries.Select(ToSnapshot)));
            Assert.That(restored.DiscoveredFactIds, Is.EqualTo(original.DiscoveredFactIds));
            Assert.That(restored.PersistentNoteIds, Is.EqualTo(original.PersistentNoteIds));
            Assert.That(restored.RunSummaries, Has.Count.EqualTo(1));
            Assert.That(restored.RunSummaries[0].RunId, Is.EqualTo("run-won"));
            Assert.That(restored.RunSummaries[0].DaysSurvived, Is.EqualTo(int.MaxValue));
            Assert.That(restored.RunSummaries[0].Status, Is.EqualTo(RunStatus.Won));
            Assert.That(restored.CompletedRunCount, Is.EqualTo(1));
            Assert.That(restored.WonRunCount, Is.EqualTo(1));
            Assert.That(restored.LostRunCount, Is.Zero);
            Assert.That(restored.TotalDaysSurvived, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void MinimalProfileRoundTripPreservesEmptyCollections()
        {
            ProfileState original = new ProfileState("profile-min", "world.min", 1);

            ProfileState restored = PersistenceJsonSerializer.DeserializeProfile(
                PersistenceJsonSerializer.SerializeProfile(original));

            Assert.That(restored.ProfileId, Is.EqualTo("profile-min"));
            Assert.That(restored.NodeDiscoveries, Is.Empty);
            Assert.That(restored.EdgeDiscoveries, Is.Empty);
            Assert.That(restored.DiscoveredFactIds, Is.Empty);
            Assert.That(restored.PersistentNoteIds, Is.Empty);
            Assert.That(restored.RunSummaries, Is.Empty);
            Assert.That(restored.CompletedRunCount, Is.Zero);
        }

        [Test]
        public void ActiveRunRoundTripPreservesMaximumValuesToolsAndRoute()
        {
            RunState original = new RunState(
                "run-maximum",
                int.MinValue,
                new RunStateConfiguration(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    "node.current"));
            original.RecordRouteNode("node.start");
            original.RecordRouteNode("node.current");
            original.EquipTool(0, new ToolSlotState("tool.axe", int.MaxValue));
            original.EquipTool(1, new ToolSlotState("tool.shovel", 0));

            string json = PersistenceJsonSerializer.SerializeRun(original);
            RunState restored = PersistenceJsonSerializer.DeserializeRun(json);

            AssertRunEquals(original, restored);
        }

        [TestCase(RunStatus.Dead)]
        [TestCase(RunStatus.Won)]
        public void TerminalRunRoundTripPreservesOutcome(RunStatus status)
        {
            RunState original = CreateMinimalRun();
            if (status == RunStatus.Dead)
                original.MarkDead();
            else
                original.MarkWon();

            RunState restored = PersistenceJsonSerializer.DeserializeRun(
                PersistenceJsonSerializer.SerializeRun(original));

            AssertRunEquals(original, restored);
            Assert.Throws<InvalidOperationException>(() => restored.AdvanceDay());
        }

        [Test]
        public void MinimalRunRoundTripPreservesEmptySlotsAndZeroValues()
        {
            RunState original = CreateMinimalRun();

            RunState restored = PersistenceJsonSerializer.DeserializeRun(
                PersistenceJsonSerializer.SerializeRun(original));

            AssertRunEquals(original, restored);
            Assert.That(restored.ToolSlots, Has.Count.EqualTo(RunState.ToolSlotCount));
            Assert.That(restored.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(restored.Route, Is.Empty);
        }

        [Test]
        public void MissingRequiredIdReturnsControlledErrorWithoutProfileData()
        {
            const string privateValue = "private-fact-that-must-not-leak";
            string json =
                "{\"schemaVersion\":2," +
                "\"worldDefinitionId\":\"world.default\"," +
                "\"worldDefinitionVersion\":1," +
                "\"nodeDiscoveries\":[]," +
                "\"edgeDiscoveries\":[]," +
                $"\"discoveredFactIds\":[\"{privateValue}\"]," +
                "\"persistentNoteIds\":[],\"runSummaries\":[]}";

            PersistenceDataException exception = Assert.Throws<PersistenceDataException>(
                () => PersistenceJsonSerializer.DeserializeProfile(json));

            Assert.That(exception.Error, Is.EqualTo(PersistenceDataError.MissingField));
            Assert.That(exception.Message, Does.Not.Contain(privateValue));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("discoveredFactIds")]
        [TestCase("persistentNoteIds")]
        public void DuplicateProfileCollectionIdReturnsControlledError(string fieldName)
        {
            ProfileStateDto dto = CreateProfileDto();
            typeof(ProfileStateDto)
                .GetProperty(ToPropertyName(fieldName))
                .SetValue(dto, new[] { "duplicate.id", "duplicate.id" });

            PersistenceDataException exception = Assert.Throws<PersistenceDataException>(
                () => ProfileStateMapper.FromDto(dto));

            Assert.That(exception.Error, Is.EqualTo(PersistenceDataError.DuplicateId));
            Assert.That(exception.FieldPath, Does.StartWith(fieldName));
        }

        [Test]
        public void DuplicateNodeDiscoveryIdReturnsControlledError()
        {
            ProfileStateDto dto = CreateProfileDto();
            dto.NodeDiscoveries = new[]
            {
                new NodeDiscoveryDto { NodeId = "node.same", State = "rumored" },
                new NodeDiscoveryDto { NodeId = "node.same", State = "visited" }
            };

            PersistenceDataException exception = Assert.Throws<PersistenceDataException>(
                () => ProfileStateMapper.FromDto(dto));

            Assert.That(exception.Error, Is.EqualTo(PersistenceDataError.DuplicateId));
            Assert.That(exception.FieldPath, Is.EqualTo("nodeDiscoveries[1].nodeId"));
        }

        [Test]
        public void DuplicateEdgeDiscoveryIdReturnsControlledError()
        {
            ProfileStateDto dto = CreateProfileDto();
            dto.EdgeDiscoveries = new[]
            {
                new EdgeDiscoveryDto { EdgeId = "edge.same", State = "sighted" },
                new EdgeDiscoveryDto { EdgeId = "edge.same", State = "traversed" }
            };

            PersistenceDataException exception = Assert.Throws<PersistenceDataException>(
                () => ProfileStateMapper.FromDto(dto));

            Assert.That(exception.Error, Is.EqualTo(PersistenceDataError.DuplicateId));
            Assert.That(exception.FieldPath, Is.EqualTo("edgeDiscoveries[1].edgeId"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void UnknownDiscoveryStateReturnsControlledError(bool node)
        {
            ProfileStateDto dto = CreateProfileDto();
            if (node)
            {
                dto.NodeDiscoveries = new[]
                {
                    new NodeDiscoveryDto { NodeId = "node.invalid", State = "unknown" }
                };
            }
            else
            {
                dto.EdgeDiscoveries = new[]
                {
                    new EdgeDiscoveryDto { EdgeId = "edge.invalid", State = "unknown" }
                };
            }

            PersistenceDataException exception = Assert.Throws<PersistenceDataException>(
                () => ProfileStateMapper.FromDto(dto));

            Assert.That(exception.Error, Is.EqualTo(PersistenceDataError.InvalidValue));
            Assert.That(
                exception.FieldPath,
                Is.EqualTo(node ? "nodeDiscoveries[0].state" : "edgeDiscoveries[0].state"));
        }

        [Test]
        public void DuplicateRunSummaryIdReturnsControlledError()
        {
            ProfileStateDto dto = CreateProfileDto();
            dto.RunSummaries = new[]
            {
                new ProfileRunSummaryDto
                {
                    RunId = "run-duplicate",
                    DaysSurvived = 1,
                    Status = RunStatusIds.Dead
                },
                new ProfileRunSummaryDto
                {
                    RunId = "run-duplicate",
                    DaysSurvived = 2,
                    Status = RunStatusIds.Won
                }
            };

            PersistenceDataException exception = Assert.Throws<PersistenceDataException>(
                () => ProfileStateMapper.FromDto(dto));

            Assert.That(exception.Error, Is.EqualTo(PersistenceDataError.DuplicateId));
            Assert.That(exception.FieldPath, Is.EqualTo("runSummaries[1].runId"));
        }

        [Test]
        public void DuplicateJsonPropertyIsRejectedInsteadOfUsingEitherValue()
        {
            const string json = "{\"schemaVersion\":1,\"schemaVersion\":2}";

            PersistenceDataException exception = Assert.Throws<PersistenceDataException>(
                () => PersistenceJsonSerializer.DeserializeProfile(json));

            Assert.That(exception.Error, Is.EqualTo(PersistenceDataError.InvalidJson));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FutureSchemaIsRejectedBeforeInterpretingVersionOneFields(bool profile)
        {
            string futureDocument = profile
                ? "{\"schemaVersion\":3}"
                : "{\"schemaVersion\":5}";

            PersistenceDataException exception = Assert.Throws<PersistenceDataException>(() =>
            {
                if (profile)
                    PersistenceJsonSerializer.DeserializeProfile(futureDocument);
                else
                    PersistenceJsonSerializer.DeserializeRun(futureDocument);
            });

            Assert.That(
                exception.Error,
                Is.EqualTo(PersistenceDataError.UnsupportedSchemaVersion));
            Assert.That(exception.FieldPath, Is.EqualTo("schemaVersion"));
        }

        [Test]
        public void MissingCollectionsAndInvalidToolSlotShapeReturnControlledErrors()
        {
            ProfileStateDto profileDto = CreateProfileDto();
            profileDto.NodeDiscoveries = null;
            RunStateDto runDto = CreateRunDto();
            runDto.ToolSlots = new[] { new ToolSlotDto() };

            PersistenceDataException profileException =
                Assert.Throws<PersistenceDataException>(
                    () => ProfileStateMapper.FromDto(profileDto));
            PersistenceDataException runException = Assert.Throws<PersistenceDataException>(
                () => RunStateMapper.FromDto(runDto));

            Assert.That(profileException.Error, Is.EqualTo(PersistenceDataError.MissingField));
            Assert.That(profileException.FieldPath, Is.EqualTo("nodeDiscoveries"));
            Assert.That(runException.Error, Is.EqualTo(PersistenceDataError.InvalidValue));
            Assert.That(runException.FieldPath, Is.EqualTo("toolSlots"));
        }

        [Test]
        public void NullToolIdReturnsMissingFieldWhileEmptyToolIdRemainsAnEmptySlot()
        {
            RunStateDto missingToolIdDto = CreateRunDto();
            missingToolIdDto.ToolSlots[0].ToolId = null;

            PersistenceDataException exception = Assert.Throws<PersistenceDataException>(
                () => RunStateMapper.FromDto(missingToolIdDto));
            RunState restored = RunStateMapper.FromDto(CreateRunDto());

            Assert.That(exception.Error, Is.EqualTo(PersistenceDataError.MissingField));
            Assert.That(exception.FieldPath, Is.EqualTo("toolSlots[0].toolId"));
            Assert.That(restored.ToolSlots[0].IsEmpty, Is.True);
        }

        [Test]
        public void PersistenceDtosAndAssemblyHaveNoUnityObjectReferences()
        {
            Type[] dtoTypes =
            {
                typeof(ProfileStateDto),
                typeof(NodeDiscoveryDto),
                typeof(EdgeDiscoveryDto),
                typeof(ProfileRunSummaryDto),
                typeof(RunStateDto),
                typeof(ToolSlotDto)
            };

            foreach (Type dtoType in dtoTypes)
            {
                PropertyInfo[] properties = dtoType.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(
                    properties.Any(property =>
                        typeof(UnityEngine.Object).IsAssignableFrom(property.PropertyType)),
                    Is.False,
                    dtoType.FullName);
            }

            string[] assemblyReferences = typeof(ProfileStateDto).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();
            Assert.That(
                assemblyReferences.Any(name =>
                    name.StartsWith("UnityEngine", StringComparison.Ordinal)),
                Is.False);
        }

        private static RunState CreateMinimalRun()
        {
            return new RunState(
                "run-minimum",
                0,
                new RunStateConfiguration(0, 0, 0, "node.start"));
        }

        private static ProfileStateDto CreateProfileDto()
        {
            return new ProfileStateDto
            {
                SchemaVersion = ProfileStateDto.CurrentSchemaVersion,
                ProfileId = "profile-main",
                WorldDefinitionId = "world.default",
                WorldDefinitionVersion = 1,
                NodeDiscoveries = Array.Empty<NodeDiscoveryDto>(),
                EdgeDiscoveries = Array.Empty<EdgeDiscoveryDto>(),
                DiscoveredFactIds = Array.Empty<string>(),
                PersistentNoteIds = Array.Empty<string>(),
                RunSummaries = Array.Empty<ProfileRunSummaryDto>()
            };
        }


        private static string ToSnapshot(NodeDiscovery discovery)
        {
            return $"{discovery.NodeId}:{discovery.State}";
        }

        private static string ToSnapshot(EdgeDiscovery discovery)
        {
            return $"{discovery.EdgeId}:{discovery.State}";
        }
        private static RunStateDto CreateRunDto()
        {
            return new RunStateDto
            {
                SchemaVersion = RunStateDto.CurrentSchemaVersion,
                RunId = "run-main",
                RunSeed = 1,
                CurrentDay = 1,
                WorldNodeId = "node.start",
                Health = 100,
                Food = 100,
                Status = RunStatusIds.Active,
                ToolSlots = new[] { new ToolSlotDto(), new ToolSlotDto() },
                Route = Array.Empty<string>()
            };
        }

        private static void AssertRunEquals(RunState expected, RunState actual)
        {
            Assert.That(actual.RunId, Is.EqualTo(expected.RunId));
            Assert.That(actual.RunSeed, Is.EqualTo(expected.RunSeed));
            Assert.That(actual.CurrentDay, Is.EqualTo(expected.CurrentDay));
            Assert.That(actual.WorldNodeId, Is.EqualTo(expected.WorldNodeId));
            Assert.That(actual.Health, Is.EqualTo(expected.Health));
            Assert.That(actual.Food, Is.EqualTo(expected.Food));
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            Assert.That(actual.Route, Is.EqualTo(expected.Route));
            Assert.That(actual.ToolSlots, Has.Count.EqualTo(expected.ToolSlots.Count));
            for (int index = 0; index < expected.ToolSlots.Count; index++)
            {
                Assert.That(
                    actual.ToolSlots[index].ToolId,
                    Is.EqualTo(expected.ToolSlots[index].ToolId));
                Assert.That(
                    actual.ToolSlots[index].RemainingUses,
                    Is.EqualTo(expected.ToolSlots[index].RemainingUses));
            }
        }

        private static string ToPropertyName(string jsonFieldName)
        {
            return char.ToUpperInvariant(jsonFieldName[0]) + jsonFieldName.Substring(1);
        }
    }
}