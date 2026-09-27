using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>
    /// Represents the runtime state of an entity on the board.
    /// This type is immutable and implements value equality.
    /// </summary>
    public sealed class BoardEntityState : IEquatable<BoardEntityState>
    {
        /// <summary>
        /// Gets the unique identifier of the entity.
        /// </summary>
        public EntityId Id { get; }

        /// <summary>
        /// Gets the definition of the entity.
        /// </summary>
        public BoardEntityDefinition Definition { get; }

        /// <summary>
        /// Gets the position of the entity on the board.
        /// </summary>
        public GridPosition Position { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BoardEntityState"/> class.
        /// </summary>
        /// <param name="id">The entity identifier. <c>EntityId(0)</c> is valid by the existing primitive contract.</param>
        /// <param name="definition">The entity definition. Cannot be null.</param>
        /// <param name="position">The entity position. Coordinates are validated against bounds only by <see cref="BoardState"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
        public BoardEntityState(EntityId id, BoardEntityDefinition definition, GridPosition position)
        {
            Id = id;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Position = position;
        }

        /// <summary>
        /// Creates a new entity state with the same definition but a different position.
        /// </summary>
        /// <param name="position">The new position.</param>
        /// <returns>A new entity state with the updated position.</returns>
        internal BoardEntityState WithPosition(GridPosition position)
        {
            return new BoardEntityState(Id, Definition, position);
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>true if the current object is equal to the <paramref name="other"/> parameter; otherwise, false.</returns>
        public bool Equals(BoardEntityState other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id.Equals(other.Id) && Definition.Equals(other.Definition) && Position.Equals(other.Position);
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((BoardEntityState)obj);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Id.GetHashCode();
                hash = hash * 23 + Definition.GetHashCode();
                hash = hash * 23 + Position.GetHashCode();
                return hash;
            }
        }
    }
}
