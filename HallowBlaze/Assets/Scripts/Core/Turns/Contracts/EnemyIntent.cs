using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Identifies the action retained until the board's locked enemy phase executes.
    /// </summary>
    public enum EnemyIntentKind
    {
        /// <summary>
        /// Move to the fixed destination, or attack the recorded player if it enters that cell.
        /// </summary>
        Move = 1,

        /// <summary>
        /// Attack only the recorded player at its original target cell, without following it.
        /// </summary>
        Attack = 2,

        /// <summary>
        /// Take no movement or attack action, regardless of subsequent player movement.
        /// </summary>
        Wait = 3
    }

    /// <summary>
    /// Retains one immutable, board-local enemy action and its declared target policy.
    /// A move's player-entry attack uses the same recorded cell; a planned attack never
    /// follows its target. Board legality and execution are validated separately.
    /// This value does not advance cadence, mutate gameplay state, or belong to a save DTO.
    /// </summary>
    public sealed class EnemyIntent
    {
        /// <summary>
        /// Gets the stable board-local identity of the actor that owns this intent.
        /// </summary>
        public EntityId ActorId { get; }

        /// <summary>
        /// Gets the action captured when the intent was created.
        /// </summary>
        public EnemyIntentKind Kind { get; }

        /// <summary>
        /// Gets the fixed movement destination or original attack cell; wait has no target cell.
        /// Coordinates are copied by value and are not constrained to a particular board's bounds.
        /// </summary>
        public GridPosition? TargetPosition { get; }

        /// <summary>
        /// Gets the original player's identity for a planned or conditional attack;
        /// wait has no target identity. A replacement occupant is not this target.
        /// </summary>
        public EntityId? TargetId { get; }

        /// <summary>
        /// Gets whether the move declares one attack on the recorded player if it enters
        /// <see cref="TargetPosition"/>. This is true only for move and never selects another cell.
        /// </summary>
        public bool AttackOnPlayerEntry { get; }

        /// <summary>
        /// Creates a validated snapshot of one action, rejecting missing or contradictory payloads.
        /// Move requires a fixed cell, a distinct player identity, and the player-entry condition.
        /// Attack requires that cell and identity without the condition; wait permits neither target
        /// nor condition. All primitive ID and coordinate values, including zero and origin, remain valid.
        /// </summary>
        /// <param name="actorId">The actor's board-local identity; zero and negative values are valid.</param>
        /// <param name="kind">One of the three defined actions.</param>
        /// <param name="targetPosition">The fixed destination or attack cell; null only for wait.</param>
        /// <param name="targetId">The original player's identity, distinct from the actor; null only for wait.</param>
        /// <param name="attackOnPlayerEntry">True for move's declared same-cell condition; false for attack and wait.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is undefined.</exception>
        /// <exception cref="ArgumentException">The payload is missing, contradictory, or targets the actor itself.</exception>
        public EnemyIntent(
            EntityId actorId,
            EnemyIntentKind kind,
            GridPosition? targetPosition = null,
            EntityId? targetId = null,
            bool attackOnPlayerEntry = false)
        {
            switch (kind)
            {
                case EnemyIntentKind.Move:
                case EnemyIntentKind.Attack:
                    ValidateTarget(actorId, targetPosition, targetId);
                    if (attackOnPlayerEntry != (kind == EnemyIntentKind.Move))
                    {
                        throw new ArgumentException(
                            "Only a move must declare the attack-on-player-entry condition.",
                            nameof(attackOnPlayerEntry));
                    }
                    break;

                case EnemyIntentKind.Wait:
                    if (targetPosition.HasValue)
                    {
                        throw new ArgumentException("Wait cannot declare a target cell.", nameof(targetPosition));
                    }
                    if (targetId.HasValue)
                    {
                        throw new ArgumentException("Wait cannot declare a target identity.", nameof(targetId));
                    }
                    if (attackOnPlayerEntry)
                    {
                        throw new ArgumentException("Wait cannot declare an attack condition.", nameof(attackOnPlayerEntry));
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "An intent requires a defined action.");
            }

            ActorId = actorId;
            Kind = kind;
            TargetPosition = targetPosition;
            TargetId = targetId;
            AttackOnPlayerEntry = attackOnPlayerEntry;
        }

        private static void ValidateTarget(EntityId actorId, GridPosition? targetPosition, EntityId? targetId)
        {
            if (!targetPosition.HasValue)
            {
                throw new ArgumentException("Move and attack require a fixed target cell.", nameof(targetPosition));
            }
            if (!targetId.HasValue)
            {
                throw new ArgumentException("Move and attack require the original player's identity.", nameof(targetId));
            }
            if (targetId.Value.Equals(actorId))
            {
                throw new ArgumentException("An enemy cannot target its own identity.", nameof(targetId));
            }
        }
    }
}
