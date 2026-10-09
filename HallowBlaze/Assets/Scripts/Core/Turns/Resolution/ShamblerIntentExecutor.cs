using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Executes one retained Shambler opportunity against the current authoritative board.
    /// Execution never plans, retargets, or charges a player action cost.
    /// </summary>
    public sealed class ShamblerIntentExecutor
    {
        /// <summary>
        /// Consumes one locked opportunity and advances cadence exactly once, including blocked
        /// actions, misses, and missing sources. A move uses only its recorded adjacent destination;
        /// an invalidated move or illegal attack emits wait. A legal attack hits only the original
        /// player still on its recorded cell, otherwise emitting a zero-damage miss at that cell.
        /// Missing or invalid sources emit no event. No player action cost is charged, and terminal
        /// handling remains with the controller; the supplied run must still be active.
        /// A move attacks without moving only when its declared condition matches the original
        /// player occupying that same legal destination; other occupants remain blockers.
        /// </summary>
        /// <param name="boardState">The authoritative board whose current layers determine legality.</param>
        /// <param name="runState">The active run whose HP is changed only by an actual hit.</param>
        /// <param name="enemyState">The board-local enemy with one retained opportunity.</param>
        /// <param name="events">The ordered, writable event collection to append actual outcomes to.</param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        /// <exception cref="InvalidOperationException">The run is terminal or no intent is locked, including duplicate execution.</exception>
        public void Execute(BoardState boardState, RunState runState,
            ShamblerState enemyState, ICollection<GameEvent> events)
        {
            if (boardState == null)
                throw new ArgumentNullException(nameof(boardState));
            if (runState == null)
                throw new ArgumentNullException(nameof(runState));
            if (enemyState == null)
                throw new ArgumentNullException(nameof(enemyState));
            if (events == null)
                throw new ArgumentNullException(nameof(events));
            if (runState.Status != RunStatus.Active)
                throw new InvalidOperationException("Enemy execution requires an active run.");

            EnemyIntent intent = enemyState.LockedIntent ??
                throw new InvalidOperationException("There is no locked intent to execute.");

            enemyState.ConsumeIntent();
            if (!boardState.TryGetEntity(intent.ActorId, out BoardEntityState actor) ||
                actor.Definition.Layer != BoardLayer.Actor ||
                !actor.Definition.Kind.Equals(EntityKind.Enemy))
            {
                return;
            }

            if (intent.Kind == EnemyIntentKind.Move)
            {
                GridPosition destination = intent.TargetPosition.Value;
                if (actor.Position.ManhattanDistance(destination) == 1 &&
                    IsWalkable(boardState, destination))
                {
                    if (!boardState.TryGetEntity(BoardLayer.Actor, destination, out _) &&
                        boardState.TryMove(actor.Id, destination))
                    {
                        events.Add(new EntityMovedEvent(actor.Id, actor.Position, destination));
                        return;
                    }
                    if (intent.AttackOnPlayerEntry && HasRecordedPlayer(boardState, intent))
                    {
                        ResolveAttack(boardState, runState, enemyState, intent, events);
                        return;
                    }
                }
            }

            if (intent.Kind == EnemyIntentKind.Attack &&
                actor.Position.ManhattanDistance(intent.TargetPosition.Value) == enemyState.Definition.AttackRange &&
                IsWalkable(boardState, intent.TargetPosition.Value))
            {
                ResolveAttack(boardState, runState, enemyState, intent, events);
                return;
            }

            events.Add(new EntityWaitedEvent(actor.Id));
        }

        private static void ResolveAttack(BoardState boardState, RunState runState,
            ShamblerState enemyState, EnemyIntent intent, ICollection<GameEvent> events)
        {
            bool hit = HasRecordedPlayer(boardState, intent);
            int previousHealth = runState.Health;
            if (hit)
                runState.TakeDamage(enemyState.Definition.Damage);
            events.Add(new EnemyAttackResolvedEvent(intent.ActorId, intent.TargetPosition.Value, hit,
                hit ? intent.TargetId : null, runState.Health - previousHealth));
        }

            private static bool HasRecordedPlayer(BoardState boardState, EnemyIntent intent)
            {
                return boardState.TryGetEntity(BoardLayer.Actor, intent.TargetPosition.Value,
                out BoardEntityState target) && target.Id.Equals(intent.TargetId.Value) &&
                target.Definition.Kind.Equals(EntityKind.Player);
            }

        private static bool IsWalkable(BoardState boardState, GridPosition position)
        {
            return boardState.TryGetEntity(BoardLayer.Terrain, position, out BoardEntityState terrain) &&
                terrain.Definition.Traits.IsWalkable &&
                (!boardState.TryGetEntity(BoardLayer.Obstacle, position, out BoardEntityState obstacle) ||
                    obstacle.Definition.Traits.IsWalkable);
        }
    }
}