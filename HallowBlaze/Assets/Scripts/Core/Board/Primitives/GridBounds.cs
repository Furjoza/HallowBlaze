using System;

namespace HallowBlaze.Core.Board.Primitives
{
    /// <summary>
    /// Represents a rectangular boundary on the grid.
    /// Immutable inclusive bounds; default contains only the origin. All edge cells are legal.
    /// </summary>
    public readonly struct GridBounds : IEquatable<GridBounds>
    {
        /// <summary>
        /// The minimum X coordinate (inclusive).
        /// </summary>
        public int MinX { get; }

        /// <summary>
        /// The minimum Y coordinate (inclusive).
        /// </summary>
        public int MinY { get; }

        /// <summary>
        /// The maximum X coordinate (inclusive).
        /// </summary>
        public int MaxX { get; }

        /// <summary>
        /// The maximum Y coordinate (inclusive).
        /// </summary>
        public int MaxY { get; }

        /// <summary>
        /// Creates a new GridBounds.
        /// </summary>
        /// <param name="minX">The minimum X coordinate (inclusive).</param>
        /// <param name="minY">The minimum Y coordinate (inclusive).</param>
        /// <param name="maxX">The maximum X coordinate (inclusive).</param>
        /// <param name="maxY">The maximum Y coordinate (inclusive).</param>
        /// <exception cref="ArgumentException">A minimum exceeds its maximum.</exception>
        public GridBounds(int minX, int minY, int maxX, int maxY)
        {
            if (minX > maxX || minY > maxY)
            {
                throw new ArgumentException("Invalid bounds: minimum coordinates must be less than or equal to maximum coordinates.");
            }

            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>
        /// Gets the width of the bounds (MaxX - MinX + 1).
        /// </summary>
        public long Width => (long)MaxX - MinX + 1;

        /// <summary>
        /// Gets the height of the bounds (MaxY - MinY + 1).
        /// </summary>
        public long Height => (long)MaxY - MinY + 1;

        /// <summary>
        /// Gets the midpoint, rounding each coordinate toward zero when between cells.
        /// </summary>
        public GridPosition Center => new GridPosition(
            (int)(((long)MinX + MaxX) / 2),
            (int)(((long)MinY + MaxY) / 2)
        );

        /// <summary>
        /// Checks if a position is within these bounds.
        /// </summary>
        /// <param name="position">The position to check.</param>
        /// <returns>True if the position is within bounds; otherwise, false.</returns>
        public bool Contains(GridPosition position)
        {
            return position.X >= MinX && position.X <= MaxX &&
                   position.Y >= MinY && position.Y <= MaxY;
        }

        /// <summary>
        /// Rejects positions outside the inclusive rectangle without modifying any state.
        /// </summary>
        /// <param name="position">The position to validate before a domain operation.</param>
        /// <exception cref="ArgumentOutOfRangeException">The position is outside these bounds.</exception>
        public void RequireContains(GridPosition position)
        {
            if (!Contains(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, "Position is outside board bounds.");
            }
        }

        /// <summary>
        /// Checks if another bounds is completely within these bounds.
        /// </summary>
        /// <param name="other">The other bounds to check.</param>
        /// <returns>True if the other bounds is completely within these bounds; otherwise, false.</returns>
        public bool Contains(GridBounds other)
        {
            return MinX <= other.MinX && MaxX >= other.MaxX &&
                   MinY <= other.MinY && MaxY >= other.MaxY;
        }

        /// <summary>
        /// Checks if this bounds intersects with another bounds.
        /// </summary>
        /// <param name="other">The other bounds to check.</param>
        /// <returns>True if the bounds intersect; otherwise, false.</returns>
        public bool Intersects(GridBounds other)
        {
            return MinX <= other.MaxX && MaxX >= other.MinX &&
                   MinY <= other.MaxY && MaxY >= other.MinY;
        }

        /// <summary>
        /// Determines whether this GridBounds is equal to another GridBounds.
        /// </summary>
        /// <param name="other">The GridBounds to compare with.</param>
        /// <returns>True if the GridBounds are equal; otherwise, false.</returns>
        public bool Equals(GridBounds other)
        {
            return MinX == other.MinX && MinY == other.MinY &&
                   MaxX == other.MaxX && MaxY == other.MaxY;
        }

        /// <summary>
        /// Determines whether this GridBounds is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True if the objects are equal; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return obj is GridBounds other && Equals(other);
        }

        /// <summary>
        /// Serves as a hash function for this GridBounds.
        /// </summary>
        /// <returns>A hash code for this GridBounds.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + MinX.GetHashCode();
                hash = hash * 23 + MinY.GetHashCode();
                hash = hash * 23 + MaxX.GetHashCode();
                hash = hash * 23 + MaxY.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Returns a string representation of this GridBounds.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString() => $"(Min: ({MinX}, {MinY}), Max: ({MaxX}, {MaxY}))";
    }
}
