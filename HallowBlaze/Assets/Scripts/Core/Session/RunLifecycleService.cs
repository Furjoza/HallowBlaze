using System;
using System.Linq;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Session
{
    public sealed class RunLifecycleService
    {
        private readonly GameSession session;
        private readonly ISaveStore saveStore;

        public RunLifecycleService(GameSession session, ISaveStore saveStore)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.saveStore = saveStore ?? throw new ArgumentNullException(nameof(saveStore));
        }

        public SaveStoreResult StartNewRun(
            string runId,
            int runSeed,
            RunStateConfiguration configuration)
        {
            RunState run = new RunState(runId, runSeed, configuration);
            SaveStoreResult profileResult = saveStore.SaveProfile(session.Profile);
            if (profileResult.IsFailure)
                return profileResult;

            SaveStoreResult saveResult = saveStore.SaveRun(run);
            if (saveResult.IsFailure)
                return saveResult;

            session.ContinueRun(run);
            return saveResult;
        }

        public SaveStoreResult<RunState> ContinueRun()
        {
            SaveStoreResult<RunState> loadResult = InspectContinue(saveStore);
            if (loadResult.IsFailure)
                return loadResult;

            RunState run = loadResult.Data;
            if (session.Profile.RunSummaries.Any(summary => summary.RunId == run.RunId))
                return SaveStoreResultExtensions.Corrupt<RunState>(
                    "The run save is not an active resumable run.");

            session.ContinueRun(run);
            return loadResult;
        }

        public static SaveStoreResult<RunState> InspectContinue(ISaveStore saveStore)
        {
            if (saveStore == null)
                throw new ArgumentNullException(nameof(saveStore));

            SaveStoreResult<ProfileState> profileResult = saveStore.LoadProfile();
            if (profileResult.IsFailure)
                return CopyFailure<RunState>(profileResult);

            SaveStoreResult<RunState> runResult = saveStore.LoadRun();
            if (runResult.IsFailure)
                return runResult;

            RunState run = runResult.Data;
            if (run == null ||
                run.Status != RunStatus.Active ||
                profileResult.Data.RunSummaries.Any(summary => summary.RunId == run.RunId))
                return SaveStoreResultExtensions.Corrupt<RunState>(
                    "The run save is not an active resumable run.");

            return runResult;
        }

        public SaveStoreResult SaveBoardBoundary()
        {
            return saveStore.SaveRun(RequireActiveRun());
        }

        public SaveStoreResult ExitToMenu()
        {
            RequireActiveRun();
            session.AbandonRun();
            return SaveStoreResult.Success();
        }

        public SaveStoreResult DeleteRunAndAbandon()
        {
            SaveStoreResult deleteResult = saveStore.DeleteRun();
            if (deleteResult.IsSuccess)
                session.AbandonRun();

            return deleteResult;
        }

        public SaveStoreResult MarkDead()
        {
            return CompleteRun(RunStatus.Dead);
        }

        public SaveStoreResult MarkWon()
        {
            return CompleteRun(RunStatus.Won);
        }

        private SaveStoreResult CompleteRun(RunStatus outcome)
        {
            if (outcome != RunStatus.Dead && outcome != RunStatus.Won)
                throw new ArgumentException("A terminal run outcome is required.", nameof(outcome));

            RunState run = session.GetCurrentRun();
            if (run.Status == RunStatus.Active)
            {
                if (outcome == RunStatus.Dead)
                    session.MarkDead();
                else
                    session.MarkWon();
            }
            else if (run.Status != outcome)
            {
                throw new InvalidOperationException(
                    "A completed run cannot be finalized with a different outcome.");
            }

            session.Profile.RecordRunSummary(
                new ProfileRunSummary(run.RunId, run.CurrentDay, outcome));

            SaveStoreResult profileResult = saveStore.SaveProfile(session.Profile);
            if (profileResult.IsFailure)
                return profileResult;

            SaveStoreResult deleteResult = saveStore.DeleteRun();
            if (deleteResult.IsSuccess)
                session.AbandonRun();

            return deleteResult;
        }

        private RunState RequireActiveRun()
        {
            RunState run = session.GetCurrentRun();
            if (run.Status != RunStatus.Active)
                throw new InvalidOperationException("The current run is already complete.");

            return run;
        }

        private static SaveStoreResult<T> CopyFailure<T>(SaveStoreResult<ProfileState> result)
        {
            return new SaveStoreResult<T>(result.Type, default, result.ErrorMessage);
        }
    }
}