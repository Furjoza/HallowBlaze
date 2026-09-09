using System;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Session
{
    public sealed class GameSession
    {
        private RunState activeRunState;

        public GameSession(ProfileState profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public event Action<RunState> OnRunStarted;
        public event Action<RunState> OnRunChanged;
        public event Action<RunState> OnRunAbandoned;

        public ProfileState Profile { get; }
        public RunState ActiveRun => activeRunState;

        public RunState StartNewRun(
            string runId,
            int runSeed,
            RunStateConfiguration configuration)
        {
            RunState nextRun = new RunState(runId, runSeed, configuration);

            AttachRun(nextRun);
            return nextRun;
        }

        public void ContinueRun(RunState run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (run.Status != RunStatus.Active)
                throw new InvalidOperationException("Only an active run can be continued.");

            AttachRun(run);
        }

        public void AbandonRun()
        {
            RunState abandonedRun = activeRunState;
            if (abandonedRun == null)
                return;

            activeRunState = null;
            OnRunAbandoned?.Invoke(abandonedRun);
        }

        public bool IsRunActive()
        {
            return activeRunState != null;
        }

        public RunState GetCurrentRun()
        {
            if (activeRunState == null)
                throw new InvalidOperationException("No active run in session.");

            return activeRunState;
        }

        public void ConsumeFood(int amount = 1)
        {
            Mutate(run => run.ConsumeFood(amount));
        }

        public void RestoreFood(int amount)
        {
            Mutate(run => run.RestoreFood(amount));
        }

        public void TakeDamage(int amount)
        {
            Mutate(run => run.TakeDamage(amount));
        }

        public void RestoreHealth(int amount)
        {
            Mutate(run => run.RestoreHealth(amount));
        }

        public void AdvanceDay()
        {
            Mutate(run => run.AdvanceDay());
        }

        public void MarkDead()
        {
            Mutate(run => run.MarkDead());
        }

        public void MarkWon()
        {
            Mutate(run => run.MarkWon());
        }

        private void Mutate(Action<RunState> mutation)
        {
            RunState run = GetCurrentRun();
            mutation(run);
            OnRunChanged?.Invoke(run);
        }

        private void AttachRun(RunState run)
        {
            AbandonRun();
            activeRunState = run;
            OnRunStarted?.Invoke(run);
        }
    }
}