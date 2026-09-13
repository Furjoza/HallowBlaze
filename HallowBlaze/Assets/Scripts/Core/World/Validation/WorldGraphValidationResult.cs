using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HallowBlaze.Core.World.Validation
{
    /// <summary>
    /// Exposes the immutable outcome of one complete macrograph validation pass.
    /// </summary>
    public sealed class WorldGraphValidationResult
    {
        private readonly ReadOnlyCollection<WorldGraphValidationDiagnostic> diagnostics;

        internal WorldGraphValidationResult(IEnumerable<WorldGraphValidationDiagnostic> diagnostics)
        {
            if (diagnostics == null)
                throw new ArgumentNullException(nameof(diagnostics));

            this.diagnostics = new List<WorldGraphValidationDiagnostic>(diagnostics).AsReadOnly();
        }

        /// <summary>
        /// Gets whether validation completed without any diagnostics.
        /// </summary>
        public bool IsValid => diagnostics.Count == 0;

        /// <summary>
        /// Gets diagnostics in deterministic code, subject, and reason order.
        /// </summary>
        public IReadOnlyList<WorldGraphValidationDiagnostic> Diagnostics => diagnostics;
    }
}