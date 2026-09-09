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
                .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");
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
            FileSystemSaveStore store = new FileSystemSaveStore(testRootPath);
            store.SaveProfile(CreateProfile("profile-v0"));
            string currentPath = Path.Combine(testRootPath, "profile.json");
            string v0Json = File.ReadAllText(currentPath)
                .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 0");
            File.WriteAllText(currentPath, v0Json);

            SaveStoreResult result = SaveMigration.MigrateProfileV0ToV1(testRootPath);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(store.LoadProfile().Data.ProfileId, Is.EqualTo("profile-v0"));
            Assert.That(
                File.ReadAllText(Path.Combine(testRootPath, "profile.backup.json")),
                Is.EqualTo(v0Json));
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

        private static RunState CreateRun(string runId)
        {
            return new RunState(
                runId,
                123,
                new RunStateConfiguration(1, 100, 80, "node.start"));
        }
    }
}