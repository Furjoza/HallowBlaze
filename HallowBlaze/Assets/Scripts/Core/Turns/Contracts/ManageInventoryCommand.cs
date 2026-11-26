using System.Collections.Generic;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>The two admitted item actions; rearranging remaining items does not consume this budget.</summary>
    public enum InventoryItemActionKind
    {
        /// <summary>Consumes exactly one carried item and applies its trusted effect before the turn cost.</summary>
        Use,
        /// <summary>Destroys exactly one carried item without creating a ground entity or applying an effect.</summary>
        Discard
    }

    /// <summary>One ordered action on an existing carried identity, not on a definition or a board entity.</summary>
    public sealed class InventoryItemAction
    {
        /// <summary>Captures an intent; the resolver validates kind, identity, duplication and the complete proposal.</summary>
        public InventoryItemAction(InventoryItemActionKind kind, string instanceId) { Kind = kind; InstanceId = instanceId; }
        /// <summary>Gets the requested action; unsupported enum values reject without effects.</summary>
        public InventoryItemActionKind Kind { get; }
        /// <summary>Gets the exact carried identity; no alternate item is selected on rejection.</summary>
        public string InstanceId { get; }
    }

    /// <summary>A proposed owner and anchor, never a trusted item definition or mutable inventory snapshot.</summary>
    public sealed class InventoryLayoutEntry
    {
        /// <summary>Stable main-grid destination.</summary>
        public const string MainGrid = "main-grid";
        /// <summary>Stable left-pocket destination.</summary>
        public const string LeftPocket = "left-pocket";
        /// <summary>Stable right-pocket destination.</summary>
        public const string RightPocket = "right-pocket";
        /// <summary>Captures one proposed remaining item location; invalid references/geometry reject at resolution.</summary>
        public InventoryLayoutEntry(string instanceId, string containerId, int column = 0, int row = 0)
        { InstanceId = instanceId; ContainerId = containerId; Column = column; Row = row; }
        /// <summary>Gets the carried instance reference, not a client-supplied definition.</summary>
        public string InstanceId { get; }
        /// <summary>Gets one of the three stable destination IDs.</summary>
        public string ContainerId { get; }
        /// <summary>Gets the horizontal grid anchor; pocket anchors must be zero.</summary>
        public int Column { get; }
        /// <summary>Gets the vertical grid anchor; pocket anchors must be zero.</summary>
        public int Row { get; }
    }

    /// <summary>One immutable complete bag proposal: zero to two ordered actions and the exact layout of all survivors.</summary>
    public sealed class ManageInventoryCommand : PlayerCommand
    {
        /// <summary>Copies both sequences; malformed/null payloads remain representable for cost-free resolver rejection.</summary>
        /// <param name="expectedRevision">The committed revision observed when the draft opened.</param>
        /// <param name="actions">Ordered Use/Discard actions; each instance may appear only once.</param>
        /// <param name="finalLayout">Exactly every remaining identity with one final owner/anchor.</param>
        public ManageInventoryCommand(int expectedRevision, IEnumerable<InventoryItemAction> actions,
            IEnumerable<InventoryLayoutEntry> finalLayout)
        {
            ExpectedRevision = expectedRevision;
            Actions = actions == null ? null : new List<InventoryItemAction>(actions).AsReadOnly();
            FinalLayout = finalLayout == null ? null : new List<InventoryLayoutEntry>(finalLayout).AsReadOnly();
        }
        /// <summary>Gets the inventory intent tag; quick-use modifiers are not part of this payload.</summary>
        public override string CommandType => "ManageInventory";
        /// <summary>Gets the revision that must still match; accepted no-op finishes also invalidate it.</summary>
        public int ExpectedRevision { get; }
        /// <summary>Gets detached ordered actions, or null for a malformed proposal.</summary>
        public IReadOnlyList<InventoryItemAction> Actions { get; }
        /// <summary>Gets detached final ownership, or null for a malformed proposal.</summary>
        public IReadOnlyList<InventoryLayoutEntry> FinalLayout { get; }
    }
}