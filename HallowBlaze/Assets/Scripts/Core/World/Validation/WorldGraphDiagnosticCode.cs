namespace HallowBlaze.Core.World.Validation
{
    /// <summary>
    /// Identifies a stable macrograph validation rule independently of its readable reason.
    /// </summary>
    public enum WorldGraphDiagnosticCode
    {
        DuplicateNodeId,
        DuplicateEdgeId,
        MissingStartNodeId,
        UnresolvedStartNodeId,
        MissingGoalNodeId,
        UnresolvedGoalNodeId,
        MissingEdgeSource,
        MissingEdgeDestination,
        UnreachableNode,
        UnexpectedDeadEnd,
        DistanceLayerMismatch,
        GoalDayMismatch
    }
}