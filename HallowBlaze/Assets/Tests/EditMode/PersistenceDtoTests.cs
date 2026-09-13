using System;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.Persistence.Mapping;
using HallowBlaze.Core.State;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class PersistenceDtoTests
    {
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
                : "{\"schemaVersion\":2}";

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