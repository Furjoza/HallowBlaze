using System;

namespace HallowBlaze.Core.Board.Primitives
{
    /// <summary>
    /// Represents the kind/category of an entity on the board.
    /// Immutable value type with deterministic equality and hashing.
    /// </summary>
    public readonly struct EntityKind : IEquatable<EntityKind>, IComparable<EntityKind>
    {
        /// <summary>
        /// The kind identifier as a string.
        /// Content IDs remain textual.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Creates a new EntityKind.
        /// </summary>
        /// <param name="id">The kind identifier.</param>
        /// <exception cref="ArgumentException">Throws ArgumentException if id is null, empty, or whitespace.</exception>
        public EntityKind(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("EntityKind id cannot be null, empty, or whitespace.", nameof(id));
            }

            Id = id;
        }

        /// <summary>
        /// Player entity kind.
        /// </summary>
        public static EntityKind Player => new EntityKind("player");

        /// <summary>
        /// Enemy entity kind.
        /// </summary>
        public static EntityKind Enemy => new EntityKind("enemy");

        /// <summary>
        /// Item entity kind.
        /// </summary>
        public static EntityKind Item => new EntityKind("item");

        /// <summary>
        /// Obstacle entity kind.
        /// </summary>
        public static EntityKind Obstacle => new EntityKind("obstacle");

        /// <summary>
        /// Determines whether this EntityKind is equal to another EntityKind.
        /// </summary>
        /// <param name="other">The EntityKind to compare with.</param>
        /// <returns>True if the EntityKinds are equal; otherwise, false.</returns>
        public bool Equals(EntityKind other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether this EntityKind is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True if the objects are equal; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return obj is EntityKind other && Equals(other);
        }

        /// <summary>
        /// Serves as a deterministic hash function for this EntityKind.
        /// Uses an explicit unchecked hash of consecutive UTF-16 characters
        /// with seed 17 and multiplier 31 for process-independent determinism.
        /// Returns 0 for default(EntityKind) without throwing an exception.
        /// </summary>
        /// <returns>A hash code for this EntityKind.</returns>
        public override int GetHashCode()
        {
            if (Id == null) return 0;
            unchecked
            {
                int hash = 17;
                for (int characterIndex = 0; characterIndex < Id.Length; characterIndex++)
                {
                    hash = hash * 31 + Id[ characterIndex ];
                }
                return hash;
            }
        }

        /// <summary>
        /// Compares this EntityKind with another EntityKind for sorting.
        /// Sorting is stable and independent of object creation order.
        /// </summary>
        /// <param name="other">The EntityKind to compare with.</param>
        /// <returns>A value indicating the relative order of the EntityKinds.</returns>
        public int CompareTo(EntityKind other)
        {
            return StringComparer.Ordinal.Compare(Id, other.Id);
        }

        /// <summary>
        /// Returns a string representation of this EntityKind.
        /// Returns an empty string for default(EntityKind) instead of null.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString() => Id ?? string.Empty;

        /// <summary>
        /// The default value for EntityKind has Id equal to null and GetHashCode equal to 0.
        /// This value is not a valid entity kind and should not be used in gameplay logic.
        /// </summary>
        public static EntityKind Default => default;
    }
}
