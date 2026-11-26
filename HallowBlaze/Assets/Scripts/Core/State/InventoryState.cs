using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.State
{
    /// <summary>Immutable consumable ownership snapshot: a 3x3 bag and two pockets, with no tool slots or turn effects.</summary>
    public sealed class InventoryState
    {
        /// <summary>The initial main bag's horizontal capacity in cells.</summary>
        public const int MainGridWidth = 3;
        /// <summary>The initial main bag's vertical capacity in cells.</summary>
        public const int MainGridHeight = 3;
        /// <summary>The number of independent single-item quick pockets.</summary>
        public const int PocketCount = 2;

        private readonly ItemCatalog catalog;
        private readonly Dictionary<string, InventoryPlacement> byInstanceId;
        private readonly ItemInstanceState[,] cells;
        private readonly ItemInstanceState[] pockets;

        private InventoryState(ItemCatalog catalog, List<InventoryPlacement> placements,
            Dictionary<string, InventoryPlacement> byInstanceId, ItemInstanceState[,] cells,
            ItemInstanceState[] pockets)
        {
            this.catalog = catalog;
            this.byInstanceId = byInstanceId;
            this.cells = cells;
            this.pockets = pockets;
            Placements = placements.AsReadOnly();
        }

        /// <summary>Gets the read-only placements; items, anchors, and the referenced catalogue are immutable.</summary>
        public IReadOnlyList<InventoryPlacement> Placements { get; }

        /// <summary>Creates an independent, empty built-in backpack without modifying a run or allocating item IDs.</summary>
        /// <exception cref="ArgumentNullException">The catalogue is null.</exception>
        public static InventoryState CreateEmpty(ItemCatalog catalog)
        {
            TryCreate(catalog, Array.Empty<InventoryPlacement>(), out InventoryState inventory, out _);
            return inventory;
        }

        /// <summary>Validates a detached full layout atomically; failed construction publishes no partial ownership.</summary>
        /// <param name="catalog">Immutable consumable content; unknown definitions are rejected, not substituted.</param>
        /// <param name="placements">Complete proposed layout, copied once and never retained as a mutable collection.</param>
        /// <param name="inventory">The validated immutable snapshot on success; otherwise null.</param>
        /// <param name="diagnostic">The first rejected invariant, or <see cref="InventoryErrorCode.None"/>.</param>
        /// <returns>Whether every item can occupy its sole requested location.</returns>
        /// <exception cref="ArgumentNullException">The catalogue is null.</exception>
        public static bool TryCreate(ItemCatalog catalog, IEnumerable<InventoryPlacement> placements,
            out InventoryState inventory, out InventoryDiagnostic diagnostic)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            inventory = null;
            if (placements == null)
            {
                diagnostic = new InventoryDiagnostic(InventoryErrorCode.NullLayout);
                return false;
            }

            List<InventoryPlacement> snapshot = new List<InventoryPlacement>();
            Dictionary<string, InventoryPlacement> instances =
                new Dictionary<string, InventoryPlacement>(StringComparer.Ordinal);
            ItemInstanceState[,] cells = new ItemInstanceState[MainGridWidth, MainGridHeight];
            ItemInstanceState[] pockets = new ItemInstanceState[PocketCount];
            foreach (InventoryPlacement placement in placements)
            {
                InventoryErrorCode code = ValidatePlacement(catalog, placement, instances, cells, pockets);
                if (code != InventoryErrorCode.None)
                {
                    diagnostic = new InventoryDiagnostic(code, placement?.Item.InstanceId, placement?.Item.DefinitionId);
                    return false;
                }
                instances.Add(placement.Item.InstanceId, placement);
                snapshot.Add(placement);
            }
            inventory = new InventoryState(catalog, snapshot, instances, cells, pockets);
            diagnostic = new InventoryDiagnostic(InventoryErrorCode.None);
            return true;
        }

        /// <summary>Proposes one additional owned item; rejection returns this exact unchanged snapshot.</summary>
        /// <param name="placement">One new instance and its destination; an existing instance must be repacked instead.</param>
        /// <param name="next">The independent accepted layout, or this inventory when rejected.</param>
        /// <param name="diagnostic">Typed content, identity, or geometry rejection, or no error.</param>
        /// <returns>Whether the new item fits without changing existing ownership.</returns>
        public bool TryPlace(InventoryPlacement placement, out InventoryState next, out InventoryDiagnostic diagnostic)
        {
            List<InventoryPlacement> proposed = new List<InventoryPlacement>(Placements) { placement };
            bool accepted = TryCreate(catalog, proposed, out InventoryState candidate, out diagnostic);
            next = accepted ? candidate : this;
            return accepted;
        }

        /// <summary>Atomically changes locations of exactly the existing instances; it cannot add, consume, or replace items.</summary>
        /// <param name="placements">Complete proposed layout with unchanged instance-to-definition identities.</param>
        /// <param name="next">A new valid snapshot on success; this unchanged snapshot on any rejection.</param>
        /// <param name="diagnostic">Geometry/content error, item-set mismatch, or no error.</param>
        /// <returns>Whether the entire repack is legal; no turn, resource, save, or board is changed.</returns>
        public bool TryRepack(IEnumerable<InventoryPlacement> placements, out InventoryState next,
            out InventoryDiagnostic diagnostic)
        {
            next = this;
            if (!TryCreate(catalog, placements, out InventoryState candidate, out diagnostic)) return false;
            if (candidate.Placements.Count != Placements.Count)
            {
                diagnostic = new InventoryDiagnostic(InventoryErrorCode.ItemSetMismatch);
                return false;
            }
            foreach (InventoryPlacement original in Placements)
            {
                if (!candidate.byInstanceId.TryGetValue(original.Item.InstanceId, out InventoryPlacement proposed) ||
                    proposed.Item.DefinitionId != original.Item.DefinitionId)
                {
                    diagnostic = new InventoryDiagnostic(InventoryErrorCode.ItemSetMismatch,
                        original.Item.InstanceId, original.Item.DefinitionId);
                    return false;
                }
            }
            next = candidate;
            return true;
        }

        /// <summary>Finds the sole owned placement for an exact ordinal instance ID without changing state.</summary>
        /// <returns>False for null or unowned identities; the output is then null.</returns>
        public bool TryGetPlacement(string instanceId, out InventoryPlacement placement)
        {
            placement = null;
            return instanceId != null && byInstanceId.TryGetValue(instanceId, out placement);
        }

        /// <summary>Gets the item covering a main-grid cell, or null for a free cell.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The column or row is outside the 3x3 grid.</exception>
        public ItemInstanceState GetMainGridItem(int column, int row)
        {
            if (column < 0 || column >= MainGridWidth) throw new ArgumentOutOfRangeException(nameof(column));
            if (row < 0 || row >= MainGridHeight) throw new ArgumentOutOfRangeException(nameof(row));
            return cells[column, row];
        }

        /// <summary>Gets the pocket's single item, or null; even a 1x1 item occupies the entire pocket.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The container is not the left or right pocket.</exception>
        public ItemInstanceState GetPocketItem(InventoryContainer pocket)
        {
            if (pocket == InventoryContainer.LeftPocket) return pockets[0];
            if (pocket == InventoryContainer.RightPocket) return pockets[1];
            throw new ArgumentOutOfRangeException(nameof(pocket));
        }

        private static InventoryErrorCode ValidatePlacement(ItemCatalog catalog, InventoryPlacement placement,
            Dictionary<string, InventoryPlacement> instances, ItemInstanceState[,] cells, ItemInstanceState[] pockets)
        {
            if (placement == null) return InventoryErrorCode.NullPlacement;
            ItemInstanceState item = placement.Item;
            if (instances.ContainsKey(item.InstanceId)) return InventoryErrorCode.DuplicateInstanceId;
            if (!catalog.TryGetDefinition(item.DefinitionId, out ItemDefinition definition))
                return InventoryErrorCode.UnknownDefinitionId;
            if (placement.Container == InventoryContainer.MainGrid)
            {
                if (placement.Column < 0 || placement.Row < 0 ||
                    definition.Width > MainGridWidth || definition.Height > MainGridHeight ||
                    placement.Column > MainGridWidth - definition.Width ||
                    placement.Row > MainGridHeight - definition.Height)
                    return InventoryErrorCode.OutOfBounds;
                for (int column = placement.Column; column < placement.Column + definition.Width; column++)
                    for (int row = placement.Row; row < placement.Row + definition.Height; row++)
                        if (cells[column, row] != null) return InventoryErrorCode.Overlap;
                for (int column = placement.Column; column < placement.Column + definition.Width; column++)
                    for (int row = placement.Row; row < placement.Row + definition.Height; row++)
                        cells[column, row] = item;
                return InventoryErrorCode.None;
            }
            if (placement.Container != InventoryContainer.LeftPocket && placement.Container != InventoryContainer.RightPocket)
                return InventoryErrorCode.InvalidContainer;
            if (placement.Column != 0 || placement.Row != 0) return InventoryErrorCode.OutOfBounds;
            if (!definition.QuickPocketEligible || definition.Width != 1 || definition.Height > MainGridHeight)
                return InventoryErrorCode.IncompatiblePocket;
            int pocketIndex = placement.Container == InventoryContainer.LeftPocket ? 0 : 1;
            if (pockets[pocketIndex] != null) return InventoryErrorCode.PocketOccupied;
            pockets[pocketIndex] = item;
            return InventoryErrorCode.None;
        }
    }
}