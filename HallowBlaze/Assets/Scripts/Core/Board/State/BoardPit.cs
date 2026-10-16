using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>Immutable, visible board-local pit; it never changes terrain walkability or damages an actor.</summary>
    public sealed class BoardPit
    {
        internal BoardPit(long id, EntityId terrainId, GridPosition position, int remainingTurns, EntityId? trappedActorId = null)
        {
            Id = id;
            TerrainId = terrainId;
            Position = position;
            RemainingTurns = remainingTurns;
            TrappedActorId = trappedActorId;
        }

        /// <summary>Gets the board-local creation sequence; a later pit never reuses this identity.</summary>
        public long Id { get; }
        /// <summary>Gets the exact terrain instance supporting this pit.</summary>
        public EntityId TerrainId { get; }
        /// <summary>Gets the publicly visible pit cell.</summary>
        public GridPosition Position { get; }
        /// <summary>Gets subsequent accepted commands before expiration; rejected commands never age it.</summary>
        public int RemainingTurns { get; }
        /// <summary>Gets the supported Shambler held until expiration, or null before first entry.</summary>
        public EntityId? TrappedActorId { get; }
    }
}