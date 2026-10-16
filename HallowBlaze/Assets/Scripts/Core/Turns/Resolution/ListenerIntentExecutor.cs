using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Executes only the announced adjacent investigation step against live sequential occupancy.</summary>
    public sealed class ListenerIntentExecutor
    {
        /// <summary>
        /// Consumes once, moving only from the recorded origin to the recorded free legal cell while
        /// the original noise remains active. Expiration, blocking or displacement yields Wait, never
        /// attack, retargeting or replanning. A newer heard noise affects only subsequent planning.
        /// Missing/invalid actors emit nothing. No run resource or player action cost is changed.
        /// </summary>
        /// <param name="board">The current authoritative board.</param>
        /// <param name="listener">The owner with one locked investigation or wait.</param>
        /// <param name="events">The ordered writable outcome collection.</param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        /// <exception cref="InvalidOperationException">No intent is locked.</exception>
        public void Execute(BoardState board, ListenerState listener, ICollection<GameEvent> events)
        {
            if (board == null)
                throw new ArgumentNullException(nameof(board));
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));
            if (events == null)
                throw new ArgumentNullException(nameof(events));
            EnemyIntent intent = listener.ConsumeIntent();
            if (!board.TryGetEntity(listener.ActorId, out BoardEntityState actor) || actor.Definition.Layer != BoardLayer.Actor ||
                !actor.Definition.Kind.Equals(EntityKind.Enemy))
                return;
            if (intent.Kind == EnemyIntentKind.Investigate && actor.Position.Equals(intent.SourcePosition.Value) &&
                board.GetActiveNoises().Count > 0)
            {
                bool signalActive = false;
                foreach (BoardNoise noise in board.GetActiveNoises())
                    signalActive |= noise.Id == intent.NoiseId && noise.Position.Equals(intent.InvestigationTarget.Value);
                GridPosition destination = intent.TargetPosition.Value;
                if (signalActive && board.TryGetEntity(BoardLayer.Terrain, destination, out BoardEntityState terrain) &&
                    terrain.Definition.Traits.IsWalkable &&
                    (!board.TryGetEntity(BoardLayer.Obstacle, destination, out BoardEntityState obstacle) || obstacle.Definition.Traits.IsWalkable) &&
                    !board.TryGetEntity(BoardLayer.Actor, destination, out _) && board.TryMove(actor.Id, destination))
                {
                    events.Add(new EntityMovedEvent(actor.Id, actor.Position, destination));
                    events.Add(new ListenerInvestigatedEvent(actor.Id, actor.Position, destination,
                        intent.NoiseId.Value, intent.InvestigationTarget.Value));
                    return;
                }
            }
            events.Add(new EntityWaitedEvent(actor.Id));
        }
    }
}