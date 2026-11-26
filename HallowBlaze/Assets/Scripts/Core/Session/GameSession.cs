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
        /// <summary>Raised after successful positive session healing with its applied amount; zero, rejected and failed mutations do not notify.</summary>
        public event Action<RunState, int> OnHealthRestored;

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

        /// <summary>Spends an explicit nonnegative cost on the active run and notifies observers.</summary>
        public void ConsumeSatiety(int amount = 1)
        {
            Mutate(run => run.ConsumeSatiety(amount));
        }

        /// <summary>Restores the active run's satiety under its cap; excess is discarded.</summary>
        public void RestoreSatiety(int amount)
        {
            Mutate(run => run.RestoreSatiety(amount));
        }

        public void TakeDamage(int amount)
        {
            Mutate(run => run.TakeDamage(amount));
        }

        /// <summary>Applies existing health restoration and reports the positive applied amount independently of later damage or refreshes.</summary>
        /// <param name="amount">Nonnegative requested healing; only the applied amount is announced.</param>
        /// <exception cref="ArgumentOutOfRangeException">The amount is negative.</exception>
        /// <exception cref="InvalidOperationException">There is no active mutable run.</exception>
        public void RestoreHealth(int amount)
        {
            RunState restoredRun = GetCurrentRun();
            ResourceGain gain = restoredRun.RestoreHealth(amount);
            OnRunChanged?.Invoke(restoredRun);
            if (gain.Applied > 0)
                OnHealthRestored?.Invoke(restoredRun, gain.Applied);
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

        /// <summary>
        /// Atomically advances the active run to the specified world node, increasing the current day by one and recording the node on the route.
        /// </summary>
        /// <param name="worldNodeId">The stable ID of the world node to advance to.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="worldNodeId"/> is not a valid stable ID.</exception>
        /// <exception cref="InvalidOperationException">Thrown when there is no active run.</exception>
        /// <exception cref="OverflowException">Thrown when the current day cannot be increased.</exception>
        public void AdvanceToWorldNode(string worldNodeId)
        {
            Mutate(run => run.AdvanceToWorldNode(worldNodeId));
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