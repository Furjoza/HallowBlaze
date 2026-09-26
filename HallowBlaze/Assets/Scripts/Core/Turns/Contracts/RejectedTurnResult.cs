using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>
    /// Represents the result of a rejected player command.
    /// A rejected command never consumes a turn, food, or tool.
    /// </summary>
    public sealed class RejectedTurnResult : TurnResult
    {
        /// <summary>
        /// Gets a value indicating whether the command was accepted.
        /// </summary>
        public override bool Accepted => false;

        /// <summary>
        /// Gets a value indicating whether the command consumes a turn.
        /// </summary>
        public override bool ConsumesTurn => false;

        /// <summary>
        /// Gets the rejection code explaining why the command was rejected.
        /// </summary>
        public CommandRejectionCode RejectionCode { get; }

        /// <summary>
        /// Gets the game events that occurred as a result of this turn.
        /// For rejected commands, this is always an empty collection.
        /// </summary>
        public override IReadOnlyList<GameEvent> Events => Array.Empty<GameEvent>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RejectedTurnResult"/> class.
        /// </summary>
        /// <param name="rejectionCode">The code explaining why the command was rejected.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="rejectionCode"/> is the default sentinel or an undefined value.
        /// </exception>
        public RejectedTurnResult(CommandRejectionCode rejectionCode)
        {
            if (rejectionCode == CommandRejectionCode.None ||
                !Enum.IsDefined(typeof(CommandRejectionCode), rejectionCode))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rejectionCode),
                    rejectionCode,
                    "A defined rejection reason is required.");
            }

            RejectionCode = rejectionCode;
        }
    }
}
