namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents stable, machine-readable rejection codes for player commands.
    /// These codes indicate why a command was rejected and are used for both
    /// gameplay feedback and deterministic behavior.
    /// </summary>
    public enum CommandRejectionCode
    {
        /// <summary>
        /// No rejection reason specified (invalid/default sentinel).
        /// </summary>
        None = 0,

        /// <summary>
        /// Invalid or unrecognized command type.
        /// </summary>
        InvalidCommand = 1,

        /// <summary>
        /// Command is invalid for the current game state.
        /// </summary>
        InvalidState = 2,

        /// <summary>
        /// Target entity does not exist or is invalid.
        /// </summary>
        InvalidTarget = 3,

        /// <summary>
        /// Movement is out of bounds of the game board.
        /// </summary>
        OutOfBounds = 4,

        /// <summary>
        /// Movement is blocked by an obstacle or entity.
        /// </summary>
        Blocked = 5,

        /// <summary>
        /// No available interaction at the target location.
        /// </summary>
        NoInteractionAvailable = 6
    }
}
