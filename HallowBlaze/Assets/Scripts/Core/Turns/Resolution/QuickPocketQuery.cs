using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>An immutable pocket selection; observing or replacing the selection is free.</summary>
    public sealed class QuickPocketOption
    {
        internal QuickPocketOption(GridPosition position, int revision, QuickItemUse item,
            ConsumableEffectDefinition effect, ResourceGain gain)
        {
            PlayerPosition = position; Revision = revision; Item = item; Effect = effect;
            Applied = gain.Applied; Wasted = gain.Wasted;
        }
        /// <summary>Gets the source cell to which this selection is bound.</summary>
        public GridPosition PlayerPosition { get; }
        /// <summary>Gets the observed committed inventory revision.</summary>
        public int Revision { get; }
        /// <summary>Gets the exact selected pocket identity.</summary>
        public QuickItemUse Item { get; }
        /// <summary>Gets the immutable trusted restoration definition.</summary>
        public ConsumableEffectDefinition Effect { get; }
        /// <summary>Gets projected applied restoration at the observed resource value.</summary>
        public int Applied { get; }
        /// <summary>Gets projected excess restoration at the observed cap.</summary>
        public int Wasted { get; }
        /// <summary>Captures one proposal; all pocket and primary preconditions are revalidated by the resolver.</summary>
        public QuickPocketCommand CreateCommand(PlayerCommand primary) =>
            new QuickPocketCommand(primary, PlayerPosition, Revision, new[] { Item });

        /// <summary>Projects only removal of this still-current pocket item for placement preview; the authoritative inventory never changes.</summary>
        /// <returns>False when revision, identity, definition or pocket ownership no longer matches.</returns>
        public bool TryProjectInventory(RunState run, out InventoryState projected)
        {
            projected = null;
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (run.InventoryRevision != Revision || !run.Inventory.TryGetPlacement(Item.InstanceId, out InventoryPlacement owned) ||
                owned.Item.DefinitionId != Item.DefinitionId ||
                owned.Container != (Item.PocketId == InventoryLayoutEntry.LeftPocket ? InventoryContainer.LeftPocket : InventoryContainer.RightPocket)) return false;
            var placements = new List<InventoryPlacement>();
            foreach (InventoryPlacement placement in run.Inventory.Placements)
                if (placement.Item.InstanceId != Item.InstanceId) placements.Add(placement);
            return run.InventoryContent.TryCreateInventory(placements, out projected, out _);
        }
    }

    /// <summary>Reads trusted pocket ownership and command compatibility without spending an item or changing the run.</summary>
    public sealed class QuickPocketQuery
    {
        /// <summary>Gets whether a primary contract admits one pocket modifier; nested quick/bag/unknown commands never do.</summary>
        public static bool Supports(PlayerCommand primary)
        {
            if (primary is GroundPickupCommand pickup)
                return pickup.Primary is MoveCommand || pickup.Primary is InteractCommand || pickup.Primary is UseToolCommand;
            return primary is MoveCommand || primary is WaitCommand || primary is InteractCommand ||
                primary is UseToolCommand || primary is PickupToolCommand;
        }

        /// <summary>Returns the pocket's exact use preview, or null for empty/ineligible/missing content or an inactive/exhausted run.</summary>
        public QuickPocketOption GetOption(BoardState board, RunState run, EntityId playerId, string pocketId)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (run.Status != RunStatus.Active || run.Health <= 0 || run.Satiety <= 0 || run.InventoryRevision == int.MaxValue ||
                !board.TryGetEntity(playerId, out BoardEntityState player) || player.Definition.Layer != BoardLayer.Actor ||
                !player.Definition.Kind.Equals(EntityKind.Player)) return null;
            InventoryContainer pocket;
            if (pocketId == InventoryLayoutEntry.LeftPocket) pocket = InventoryContainer.LeftPocket;
            else if (pocketId == InventoryLayoutEntry.RightPocket) pocket = InventoryContainer.RightPocket;
            else return null;
            ItemInstanceState item = run.Inventory.GetPocketItem(pocket);
            if (item == null || !run.InventoryContent.Items.TryGetDefinition(item.DefinitionId, out ItemDefinition definition) ||
                !definition.QuickPocketEligible || definition.Width != 1 || definition.Height > 3 ||
                !run.InventoryContent.Effects.TryGetDefinition(definition.EffectId, out ConsumableEffectDefinition effect)) return null;
            bool health = effect.Resource == ConsumableResource.Health;
            var gain = new ResourceGain(health ? run.Health : run.Satiety, effect.Amount,
                health ? run.ResourceRules.MaximumHealth : run.ResourceRules.MaximumSatiety);
            return new QuickPocketOption(player.Position, run.InventoryRevision,
                new QuickItemUse(pocketId, item.InstanceId, item.DefinitionId), effect, gain);
        }
    }
}