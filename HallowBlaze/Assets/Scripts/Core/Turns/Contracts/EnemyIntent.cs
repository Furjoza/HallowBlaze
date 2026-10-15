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
        Wait = 3,

        /// <summary>Take one fixed step toward a heard noise, without attacking any occupant.</summary>
        Investigate = 4
    }

    /// <summary>
    /// Retains one immutable, board-local enemy action and its declared target policy.
    /// A move's player-entry attack uses the same recorded cell; a planned attack never
    /// follows its target. Investigation retains its origin, next step and noise source without
    /// an attack condition. Board legality and execution are validated separately.
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
        /// Gets the fixed movement/investigation step or original attack cell; wait has no target cell.
        /// Coordinates are copied by value and are not constrained to a particular board's bounds.
        /// </summary>
        public GridPosition? TargetPosition { get; }

        /// <summary>
        /// Gets the original player's identity for a planned or conditional attack;
        /// wait and investigation have no target identity. A replacement occupant is not this target.
        /// </summary>
        public EntityId? TargetId { get; }

        /// <summary>
        /// Gets whether the move declares one attack on the recorded player if it enters
        /// <see cref="TargetPosition"/>. This is true only for move and never selects another cell.
        /// </summary>
        public bool AttackOnPlayerEntry { get; }

        /// <summary>Gets the heard noise's fixed source cell for investigation, otherwise null.</summary>
        public GridPosition? InvestigationTarget { get; }
        /// <summary>Gets the exact board-local emission being investigated, otherwise null.</summary>
        public long? NoiseId { get; }
        /// <summary>Gets the recorded investigation origin; a displaced actor cannot execute this step.</summary>
        public GridPosition? SourcePosition { get; }

        /// <summary>
        /// Creates a validated snapshot of one action, rejecting missing or contradictory payloads.
        /// Move requires a fixed cell, a distinct player identity, and the player-entry condition.
        /// Attack requires that cell and identity without the condition; wait permits neither target
        /// nor condition. Investigation requires a fixed adjacent step, recorded origin and noise
        /// source/emission ID, never an actor target. All primitive IDs and coordinates, including zero, remain valid.
        /// </summary>
        /// <param name="actorId">The actor's board-local identity; zero and negative values are valid.</param>
        /// <param name="kind">One of the defined actions.</param>
        /// <param name="targetPosition">The fixed step or attack cell; null only for wait.</param>
        /// <param name="targetId">The original player's identity for Move/Attack; null for Wait/Investigate.</param>
        /// <param name="attackOnPlayerEntry">True only for Move's declared same-cell condition.</param>
        /// <param name="investigationTarget">The heard source cell, required only for investigation.</param>
        /// <param name="noiseId">A nonnegative board emission ID, required only for investigation.</param>
        /// <param name="sourcePosition">The recorded origin adjacent to the investigation step, otherwise null.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is undefined.</exception>
        /// <exception cref="ArgumentException">The payload is missing, contradictory, or targets the actor itself.</exception>
        public EnemyIntent(
            EntityId actorId,
            EnemyIntentKind kind,
            GridPosition? targetPosition = null,
            EntityId? targetId = null,
            bool attackOnPlayerEntry = false,
            GridPosition? investigationTarget = null,
            long? noiseId = null,
            GridPosition? sourcePosition = null)
        {
            if (kind != EnemyIntentKind.Investigate &&
                (investigationTarget.HasValue || noiseId.HasValue || sourcePosition.HasValue))
                throw new ArgumentException("Only investigation may retain noise and origin fields.");
            switch (kind)
            {
                case EnemyIntentKind.Investigate:
                    if (!targetPosition.HasValue || !investigationTarget.HasValue || !noiseId.HasValue || noiseId < 0 ||
                        !sourcePosition.HasValue || sourcePosition.Value.ManhattanDistance(targetPosition.Value) != 1 ||
                        targetId.HasValue || attackOnPlayerEntry)
                        throw new ArgumentException("Investigation requires one adjacent step, an origin and a noise target, without an attack.");
                    break;
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
            InvestigationTarget = investigationTarget;
            NoiseId = noiseId;
            SourcePosition = sourcePosition;
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
