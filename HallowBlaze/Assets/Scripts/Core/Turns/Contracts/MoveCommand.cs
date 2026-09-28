using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents a player command to move in a specific direction.
    /// </summary>
    public sealed class MoveCommand : PlayerCommand
    {
        /// <summary>
        /// Gets the direction of movement.
        /// </summary>
        public Direction Direction { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MoveCommand"/> class.
        /// </summary>
        /// <param name="direction">The direction to move. Must be a valid cardinal direction.</param>
        /// <exception cref="ArgumentException"><paramref name="direction"/> is not a cardinal direction.</exception>
        public MoveCommand(Direction direction)
        {
            if (!direction.IsValid)
            {
                throw new ArgumentException("Direction must be a valid cardinal direction (North, East, South, or West).", nameof(direction));
            }

            Direction = direction;
        }

        /// <summary>
        /// Gets the type of the command.
        /// </summary>
        public override string CommandType => "Move";
    }
}
