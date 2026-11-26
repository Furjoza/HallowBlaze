namespace HallowBlaze.Core.State
{
    /// <summary>Immutable run-local item identity; shared definitions never own inventory placement.</summary>
    public sealed class ItemInstanceState
    {
        /// <summary>Materializes a detached identity without allocating, placing, or consuming an item.</summary>
        /// <param name="instanceId">Stable run-local ID; the containing inventory rejects duplicates.</param>
        /// <param name="definitionId">Stable content reference; the containing inventory verifies catalogue membership.</param>
        /// <exception cref="InventoryValidationException">An ID is empty or has surrounding whitespace.</exception>
        public ItemInstanceState(string instanceId, string definitionId)
        {
            ItemDefinition.RequireId(instanceId, nameof(instanceId));
            ItemDefinition.RequireId(definitionId, nameof(definitionId));
            InstanceId = instanceId;
            DefinitionId = definitionId;
        }

        /// <summary>Gets the stable identity within its run, distinct from the content ID.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the referenced shared content ID.</summary>
        public string DefinitionId { get; }
    }
}