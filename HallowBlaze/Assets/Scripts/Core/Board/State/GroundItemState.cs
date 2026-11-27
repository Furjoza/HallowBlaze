using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>Immutable board-owned consumable identity; position belongs to the entity and trusted effects belong to run content.</summary>
    public sealed class GroundItemState
    {
        /// <summary>Binds stable item and definition identities to one board-local entity without granting or consuming it.</summary>
        /// <exception cref="ArgumentException">An item or definition ID is empty or padded.</exception>
        public GroundItemState(EntityId entityId, string instanceId, string definitionId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || instanceId != instanceId.Trim())
                throw new ArgumentException("A stable item identity is required.", nameof(instanceId));
            if (string.IsNullOrWhiteSpace(definitionId) || definitionId != definitionId.Trim())
                throw new ArgumentException("A stable definition identity is required.", nameof(definitionId));
            EntityId = entityId; InstanceId = instanceId; DefinitionId = definitionId;
        }
        /// <summary>Gets the exact ground entity, never a Unity instance ID.</summary>
        public EntityId EntityId { get; }
        /// <summary>Gets the unchanged identity transferred into carried ownership by Store.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the trusted consumable definition reference, not a resource reward.</summary>
        public string DefinitionId { get; }
    }

    public sealed partial class BoardState
    {
        private readonly Dictionary<EntityId, GroundItemState> groundItems = new Dictionary<EntityId, GroundItemState>();
        /// <summary>Gets the transient accepted-pickup sequence; old decisions cannot replay after Leave and later reentry.</summary>
        public int PickupRevision { get; private set; }

        /// <summary>Advances one prevalidated pickup after primary success; stale or exhausted revisions reject without mutation.</summary>
        public bool TryCommitPickupRevision(int expectedRevision)
        {
            if (expectedRevision != PickupRevision || PickupRevision == int.MaxValue) return false;
            PickupRevision++;
            return true;
        }

        /// <summary>Binds one authored item to a stable consumable; binding cannot replace tools or an existing identity.</summary>
        /// <returns>False without mutation for absent/non-item entities, tools or duplicate bindings/identities.</returns>
        public bool TryBindGroundItem(GroundItemState item)
        {
            if (item == null || !TryGetEntity(item.EntityId, out BoardEntityState entity) ||
                entity.Definition.Layer != BoardLayer.Item || !entity.Definition.Kind.Equals(EntityKind.Item) ||
                entity.Definition.Traits.IsExit || entity.Definition.ContentId != item.DefinitionId ||
                groundTools.ContainsKey(item.EntityId) || groundItems.ContainsKey(item.EntityId))
                return false;
            foreach (GroundItemState owned in groundItems.Values)
                if (owned.InstanceId == item.InstanceId) return false;
            groundItems.Add(item.EntityId, item);
            return true;
        }

        /// <summary>Reads the exact immutable ground instance without allocation, resource effects or transfer.</summary>
        public bool TryGetGroundItem(EntityId entityId, out GroundItemState item) => groundItems.TryGetValue(entityId, out item);
    }
}