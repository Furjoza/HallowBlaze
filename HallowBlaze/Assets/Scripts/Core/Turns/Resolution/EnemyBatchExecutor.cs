using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Dispatches retained enemy opportunities serially in stable board-local initiative order.
    /// This isolated foundation is not a production conflict resolver or a new turn owner.
    /// </summary>
    public sealed class EnemyBatchExecutor
    {
        private readonly ShamblerIntentExecutor executor = new ShamblerIntentExecutor();

        /// <summary>
        /// Validates the entire batch before dispatch, then appends each actor's outcomes unchanged
        /// in ascending ID order. Each single-enemy executor consumes its own intent exactly once.
        /// Movement uses live occupancy: later actors may enter cells vacated by earlier actors.
        /// Another enemy on the destination produces a wait without retry; reciprocal swaps remain blocked.
        /// The declared attack condition for the original player on a locked destination remains unchanged.
        /// No player cost or extra turn is applied. The caller owns board/run lifetime and terminal
        /// checks and must not mutate supplied states concurrently during this synchronous phase.
        /// </summary>
        /// <param name="boardState">The authoritative board used by the single-enemy executor.</param>
        /// <param name="runState">The active run; terminal handling remains with the controller.</param>
        /// <param name="enemies">Unique non-null states, each retaining one locked intent.</param>
        /// <param name="events">The ordered writable collection to append outcome events to.</param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        /// <exception cref="ArgumentException">An enemy entry is null or an actor identity is duplicated.</exception>
        /// <exception cref="InvalidOperationException">The run is terminal or an enemy has no locked intent.</exception>
        public void Execute(BoardState boardState, RunState runState, IEnumerable<ShamblerState> enemies,
            ICollection<GameEvent> events)
        {
            if (boardState == null)
                throw new ArgumentNullException(nameof(boardState));
            if (runState == null)
                throw new ArgumentNullException(nameof(runState));
            if (events == null)
                throw new ArgumentNullException(nameof(events));
            if (runState.Status != RunStatus.Active)
                throw new InvalidOperationException("Enemy execution requires an active run.");

            IReadOnlyList<ShamblerState> ordered = EnemyInitiativeOrder.Create(enemies);
            foreach (ShamblerState enemy in ordered)
            {
                if (enemy.LockedIntent == null)
                    throw new InvalidOperationException("Every enemy must retain an intent before batch execution.");
            }
            foreach (ShamblerState enemy in ordered)
                executor.Execute(boardState, runState, enemy, events);
        }
    }
}