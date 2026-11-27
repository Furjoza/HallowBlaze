using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.State
{
    /// <summary>Immutable item/effect resolution context; save files never define their own trusted content.</summary>
    public sealed class InventoryContent
    {
        /// <summary>Gets the empty initial publication; carried content is opt-in until the item integration cards.</summary>
        public static InventoryContent Empty { get; } = new InventoryContent(
            new ItemCatalog(Array.Empty<ItemDefinition>()), new ConsumableEffectCatalog(Array.Empty<ConsumableEffectDefinition>()));

        /// <summary>Combines immutable catalogues without granting items or executing effects.</summary>
        /// <exception cref="ArgumentNullException">Either catalogue is null.</exception>
        public InventoryContent(ItemCatalog items, ConsumableEffectCatalog effects)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        /// <summary>Gets trusted fixed-footprint item definitions, separate from run-local instances.</summary>
        public ItemCatalog Items { get; }
        /// <summary>Gets trusted required effects; unknown references reject rather than dropping items.</summary>
        public ConsumableEffectCatalog Effects { get; }

        /// <summary>Validates geometry, unique ownership and all effects required by the proposed carried items.</summary>
        /// <param name="placements">The complete proposed layout; no mutable collection is retained.</param>
        /// <param name="inventory">An immutable validated snapshot, or null on rejection.</param>
        /// <param name="diagnostic">The first rejected geometry, identity or effect rule, or no error.</param>
        /// <returns>Whether the complete snapshot can be committed or restored without substitution.</returns>
        public bool TryCreateInventory(IEnumerable<InventoryPlacement> placements, out InventoryState inventory,
            out InventoryDiagnostic diagnostic)
        {
            if (!InventoryState.TryCreate(Items, placements, out InventoryState candidate, out diagnostic))
            {
                inventory = null;
                return false;
            }
            foreach (InventoryPlacement placement in candidate.Placements)
            {
                Items.TryGetDefinition(placement.Item.DefinitionId, out ItemDefinition definition);
                if (!Effects.TryGetDefinition(definition.EffectId, out _))
                {
                    inventory = null;
                    diagnostic = new InventoryDiagnostic(InventoryErrorCode.UnknownEffectId,
                        placement.Item.InstanceId, placement.Item.DefinitionId);
                    return false;
                }
            }
            inventory = candidate;
            return true;
        }
    }
}