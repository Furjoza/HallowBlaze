using System;
using HallowBlaze.Core.Board.State;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Declares an accepted action's immutable noise, identical to the authoritative hearing snapshot.</summary>
    public sealed class NoiseEvent : GameEvent
    {
        /// <summary>Wraps the original board-owned emission without recomputing its footprint.</summary>
        /// <param name="noise">The immutable source, position, strength, lifetime and audible cells at emission.</param>
        /// <exception cref="ArgumentNullException">The noise is null.</exception>
        public NoiseEvent(BoardNoise noise)
        {
            Noise = noise ?? throw new ArgumentNullException(nameof(noise));
        }

        /// <inheritdoc />
        public override string EventType => "Noise";
        /// <summary>Gets the original emission; remaining lifetime in current BoardState may subsequently be lower.</summary>
        public BoardNoise Noise { get; }
    }
}