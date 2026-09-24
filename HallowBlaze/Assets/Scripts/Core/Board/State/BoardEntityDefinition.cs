using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>
    /// Defines immutable content data for one board entity type.
    /// </summary>
    public sealed class BoardEntityDefinition : IEquatable<BoardEntityDefinition>
    {
        /// <summary>Gets the occupancy layer used by instances of this definition.</summary>
        public BoardLayer Layer { get; }

        /// <summary>Gets the stable kind of entity.</summary>
        public EntityKind Kind { get; }

        /// <summary>Gets the stable textual content identifier.</summary>
        public string ContentId { get; }

        /// <summary>Creates immutable content data for a board entity.</summary>
        /// <param name="layer">The occupancy layer.</param>
        /// <param name="kind">The stable entity kind.</param>
        /// <param name="contentId">The stable textual content identifier.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> is not a defined board layer.</exception>
        /// <exception cref="ArgumentException"><paramref name="kind"/> or <paramref name="contentId"/> is invalid.</exception>
        public BoardEntityDefinition(BoardLayer layer, EntityKind kind, string contentId)
        {
            if (layer < BoardLayer.Terrain || layer > BoardLayer.Actor)
                throw new ArgumentOutOfRangeException(nameof(layer));
            if (string.IsNullOrWhiteSpace(kind.Id))
                throw new ArgumentException("Entity kind must be non-empty.", nameof(kind));
            if (string.IsNullOrWhiteSpace(contentId) ||
                !string.Equals(contentId, contentId.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Content ID must be non-empty and have no surrounding whitespace.", nameof(contentId));
            }

            Layer = layer;
            Kind = kind;
            ContentId = contentId;
        }

        /// <summary>Determines whether this definition has the same immutable content as another definition.</summary>
        /// <param name="other">The definition to compare.</param>
        /// <returns><see langword="true"/> when all fields are equal.</returns>
        public bool Equals(BoardEntityDefinition other)
        {
            return other != null &&
                Layer == other.Layer &&
                Kind.Equals(other.Kind) &&
                string.Equals(ContentId, other.ContentId, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is BoardEntityDefinition other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)Layer;
                hash = hash * 31 + Kind.GetHashCode();
                for (int index = 0; index < ContentId.Length; index++)
                    hash = hash * 31 + ContentId[index];
                return hash;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Layer}:{Kind}:{ContentId}";
        }
    }
}
