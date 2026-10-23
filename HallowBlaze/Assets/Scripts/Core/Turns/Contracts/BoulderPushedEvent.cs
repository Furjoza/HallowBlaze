using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Records one committed cardinal boulder push and the simultaneous player advance, never two independent actions.</summary>
    public sealed class BoulderPushedEvent : GameEvent
    {
        /// <summary>Captures both pre-action cells and the boulder's destination for synchronized read-only replay.</summary>
        /// <exception cref="ArgumentException">Participants coincide or the positions do not describe a straight single-cell push.</exception>
        public BoulderPushedEvent(EntityId playerId, EntityId boulderId, GridPosition playerFrom, GridPosition boulderFrom, GridPosition boulderTo)
        {
            if (playerId.Equals(boulderId) || playerFrom.ManhattanDistance(boulderFrom) != 1 ||
                (long)boulderFrom.X - playerFrom.X != (long)boulderTo.X - boulderFrom.X ||
                (long)boulderFrom.Y - playerFrom.Y != (long)boulderTo.Y - boulderFrom.Y)
                throw new ArgumentException("A push requires distinct participants and two equal cardinal steps.");
            PlayerId = playerId;
            BoulderId = boulderId;
            PlayerFrom = playerFrom;
            BoulderFrom = boulderFrom;
            BoulderTo = boulderTo;
        }

        /// <summary>Gets the actor that already advanced under the shared command cost.</summary>
        public EntityId PlayerId { get; }
        /// <summary>Gets the exact board-local obstacle, not a prefab or content ID.</summary>
        public EntityId BoulderId { get; }
        /// <summary>Gets the player's cell before the atomic commit.</summary>
        public GridPosition PlayerFrom { get; }
        /// <summary>Gets the boulder's cell before the atomic commit.</summary>
        public GridPosition BoulderFrom { get; }
        /// <summary>Gets the player's destination, exactly the boulder's former cell.</summary>
        public GridPosition PlayerTo => BoulderFrom;
        /// <summary>Gets the boulder's committed destination.</summary>
        public GridPosition BoulderTo { get; }
        /// <inheritdoc />
        public override string EventType => "BoulderPushed";
    }
}