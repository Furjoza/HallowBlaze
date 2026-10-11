using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Records an attack at its announced cell without asking presentation to find a target
    /// or recalculate damage. This immutable fact does not mutate gameplay state.
    /// </summary>
    public sealed class EnemyAttackResolvedEvent : GameEvent
    {
        /// <summary>Gets the stable board-local identity of the attacking enemy.</summary>
        public EntityId AttackerId { get; }

        /// <summary>Gets the fixed cell where the attack was attempted, including on a miss.</summary>
        public GridPosition TargetPosition { get; }

        /// <summary>Gets whether the recorded player was hit at the announced cell.</summary>
        public bool IsHit { get; }

        /// <summary>Gets the actually hit player's identity, or null for a miss.</summary>
        public EntityId? AffectedTargetId { get; }

        /// <summary>
        /// Gets the signed actual HP change after clamping, not the variant's nominal damage.
        /// This is nonpositive for a hit and zero for a miss.
        /// </summary>
        public int HealthChange { get; }

        /// <summary>Gets the stable discriminator used by turn-result consumers.</summary>
        public override string EventType => "EnemyAttackResolved";

        /// <summary>
        /// Captures a fixed-cell hit or miss. A hit requires a distinct affected identity and
        /// nonpositive HP change; a miss has no affected identity and exactly zero HP change.
        /// Primitive identity and coordinate values are retained without board validation.
        /// </summary>
        /// <param name="attackerId">The enemy that attempted the attack.</param>
        /// <param name="targetPosition">The announced cell, never a replacement target's position.</param>
        /// <param name="isHit">True only when the recorded player was actually hit.</param>
        /// <param name="affectedTargetId">The player actually hit, or null when the attack missed.</param>
        /// <param name="healthChange">Actual signed HP difference, including clamping at zero HP.</param>
        /// <exception cref="ArgumentException">Hit/identity or miss/health payloads contradict each other.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The HP change is positive.</exception>
        public EnemyAttackResolvedEvent(EntityId attackerId, GridPosition targetPosition,
            bool isHit, EntityId? affectedTargetId, int healthChange)
        {
            if (isHit != affectedTargetId.HasValue ||
                affectedTargetId.HasValue && affectedTargetId.Value.Equals(attackerId))
            {
                throw new ArgumentException("Only a hit identifies a distinct affected target.", nameof(affectedTargetId));
            }
            if (healthChange > 0)
                throw new ArgumentOutOfRangeException(nameof(healthChange), "An attack cannot restore HP.");
            if (!isHit && healthChange != 0)
                throw new ArgumentException("A miss cannot change HP.", nameof(healthChange));

            AttackerId = attackerId;
            TargetPosition = targetPosition;
            IsHit = isHit;
            AffectedTargetId = affectedTargetId;
            HealthChange = healthChange;
        }
    }
}