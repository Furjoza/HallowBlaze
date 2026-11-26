using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>A transient bag proposal, separate from authoritative run state and never serialized into a save.</summary>
    public sealed class InventoryDraft
    {
        private readonly InventoryContent content;
        private readonly List<InventoryItemAction> actions = new List<InventoryItemAction>();

        /// <summary>Captures the observed revision and immutable layout without applying effects or paying a turn.</summary>
        /// <exception cref="ArgumentNullException">The run is null.</exception>
        public InventoryDraft(RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            ExpectedRevision = run.InventoryRevision;
            Layout = run.Inventory;
            content = run.InventoryContent;
            Actions = actions.AsReadOnly();
        }

        /// <summary>Gets the revision captured on opening; only the resolver may accept it.</summary>
        public int ExpectedRevision { get; }
        /// <summary>Gets the current immutable proposal of surviving items, not a run-owned mutable collection.</summary>
        public InventoryState Layout { get; private set; }
        /// <summary>Gets at most two staged ordered actions; staging never executes an effect.</summary>
        public IReadOnlyList<InventoryItemAction> Actions { get; }

        /// <summary>Stages one Use or Discard and removes its identity only from this draft.</summary>
        /// <returns>False without draft changes for a third, duplicate, missing or unsupported action.</returns>
        public bool TryStage(InventoryItemActionKind kind, string instanceId)
        {
            if (actions.Count == 2 || kind != InventoryItemActionKind.Use && kind != InventoryItemActionKind.Discard ||
                !Layout.TryGetPlacement(instanceId, out _)) return false;
            if (!content.TryCreateInventory(Layout.Placements.Where(value => value.Item.InstanceId != instanceId),
                out InventoryState next, out _)) return false;
            actions.Add(new InventoryItemAction(kind, instanceId));
            Layout = next;
            return true;
        }

        /// <summary>Proposes a complete rearrangement of all survivors without consuming the two-action budget.</summary>
        /// <returns>False with the same draft layout when identity, geometry or pocket compatibility is invalid.</returns>
        public bool TryRepack(IEnumerable<InventoryPlacement> placements)
        {
            if (!Layout.TryRepack(placements, out InventoryState next, out _)) return false;
            Layout = next;
            return true;
        }

        /// <summary>Copies a complete payload; it is still subject to live revision, content and state validation.</summary>
        /// <returns>An immutable proposal with only instance references, action kinds and final owners/anchors.</returns>
        public ManageInventoryCommand CreateCommand()
        {
            return new ManageInventoryCommand(ExpectedRevision, actions, Layout.Placements.Select(value =>
                new InventoryLayoutEntry(value.Item.InstanceId, value.Container == InventoryContainer.MainGrid
                    ? InventoryLayoutEntry.MainGrid : value.Container == InventoryContainer.LeftPocket
                        ? InventoryLayoutEntry.LeftPocket : InventoryLayoutEntry.RightPocket, value.Column, value.Row)));
        }
    }
}