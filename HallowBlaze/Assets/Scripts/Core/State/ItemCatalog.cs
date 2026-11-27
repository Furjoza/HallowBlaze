using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.State
{
    /// <summary>Validated immutable consumable catalogue, separate from tool equipment and run-local identities.</summary>
    public sealed class ItemCatalog
    {
        private readonly Dictionary<string, ItemDefinition> byId;

        /// <summary>Copies a content sequence and rejects null entries or duplicate ordinal definition IDs.</summary>
        /// <exception cref="ArgumentNullException">The definition sequence is null.</exception>
        /// <exception cref="InventoryValidationException">An entry is null or repeats an existing definition ID.</exception>
        public ItemCatalog(IEnumerable<ItemDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            byId = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            List<ItemDefinition> snapshot = new List<ItemDefinition>();
            foreach (ItemDefinition definition in definitions)
            {
                if (definition == null)
                    throw new InventoryValidationException(
                        new InventoryDiagnostic(InventoryErrorCode.NullDefinition), nameof(definitions));
                if (byId.ContainsKey(definition.Id))
                    throw new InventoryValidationException(
                        new InventoryDiagnostic(InventoryErrorCode.DuplicateDefinitionId, definitionId: definition.Id),
                        nameof(definitions));
                byId.Add(definition.Id, definition);
                snapshot.Add(definition);
            }
            Definitions = snapshot.AsReadOnly();
        }

        /// <summary>Gets the detached, read-only definition sequence; contained definitions are immutable.</summary>
        public IReadOnlyList<ItemDefinition> Definitions { get; }

        /// <summary>Looks up exact ordinal content identity without allocating or substituting unknown items.</summary>
        /// <param name="definitionId">Content ID; null or unknown IDs return false.</param>
        /// <param name="definition">The immutable definition on success; otherwise null.</param>
        /// <returns>Whether the catalogue contains this exact ID.</returns>
        public bool TryGetDefinition(string definitionId, out ItemDefinition definition)
        {
            definition = null;
            return definitionId != null && byId.TryGetValue(definitionId, out definition);
        }
    }
}