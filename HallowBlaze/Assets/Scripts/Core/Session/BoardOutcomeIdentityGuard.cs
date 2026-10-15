using System;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Session
{
    /// <summary>Checks full current-state identity before any lifecycle or persistence effect.</summary>
    public static class BoardOutcomeIdentityGuard
    {
        /// <summary>Validates a request against the active run; checkpoint and segment modes never fall back.</summary>
        /// <param name="diagnostic">Stable failure prefix and reproducible address context, or null on success.</param>
        /// <returns>Whether the request matches the current run-owned boundary without mutation.</returns>
        public static bool ValidateRequest(BoardRequest request, RunState run, out string diagnostic)
        {
            diagnostic = "InvalidBoardIdentity:" + (request?.Address?.ToString() ?? "legacy-checkpoint");
            if (request == null || run == null || run.Status != RunStatus.Active ||
                request.RunId != run.RunId || request.RunSeed != run.RunSeed || request.CurrentDay != run.CurrentDay ||
                request.WorldNodeId != run.WorldNodeId)
                return false;
            if (run.ActiveLeg == null)
            {
                if (request.Address != null || request.BoardSeed != run.GetBoardSeed())
                    return false;
            }
            else if (request.Address == null || !request.Address.Equals(BoardAddressFactory.CreateFromState(run)))
                return false;
            diagnostic = null;
            return true;
        }

        /// <summary>Validates the originating request against both the expected board and current run.</summary>
        /// <returns>False with no effects for foreign fields, modes, cursor or traversal.</returns>
        public static bool ValidateOutcome(BoardOutcome outcome, BoardRequest expected, RunState run, out string diagnostic)
        {
            diagnostic = "ForeignBoardOutcome:" + (outcome?.Request.Address?.ToString() ?? "legacy-checkpoint");
            if (outcome == null || expected == null || !expected.HasSameIdentity(outcome.Request))
                return false;
            return ValidateRequest(outcome.Request, run, out diagnostic);
        }
    }
}