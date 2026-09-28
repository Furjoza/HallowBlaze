using System;
using System.Collections.Generic;
using System.Linq;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents the result of an accepted player command.
    /// An accepted command always consumes exactly one turn.
    /// </summary>
    public sealed class AcceptedTurnResult : TurnResult
    {
        /// <summary>
        /// Gets a value indicating whether the command was accepted.
        /// </summary>
        public override bool Accepted => true;

        /// <summary>
        /// Gets a value indicating whether the command consumes a turn.
        /// </summary>
        public override bool ConsumesTurn => true;

        /// <summary>
        /// Gets the game events that occurred as a result of this turn.
        /// This is an immutable snapshot that preserves caller order.
        /// </summary>
        public override IReadOnlyList<GameEvent> Events { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AcceptedTurnResult"/> class.
        /// </summary>
        /// <param name="events">The game events in presentation order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="events"/> contains a null entry.</exception>
        public AcceptedTurnResult(IEnumerable<GameEvent> events)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            var eventList = events.ToList();
            if (eventList.Contains(null))
            {
                throw new ArgumentException("Events collection contains null entries.", nameof(events));
            }

            Events = eventList.AsReadOnly();
        }
    }
}
