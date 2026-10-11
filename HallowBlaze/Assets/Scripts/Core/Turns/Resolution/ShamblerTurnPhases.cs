using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Binds one board-local Shambler to the existing controller phases without changing their order.
    /// The supplied state remains the sole intent/cadence owner; compose a fresh adapter per board.
    /// </summary>
    public sealed class ShamblerTurnPhases
    {
        private readonly ShamblerState enemyState;
        private readonly ShamblerPlanner planner = new ShamblerPlanner();
        private readonly ShamblerIntentExecutor executor = new ShamblerIntentExecutor();

        /// <summary>
        /// Gets phase 5 locked execution and phase 8 next planning, with the caller's environment phase.
        /// The controller alone decides whether an accepted turn reaches either enemy phase.
        /// </summary>
        public TurnPhaseHandlers Handlers { get; }

        /// <summary>
        /// Creates delegates without planning, consuming an opportunity, or mutating board/run state.
        /// Call <see cref="PlanInitialIntent"/> before submitting the first player command.
        /// </summary>
        /// <param name="enemyState">The single enemy's existing board-local state, retained by reference.</param>
        /// <param name="applyEnvironment">The existing phase 6 behavior, or null for no environment effects.</param>
        /// <exception cref="ArgumentNullException"><paramref name="enemyState"/> is null.</exception>
        public ShamblerTurnPhases(ShamblerState enemyState, TurnPhaseHandler applyEnvironment = null)
        {
            this.enemyState = enemyState ?? throw new ArgumentNullException(nameof(enemyState));
            Handlers = new TurnPhaseHandlers(ExecuteLockedIntent, applyEnvironment, PlanNextIntent);
        }

        /// <summary>
        /// Retains the initial plan before player input without advancing cadence or mutating board/run.
        /// An already locked intent cannot be replaced; rejected initialization preserves that intent.
        /// </summary>
        /// <param name="boardState">The authoritative board to read for the initial opportunity.</param>
        /// <param name="playerId">The original player's board-local identity.</param>
        /// <exception cref="ArgumentNullException"><paramref name="boardState"/> is null.</exception>
        /// <exception cref="InvalidOperationException">An intent is already locked.</exception>
        public void PlanInitialIntent(BoardState boardState, EntityId playerId)
        {
            enemyState.LockIntent(planner.Plan(boardState, enemyState, playerId));
        }

        private void ExecuteLockedIntent(BoardState boardState, RunState runState,
            EntityId playerId, ICollection<GameEvent> events)
        {
            executor.Execute(boardState, runState, enemyState, events);
        }

        private void PlanNextIntent(BoardState boardState, RunState runState,
            EntityId playerId, ICollection<GameEvent> events)
        {
            enemyState.LockIntent(planner.Plan(boardState, enemyState, playerId));
        }
    }
}