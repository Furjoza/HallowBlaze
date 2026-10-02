using System;
using System.Collections.Generic;
using System.Threading;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Resolves complete turns for one board in the nine phases of the game contract.
    /// Own one controller per active board and route every command through it; board and run
    /// must not be mutated externally while it resolves. A terminal board requires a new controller.
    /// </summary>
    public sealed class TurnController
    {
        private readonly BoardState boardState;
        private readonly RunState runState;
        private readonly EntityId playerId;
        private readonly IPlayerPhaseResolver playerResolver;
        private readonly TurnPhaseHandlers phases;
        private int resolving;
        private int terminal;

        /// <summary>Gets whether a turn is currently resolving, including all later phases.</summary>
        public bool IsResolving => Volatile.Read(ref resolving) != 0;

        /// <summary>Gets whether this board has ended; further commands are rejected without effects.</summary>
        public bool IsTerminal => Volatile.Read(ref terminal) != 0;

        /// <summary>Creates the sole command entry point for one board's lifetime.</summary>
        /// <param name="boardState">The authoritative board; do not share it with another active controller.</param>
        /// <param name="runState">The authoritative run associated with the board.</param>
        /// <param name="playerId">The board-local player identifier.</param>
        /// <param name="playerResolver">The atomic player phase, or null for the existing movement resolver.</param>
        /// <param name="phases">Later phase implementations, or null for no-op placeholders.</param>
        /// <exception cref="ArgumentNullException">The board or run is null.</exception>
        public TurnController(
            BoardState boardState,
            RunState runState,
            EntityId playerId,
            IPlayerPhaseResolver playerResolver = null,
            TurnPhaseHandlers phases = null)
        {
            this.boardState = boardState ?? throw new ArgumentNullException(nameof(boardState));
            this.runState = runState ?? throw new ArgumentNullException(nameof(runState));
            this.playerId = playerId;
            this.playerResolver = playerResolver ?? new TurnResolver();
            this.phases = phases ?? new TurnPhaseHandlers();
        }

        /// <summary>
        /// Resolves a command and returns one immutable result before presentation begins.
        /// Concurrent or reentrant calls and commands after board completion are rejected with
        /// InvalidState and no effects. The player phase owns all validation and action costs.
        /// </summary>
        /// <param name="command">The complete player intent, including any future composite intent.</param>
        /// <returns>A cost-free rejection or the complete accepted turn's events in execution order.</returns>
        /// <remarks>
        /// The resolution lock is always released, including on rejection and exception.
        /// Exceptions propagate as implementation faults; mutated state is not rolled back.
        /// Presentation must maintain its own input gate while replaying the returned events.
        /// Run lifecycle and persistence remain the caller's responsibility.
        /// </remarks>
        public TurnResult Resolve(PlayerCommand command)
        {
            if (Interlocked.CompareExchange(ref resolving, 1, 0) != 0)
                return new RejectedTurnResult(CommandRejectionCode.InvalidState);

            try
            {
                if (IsTerminal)
                    return new RejectedTurnResult(CommandRejectionCode.InvalidState);

                // Phases 1-4: validation, effects/rewards, cost, immediate terminal outcome.
                TurnResult playerResult = playerResolver.Resolve(boardState, runState, playerId, command);
                if (playerResult == null)
                    throw new InvalidOperationException("The player phase must return a complete result.");
                if (!playerResult.Accepted)
                    return playerResult;

                var events = new List<GameEvent>(playerResult.Events);
                if (CheckTerminal(events))
                    return new AcceptedTurnResult(events);

                // Phases 5-6: execute already locked intents, then apply the environment.
                phases.ExecuteLockedIntents(boardState, runState, playerId, events);
                phases.ApplyEnvironment(boardState, runState, playerId, events);

                // Phase 7: terminal outcomes stop next-turn planning.
                if (CheckTerminal(events))
                    return new AcceptedTurnResult(events);

                // Phase 8: plan and describe the next intents without changing gameplay state.
                phases.PlanNextIntents(boardState, runState, playerId, events);
                return new AcceptedTurnResult(events);
            }
            finally
            {
                // Phase 9: release domain input ownership on every path.
                Volatile.Write(ref resolving, 0);
            }
        }

        private bool CheckTerminal(List<GameEvent> events)
        {
            bool starved = false;
            bool died = false;
            bool exited = false;
            foreach (GameEvent gameEvent in events)
            {
                if (gameEvent is PlayerStarvedEvent starvation && starvation.EntityId.Equals(playerId))
                    starved = true;
                else if (gameEvent is PlayerDiedEvent death && death.EntityId.Equals(playerId))
                    died = true;
                else if (gameEvent is ExitReachedEvent exit && exit.PlayerId.Equals(playerId))
                    exited = true;
            }

            if (runState.Food <= 0 && !starved && !died)
            {
                events.Add(new PlayerStarvedEvent(playerId));
                starved = true;
            }
            if ((runState.Health <= 0 || runState.Status == RunStatus.Dead) && !starved && !died)
            {
                events.Add(new PlayerDiedEvent(playerId));
                died = true;
            }

            // A dead player cannot complete the board, including a simultaneous late-phase exit.
            if (starved || died)
                events.RemoveAll(gameEvent => gameEvent is ExitReachedEvent exit && exit.PlayerId.Equals(playerId));

            bool ended = starved || died || exited || runState.Status != RunStatus.Active;
            if (ended)
                Volatile.Write(ref terminal, 1);
            return ended;
        }
    }
}
