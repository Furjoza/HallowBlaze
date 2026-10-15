using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Plans a fixed shortest-path investigation of remembered noise without hearing, mutation or player pursuit.</summary>
    public sealed class ListenerPlanner
    {
        /// <summary>
        /// Uses the last signal heard at emission while it remains active. Equal shortest paths use
        /// North/East/West/South. No signal, arrival, missing actor or no legal first step yields Wait.
        /// An occupied source can be approached but never entered or attacked. Reads never change memory or intent.
        /// </summary>
        /// <param name="board">The authoritative board, including hearing memory.</param>
        /// <param name="listener">The board-local intent owner.</param>
        /// <returns>An immutable investigation with fixed next cell/noise source, or target-free wait.</returns>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        public EnemyIntent Plan(BoardState board, ListenerState listener)
        {
            if (board == null)
                throw new ArgumentNullException(nameof(board));
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));
            var wait = new EnemyIntent(listener.ActorId, EnemyIntentKind.Wait);
            if (!board.TryGetEntity(listener.ActorId, out BoardEntityState actor) || actor.Definition.Layer != BoardLayer.Actor ||
                !actor.Definition.Kind.Equals(EntityKind.Enemy) || !board.TryGetListenerNoise(listener.ActorId, out BoardNoise noise))
                return wait;
            EntityId? targetOccupant = board.TryGetEntity(BoardLayer.Actor, noise.Position, out BoardEntityState occupant)
                ? occupant.Id : (EntityId?)null;
            GridPosition? step = ShamblerPlanner.FirstStepTo(board, actor.Position, noise.Position, targetOccupant);
            if (!step.HasValue || board.TryGetEntity(BoardLayer.Actor, step.Value, out _))
                return wait;
            return new EnemyIntent(actor.Id, EnemyIntentKind.Investigate, step,
                investigationTarget: noise.Position, noiseId: noise.Id, sourcePosition: actor.Position);
        }
    }
}