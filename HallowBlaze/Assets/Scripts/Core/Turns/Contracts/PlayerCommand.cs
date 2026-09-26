namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a player's immutable board-action intent for resolution.
    /// </summary>
    public abstract class PlayerCommand
    {
        /// <summary>
        /// Gets the type of the command.
        /// </summary>
        public abstract string CommandType { get; }
    }
}
