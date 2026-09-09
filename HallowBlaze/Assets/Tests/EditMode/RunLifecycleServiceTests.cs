using System;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class RunLifecycleServiceTests
    {
        [Test]
        public void ExitToMenuCommitsBoundaryAndPreservesContinue()
        {
            FakeSaveStore store = new FakeSaveStore();
            GameSession firstSession = CreateSession();
            RunLifecycleService firstLifecycle = new RunLifecycleService(firstSession, store);
            Assert.That(firstLifecycle.StartNewRun("run-001", 7, CreateConfiguration()).IsSuccess, Is.True);
            firstSession.ConsumeFood(12);

            Assert.That(firstLifecycle.ExitToMenu().IsSuccess, Is.True);
            Assert.That(firstSession.ActiveRun, Is.Null);

            GameSession restoredSession = CreateSession();
            SaveStoreResult<RunState> continueResult =
                new RunLifecycleService(restoredSession, store).ContinueRun();

            Assert.That(continueResult.IsSuccess, Is.True);
            Assert.That(restoredSession.ActiveRun.Food, Is.EqualTo(88));
        }

        [TestCase(RunStatus.Dead)]
        [TestCase(RunStatus.Won)]
        public void TerminalOutcomeSavesProfileAndClosesRun(RunStatus outcome)
        {
            FakeSaveStore store = new FakeSaveStore();
            GameSession session = CreateSession();
            RunLifecycleService lifecycle = new RunLifecycleService(session, store);
            lifecycle.StartNewRun("run-terminal", 11, CreateConfiguration());
            session.AdvanceDay();

            SaveStoreResult result = outcome == RunStatus.Dead
                ? lifecycle.MarkDead()
                : lifecycle.MarkWon();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(store.Run, Is.Null);
            Assert.That(store.Profile.RunSummaries, Has.Count.EqualTo(1));
            Assert.That(store.Profile.RunSummaries[0].Status, Is.EqualTo(outcome));
            Assert.That(new RunLifecycleService(CreateSession(), store).ContinueRun().Type,
                Is.EqualTo(SaveStoreResultType.Missing));
        }

        [Test]
        public void FailedExitSaveKeepsRunAttached()
        {
            FakeSaveStore store = new FakeSaveStore();
            GameSession session = CreateSession();
            RunLifecycleService lifecycle = new RunLifecycleService(session, store);
            lifecycle.StartNewRun("run-001", 7, CreateConfiguration());
            store.FailRunSave = true;

            SaveStoreResult result = lifecycle.ExitToMenu();

            Assert.That(result.Type, Is.EqualTo(SaveStoreResultType.IoError));
            Assert.That(session.ActiveRun, Is.Not.Null);
        }

        [Test]
        public void FailedTerminalPersistenceCanBeRetriedWithoutDuplicatingSummary()
        {
            FakeSaveStore store = new FakeSaveStore();
            GameSession session = CreateSession();
            RunLifecycleService lifecycle = new RunLifecycleService(session, store);
            lifecycle.StartNewRun("run-retry", 7, CreateConfiguration());
            store.FailProfileSave = true;

            Assert.That(lifecycle.MarkDead().Type, Is.EqualTo(SaveStoreResultType.IoError));
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(session.Profile.RunSummaries, Has.Count.EqualTo(1));

            store.FailProfileSave = false;
            Assert.That(lifecycle.MarkDead().IsSuccess, Is.True);
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(session.Profile.RunSummaries, Has.Count.EqualTo(1));
        }

        [Test]
        public void ContinueRejectsTerminalOrAlreadySummarizedRun()
        {
            FakeSaveStore store = new FakeSaveStore();
            RunState terminal = new RunState("run-terminal", 1, CreateConfiguration());
            terminal.MarkDead();
            store.SaveProfile(CreateSession().Profile);
            store.Run = terminal;

            Assert.That(
                new RunLifecycleService(CreateSession(), store).ContinueRun().Type,
                Is.EqualTo(SaveStoreResultType.Corrupt));

            GameSession summarizedSession = CreateSession();
            summarizedSession.Profile.RecordRunSummary(
                new ProfileRunSummary("run-summarized", 1, RunStatus.Won));
            store.SaveProfile(summarizedSession.Profile);
            store.Run = new RunState("run-summarized", 2, CreateConfiguration());

            Assert.That(
                new RunLifecycleService(summarizedSession, store).ContinueRun().Type,
                Is.EqualTo(SaveStoreResultType.Corrupt));
        }

        [Test]
        public void ContinueInspectionRejectsActiveRunAlreadySummarizedBySavedProfile()
        {
            FakeSaveStore store = new FakeSaveStore();
            ProfileState profile = new ProfileState("profile-001", "world-001", 1);
            profile.RecordRunSummary(
                new ProfileRunSummary("run-summarized", 1, RunStatus.Won));
            store.SaveProfile(profile);
            store.Run = new RunState("run-summarized", 2, CreateConfiguration());

            SaveStoreResult<RunState> result = RunLifecycleService.InspectContinue(store);

            Assert.That(result.Type, Is.EqualTo(SaveStoreResultType.Corrupt));
        }

        [Test]
        public void NewRunRequiresProfileSaveBeforePublishingRun()
        {
            FakeSaveStore store = new FakeSaveStore { FailProfileSave = true };
            GameSession session = CreateSession();

            SaveStoreResult result = new RunLifecycleService(session, store).StartNewRun(
                "run-001",
                7,
                CreateConfiguration());

            Assert.That(result.Type, Is.EqualTo(SaveStoreResultType.IoError));
            Assert.That(store.Run, Is.Null);
            Assert.That(session.ActiveRun, Is.Null);
        }

        private static GameSession CreateSession()
        {
            return new GameSession(new ProfileState("profile-001", "world-001", 1));
        }

        private static RunStateConfiguration CreateConfiguration()
        {
            return new RunStateConfiguration(100, 100, 0, "forest.start");
        }

        private sealed class FakeSaveStore : ISaveStore
        {
            public ProfileState Profile { get; private set; }
            public RunState Run { get; set; }
            public bool FailRunSave { get; set; }
            public bool FailProfileSave { get; set; }

            public SaveStoreResult SaveProfile(ProfileState profile)
            {
                if (FailProfileSave)
                    return SaveStoreResult.IoError();

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
                if (FailRunSave)
                    return SaveStoreResult.IoError();

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