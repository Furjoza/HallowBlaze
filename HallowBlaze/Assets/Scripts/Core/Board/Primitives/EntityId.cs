using System;

namespace HallowBlaze.Core.Board.Primitives
{
    /// <summary>
    /// Represents a unique identifier for an entity on the board.
    /// Immutable value type with deterministic equality and hashing.
    /// Identity is board-local and need not survive a board reset. All long values are valid;
    /// default equals ID zero. The caller owns uniqueness and deterministic assignment from
    /// board data, never from view creation order or Unity instance identifiers.
    /// Content identifiers remain separate textual IDs.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        /// <summary>
        /// The unique identifier value.
        /// </summary>
        public long Value { get; }

        /// <summary>
        /// Creates a new EntityId.
        /// </summary>
        /// <param name="value">The unique identifier value.</param>
        public EntityId(long value)
        {
            Value = value;
        }

        /// <summary>
        /// Determines whether this EntityId is equal to another EntityId.
        /// </summary>
        /// <param name="other">The EntityId to compare with.</param>
        /// <returns>True if the EntityIds are equal; otherwise, false.</returns>
        public bool Equals(EntityId other)
        {
            return Value == other.Value;
        }

        /// <summary>
        /// Determines whether this EntityId is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True if the objects are equal; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return obj is EntityId other && Equals(other);
        }

        /// <summary>
        /// Returns the deterministic XOR of the low and high 32-bit halves of the ID.
        /// </summary>
        /// <returns>A hash code for this EntityId.</returns>
        public override int GetHashCode()
        {
            return unchecked((int)Value ^ (int)(Value >> 32));
        }

        /// <summary>
        /// Compares this EntityId with another EntityId for sorting.
        /// Sorts by signed numeric value ascending, independently of object creation order.
        /// </summary>
        /// <param name="other">The EntityId to compare with.</param>
        /// <returns>A value indicating the relative order of the EntityIds.</returns>
        public int CompareTo(EntityId other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <summary>
        /// Returns a string representation of this EntityId.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString() => $"EntityId({Value})";
    }
}
