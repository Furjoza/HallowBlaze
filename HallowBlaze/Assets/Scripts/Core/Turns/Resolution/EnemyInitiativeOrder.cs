using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Defines enemy initiative solely by ascending signed board-local identity, never input/view order.
    /// </summary>
    public static class EnemyInitiativeOrder
    {
        /// <summary>
        /// Validates and orders a detached read-only collection, retaining the original enemy references.
        /// All IDs, including negative and extreme values, use EntityId.CompareTo. Empty input is valid.
        /// This operation does not query a board, plan, execute, or mutate enemy state or the input.
        /// </summary>
        /// <param name="enemies">Unique non-null enemy states in any enumeration order.</param>
        /// <returns>A detached collection sorted by ascending actor identity.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="enemies"/> is null.</exception>
        /// <exception cref="ArgumentException">An entry is null or an actor ID occurs more than once.</exception>
        public static IReadOnlyList<ShamblerState> Create(IEnumerable<ShamblerState> enemies)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));
            var ordered = new List<ShamblerState>();
            var identities = new HashSet<EntityId>();
            foreach (ShamblerState enemy in enemies)
            {
                if (enemy == null || !identities.Add(enemy.ActorId))
                    throw new ArgumentException("An enemy batch requires unique, non-null states.", nameof(enemies));
                ordered.Add(enemy);
            }
            ordered.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
            return ordered.AsReadOnly();
        }
    }
}