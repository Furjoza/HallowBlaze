using System.Collections.Generic;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents the immutable outcome of resolving one player command.
    /// </summary>
    public abstract class TurnResult
    {
        /// <summary>
        /// Gets a value indicating whether the command was accepted.
        /// </summary>
        public abstract bool Accepted { get; }

        /// <summary>
        /// Gets a value indicating whether the command consumes a turn.
        /// </summary>
        public abstract bool ConsumesTurn { get; }

        /// <summary>
        /// Gets the game events that occurred as a result of this turn.
        /// This is an immutable snapshot that preserves caller order.
        /// </summary>
        public abstract IReadOnlyList<GameEvent> Events { get; }
    }
}
