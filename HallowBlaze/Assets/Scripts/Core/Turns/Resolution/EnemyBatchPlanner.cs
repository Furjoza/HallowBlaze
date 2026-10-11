using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Plans one enemy batch from a single borrowed, unchanged authoritative board.
    /// No movement is simulated between planners and no cadence step is consumed.
    /// </summary>
    public sealed class EnemyBatchPlanner
    {
        private readonly ShamblerPlanner planner = new ShamblerPlanner();

        /// <summary>
        /// Validates the whole batch before retaining one next intent per enemy in stable initiative order.
        /// Existing locked intents cannot be replaced. The caller must not mutate the board or enemy
        /// states during this synchronous call. Only intent retention changes; board/run/cadence do not.
        /// </summary>
        /// <param name="boardState">The common authoritative board borrowed by every single-enemy planner.</param>
        /// <param name="enemies">Unique enemy states with no currently locked intent.</param>
        /// <param name="playerId">The original player's board-local identity.</param>
        /// <returns>A detached read-only batch of the exact retained intents in ascending actor-ID order.</returns>
        /// <exception cref="ArgumentNullException">The board or enemy collection is null.</exception>
        /// <exception cref="ArgumentException">An enemy entry is null or an actor ID is duplicated.</exception>
        /// <exception cref="InvalidOperationException">An enemy already has a locked intent.</exception>
        public IReadOnlyList<EnemyIntent> Plan(BoardState boardState, IEnumerable<ShamblerState> enemies,
            EntityId playerId)
        {
            if (boardState == null)
                throw new ArgumentNullException(nameof(boardState));
            IReadOnlyList<ShamblerState> ordered = EnemyInitiativeOrder.Create(enemies);
            foreach (ShamblerState enemy in ordered)
            {
                if (enemy.LockedIntent != null)
                    throw new InvalidOperationException("The entire batch must be ready for next-intent planning.");
            }

            var intents = new List<EnemyIntent>(ordered.Count);
            foreach (ShamblerState enemy in ordered)
                intents.Add(planner.Plan(boardState, enemy, playerId));
            for (int index = 0; index < ordered.Count; index++)
                ordered[index].LockIntent(intents[index]);
            return intents.AsReadOnly();
        }
    }
}