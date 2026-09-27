using System;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>
    /// Immutable value type that classifies an entity's behavior on the board.
    /// </summary>
    public readonly struct BoardEntityTraits : IEquatable<BoardEntityTraits>
    {
        /// <summary>Gets whether the entity can be walked through.</summary>
        public bool IsWalkable { get; }

        /// <summary>Gets whether the entity is an exit point.</summary>
        public bool IsExit { get; }

        /// <summary>Gets whether the entity can be interacted with.</summary>
        public bool IsInteractable { get; }

        /// <summary>Gets the default traits (not walkable, not an exit, not interactable).</summary>
        public static BoardEntityTraits Default => new BoardEntityTraits(false, false, false);

        /// <summary>Creates immutable traits for a board entity.</summary>
        /// <param name="isWalkable">Whether the entity can be walked through.</param>
        /// <param name="isExit">Whether the entity is an exit point.</param>
        /// <param name="isInteractable">Whether the entity can be interacted with.</param>
        public BoardEntityTraits(bool isWalkable, bool isExit, bool isInteractable)
        {
            IsWalkable = isWalkable;
            IsExit = isExit;
            IsInteractable = isInteractable;
        }

        /// <summary>Determines whether this traits instance is equal to another.</summary>
        /// <param name="other">The traits to compare.</param>
        /// <returns><see langword="true"/> when all fields are equal.</returns>
        public bool Equals(BoardEntityTraits other)
        {
            return IsWalkable == other.IsWalkable &&
                   IsExit == other.IsExit &&
                   IsInteractable == other.IsInteractable;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is BoardEntityTraits other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + IsWalkable.GetHashCode();
                hash = hash * 31 + IsExit.GetHashCode();
                hash = hash * 31 + IsInteractable.GetHashCode();
                return hash;
            }
        }

        /// <summary>Determines whether two traits instances are equal.</summary>
        /// <param name="left">The first traits.</param>
        /// <param name="right">The second traits.</param>
        /// <returns><see langword="true"/> when all fields are equal.</returns>
        public static bool operator ==(BoardEntityTraits left, BoardEntityTraits right)
        {
            return left.Equals(right);
        }

        /// <summary>Determines whether two traits instances are not equal.</summary>
        /// <param name="left">The first traits.</param>
        /// <param name="right">The second traits.</param>
        /// <returns><see langword="true"/> when any field differs.</returns>
        public static bool operator !=(BoardEntityTraits left, BoardEntityTraits right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Walkable:{IsWalkable},Exit:{IsExit},Interactable:{IsInteractable}";
        }
    }
}
