using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Binds a board-local enemy batch to existing controller phases without changing their order.
    /// States remain the sole intent/cadence owners; compose a fresh adapter per board.
    /// </summary>
    public sealed class EnemyBatchTurnPhases
    {
        private readonly IReadOnlyList<ShamblerState> enemies;
        private readonly EnemyBatchPlanner planner = new EnemyBatchPlanner();
        private readonly EnemyBatchExecutor executor = new EnemyBatchExecutor();

        /// <summary>
        /// Gets phase 5 locked execution and phase 8 next planning, with the caller's environment phase.
        /// The controller alone decides whether an accepted turn reaches either enemy phase.
        /// </summary>
        public TurnPhaseHandlers Handlers { get; }

        /// <summary>
        /// Captures a detached roster of the supplied states and creates delegates without planning
        /// or consuming opportunities. Call <see cref="PlanInitialIntents"/> before the first command.
        /// Later changes to the caller's collection do not change this board's roster.
        /// </summary>
        /// <param name="enemies">Unique non-null board-local states, retained by reference.</param>
        /// <param name="applyEnvironment">The existing phase 6 behavior, or null for no environment effects.</param>
        /// <exception cref="ArgumentNullException"><paramref name="enemies"/> is null.</exception>
        /// <exception cref="ArgumentException">An entry is null or an actor identity is duplicated.</exception>
        public EnemyBatchTurnPhases(IEnumerable<ShamblerState> enemies, TurnPhaseHandler applyEnvironment = null)
        {
            this.enemies = EnemyInitiativeOrder.Create(enemies);
            Handlers = new TurnPhaseHandlers(ExecuteLockedIntents, applyEnvironment, PlanNextIntents);
        }

        /// <summary>
        /// Retains the initial batch before player input without advancing cadence or mutating the board.
        /// An existing locked intent prevents initialization of the entire batch without replacing plans.
        /// </summary>
        /// <param name="boardState">The authoritative board shared by all initial planners.</param>
        /// <param name="playerId">The original player's board-local identity.</param>
        /// <exception cref="ArgumentNullException"><paramref name="boardState"/> is null.</exception>
        /// <exception cref="InvalidOperationException">An enemy already retains an intent.</exception>
        public void PlanInitialIntents(BoardState boardState, EntityId playerId)
        {
            planner.Plan(boardState, enemies, playerId);
        }

        private void ExecuteLockedIntents(BoardState boardState, RunState runState,
            EntityId playerId, ICollection<GameEvent> events)
        {
            executor.Execute(boardState, runState, enemies, events);
        }

        private void PlanNextIntents(BoardState boardState, RunState runState,
            EntityId playerId, ICollection<GameEvent> events)
        {
            planner.Plan(boardState, enemies, playerId);
        }
    }
}