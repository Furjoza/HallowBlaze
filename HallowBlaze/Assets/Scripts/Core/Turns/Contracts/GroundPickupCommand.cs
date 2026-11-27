using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>The conscious decision completing one movement or item-producing interaction.</summary>
    public enum GroundPickupChoice
    {
        /// <summary>Remove the exact ground item and apply its trusted effect before the selected cost.</summary>
        UseNow,
        /// <summary>Move the same instance to one explicit free bag/pocket placement without rearranging other items.</summary>
        Store,
        /// <summary>Finish the primary action while leaving the same instance on the ground.</summary>
        Leave
    }

    /// <summary>One atomic primary intent and pickup decision; a modal never commits half a movement or excavation.</summary>
    public sealed class GroundPickupCommand : PlayerCommand
    {
        /// <summary>Captures complete optimistic preconditions; malformed or stale values reject before effects.</summary>
        public GroundPickupCommand(PlayerCommand primary, GridPosition playerPosition, int expectedRevision,
            EntityId entityId, string instanceId, string definitionId, GridPosition itemPosition,
            GroundPickupChoice choice, InventoryLayoutEntry placement = null, int expectedPickupRevision = 0)
        {
            Primary = primary; PlayerPosition = playerPosition; ExpectedRevision = expectedRevision;
            EntityId = entityId; InstanceId = instanceId; DefinitionId = definitionId; ItemPosition = itemPosition;
            Choice = choice; Placement = placement;
            ExpectedPickupRevision = expectedPickupRevision;
        }
        /// <inheritdoc />
        public override string CommandType => "GroundPickup";
        /// <summary>Gets the single movement/interaction/tool intent; nested composites and bag commands are unsupported.</summary>
        public PlayerCommand Primary { get; }
        /// <summary>Gets the observed source position, preventing repeated Leave from becoming another movement.</summary>
        public GridPosition PlayerPosition { get; }
        /// <summary>Gets the inventory revision observed before the decision.</summary>
        public int ExpectedRevision { get; }
        /// <summary>Gets the transient board pickup sequence observed before selection, preventing replay after Leave/reentry.</summary>
        public int ExpectedPickupRevision { get; }
        /// <summary>Gets the exact ground entity (or source replaced by a cache reward).</summary>
        public EntityId EntityId { get; }
        /// <summary>Gets the exact stable instance transferred, never allocated during confirmation.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the trusted definition reference observed during preview.</summary>
        public string DefinitionId { get; }
        /// <summary>Gets the item's observed position.</summary>
        public GridPosition ItemPosition { get; }
        /// <summary>Gets Use now, Store or Leave; unknown values reject.</summary>
        public GroundPickupChoice Choice { get; }
        /// <summary>Gets the sole proposed Store placement; null for Use now and Leave.</summary>
        public InventoryLayoutEntry Placement { get; }
    }

    /// <summary>Reports one explicit decision and exact ground/carried identity without owning transfer logic.</summary>
    public sealed class GroundPickupResolvedEvent : GameEvent
    {
        /// <summary>Captures an accepted composite decision; applied/wasted amounts are zero for Store and Leave.</summary>
        public GroundPickupResolvedEvent(EntityId playerId, GroundPickupCommand command, string resourceId = null,
            int applied = 0, int wasted = 0)
        { PlayerId = playerId; Command = command; ResourceId = resourceId; Applied = applied; Wasted = wasted; }
        /// <inheritdoc />
        public override string EventType => "GroundPickupResolved";
        /// <summary>Gets the board-local recipient.</summary>
        public EntityId PlayerId { get; }
        /// <summary>Gets the accepted decision and immutable snapshots.</summary>
        public GroundPickupCommand Command { get; }
        /// <summary>Gets health/satiety only for Use now.</summary>
        public string ResourceId { get; }
        /// <summary>Gets actual applied restoration, excluding cost and later damage.</summary>
        public int Applied { get; }
        /// <summary>Gets excess restoration discarded at the resource cap.</summary>
        public int Wasted { get; }
    }
}