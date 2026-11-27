using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>A read-only exact pickup preview; callers must choose placement rather than accepting an automatic best fit.</summary>
    public sealed class GroundPickupOption
    {
        internal GroundPickupOption(PlayerCommand primary, GridPosition playerPosition, int revision,
            GroundItemState item, GridPosition position, RunState run, int pickupRevision, InventoryState availableInventory = null)
        {
            Primary = primary; PlayerPosition = playerPosition; Revision = revision; Item = item; Position = position;
            PickupRevision = pickupRevision;
            UseAvailable = !run.Inventory.TryGetPlacement(item.InstanceId, out _) &&
                run.InventoryContent.Items.TryGetDefinition(item.DefinitionId, out ItemDefinition definition) &&
                run.InventoryContent.Effects.TryGetDefinition(definition.EffectId, out _);
            CanStore = UseAvailable && run.InventoryRevision < int.MaxValue && HasPlacement(availableInventory ?? run.Inventory, item);
            UseUnavailableReason = UseAvailable ? null : "Missing content or duplicate ownership";
            StoreUnavailableReason = CanStore ? null : !UseAvailable ? UseUnavailableReason :
                run.InventoryRevision == int.MaxValue ? "Inventory revision exhausted" : "No legal placement";
        }
        /// <summary>Gets the uncommitted primary intent.</summary>
        public PlayerCommand Primary { get; }
        /// <summary>Gets the observed player source position.</summary>
        public GridPosition PlayerPosition { get; }
        /// <summary>Gets the observed inventory revision.</summary>
        public int Revision { get; }
        /// <summary>Gets the transient board pickup sequence; it changes even when the decision leaves the item untouched.</summary>
        public int PickupRevision { get; }
        /// <summary>Gets the exact immutable existing or predicted cache reward identity.</summary>
        public GroundItemState Item { get; }
        /// <summary>Gets the target's position.</summary>
        public GridPosition Position { get; }
        /// <summary>Gets whether its required trusted effect exists; bag capacity does not restrict use.</summary>
        public bool UseAvailable { get; }
        /// <summary>Gets whether at least one legal placement exists, without selecting or moving an item.</summary>
        public bool CanStore { get; }
        /// <summary>Gets a disabled Use reason, or null when legal; capacity never disables Use.</summary>
        public string UseUnavailableReason { get; }
        /// <summary>Gets a disabled Store reason, or null when at least one placement is legal.</summary>
        public string StoreUnavailableReason { get; }
        /// <summary>Creates a complete proposal; live state is revalidated at resolution.</summary>
        public GroundPickupCommand CreateCommand(GroundPickupChoice choice, InventoryLayoutEntry placement = null) =>
            new GroundPickupCommand(Primary, PlayerPosition, Revision, Item.EntityId, Item.InstanceId, Item.DefinitionId, Position, choice, placement, PickupRevision);

        private static bool HasPlacement(InventoryState inventory, GroundItemState item)
        {
            var instance = new ItemInstanceState(item.InstanceId, item.DefinitionId);
            for (int column = 0; column < InventoryState.MainGridWidth; column++)
                for (int row = 0; row < InventoryState.MainGridHeight; row++)
                    if (inventory.TryPlace(new InventoryPlacement(instance, InventoryContainer.MainGrid, column, row), out _, out _)) return true;
            return inventory.TryPlace(new InventoryPlacement(instance, InventoryContainer.LeftPocket), out _, out _) ||
                inventory.TryPlace(new InventoryPlacement(instance, InventoryContainer.RightPocket), out _, out _);
        }
    }

    /// <summary>Discovers entry decisions without moving actors, resolving effects, allocating identities or changing RNG.</summary>
    public sealed class GroundPickupQuery
    {
        private readonly InteractionQuery interactions;
        private readonly ToolUseQuery tools;

        /// <summary>Shares the resolver's interaction and tool dispatch, including cache rules.</summary>
        public GroundPickupQuery(InteractionQuery interactions = null, ToolUseQuery tools = null)
        { this.interactions = interactions ?? new InteractionQuery(); this.tools = tools ?? new ToolUseQuery(); }

        /// <summary>Reads entry or final cache-production decisions without mutation; standing, Wait and legacy consumable interactions never prompt.</summary>
        /// <param name="board">The authoritative current board.</param>
        /// <param name="run">The authoritative current run and trusted content.</param>
        /// <param name="playerId">The acting player.</param>
        /// <param name="primary">The uncommitted movement, interaction or tool intent.</param>
        /// <param name="availableInventory">Optional detached post-quick-use layout for capacity preview only; resolution validates its own complete proposal.</param>
        /// <returns>Null unless the validated movement enters a bound consumable or an interaction produces a new concrete cache reward.</returns>
        public GroundPickupOption GetOption(BoardState board, RunState run, EntityId playerId, PlayerCommand primary, InventoryState availableInventory = null)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (run.Status != RunStatus.Active || run.Health <= 0 || run.Satiety <= 0 ||
                !board.TryGetEntity(playerId, out BoardEntityState player)) return null;
            BoardEntityState target;
            IInteractionAction action;
            if (primary is MoveCommand move)
            {
                GridPosition destination;
                try { destination = player.Position.Move(move.Direction); }
                catch (OverflowException) { return null; }
                if (!board.Bounds.Contains(destination) || !board.TryGetEntity(BoardLayer.Terrain, destination, out BoardEntityState terrain) ||
                    !terrain.Definition.Traits.IsWalkable || board.TryGetEntity(BoardLayer.Actor, destination, out _) ||
                    board.TryGetEntity(BoardLayer.Obstacle, destination, out BoardEntityState obstacle) && !obstacle.Definition.Traits.IsWalkable)
                    return null;
                if (!board.TryGetEntity(BoardLayer.Item, destination, out target) ||
                    !board.TryGetGroundItem(target.Id, out GroundItemState item)) return null;
                return new GroundPickupOption(primary, player.Position, run.InventoryRevision, item, destination, run, board.PickupRevision, availableInventory);
            }
            if (primary is InteractCommand interact)
            {
                if (interactions.Validate(board, run, playerId, interact, out target, out action) != InteractionAvailability.Available) return null;
            }
            else if (primary is UseToolCommand use)
            {
                if (tools.Validate(board, run, playerId, use, out target, out ToolAction tool) != CommandRejectionCode.None) return null;
                action = tool.Behavior;
            }
            else return null;
            if (!(action is CacheDigAction cache) || !cache.TryPreviewReward(board, run, target, out GroundItemState ground)) return null;
            return new GroundPickupOption(primary, player.Position, run.InventoryRevision, ground, target.Position, run, board.PickupRevision, availableInventory);
        }
    }

    /// <summary>Reconstructs stable run/boundary-local identities without an allocator, RNG or Unity references.</summary>
    public static class GroundItemIdentity
    {
        /// <summary>Returns the same identity at the same saved entry, and a distinct identity on later traversals/segments.</summary>
        public static string Create(RunState run, EntityId entityId) => string.Join(".", run.RunId, "ground",
            run.CompletedDayCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            run.TraversalSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (run.ActiveLeg?.SegmentIndex ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            entityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}