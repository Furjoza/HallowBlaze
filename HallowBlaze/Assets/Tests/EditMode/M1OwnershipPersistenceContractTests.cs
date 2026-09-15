using System;
using System.IO;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using NUnit.Framework;
using UnityEngine;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class M1OwnershipPersistenceContractTests
    {
        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            testRoot = Path.Combine(
                Path.GetTempPath(),
                "HallowBlaze-M1.8-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(testRoot) &&
                testRoot.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(testRoot).StartsWith("HallowBlaze-M1.8-", StringComparison.Ordinal) &&
                Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void FullLifecycleAcrossFreshSessionsPreservesProfileAndIsolatesRuns()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRoot);
            ProfileState profile = CreateProfile("profile-main");
            profile.DiscoverFact("fact.first-run");
            GameSession firstSession = new GameSession(profile);
            RunLifecycleService firstLifecycle = new RunLifecycleService(firstSession, store);

            Assert.That(firstLifecycle.StartNewRun(
                "run-dead",
                101,
                CreateConfiguration()).IsSuccess, Is.True);
            firstSession.AdvanceDay();
            firstSession.ConsumeFood(20);
            Assert.That(firstLifecycle.SaveBoardBoundary().IsSuccess, Is.True);

            GameSession continuedSession = LoadSession(store);
            RunLifecycleService continuedLifecycle = new RunLifecycleService(continuedSession, store);
            Assert.That(continuedLifecycle.ContinueRun().IsSuccess, Is.True);
            Assert.That(continuedSession.ActiveRun.CurrentDay, Is.EqualTo(1));
            Assert.That(continuedSession.ActiveRun.Food, Is.EqualTo(80));
            Assert.That(continuedLifecycle.MarkDead().IsSuccess, Is.True);

            GameSession secondSession = LoadSession(store);
            RunLifecycleService secondLifecycle = new RunLifecycleService(secondSession, store);
            Assert.That(secondSession.Profile.DiscoveredFactIds,
                Does.Contain("fact.first-run"));
            Assert.That(secondSession.Profile.RunSummaries, Has.Count.EqualTo(1));
            Assert.That(secondLifecycle.ContinueRun().Type,
                Is.EqualTo(SaveStoreResultType.Missing));

            Assert.That(secondLifecycle.StartNewRun(
                "run-won",
                202,
                CreateConfiguration()).IsSuccess, Is.True);
            Assert.That(secondSession.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(secondSession.ActiveRun.Health, Is.EqualTo(100));
            secondSession.AdvanceDay();
            secondSession.AdvanceDay();
            Assert.That(secondLifecycle.MarkWon().IsSuccess, Is.True);

            ProfileState completedProfile = store.LoadProfile().Data;
            Assert.That(completedProfile.DiscoveredFactIds,
                Does.Contain("fact.first-run"));
            Assert.That(completedProfile.RunSummaries, Has.Count.EqualTo(2));
            Assert.That(completedProfile.RunSummaries[0].RunId, Is.EqualTo("run-dead"));
            Assert.That(completedProfile.RunSummaries[0].Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(completedProfile.RunSummaries[1].RunId, Is.EqualTo("run-won"));
            Assert.That(completedProfile.RunSummaries[1].Status, Is.EqualTo(RunStatus.Won));
            Assert.That(store.LoadRun().Type, Is.EqualTo(SaveStoreResultType.Missing));
        }

        [Test]
        public void BoardBoundaryRestoresLastCommittedStateOnly()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRoot);
            GameSession session = new GameSession(CreateProfile("profile-boundary"));
            RunLifecycleService lifecycle = new RunLifecycleService(session, store);
            lifecycle.StartNewRun("run-boundary", 303, CreateConfiguration());
            session.AdvanceDay();
            session.ConsumeFood(15);
            int boardSeed = session.ActiveRun.GetBoardSeed();
            Assert.That(lifecycle.SaveBoardBoundary().IsSuccess, Is.True);

            session.ConsumeFood(40);
            session.TakeDamage(25);

            GameSession restoredSession = LoadSession(store);
            SaveStoreResult<RunState> result =
                new RunLifecycleService(restoredSession, store).ContinueRun();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(restoredSession.ActiveRun.CurrentDay, Is.EqualTo(1));
            Assert.That(restoredSession.ActiveRun.Food, Is.EqualTo(85));
            Assert.That(restoredSession.ActiveRun.Health, Is.EqualTo(100));
            Assert.That(restoredSession.ActiveRun.GetBoardSeed(), Is.EqualTo(boardSeed));
        }

        [Test]
        public void ExitToMenuPreservesTargetRunWhileLegacyAbandonDeletesIt()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRoot);
            GameSession targetSession = new GameSession(CreateProfile("profile-exit"));
            RunLifecycleService targetLifecycle = new RunLifecycleService(targetSession, store);
            targetLifecycle.StartNewRun("run-target", 404, CreateConfiguration());
            targetSession.ConsumeFood(10);

            Assert.That(targetLifecycle.ExitToMenu().IsSuccess, Is.True);
            Assert.That(targetSession.ActiveRun, Is.Null);

            GameSession continuedSession = LoadSession(store);
            RunLifecycleService continuedLifecycle = new RunLifecycleService(continuedSession, store);
            Assert.That(continuedLifecycle.ContinueRun().IsSuccess, Is.True);
            Assert.That(continuedSession.ActiveRun.Food, Is.EqualTo(100));

            Assert.That(continuedLifecycle.DeleteRunAndAbandon().IsSuccess, Is.True);
            Assert.That(continuedSession.ActiveRun, Is.Null);
            Assert.That(store.LoadRun().Type, Is.EqualTo(SaveStoreResultType.Missing));
            Assert.That(store.LoadProfile().Data.ProfileId, Is.EqualTo("profile-exit"));
        }

        [Test]
        public void CorruptRunRecoversValidatedBackupOrReturnsControlledFailure()
        {
            string recoveredRoot = Path.Combine(testRoot, "recovered");
            FileSystemSaveStore recoveredStore = new FileSystemSaveStore(recoveredRoot);
            GameSession recoveredSession = new GameSession(CreateProfile("profile-recovered"));
            RunLifecycleService recoveredLifecycle =
                new RunLifecycleService(recoveredSession, recoveredStore);
            recoveredLifecycle.StartNewRun("run-recovered", 505, CreateConfiguration());
            recoveredSession.ConsumeFood(20);
            recoveredLifecycle.SaveBoardBoundary();
            File.WriteAllText(Path.Combine(recoveredRoot, "run.json"), "not-json");

            SaveStoreResult<RunState> recoveredResult =
                RunLifecycleService.InspectContinue(recoveredStore);

            Assert.That(recoveredResult.Type, Is.EqualTo(SaveStoreResultType.Recovered));
            Assert.That(recoveredResult.Data.RunId, Is.EqualTo("run-recovered"));
            Assert.That(recoveredResult.Data.Food, Is.EqualTo(100));

            string corruptRoot = Path.Combine(testRoot, "corrupt");
            FileSystemSaveStore corruptStore = new FileSystemSaveStore(corruptRoot);
            corruptStore.SaveProfile(CreateProfile("profile-corrupt"));
            File.WriteAllText(Path.Combine(corruptRoot, "run.json"), "not-json");

            SaveStoreResult<RunState> corruptResult =
                RunLifecycleService.InspectContinue(corruptStore);

            Assert.That(corruptResult.Type, Is.EqualTo(SaveStoreResultType.Corrupt));
            Assert.That(corruptResult.Data, Is.Null);
        }

        [Test]
        public void CorruptProfileRejectsContinueWithoutMutatingValidRunFiles()
        {
            string corruptRoot = Path.Combine(testRoot, "corrupt-profile");
            FileSystemSaveStore store = new FileSystemSaveStore(corruptRoot);
            GameSession sourceSession = new GameSession(CreateProfile("profile-valid"));
            RunLifecycleService sourceLifecycle = new RunLifecycleService(sourceSession, store);

            Assert.That(sourceLifecycle.StartNewRun(
                "run-valid",
                606,
                CreateConfiguration()).IsSuccess, Is.True);
            sourceSession.ConsumeFood(20);
            Assert.That(sourceLifecycle.SaveBoardBoundary().IsSuccess, Is.True);

            string runPath = Path.Combine(corruptRoot, "run.json");
            string runBackupPath = Path.Combine(corruptRoot, "run.backup.json");
            byte[] runBefore = File.ReadAllBytes(runPath);
            byte[] runBackupBefore = File.ReadAllBytes(runBackupPath);
            File.WriteAllText(Path.Combine(corruptRoot, "profile.json"), "not-json");

            GameSession continuedSession = new GameSession(CreateProfile("profile-fallback"));
            SaveStoreResult<RunState> result =
                new RunLifecycleService(continuedSession, store).ContinueRun();

            Assert.That(result.Type, Is.EqualTo(SaveStoreResultType.Corrupt));
            Assert.That(result.Data, Is.Null);
            Assert.That(continuedSession.ActiveRun, Is.Null);
            CollectionAssert.AreEqual(runBefore, File.ReadAllBytes(runPath));
            CollectionAssert.AreEqual(runBackupBefore, File.ReadAllBytes(runBackupPath));
        }

        [Test]
        public void SeparateRootsKeepProfilesRunsAndCleanupIsolated()
        {
            string firstRoot = Path.Combine(testRoot, "first");
            string secondRoot = Path.Combine(testRoot, "second");
            FileSystemSaveStore firstStore = new FileSystemSaveStore(firstRoot);
            FileSystemSaveStore secondStore = new FileSystemSaveStore(secondRoot);
            GameSession firstSession = new GameSession(CreateProfile("profile-first"));
            GameSession secondSession = new GameSession(CreateProfile("profile-second"));
            RunLifecycleService firstLifecycle = new RunLifecycleService(firstSession, firstStore);
            RunLifecycleService secondLifecycle = new RunLifecycleService(secondSession, secondStore);

            firstSession.Profile.DiscoverFact("fact.first");
            secondSession.Profile.DiscoverFact("fact.second");
            firstLifecycle.StartNewRun("run-first", 1, CreateConfiguration());
            secondLifecycle.StartNewRun("run-second", 2, CreateConfiguration());
            secondSession.ConsumeFood(30);
            secondLifecycle.SaveBoardBoundary();
            string secondProfileBefore = File.ReadAllText(Path.Combine(secondRoot, "profile.json"));
            string secondRunBefore = File.ReadAllText(Path.Combine(secondRoot, "run.json"));

            Assert.That(firstLifecycle.MarkDead().IsSuccess, Is.True);

            Assert.That(File.ReadAllText(Path.Combine(secondRoot, "profile.json")),
                Is.EqualTo(secondProfileBefore));
            Assert.That(File.ReadAllText(Path.Combine(secondRoot, "run.json")),
                Is.EqualTo(secondRunBefore));
            Assert.That(secondStore.LoadProfile().Data.ProfileId, Is.EqualTo("profile-second"));
            Assert.That(secondStore.LoadRun().Data.RunId, Is.EqualTo("run-second"));
            Assert.That(secondStore.LoadRun().Data.Food, Is.EqualTo(70));
        }

        [Test]
        public void PersistedFixturesDeclareSchemaVersionOne()
        {
            FileSystemSaveStore store = new FileSystemSaveStore(testRoot);
            GameSession session = new GameSession(CreateProfile("profile-schema"));
            RunLifecycleService lifecycle = new RunLifecycleService(session, store);

            Assert.That(lifecycle.StartNewRun(
                "run-schema",
                707,
                CreateConfiguration()).IsSuccess, Is.True);

            SchemaEnvelope profileEnvelope = JsonUtility.FromJson<SchemaEnvelope>(
                File.ReadAllText(Path.Combine(testRoot, "profile.json")));
            SchemaEnvelope runEnvelope = JsonUtility.FromJson<SchemaEnvelope>(
                File.ReadAllText(Path.Combine(testRoot, "run.json")));

            Assert.That(profileEnvelope.schemaVersion, Is.EqualTo(1));
            Assert.That(runEnvelope.schemaVersion, Is.EqualTo(1));
        }

        [Serializable]
        private sealed class SchemaEnvelope
        {
            public int schemaVersion = -1;
        }

        private static GameSession LoadSession(FileSystemSaveStore store)
        {
            SaveStoreResult<ProfileState> result = store.LoadProfile();
            Assert.That(result.IsSuccess, Is.True);
            return new GameSession(result.Data);
        }

        private static ProfileState CreateProfile(string profileId)
        {
            return new ProfileState(profileId, "world.default", 1);
        }

        private static RunStateConfiguration CreateConfiguration()
        {
            return new RunStateConfiguration(100, 100, 0, "forest.start");
        }
    }
}