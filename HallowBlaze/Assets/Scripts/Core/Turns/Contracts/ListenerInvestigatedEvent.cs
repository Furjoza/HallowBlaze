using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Records an actually executed Listener investigation step, not a plan, noise emission or source sighting.</summary>
    public sealed class ListenerInvestigatedEvent : GameEvent
    {
        /// <summary>Captures the live noise and exact step already performed by the retained-intent executor.</summary>
        public ListenerInvestigatedEvent(EntityId actorId, GridPosition from, GridPosition to, long noiseId, GridPosition noiseSource)
        {
            if (from.ManhattanDistance(to) != 1) throw new ArgumentException("Investigation must be one actual cardinal step.", nameof(to));
            if (noiseId < 0) throw new ArgumentOutOfRangeException(nameof(noiseId));
            ActorId = actorId;
            From = from;
            To = to;
            NoiseId = noiseId;
            NoiseSource = noiseSource;
        }
        /// <summary>Gets the Listener whose actual movement precedes this event.</summary>
        public EntityId ActorId { get; }
        /// <summary>Gets its pre-execution cell.</summary>
        public GridPosition From { get; }
        /// <summary>Gets the exact announced and executed destination.</summary>
        public GridPosition To { get; }
        /// <summary>Gets the live signal identity controlling that retained opportunity.</summary>
        public long NoiseId { get; }
        /// <summary>Gets the signal's fixed source cell, not the player's current position.</summary>
        public GridPosition NoiseSource { get; }
        /// <inheritdoc />
        public override string EventType => "ListenerInvestigated";
    }
}