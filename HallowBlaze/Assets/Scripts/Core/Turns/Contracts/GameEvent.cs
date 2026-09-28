namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents an immutable fact emitted by turn resolution for ordered presentation.
    /// </summary>
    public abstract class GameEvent
    {
        /// <summary>
        /// Gets the type of the event.
        /// </summary>
        public abstract string EventType { get; }
    }
}
