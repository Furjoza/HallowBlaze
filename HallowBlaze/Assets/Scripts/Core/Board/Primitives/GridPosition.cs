using System;

namespace HallowBlaze.Core.Board.Primitives
{
    /// <summary>
    /// Represents a position on the grid board.
    /// Immutable value type with deterministic equality and hashing. Default is the origin.
    /// Positive X points east and positive Y points north. Bounds are validated separately.
    /// </summary>
    public readonly struct GridPosition : IEquatable<GridPosition>, IComparable<GridPosition>
    {
        /// <summary>
        /// The X coordinate (column) of the grid position.
        /// </summary>
        public int X { get; }

        /// <summary>
        /// The Y coordinate (row) of the grid position.
        /// </summary>
        public int Y { get; }

        /// <summary>
        /// Creates a new GridPosition.
        /// </summary>
        /// <param name="x">The X coordinate (column).</param>
        /// <param name="y">The Y coordinate (row).</param>
        public GridPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// Gets the default board size (8x8).
        /// </summary>
        public static GridBounds DefaultBoardBounds => new GridBounds(0, 0, 7, 7);

        /// <summary>
        /// Checks if this position is within the specified bounds.
        /// </summary>
        /// <param name="bounds">The bounds to check against.</param>
        /// <returns>True if the position is within bounds; otherwise, false.</returns>
        public bool IsWithin(GridBounds bounds)
        {
            return bounds.Contains(this);
        }

        /// <summary>
        /// Gets unbounded neighbors in north, east, south, west order.
        /// </summary>
        /// <returns>Array of neighboring GridPosition objects.</returns>
        /// <exception cref="OverflowException">A neighbor exceeds the integer coordinate range.</exception>
        public GridPosition[] GetCardinalNeighbors()
        {
            return new[]
            {
                Move(Direction.North),
                Move(Direction.East),
                Move(Direction.South),
                Move(Direction.West)
            };
        }

        /// <summary>
        /// Returns a one-cell offset without mutating state or validating board bounds.
        /// </summary>
        /// <param name="direction">The direction to move.</param>
        /// <returns>A new GridPosition representing the moved position.</returns>
        /// <exception cref="ArgumentException">The direction is the invalid default value.</exception>
        /// <exception cref="OverflowException">The offset exceeds the integer coordinate range.</exception>
        public GridPosition Move(Direction direction)
        {
            if (!direction.IsValid)
            {
                throw new ArgumentException("A cardinal direction is required.", nameof(direction));
            }
            return new GridPosition(checked(X + direction.DeltaX), checked(Y + direction.DeltaY));
        }

        /// <summary>
        /// Calculates the Manhattan distance to another position.
        /// </summary>
        /// <param name="other">The other position.</param>
        /// <returns>The Manhattan distance.</returns>
        public long ManhattanDistance(GridPosition other)
        {
            return Math.Abs((long)X - other.X) + Math.Abs((long)Y - other.Y);
        }

        /// <summary>
        /// Determines whether this GridPosition is equal to another GridPosition.
        /// </summary>
        /// <param name="other">The GridPosition to compare with.</param>
        /// <returns>True if the GridPositions are equal; otherwise, false.</returns>
        public bool Equals(GridPosition other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <summary>
        /// Determines whether this GridPosition is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True if the objects are equal; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return obj is GridPosition other && Equals(other);
        }

        /// <summary>
        /// Serves as a hash function for this GridPosition.
        /// </summary>
        /// <returns>A hash code for this GridPosition.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + X.GetHashCode();
                hash = hash * 23 + Y.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Compares this GridPosition with another GridPosition for sorting.
        /// Sorts by X ascending, then Y ascending, independently of creation order.
        /// </summary>
        /// <param name="other">The GridPosition to compare with.</param>
        /// <returns>A value indicating the relative order of the positions.</returns>
        public int CompareTo(GridPosition other)
        {
            int xCompare = X.CompareTo(other.X);
            if (xCompare != 0) return xCompare;
            return Y.CompareTo(other.Y);
        }

        /// <summary>
        /// Returns a string representation of this GridPosition.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString() => $"({X}, {Y})";
    }
}
