using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Shares one ascending actor-ID initiative across Shambler and Listener opportunities on one board.</summary>
    public sealed class MixedEnemyTurnPhases
    {
        private readonly SortedDictionary<EntityId, ShamblerState> shamblers = new SortedDictionary<EntityId, ShamblerState>();
        private readonly SortedDictionary<EntityId, ListenerState> listeners = new SortedDictionary<EntityId, ListenerState>();
        private readonly List<EntityId> initiative = new List<EntityId>();

        /// <summary>Captures detached rosters without planning, hearing or consuming opportunities.</summary>
        /// <param name="shamblers">Existing pursuit states in any input order.</param>
        /// <param name="listeners">Noise-only states already registered on the board before emissions.</param>
        /// <param name="applyEnvironment">Optional existing environment behavior.</param>
        /// <exception cref="ArgumentNullException">A roster is null.</exception>
        /// <exception cref="ArgumentException">An entry is null or any actor ID occurs more than once across both rosters.</exception>
        public MixedEnemyTurnPhases(IEnumerable<ShamblerState> shamblers, IEnumerable<ListenerState> listeners,
            TurnPhaseHandler applyEnvironment = null)
        {
            foreach (ShamblerState enemy in EnemyInitiativeOrder.Create(shamblers))
            {
                this.shamblers.Add(enemy.ActorId, enemy);
                initiative.Add(enemy.ActorId);
            }
            if (listeners == null)
                throw new ArgumentNullException(nameof(listeners));
            foreach (ListenerState listener in listeners)
            {
                if (listener == null || this.shamblers.ContainsKey(listener.ActorId) || this.listeners.ContainsKey(listener.ActorId))
                    throw new ArgumentException("Enemy identities must be unique across archetypes.", nameof(listeners));
                this.listeners.Add(listener.ActorId, listener);
                initiative.Add(listener.ActorId);
            }
            initiative.Sort();
            Handlers = new TurnPhaseHandlers(Execute, applyEnvironment, (board, run, player, events) => PlanInitialIntents(board, player));
        }

        /// <summary>Gets one locked execution phase and one next-plan phase; the controller owns admission and terminals.</summary>
        public TurnPhaseHandlers Handlers { get; }

        /// <summary>
        /// Validates all owners, computes all plans from the same unchanged board, then locks the whole batch.
        /// Neither hearing, cadence nor board state changes. Existing announcements cannot be replaced.
        /// </summary>
        /// <param name="board">The common authoritative board.</param>
        /// <param name="playerId">The original player, used only by Shambler planners.</param>
        /// <exception cref="ArgumentNullException">The board is null.</exception>
        /// <exception cref="InvalidOperationException">Any actor already retains a plan.</exception>
        public void PlanInitialIntents(BoardState board, EntityId playerId)
        {
            if (board == null)
                throw new ArgumentNullException(nameof(board));
            foreach (EntityId actorId in initiative)
                if (GetIntent(actorId) != null)
                    throw new InvalidOperationException("Every actor must finish its previous opportunity before planning.");
            var plans = new List<EnemyIntent>();
            var shamblerPlanner = new ShamblerPlanner();
            var listenerPlanner = new ListenerPlanner();
            foreach (EntityId actorId in initiative)
                plans.Add(shamblers.TryGetValue(actorId, out ShamblerState shambler)
                    ? shamblerPlanner.Plan(board, shambler, playerId)
                    : listenerPlanner.Plan(board, listeners[actorId]));
            foreach (EnemyIntent plan in plans)
                if (shamblers.TryGetValue(plan.ActorId, out ShamblerState shambler)) shambler.LockIntent(plan);
                else listeners[plan.ActorId].LockIntent(plan);
        }

        private EnemyIntent GetIntent(EntityId actorId) => shamblers.TryGetValue(actorId, out ShamblerState shambler)
            ? shambler.LockedIntent : listeners[actorId].LockedIntent;

        private void Execute(BoardState board, RunState run, EntityId player, ICollection<GameEvent> events)
        {
            if (board == null || run == null || events == null)
                throw new ArgumentNullException("Mixed enemy execution requires board, run and events.");
            if (run.Status != RunStatus.Active)
                throw new InvalidOperationException("Enemy execution requires an active run.");
            foreach (EntityId actorId in initiative)
                if (GetIntent(actorId) == null)
                    throw new InvalidOperationException("Every actor must retain one announced opportunity.");
            var shamblerExecutor = new ShamblerIntentExecutor();
            var listenerExecutor = new ListenerIntentExecutor();
            foreach (EntityId actorId in initiative)
                if (shamblers.TryGetValue(actorId, out ShamblerState shambler))
                    shamblerExecutor.Execute(board, run, shambler, events);
                else
                    listenerExecutor.Execute(board, listeners[actorId], events);
        }
    }
}