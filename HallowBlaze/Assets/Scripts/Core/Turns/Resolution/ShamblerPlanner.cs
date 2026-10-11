using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Chooses one eligible Shambler opportunity from the authoritative grid without mutation.
    /// Shortest legal paths use North, East, West, South priority for equal first steps.
    /// Cadence eligibility and intent retention belong to the caller, not this planner.
    /// </summary>
    public sealed class ShamblerPlanner
    {
        private static readonly Direction[] Directions =
        {
            Direction.North,
            Direction.East,
            Direction.West,
            Direction.South
        };

        /// <summary>
        /// Plans an attack on a legal orthogonally adjacent player, otherwise one shortest-path
        /// move, or wait when the target is unreachable or either identity is not its expected actor.
        /// The player's occupied cell is a terminal target, never a movement destination.
        /// A move declares attack on that same cell if the recorded player enters it later.
        /// This method does not read or advance cadence, replace a locked intent, consume resources,
        /// or mutate the board. The caller must select an eligible opportunity before calling it.
        /// </summary>
        /// <param name="boardState">The board snapshot whose layers and bounds determine legality.</param>
        /// <param name="enemyState">The enemy identity and immutable attack configuration.</param>
        /// <param name="playerId">The original player's board-local identity.</param>
        /// <returns>An immutable move, attack, or target-free wait owned by the enemy.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="boardState"/> or <paramref name="enemyState"/> is null.
        /// </exception>
        public EnemyIntent Plan(BoardState boardState, ShamblerState enemyState, EntityId playerId)
        {
            if (boardState == null)
                throw new ArgumentNullException(nameof(boardState));
            if (enemyState == null)
                throw new ArgumentNullException(nameof(enemyState));

            if (!boardState.TryGetEntity(enemyState.ActorId, out BoardEntityState enemy) ||
                enemy.Definition.Layer != BoardLayer.Actor ||
                !enemy.Definition.Kind.Equals(EntityKind.Enemy) ||
                !boardState.TryGetEntity(playerId, out BoardEntityState player) ||
                player.Definition.Layer != BoardLayer.Actor ||
                !player.Definition.Kind.Equals(EntityKind.Player) ||
                !IsAccessible(boardState, player.Position, playerId))
            {
                return new EnemyIntent(enemyState.ActorId, EnemyIntentKind.Wait);
            }

            if (enemy.Position.ManhattanDistance(player.Position) == enemyState.Definition.AttackRange)
            {
                return new EnemyIntent(enemy.Id, EnemyIntentKind.Attack, player.Position, playerId);
            }

            var frontier = new Queue<GridPosition>();
            var firstSteps = new Dictionary<GridPosition, GridPosition>
            {
                { enemy.Position, enemy.Position }
            };
            frontier.Enqueue(enemy.Position);

            while (frontier.Count > 0)
            {
                GridPosition current = frontier.Dequeue();
                foreach (Direction direction in Directions)
                {
                    if (!TryGetNeighbor(boardState.Bounds, current, direction, out GridPosition next) ||
                        firstSteps.ContainsKey(next) ||
                        !IsAccessible(boardState, next, playerId))
                    {
                        continue;
                    }

                    GridPosition firstStep = current.Equals(enemy.Position) ? next : firstSteps[current];
                    if (next.Equals(player.Position))
                    {
                        return new EnemyIntent(enemy.Id, EnemyIntentKind.Move, firstStep, playerId,
                            attackOnPlayerEntry: true);
                    }

                    firstSteps.Add(next, firstStep);
                    frontier.Enqueue(next);
                }
            }

            return new EnemyIntent(enemy.Id, EnemyIntentKind.Wait);
        }

        private static bool IsAccessible(BoardState boardState, GridPosition position, EntityId playerId)
        {
            if (!boardState.TryGetEntity(BoardLayer.Terrain, position, out BoardEntityState terrain) ||
                !terrain.Definition.Traits.IsWalkable)
            {
                return false;
            }
            if (boardState.TryGetEntity(BoardLayer.Obstacle, position, out BoardEntityState obstacle) &&
                !obstacle.Definition.Traits.IsWalkable)
            {
                return false;
            }

            return !boardState.TryGetEntity(BoardLayer.Actor, position, out BoardEntityState actor) ||
                actor.Id.Equals(playerId);
        }

        private static bool TryGetNeighbor(
            GridBounds bounds, GridPosition position, Direction direction, out GridPosition neighbor)
        {
            neighbor = default;
            if (direction.DeltaX > 0 && position.X == bounds.MaxX ||
                direction.DeltaX < 0 && position.X == bounds.MinX ||
                direction.DeltaY > 0 && position.Y == bounds.MaxY ||
                direction.DeltaY < 0 && position.Y == bounds.MinY)
            {
                return false;
            }

            neighbor = position.Move(direction);
            return true;
        }
    }
}