using System;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;
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

        [Test]
        public void MissingFilesReturnTypedResults()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);

            Assert.That(store.LoadProfile().Type, Is.EqualTo(SaveStoreResultType.Missing));
            Assert.That(store.LoadRun().Type, Is.EqualTo(SaveStoreResultType.Missing));
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
            store.SaveRun(CreateRun("run-v0"));
            string currentPath = Path.Combine(testRootPath, "run.json");
            string v0Json = File.ReadAllText(currentPath)
                .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 0");
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