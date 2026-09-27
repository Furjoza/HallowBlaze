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

        /// <summary>Gets the immutable traits that classify this entity's behavior.</summary>
        public BoardEntityTraits Traits { get; }

        /// <summary>
        /// Gets the amount of Food restored automatically when the player enters this item's cell.
        /// Zero indicates that the entity is not automatic food.
        /// </summary>
        public int AutomaticFoodReward { get; }

        /// <summary>Creates immutable content data for a board entity.</summary>
        /// <param name="layer">The occupancy layer.</param>
        /// <param name="kind">The stable entity kind.</param>
        /// <param name="contentId">The stable textual content identifier.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> is not a defined board layer.</exception>
        /// <exception cref="ArgumentException"><paramref name="kind"/> or <paramref name="contentId"/> is invalid.</exception>
        public BoardEntityDefinition(BoardLayer layer, EntityKind kind, string contentId)
            : this(layer, kind, contentId, BoardEntityTraits.Default, 0)
        {
        }

        /// <summary>Creates immutable content data for a board entity with explicit traits.</summary>
        /// <param name="layer">The occupancy layer.</param>
        /// <param name="kind">The stable entity kind.</param>
        /// <param name="contentId">The stable textual content identifier.</param>
        /// <param name="traits">The traits that classify this entity's behavior.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> is not a defined board layer.</exception>
        /// <exception cref="ArgumentException"><paramref name="kind"/> or <paramref name="contentId"/> is invalid.</exception>
        public BoardEntityDefinition(BoardLayer layer, EntityKind kind, string contentId, BoardEntityTraits traits)
            : this(layer, kind, contentId, traits, 0)
        {
        }

        /// <summary>Creates immutable content data for a board entity with explicit traits and automatic food reward.</summary>
        /// <param name="layer">The occupancy layer.</param>
        /// <param name="kind">The stable entity kind.</param>
        /// <param name="contentId">The stable textual content identifier.</param>
        /// <param name="traits">The traits that classify this entity's behavior.</param>
        /// <param name="automaticFoodReward">Food restored when entering this item's cell, or zero for no automatic reward.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="layer"/> is not a defined board layer, <paramref name="automaticFoodReward"/> is negative,
        /// or a positive reward is assigned outside the item layer.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="kind"/> or <paramref name="contentId"/> is invalid.</exception>
        public BoardEntityDefinition(
            BoardLayer layer,
            EntityKind kind,
            string contentId,
            BoardEntityTraits traits,
            int automaticFoodReward)
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
            if (automaticFoodReward < 0 ||
                automaticFoodReward > 0 && layer != BoardLayer.Item)
            {
                throw new ArgumentOutOfRangeException(nameof(automaticFoodReward));
            }

            Layer = layer;
            Kind = kind;
            ContentId = contentId;
            Traits = traits;
            AutomaticFoodReward = automaticFoodReward;
        }

        /// <summary>Determines whether this definition has the same immutable content as another definition.</summary>
        /// <param name="other">The definition to compare.</param>
        /// <returns><see langword="true"/> when all fields are equal.</returns>
        public bool Equals(BoardEntityDefinition other)
        {
            return other != null &&
                Layer == other.Layer &&
                Kind.Equals(other.Kind) &&
                string.Equals(ContentId, other.ContentId, StringComparison.Ordinal) &&
                Traits.Equals(other.Traits) &&
                AutomaticFoodReward == other.AutomaticFoodReward;
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
                hash = hash * 31 + Traits.GetHashCode();
                hash = hash * 31 + AutomaticFoodReward;
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
