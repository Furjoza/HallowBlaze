using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Owns phases 1-4: command validation, direct effects and rewards, action cost,
    /// and the immediate terminal outcome, independently of the command's concrete type.
    /// </summary>
    public interface IPlayerPhaseResolver
    {
        /// <summary>
        /// Validates the entire command before the first mutation, then resolves it once.
        /// Rejection leaves board and run unchanged and consumes no turn or resources.
        /// Acceptance commits all player effects and one action cost before returning one
        /// immutable, ordered result. Rewards precede cost; starvation prevents exit (O-002).
        /// Composite commands must validate all constituent actions before applying any.
        /// </summary>
        /// <param name="boardState">The authoritative board, exclusively owned during resolution.</param>
        /// <param name="runState">The authoritative run, exclusively owned during resolution.</param>
        /// <param name="playerId">The board-local player identifier.</param>
        /// <param name="command">The complete intent to validate; null must be rejected.</param>
        /// <returns>A non-null accepted or rejected result; never a partial turn or modal draft.</returns>
        /// <remarks>
        /// This is a validation-before-mutation contract, not an exception rollback mechanism.
        /// Implementations must report terminal outcomes through the existing player events.
        /// </remarks>
        TurnResult Resolve(BoardState boardState, RunState runState, EntityId playerId, PlayerCommand command);
    }
}
