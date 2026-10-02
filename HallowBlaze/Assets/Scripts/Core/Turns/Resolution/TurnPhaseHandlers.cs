using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Executes one synchronous phase of an already accepted turn.</summary>
    /// <param name="boardState">The authoritative board, borrowed only for this call.</param>
    /// <param name="runState">The authoritative run, borrowed only for this call.</param>
    /// <param name="playerId">The board-local player identifier.</param>
    /// <param name="events">Append-only ordered output; do not retain or modify it after returning.</param>
    /// <remarks>
    /// Execution must be deterministic. Do not charge another player action cost or reject
    /// the accepted command. Report a reached exit explicitly with ExitReachedEvent.
    /// Exceptions indicate implementation faults; they are not cost-free command rejections.
    /// </remarks>
    public delegate void TurnPhaseHandler(
        BoardState boardState, RunState runState, EntityId playerId, ICollection<GameEvent> events);

    /// <summary>
    /// Supplies the later turn phases without implementing enemy AI or environmental rules.
    /// Omitted handlers are no-ops. The controller owns their execution order.
    /// </summary>
    public sealed class TurnPhaseHandlers
    {
        /// <summary>Gets phase 5, which executes previously shown intents without replanning them.</summary>
        public TurnPhaseHandler ExecuteLockedIntents { get; }

        /// <summary>Gets phase 6, which applies environmental effects before the second terminal check.</summary>
        public TurnPhaseHandler ApplyEnvironment { get; }

        /// <summary>
        /// Gets phase 8, which prepares the next locked intents and their presentation events.
        /// Planning must not mutate gameplay state or introduce a terminal outcome.
        /// </summary>
        public TurnPhaseHandler PlanNextIntents { get; }

        /// <summary>Creates the phase extension points for one controller.</summary>
        /// <param name="executeLockedIntents">Phase 5 implementation, or null for no enemies.</param>
        /// <param name="applyEnvironment">Phase 6 implementation, or null for no environmental effects.</param>
        /// <param name="planNextIntents">Phase 8 implementation, or null for no intent planner.</param>
        public TurnPhaseHandlers(
            TurnPhaseHandler executeLockedIntents = null,
            TurnPhaseHandler applyEnvironment = null,
            TurnPhaseHandler planNextIntents = null)
        {
            ExecuteLockedIntents = executeLockedIntents ?? DoNothing;
            ApplyEnvironment = applyEnvironment ?? DoNothing;
            PlanNextIntents = planNextIntents ?? DoNothing;
        }

        private static void DoNothing(
            BoardState boardState, RunState runState, EntityId playerId, ICollection<GameEvent> events)
        {
        }
    }
}
