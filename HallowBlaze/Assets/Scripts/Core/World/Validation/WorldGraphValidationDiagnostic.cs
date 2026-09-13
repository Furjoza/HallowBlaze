namespace HallowBlaze.Core.World.Validation
{
    /// <summary>
    /// Describes one stable validation failure and the graph subject that caused it.
    /// </summary>
    public sealed class WorldGraphValidationDiagnostic
    {
        internal WorldGraphValidationDiagnostic(
            WorldGraphDiagnosticCode code,
            string subject,
            string reason)
        {
            Code = code;
            Subject = subject;
            Reason = reason;
        }

        /// <summary>
        /// Gets the stable machine-readable rule code.
        /// </summary>
        public WorldGraphDiagnosticCode Code { get; }

        /// <summary>
        /// Gets the stable ID or definition field associated with the failure.
        /// </summary>
        public string Subject { get; }

        /// <summary>
        /// Gets the readable explanation of the failure.
        /// </summary>
        public string Reason { get; }
    }
}