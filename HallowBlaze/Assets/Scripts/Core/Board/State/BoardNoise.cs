using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>
    /// Immutable snapshot of a noise event emitted from a board position.
    /// The footprint is computed once at emission time and remains stable even if the board changes later.
    /// </summary>
    public sealed class BoardNoise : IEquatable<BoardNoise>
    {
        private readonly HashSet<GridPosition> audibleCells;

        /// <summary>Gets the detached audible footprint in coordinate order, shared by UI and hearing queries.</summary>
        public IReadOnlyList<GridPosition> AudibleCells { get; }

        /// <summary>Gets the board-local monotonically increasing identifier.</summary>
        public long Id { get; }

        /// <summary>Gets the source event identifier.</summary>
        public EntityId SourceId { get; }

        /// <summary>Gets the emission position.</summary>
        public GridPosition Position { get; }

        /// <summary>Gets the maximum number of orthogonal steps from the source.</summary>
        public int Strength { get; }

        /// <summary>Gets the remaining turns before this noise expires.</summary>
        public int RemainingTurns { get; }

        /// <summary>
        /// Creates an immutable noise snapshot by copying its precomputed audible footprint.
        /// </summary>
        /// <param name="id">The board-local monotonically increasing identifier.</param>
        /// <param name="sourceId">The source event identifier.</param>
        /// <param name="position">The emission position.</param>
        /// <param name="strength">The maximum number of orthogonal steps from the source.</param>
        /// <param name="remainingTurns">The remaining turns before expiration.</param>
        /// <param name="audibleCells">The precomputed set of audible cells.</param>
        internal BoardNoise(long id, EntityId sourceId, GridPosition position, int strength, int remainingTurns, IEnumerable<GridPosition> audibleCells)
        {
            Id = id;
            SourceId = sourceId;
            Position = position;
            Strength = strength;
            RemainingTurns = remainingTurns;
            this.audibleCells = new HashSet<GridPosition>(audibleCells ?? throw new ArgumentNullException(nameof(audibleCells)));
            var orderedCells = new List<GridPosition>(this.audibleCells);
            orderedCells.Sort((left, right) => left.X != right.X ? left.X.CompareTo(right.X) : left.Y.CompareTo(right.Y));
            AudibleCells = orderedCells.AsReadOnly();
        }

        /// <summary>
        /// Determines whether the specified position is within the audible footprint.
        /// </summary>
        /// <param name="position">The position to check.</param>
        /// <returns><see langword="true"/> when the position is audible; otherwise, <see langword="false"/>.</returns>
        public bool IsAudible(GridPosition position)
        {
            return audibleCells.Contains(position);
        }

        /// <summary>
        /// Determines whether this noise instance is equal to another.
        /// </summary>
        /// <param name="other">The noise to compare.</param>
        /// <returns><see langword="true"/> when all fields are equal.</returns>
        public bool Equals(BoardNoise other)
        {
            if (other is null)
                return false;

            return Id == other.Id &&
                   SourceId.Equals(other.SourceId) &&
                   Position.Equals(other.Position) &&
                   Strength == other.Strength &&
                   RemainingTurns == other.RemainingTurns &&
                   audibleCells.SetEquals(other.audibleCells);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is BoardNoise other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Id.GetHashCode();
                hash = hash * 23 + SourceId.GetHashCode();
                hash = hash * 23 + Position.GetHashCode();
                hash = hash * 23 + Strength.GetHashCode();
                hash = hash * 23 + RemainingTurns.GetHashCode();
                foreach (GridPosition cell in AudibleCells)
                    hash = hash * 23 + cell.GetHashCode();
                return hash;
            }
        }

    }
}

