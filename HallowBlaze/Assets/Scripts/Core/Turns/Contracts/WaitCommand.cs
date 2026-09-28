namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a player command to wait (skip turn).
    /// Waiting is always a valid command that consumes one turn.
    /// </summary>
    public sealed class WaitCommand : PlayerCommand
    {
        /// <summary>
        /// Gets the type of the command.
        /// </summary>
        public override string CommandType => "Wait";
    }
}
