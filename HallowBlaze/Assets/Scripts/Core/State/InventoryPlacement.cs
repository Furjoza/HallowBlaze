using System;

namespace HallowBlaze.Core.State
{
    /// <summary>Names consumable containers only; the two generic tool slots are not inventory locations.</summary>
    public enum InventoryContainer
    {
        /// <summary>The fixed-orientation rectangular main bag.</summary>
        MainGrid,
        /// <summary>The left, single-item quick pocket.</summary>
        LeftPocket,
        /// <summary>The right, single-item quick pocket.</summary>
        RightPocket
    }

    /// <summary>Immutable proposed or committed ownership of one item at one container-local anchor.</summary>
    public sealed class InventoryPlacement
    {
        /// <summary>Creates placement data; the inventory validates bounds, content, collisions, and uniqueness.</summary>
        /// <param name="item">Immutable run-local identity; never a tool instance.</param>
        /// <param name="container">Main grid or a single-item pocket.</param>
        /// <param name="column">Zero-based horizontal anchor; pockets require zero.</param>
        /// <param name="row">Zero-based vertical anchor; pockets require zero.</param>
        /// <exception cref="ArgumentNullException">The item is null.</exception>
        public InventoryPlacement(ItemInstanceState item, InventoryContainer container, int column = 0, int row = 0)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Container = container;
            Column = column;
            Row = row;
        }

        /// <summary>Gets the immutable item identity owned by this placement.</summary>
        public ItemInstanceState Item { get; }
        /// <summary>Gets the destination container, separate from tool equipment.</summary>
        public InventoryContainer Container { get; }
        /// <summary>Gets the horizontal anchor in cells, with no rotation.</summary>
        public int Column { get; }
        /// <summary>Gets the vertical anchor in cells.</summary>
        public int Row { get; }
    }
}