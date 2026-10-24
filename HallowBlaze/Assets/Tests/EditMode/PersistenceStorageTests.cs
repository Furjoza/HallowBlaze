using System;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class PersistenceStorageTests
    {
        private string testRootPath;

        [SetUp]
        public void SetUp()
        {
            testRootPath = Path.Combine(
                Path.GetTempPath(),
                "HallowBlaze-M1.6-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRootPath))
                Directory.Delete(testRootPath, true);
        }

        [Test]
        public void SaveAndLoadKeepProfileAndRunInSeparateFiles()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);

            Assert.That(store.SaveProfile(CreateProfile("profile-main")).IsSuccess, Is.True);
            Assert.That(store.SaveRun(CreateRun("run-main")).IsSuccess, Is.True);

            Assert.That(File.Exists(Path.Combine(testRootPath, "profile.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(testRootPath, "run.json")), Is.True);
            Assert.That(store.LoadProfile().Data.ProfileId, Is.EqualTo("profile-main"));
            Assert.That(store.LoadRun().Data.RunId, Is.EqualTo("run-main"));
        }

        /// <summary>Equipment survives file round-trip and backup recovery without changing profile knowledge or resetting the allocator.</summary>
        [Test]
        public void ToolEquipmentRoundTripsAndRecoversWithoutMutatingProfile()
        {
            var store = new FileSystemSaveStore(testRootPath);
            ProfileState profile = CreateProfile("tool-owner");
            profile.AdvanceNodeDiscovery("forest.start", NodeDiscoveryState.Visited);
            Assert.That(store.SaveProfile(profile).IsSuccess, Is.True);
            byte[] profileBefore = File.ReadAllBytes(Path.Combine(testRootPath, "profile.json"));
            RunState run = CreateRun("tool-run");
            run.ClearTool(0);
            run.ClearTool(1);
            ToolInstanceState removed = run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3));
            run.EquipTool(0, removed);
            run.RemoveTool(0);
            ToolInstanceState kept = run.CreateToolInstance(new ToolDefinition("unknown.retained", "Unknown", 2));
            run.EquipTool(1, kept);
            run.TrySpendToolCharge(1, kept.InstanceId);
            Assert.That(store.SaveRun(run).IsSuccess, Is.True);
            RunState restored = new FileSystemSaveStore(testRootPath).LoadRun().Data;
            Assert.That(restored.ToolSlots[0].IsEmpty, Is.True);
            Assert.That(restored.ToolSlots[1].Instance.InstanceId, Is.EqualTo(kept.InstanceId));
            Assert.That(restored.ToolSlots[1].Instance.Charges, Is.EqualTo(1));
            Assert.That(restored.ToolInstanceSequence, Is.EqualTo(run.ToolInstanceSequence));
            Assert.That(restored.TrySpendToolCharge(1, kept.InstanceId), Is.True);
            Assert.That(run.ToolSlots[1].Instance.Charges, Is.EqualTo(1));
            Assert.That(store.SaveRun(run).IsSuccess, Is.True);
            File.WriteAllText(Path.Combine(testRootPath, "run.json"), "{corrupt");
            var recovered = store.LoadRun();
            Assert.That(recovered.Type, Is.EqualTo(SaveStoreResultType.Recovered));
            Assert.That(recovered.Data.ToolSlots[1].Instance.DefinitionId, Is.EqualTo("unknown.retained"));
            Assert.That(recovered.Data.ToolSlots[1].Instance.Charges, Is.EqualTo(1));
            Assert.That(recovered.Data.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3)).InstanceId,
                Is.Not.EqualTo(removed.InstanceId).And.Not.EqualTo(kept.InstanceId));
            recovered.Data.Reset("fresh-run", 42, new RunStateConfiguration(100, 100, 0, "forest.start"));
            Assert.That(recovered.Data.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(File.ReadAllBytes(Path.Combine(testRootPath, "profile.json")), Is.EqualTo(profileBefore));
        }

        /// <summary>Accepted charge changes persist at the existing run-save boundary; rejected replay cannot alter that snapshot.</summary>
        [Test]
        public void ToolCommandChargesUseExistingSaveBoundaryWithoutSchemaChange()
        {
            var store = new FileSystemSaveStore(testRootPath);
            RunState run = CreateRun("tool-command");
            run.ClearTool(0);
            ToolInstanceState tool = run.CreateToolInstance(new ToolDefinition("test.tool", "Test", 3));
            run.EquipTool(0, tool);
            var board = new BoardState();
            var playerId = new EntityId(1);
            var targetId = new EntityId(2);
            Assert.That(board.TryAdd(new BoardEntityState(playerId, new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Player, "player", new BoardEntityTraits(false, false, false)), new GridPosition(1, 1))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(targetId, new BoardEntityDefinition(BoardLayer.Item,
                EntityKind.Item, "test.target", new BoardEntityTraits(false, false, true)), new GridPosition(1, 2))), Is.True);
            var tools = new ToolUseQuery(new[] { new ToolAction("test.tool", new ContextInteractionAction(), "Interact", 2, 2) });
            var controller = new TurnController(board, run, playerId, new TurnResolver(tools: tools));
            UseToolCommand command = tools.GetOptions(board, run, playerId).Single().CreateCommand();
            Assert.That(controller.Resolve(command).Accepted, Is.True);
            Assert.That(store.SaveRun(run).IsSuccess, Is.True);
            RunState restored = new FileSystemSaveStore(testRootPath).LoadRun().Data;
            Assert.That(restored.ToolSlots[0].Instance.InstanceId, Is.EqualTo(tool.InstanceId));
            Assert.That(restored.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            Assert.That(restored.Food, Is.EqualTo(run.Food));
            Assert.That(File.ReadAllText(Path.Combine(testRootPath, "run.json")), Does.Contain("\"schemaVersion\": 5"));
            Assert.That(controller.Resolve(command).Accepted, Is.False);
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
        }

        [Test]
        public void MissingFilesReturnTypedResults()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);

            Assert.That(store.LoadProfile().Type, Is.EqualTo(SaveStoreResultType.Missing));
            Assert.That(store.LoadRun().Type, Is.EqualTo(SaveStoreResultType.Missing));
        }

        [TestCase("pending")]
        [TestCase("consumed")]
        [TestCase("intermediate")]
        [TestCase("complete")]
        public void TravelRunRecovery_PreservesProfileAndStableBoundary(string boundary)
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);
            ProfileState profile = CreateProfile("profile-travel");
            profile.AdvanceNodeDiscovery("b", NodeDiscoveryState.Visited);
            profile.AdvanceEdgeDiscovery("edge", EdgeDiscoveryState.Traversed);
            Assert.That(store.SaveProfile(profile).IsSuccess, Is.True);
            string profilePath = Path.Combine(testRootPath, "profile.json");
            byte[] profileBytes = File.ReadAllBytes(profilePath);
            RunState run = PersistenceDtoTests.TravelBoundary(boundary);
            Assert.That(store.SaveRun(run).IsSuccess, Is.True);
            RunState loaded = new FileSystemSaveStore(testRootPath).LoadRun().Data;
            PersistenceDtoTests.AssertTravelEquals(run, loaded);
            if (run.ActiveLeg != null)
                Assert.That(BoardAddressFactory.CreateFromState(loaded), Is.EqualTo(BoardAddressFactory.CreateFromState(run)));
            Assert.That(store.SaveRun(run).IsSuccess, Is.True);
            string runPath = Path.Combine(testRootPath, "run.json");
            File.WriteAllText(runPath, "{bad-json");
            var recovered = new FileSystemSaveStore(testRootPath).LoadRun();
            Assert.That(recovered.Type, Is.EqualTo(SaveStoreResultType.Recovered));
            PersistenceDtoTests.AssertTravelEquals(run, recovered.Data);
            File.WriteAllText(Path.Combine(testRootPath, "run.backup.json"), "{bad-backup");
            Assert.That(store.LoadRun().Type, Is.EqualTo(SaveStoreResultType.Corrupt));
            Assert.That(File.ReadAllBytes(profilePath), Is.EqualTo(profileBytes));
            Assert.That(store.LoadProfile().Data.GetEdgeDiscoveryState("edge"), Is.EqualTo(EdgeDiscoveryState.Traversed));
            const string legacy = "{\"schemaVersion\":1,\"runId\":\"old\",\"runSeed\":7,\"currentDay\":3,\"worldNodeId\":\"a\",\"health\":80,\"food\":90,\"status\":\"active\",\"toolSlots\":[{\"toolId\":\"axe\",\"remainingUses\":2},{\"toolId\":\"\",\"remainingUses\":0}],\"route\":[\"a\",\"a\"]}";
            File.WriteAllText(runPath, legacy);
            RunState migrated = new FileSystemSaveStore(testRootPath).LoadRun().Data;
            Assert.That(migrated.CompletedDayCount, Is.EqualTo(3));
            Assert.That(migrated.TravelHistory, Has.Count.EqualTo(2));
            Assert.That(migrated.TravelHistory.All(entry => entry.EdgeId == null && entry.LegId == null), Is.True);
            Assert.That(File.ReadAllText(runPath), Is.EqualTo(legacy), "Reading must not rewrite an old file.");
            Assert.That(store.SaveRun(migrated).IsSuccess, Is.True);
            Assert.That(File.ReadAllText(runPath), Does.Contain("\"schemaVersion\": 5")
                .And.Contain(RunState.LegacyRandomAlgorithmId));
            Assert.That(File.ReadAllText(Path.Combine(testRootPath, "run.backup.json")), Is.EqualTo(legacy));
            Assert.That(File.ReadAllBytes(profilePath), Is.EqualTo(profileBytes));
        }

        [Test]
        public void DeleteRunRemovesCurrentAndBackupWithoutTouchingProfile()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);
            store.SaveProfile(CreateProfile("profile-main"));
            store.SaveRun(CreateRun("run-first"));
            store.SaveRun(CreateRun("run-second"));

            SaveStoreResult result = store.DeleteRun();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(store.LoadRun().Type, Is.EqualTo(SaveStoreResultType.Missing));
            Assert.That(store.LoadProfile().Data.ProfileId, Is.EqualTo("profile-main"));
            Assert.That(File.Exists(Path.Combine(testRootPath, "run.backup.json")), Is.False);
        }

        [Test]
        public void SecondSaveCreatesLastKnownGoodBackup()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);
            store.SaveProfile(CreateProfile("profile-first"));

            Assert.That(store.SaveProfile(CreateProfile("profile-second")).IsSuccess, Is.True);

            string backupJson = File.ReadAllText(
                Path.Combine(testRootPath, "profile.backup.json"));
            ProfileState backup = PersistenceJsonSerializer.DeserializeProfile(backupJson);
            Assert.That(backup.ProfileId, Is.EqualTo("profile-first"));
            Assert.That(store.LoadProfile().Data.ProfileId, Is.EqualTo("profile-second"));
        }

        [Test]
        public void CorruptCurrentLoadsValidatedBackupAsRecovered()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);
            store.SaveProfile(CreateProfile("profile-backup"));
            store.SaveProfile(CreateProfile("profile-current"));
            File.WriteAllText(Path.Combine(testRootPath, "profile.json"), "not-json");

            SaveStoreResult<ProfileState> result = store.LoadProfile();

            Assert.That(result.Type, Is.EqualTo(SaveStoreResultType.Recovered));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data.ProfileId, Is.EqualTo("profile-backup"));
        }

        [Test]
        public void ResetGameCannotRecoverDiscoveryOrRunFromPreviousGame()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);
            ProfileState discoveredProfile = new ProfileState(
                "profile-main",
                "world.default",
                1);
            discoveredProfile.AdvanceNodeDiscovery(
                "forest.west-trail",
                NodeDiscoveryState.Visited);
            store.SaveProfile(discoveredProfile);
            store.SaveProfile(discoveredProfile);
            store.SaveRun(CreateRun("run-previous"));
            store.SaveRun(CreateRun("run-previous"));

            ProfileState freshProfile = new ProfileState(
                "profile-main",
                "world.default",
                1);
            SaveStoreResult resetResult = store.ResetGame(
                freshProfile,
                CreateRun("run-fresh"));

            Assert.That(resetResult.IsSuccess, Is.True);
            Assert.That(File.Exists(
                Path.Combine(testRootPath, "profile.backup.json")), Is.False);
            Assert.That(File.Exists(
                Path.Combine(testRootPath, "run.backup.json")), Is.False);
            Assert.That(store.LoadProfile().Data.NodeDiscoveries, Is.Empty);
            Assert.That(store.LoadRun().Data.RunId, Is.EqualTo("run-fresh"));

            File.WriteAllText(
                Path.Combine(testRootPath, "profile.json"),
                "not-json");
            Assert.That(store.LoadProfile().Type, Is.EqualTo(SaveStoreResultType.Corrupt));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ResetGameWriteFailureRestoresEveryPreviousSave(int failingReplaceCall)
        {
            FileSystemSaveStore initialStore = new FileSystemSaveStore(testRootPath);
            ProfileState previousProfile = new ProfileState(
                "profile-main",
                "world.default",
                1);
            previousProfile.AdvanceNodeDiscovery(
                "forest.west-trail",
                NodeDiscoveryState.Visited);
            initialStore.SaveProfile(previousProfile);
            initialStore.SaveProfile(previousProfile);
            initialStore.SaveRun(CreateRun("run-previous"));
            initialStore.SaveRun(CreateRun("run-previous"));
            string[] saveFileNames =
            {
                "profile.json",
                "profile.backup.json",
                "run.json",
                "run.backup.json"
            };
            string[] previousContents = saveFileNames
                .Select(fileName => File.ReadAllText(Path.Combine(testRootPath, fileName)))
                .ToArray();
            int replaceCallCount = 0;
            FileSystemSaveStore failingStore = new FileSystemSaveStore(
                testRootPath,
                (source, destination, backup) =>
                {
                    replaceCallCount++;
                    if (replaceCallCount == failingReplaceCall)
                        throw new IOException("Injected new-game replace failure.");

                    File.Replace(source, destination, backup);
                });

            SaveStoreResult result = failingStore.ResetGame(
                new ProfileState("profile-main", "world.default", 1),
                CreateRun("run-fresh"));

            Assert.That(result.Type, Is.EqualTo(SaveStoreResultType.IoError));
            Assert.That(
                saveFileNames.Select(fileName => File.ReadAllText(
                    Path.Combine(testRootPath, fileName))),
                Is.EqualTo(previousContents));
            Assert.That(
                Directory.GetFiles(testRootPath, ".*.tmp*"),
                Is.Empty);
        }

        [Test]
        public void ResetGameSnapshotFailureLeavesEveryPreviousSaveUntouched()
        {
            FileSystemSaveStore initialStore = new FileSystemSaveStore(testRootPath);
            ProfileState previousProfile = new ProfileState(
                "profile-main",
                "world.default",
                1);
            previousProfile.AdvanceNodeDiscovery(
                "forest.west-trail",
                NodeDiscoveryState.Visited);
            initialStore.SaveProfile(previousProfile);
            initialStore.SaveProfile(previousProfile);
            initialStore.SaveRun(CreateRun("run-previous"));
            initialStore.SaveRun(CreateRun("run-previous"));
            string[] saveFileNames =
            {
                "profile.json",
                "profile.backup.json",
                "run.json",
                "run.backup.json"
            };
            byte[][] previousContents = saveFileNames
                .Select(fileName => File.ReadAllBytes(Path.Combine(testRootPath, fileName)))
                .ToArray();
            int copyCallCount = 0;
            FileSystemSaveStore failingStore = new FileSystemSaveStore(
                testRootPath,
                copyFile: (source, destination) =>
                {
                    copyCallCount++;
                    if (copyCallCount == 2)
                        throw new IOException("Injected new-game snapshot failure.");

                    File.Copy(source, destination);
                });

            SaveStoreResult result = failingStore.ResetGame(
                new ProfileState("profile-main", "world.default", 1),
                CreateRun("run-fresh"));

            Assert.That(result.Type, Is.EqualTo(SaveStoreResultType.IoError));
            for (int index = 0; index < saveFileNames.Length; index++)
            {
                Assert.That(
                    File.ReadAllBytes(Path.Combine(testRootPath, saveFileNames[index])),
                    Is.EqualTo(previousContents[index]));
            }

            Assert.That(
                Directory.GetFiles(testRootPath, ".*.tmp*"),
                Is.Empty);
        }

        [Test]
        public void InterruptedReplacePreservesCurrentAndCleansTemporaryFile()
        {
            FileSystemSaveStore initialStore = new FileSystemSaveStore(testRootPath);
            initialStore.SaveProfile(CreateProfile("profile-safe"));
            FileSystemSaveStore failingStore = new FileSystemSaveStore(
                testRootPath,
                (_, _, _) => throw new IOException("Injected replace failure."));

            SaveStoreResult result = failingStore.SaveProfile(
                CreateProfile("profile-never-committed"));

            Assert.That(result.Type, Is.EqualTo(SaveStoreResultType.IoError));
            Assert.That(initialStore.LoadProfile().Data.ProfileId, Is.EqualTo("profile-safe"));
            Assert.That(
                Directory.GetFiles(testRootPath, "*.tmp").Concat(
                    Directory.GetFiles(testRootPath, ".*.tmp")),
                Is.Empty);
        }

        [Test]
        public void FileSystemFailureReturnsTypedIoError()
        {
            Directory.CreateDirectory(testRootPath);
            string pathOccupiedByFile = Path.Combine(testRootPath, "occupied");
            File.WriteAllText(pathOccupiedByFile, "occupied");
            FileSystemSaveStore store = new FileSystemSaveStore(pathOccupiedByFile);

            SaveStoreResult result = store.SaveProfile(CreateProfile("profile-io"));

            Assert.That(result.Type, Is.EqualTo(SaveStoreResultType.IoError));
            Assert.That(result.ErrorMessage, Does.Not.Contain("profile-io"));
        }

        [Test]
        public void FutureRunSchemaDoesNotRecoverAnOlderBackupOrRewriteFiles()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);
            RunState run = PersistenceDtoTests.TravelBoundary("consumed");
            Assert.That(store.SaveRun(run).IsSuccess, Is.True);
            Assert.That(store.SaveRun(run).IsSuccess, Is.True);
            string currentPath = Path.Combine(testRootPath, "run.json");
            string backupPath = Path.Combine(testRootPath, "run.backup.json");
            string backup = File.ReadAllText(backupPath);
            var document = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(currentPath));
            document["schemaVersion"] = 6;
            string future = document.ToString();
            File.WriteAllText(currentPath, future);
            Assert.That(store.LoadRun().Type, Is.EqualTo(SaveStoreResultType.UnsupportedFutureSchema));
            Assert.That(File.ReadAllText(currentPath), Is.EqualTo(future));
            Assert.That(File.ReadAllText(backupPath), Is.EqualTo(backup));
        }

        [Test]
        public void FutureCurrentIsNotRecoveredFromOlderBackup()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);
            store.SaveProfile(CreateProfile("profile-backup"));
            store.SaveProfile(CreateProfile("profile-current"));
            string currentPath = Path.Combine(testRootPath, "profile.json");
            string futureJson = File.ReadAllText(currentPath)
                .Replace("\"schemaVersion\": 2", "\"schemaVersion\": 3");
            File.WriteAllText(currentPath, futureJson);

            SaveStoreResult<ProfileState> result = store.LoadProfile();

            Assert.That(
                result.Type,
                Is.EqualTo(SaveStoreResultType.UnsupportedFutureSchema));
            Assert.That(File.ReadAllText(currentPath), Is.EqualTo(futureJson));
        }

        [Test]
        public void ProfileV0MigrationValidatesV1AndBacksUpSource()
        {
            string currentPath = Path.Combine(testRootPath, "profile.json");
            Directory.CreateDirectory(testRootPath);
            string v0Json = CreateLegacyProfileJson(0);
            File.WriteAllText(currentPath, v0Json);

            SaveStoreResult result = SaveMigration.MigrateProfileV0ToV1(testRootPath);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                File.ReadAllText(currentPath),
                Does.Contain("\"schemaVersion\": 1"));
            Assert.That(
                File.ReadAllText(Path.Combine(testRootPath, "profile.backup.json")),
                Is.EqualTo(v0Json));
        }

        [Test]
        public void ProfileV1MigrationMapsLegacyDiscoveriesToSightedAndBacksUpSource()
        {
            Directory.CreateDirectory(testRootPath);
            string currentPath = Path.Combine(testRootPath, "profile.json");
            string v1Json = CreateLegacyProfileJson(1);
            File.WriteAllText(currentPath, v1Json);

            SaveStoreResult migration = SaveMigration.MigrateProfileV1ToV2(testRootPath);
            SaveStoreResult<ProfileState> load =
                new FileSystemSaveStore(testRootPath).LoadProfile();

            Assert.That(migration.IsSuccess, Is.True);
            Assert.That(load.IsSuccess, Is.True);
            Assert.That(load.Data.ProfileId, Is.EqualTo("profile-legacy"));
            Assert.That(load.Data.WorldDefinitionId, Is.EqualTo("world.legacy"));
            Assert.That(load.Data.WorldDefinitionVersion, Is.EqualTo(4));
            Assert.That(
                load.Data.NodeDiscoveries.Select(discovery => discovery.NodeId),
                Is.EqualTo(new[] { "node.second", "node.first" }));
            Assert.That(
                load.Data.NodeDiscoveries.Select(discovery => discovery.State),
                Is.All.EqualTo(NodeDiscoveryState.Sighted));
            Assert.That(
                load.Data.EdgeDiscoveries.Select(discovery => discovery.EdgeId),
                Is.EqualTo(new[] { "edge.second-first", "edge.first-goal" }));
            Assert.That(
                load.Data.EdgeDiscoveries.Select(discovery => discovery.State),
                Is.All.EqualTo(EdgeDiscoveryState.Sighted));
            Assert.That(load.Data.DiscoveredFactIds, Is.EqualTo(new[] { "fact.legacy" }));
            Assert.That(load.Data.PersistentNoteIds, Is.EqualTo(new[] { "note.legacy" }));
            Assert.That(load.Data.RunSummaries, Has.Count.EqualTo(1));
            Assert.That(load.Data.RunSummaries[0].RunId, Is.EqualTo("run-legacy"));
            Assert.That(load.Data.RunSummaries[0].DaysSurvived, Is.EqualTo(3));
            Assert.That(load.Data.RunSummaries[0].Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(
                File.ReadAllText(Path.Combine(testRootPath, "profile.backup.json")),
                Is.EqualTo(v1Json));
            Assert.That(
                Directory.GetFiles(testRootPath, ".profile.json.migration.*.tmp"),
                Is.Empty);
        }

        [Test]
        public void RunV0MigrationPreservesRunAndFutureVersionIsProtected()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);
            Directory.CreateDirectory(testRootPath);
            string currentPath = Path.Combine(testRootPath, "run.json");
            const string v0Json = "{\"schemaVersion\":0,\"runId\":\"run-v0\",\"runSeed\":7,\"currentDay\":3,\"worldNodeId\":\"node.start\",\"health\":80,\"food\":90,\"status\":\"active\",\"toolSlots\":[{\"toolId\":\"axe\",\"remainingUses\":2},{\"toolId\":\"\",\"remainingUses\":0}],\"route\":[\"node.start\"]}";
            File.WriteAllText(currentPath, v0Json);

            Assert.That(SaveMigration.MigrateRunV0ToV1(testRootPath).IsSuccess, Is.True);
            Assert.That(store.LoadRun().Data.RunId, Is.EqualTo("run-v0"));

            string futureJson = File.ReadAllText(currentPath)
                .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");
            File.WriteAllText(currentPath, futureJson);
            SaveStoreResult futureResult = SaveMigration.MigrateRunV0ToV1(testRootPath);
            Assert.That(
                futureResult.Type,
                Is.EqualTo(SaveStoreResultType.UnsupportedFutureSchema));
            Assert.That(File.ReadAllText(currentPath), Is.EqualTo(futureJson));
        }

        private static ProfileState CreateProfile(string profileId)
        {
            ProfileState profile = new ProfileState(profileId, "world.default", 1);
            profile.DiscoverFact("fact.persistence");
            return profile;
        }

        private static string CreateLegacyProfileJson(int schemaVersion)
        {
            return
                "{\n" +
                $"  \"schemaVersion\": {schemaVersion},\n" +
                "  \"profileId\": \"profile-legacy\",\n" +
                "  \"worldDefinitionId\": \"world.legacy\",\n" +
                "  \"worldDefinitionVersion\": 4,\n" +
                "  \"discoveredNodeIds\": [\"node.second\", \"node.first\"],\n" +
                "  \"discoveredEdgeIds\": [\"edge.second-first\", \"edge.first-goal\"],\n" +
                "  \"discoveredFactIds\": [\"fact.legacy\"],\n" +
                "  \"persistentNoteIds\": [\"note.legacy\"],\n" +
                "  \"runSummaries\": [{\"runId\": \"run-legacy\", \"daysSurvived\": 3, \"status\": \"dead\"}]\n" +
                "}";
        }

        private static RunState CreateRun(string runId)
        {
            return new RunState(
                runId,
                123,
                new RunStateConfiguration(1, 100, 80, "node.start"));
        }
    }
}