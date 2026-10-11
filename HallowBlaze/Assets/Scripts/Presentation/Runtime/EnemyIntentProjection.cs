using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Identifies persistent action shapes without computing or expanding enemy behavior.</summary>
    public enum EnemyIntentSymbol
    {
        /// <summary>A fixed movement destination without a player-entry attack badge.</summary>
        Move = 1,
        /// <summary>A fixed movement destination with the declared player-entry attack badge.</summary>
        ConditionalMove = 2,
        /// <summary>An attack at the recorded cell rather than at a newly selected target.</summary>
        Attack = 3,
        /// <summary>A presentation-only investigation example until a domain rule is accepted.</summary>
        Investigate = 4,
        /// <summary>An opportunity with no movement, attack, or target.</summary>
        Wait = 5
    }

    /// <summary>
    /// Copies one retained intent into immutable display data without reading gameplay state or planning.
    /// Source cells are supplied by the owner; this value is not an executable intent or save DTO.
    /// </summary>
    public sealed class EnemyIntentProjection : IEquatable<EnemyIntentProjection>
    {
        /// <summary>Gets the exact board-local identity of the source actor.</summary>
        public EntityId SourceId { get; }
        /// <summary>Gets the supplied authoritative source cell copied at projection time.</summary>
        public GridPosition SourcePosition { get; }
        /// <summary>Gets the distinct action shape, including the persistent conditional movement badge.</summary>
        public EnemyIntentSymbol Symbol { get; }
        /// <summary>Gets the recorded destination or attack cell, or null for wait.</summary>
        public GridPosition? TargetPosition { get; }
        /// <summary>Gets the recorded original player identity for attacks, or null for non-attacking examples.</summary>
        public EntityId? TargetId { get; }
        /// <summary>Gets whether the symbol preserves the declared same-cell player-entry attack condition.</summary>
        public bool AttackOnPlayerEntry => Symbol == EnemyIntentSymbol.ConditionalMove;

        private EnemyIntentProjection(EntityId sourceId, GridPosition sourcePosition, EnemyIntentSymbol symbol,
            GridPosition? targetPosition, EntityId? targetId)
        {
            SourceId = sourceId;
            SourcePosition = sourcePosition;
            Symbol = symbol;
            TargetPosition = targetPosition;
            TargetId = targetId;
        }

        /// <summary>
        /// Copies accepted intent fields and the supplied source cell without board queries or side effects.
        /// Unsupported actions fail explicitly; no invalid action is silently displayed as wait.
        /// </summary>
        /// <param name="intent">The exact retained immutable intent; it is never consumed or replaced.</param>
        /// <param name="sourcePosition">The authoritative source cell; no bounds or occupancy are inferred.</param>
        /// <returns>A detached value preserving source identity, target identity/cell, and declared condition.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="intent"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The domain action has no accepted display mapping.</exception>
        public static EnemyIntentProjection FromRetainedIntent(EnemyIntent intent, GridPosition sourcePosition)
        {
            if (intent == null)
                throw new ArgumentNullException(nameof(intent));
            EnemyIntentSymbol symbol;
            switch (intent.Kind)
            {
                case EnemyIntentKind.Move:
                    symbol = intent.AttackOnPlayerEntry ? EnemyIntentSymbol.ConditionalMove : EnemyIntentSymbol.Move;
                    break;
                case EnemyIntentKind.Attack:
                    symbol = EnemyIntentSymbol.Attack;
                    break;
                case EnemyIntentKind.Wait:
                    symbol = EnemyIntentSymbol.Wait;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "No display mapping is accepted for this action.");
            }
            return new EnemyIntentProjection(intent.ActorId, sourcePosition, symbol, intent.TargetPosition, intent.TargetId);
        }

        /// <summary>
        /// Creates validated prototype or legend data, including ordinary move and investigate examples
        /// absent from current Shambler plans. This does not create a domain action or Listener behavior.
        /// </summary>
        /// <param name="sourceId">The example actor identity; zero and negative values are valid.</param>
        /// <param name="sourcePosition">The copied source cell, without inferred board legality.</param>
        /// <param name="symbol">One of the five declared prototype shapes.</param>
        /// <param name="targetPosition">A fixed target for non-wait shapes; null only for wait.</param>
        /// <param name="targetId">A distinct original player only for attack or conditional movement.</param>
        /// <returns>Immutable example data, never an executable intent.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbol"/> is undefined.</exception>
        /// <exception cref="ArgumentException">Target fields contradict the selected symbol.</exception>
        public static EnemyIntentProjection CreateExample(EntityId sourceId, GridPosition sourcePosition,
            EnemyIntentSymbol symbol, GridPosition? targetPosition = null, EntityId? targetId = null)
        {
            if (symbol < EnemyIntentSymbol.Move || symbol > EnemyIntentSymbol.Wait)
                throw new ArgumentOutOfRangeException(nameof(symbol));
            if (targetPosition.HasValue != (symbol != EnemyIntentSymbol.Wait))
                throw new ArgumentException("Only wait has no target cell.", nameof(targetPosition));
            bool requiresPlayer = symbol == EnemyIntentSymbol.Attack || symbol == EnemyIntentSymbol.ConditionalMove;
            if (targetId.HasValue != requiresPlayer || targetId.HasValue && targetId.Value.Equals(sourceId))
                throw new ArgumentException("Only attacking shapes identify a distinct original player.", nameof(targetId));
            return new EnemyIntentProjection(sourceId, sourcePosition, symbol, targetPosition, targetId);
        }

        /// <summary>Compares all copied fields, not object identity or current gameplay state.</summary>
        /// <param name="other">Another projection, or null for an unequal value.</param>
        /// <returns>True only when all source, action, and target values match.</returns>
        public bool Equals(EnemyIntentProjection other)
        {
            return other != null && SourceId.Equals(other.SourceId) && SourcePosition.Equals(other.SourcePosition) &&
                Symbol == other.Symbol && Nullable.Equals(TargetPosition, other.TargetPosition) && Nullable.Equals(TargetId, other.TargetId);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is EnemyIntentProjection other && Equals(other);

        /// <summary>Hashes the same immutable source, action, and target values used by equality.</summary>
        /// <returns>A hash independent of mutable gameplay state.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceId.GetHashCode();
                hash = hash * 23 + SourcePosition.GetHashCode();
                hash = hash * 23 + (int)Symbol;
                hash = hash * 23 + TargetPosition.GetHashCode();
                return hash * 23 + TargetId.GetHashCode();
            }
        }
    }
}