using System;

namespace HallowBlaze.Core.Board.Primitives
{
    /// <summary>
    /// Represents a direction on the grid (cardinal directions only).
    /// Immutable value type with deterministic equality and hashing. Default is invalid.
    /// </summary>
    public readonly struct Direction : IEquatable<Direction>, IComparable<Direction>
    {
        /// <summary>
        /// The X delta (column change) for this direction.
        /// </summary>
        public int DeltaX { get; }

        /// <summary>
        /// The Y delta (row change) for this direction.
        /// </summary>
        public int DeltaY { get; }

        /// <summary>
        /// North direction (0, +1).
        /// </summary>
        public static Direction North => new Direction(0, 1);

        /// <summary>
        /// East direction (+1, 0).
        /// </summary>
        public static Direction East => new Direction(1, 0);

        /// <summary>
        /// South direction (0, -1).
        /// </summary>
        public static Direction South => new Direction(0, -1);

        /// <summary>
        /// West direction (-1, 0).
        /// </summary>
        public static Direction West => new Direction(-1, 0);

        /// <summary>
        /// Determines whether this Direction is valid (one of the four cardinal directions).
        /// </summary>
        /// <returns>True if this is a valid cardinal direction; otherwise, false.</returns>
        public bool IsValid => DeltaX == 0 && DeltaY == 1 || DeltaX == 1 && DeltaY == 0 || DeltaX == 0 && DeltaY == -1 || DeltaX == -1 && DeltaY == 0;

        /// <summary>
        /// Creates a new Direction.
        /// </summary>
        /// <param name="deltaX">The X delta (column change).</param>
        /// <param name="deltaY">The Y delta (row change).</param>
        /// <exception cref="ArgumentException">The offset is not a cardinal unit direction.</exception>
        public Direction(int deltaX, int deltaY)
        {
            if (deltaX == 0 && deltaY == 1)
            {
                DeltaX = deltaX;
                DeltaY = deltaY;
                return;
            }
            if (deltaX == 1 && deltaY == 0)
            {
                DeltaX = deltaX;
                DeltaY = deltaY;
                return;
            }
            if (deltaX == 0 && deltaY == -1)
            {
                DeltaX = deltaX;
                DeltaY = deltaY;
                return;
            }
            if (deltaX == -1 && deltaY == 0)
            {
                DeltaX = deltaX;
                DeltaY = deltaY;
                return;
            }
            throw new ArgumentException("Direction must be one of the four cardinal directions (North, East, South, West).");
        }

        /// <summary>
        /// Gets the opposite direction.
        /// </summary>
        /// <returns>The opposite Direction.</returns>
        /// <exception cref="InvalidOperationException">This is the invalid default direction.</exception>
        public Direction Opposite()
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("Cannot get opposite of default direction.");
            }
            return new Direction(-DeltaX, -DeltaY);
        }

        /// <summary>
        /// Determines whether this Direction is equal to another Direction.
        /// </summary>
        /// <param name="other">The Direction to compare with.</param>
        /// <returns>True if the Directions are equal; otherwise, false.</returns>
        public bool Equals(Direction other)
        {
            return DeltaX == other.DeltaX && DeltaY == other.DeltaY;
        }

        /// <summary>
        /// Determines whether this Direction is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True if the objects are equal; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return obj is Direction other && Equals(other);
        }

        /// <summary>
        /// Serves as a hash function for this Direction.
        /// </summary>
        /// <returns>A hash code for this Direction.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + DeltaX.GetHashCode();
                hash = hash * 23 + DeltaY.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Compares this Direction with another Direction for sorting.
        /// Sorts by DeltaX ascending, then DeltaY ascending, independently of creation order.
        /// </summary>
        /// <param name="other">The Direction to compare with.</param>
        /// <returns>A value indicating the relative order of the directions.</returns>
        public int CompareTo(Direction other)
        {
            int xCompare = DeltaX.CompareTo(other.DeltaX);
            if (xCompare != 0) return xCompare;
            return DeltaY.CompareTo(other.DeltaY);
        }

        /// <summary>
        /// Returns a string representation of this Direction.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString()
        {
            if (DeltaX == 0 && DeltaY == 1) return "North";
            if (DeltaX == 1 && DeltaY == 0) return "East";
            if (DeltaX == 0 && DeltaY == -1) return "South";
            if (DeltaX == -1 && DeltaY == 0) return "West";
            return "Default";
        }
    }
}
