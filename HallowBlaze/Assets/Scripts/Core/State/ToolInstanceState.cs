using System;

namespace HallowBlaze.Core.State
{
    /// <summary>Immutable run-local tool identity and charges; it has no Unity or atlas-source ownership.</summary>
    public sealed class ToolInstanceState
    {
        /// <summary>Materializes a detached instance, including an unknown definition retained by a save.</summary>
        /// <param name="instanceId">Run-local instance ID; the run owns allocation and duplicate prevention.</param>
        /// <param name="definitionId">Stable content ID, never a source or instance ID.</param>
        /// <param name="charges">Nonnegative remaining uses; unknown content is retained without substitution.</param>
        /// <exception cref="ArgumentException">An ID is empty or has surrounding whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Charges are negative.</exception>
        public ToolInstanceState(string instanceId, string definitionId, int charges)
        {
            ToolDefinition.RequireId(instanceId, nameof(instanceId));
            ToolDefinition.RequireId(definitionId, nameof(definitionId));
            if (charges < 0) throw new ArgumentOutOfRangeException(nameof(charges));
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Charges = charges;
        }

        /// <summary>Gets this run-local identity, unchanged by charge spending or slot replacement.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the referenced immutable content, distinct from an eventual authored source ID.</summary>
        public string DefinitionId { get; }
        /// <summary>Gets remaining uses; zero is a valid depleted instance, not an empty slot.</summary>
        public int Charges { get; }

        /// <summary>Creates the next immutable charge snapshot without modifying this instance or any run.</summary>
        /// <param name="next">One fewer charge on success; this same unchanged instance at zero.</param>
        /// <returns>False at zero; no action, turn, food or noise is resolved by this model operation.</returns>
        public bool TrySpendCharge(out ToolInstanceState next)
        {
            next = Charges == 0 ? this : new ToolInstanceState(InstanceId, DefinitionId, Charges - 1);
            return Charges > 0;
        }
    }
}