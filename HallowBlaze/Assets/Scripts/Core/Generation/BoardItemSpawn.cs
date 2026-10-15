using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Generation
{
    /// <summary>
    /// Describes one initial generic item instance without carried inventory or use-effect state.
    /// Its entity supplies position; its definition ID is the entity's content ID.
    /// </summary>
    public sealed class BoardItemSpawn
    {
        /// <summary>Creates a textual instance identity and optional fixed-source binding.</summary>
        /// <param name="instanceId">Stable board-local spawn identity, qualified by the blueprint's full BoardAddress.</param>
        /// <param name="entityId">The unique matching non-exit item-layer entity; exit markers are not collectible items.</param>
        /// <param name="itemDefinitionId">Stable generic item definition ID, not a prefab name.</param>
        /// <param name="resourceFamilyId">Stable resource category used for initial count aggregation.</param>
        /// <param name="sourceId">Stable authored fixed-source ID, or null for a dynamic spawn.</param>
        /// <param name="initiallyObservable">Whether this item is observable at initial presentation; hidden items must not leak into atlas observations.</param>
        /// <exception cref="System.ArgumentException">A required ID, or a supplied source ID, is invalid.</exception>
        public BoardItemSpawn(string instanceId, EntityId entityId, string itemDefinitionId,
            string resourceFamilyId, string sourceId = null, bool initiallyObservable = true)
        {
            GenerationContract.RequireId(instanceId, nameof(instanceId));
            GenerationContract.RequireId(itemDefinitionId, nameof(itemDefinitionId));
            GenerationContract.RequireId(resourceFamilyId, nameof(resourceFamilyId));
            if (sourceId != null) GenerationContract.RequireId(sourceId, nameof(sourceId));
            InstanceId = instanceId;
            EntityId = entityId;
            ItemDefinitionId = itemDefinitionId;
            ResourceFamilyId = resourceFamilyId;
            SourceId = sourceId;
            InitiallyObservable = initiallyObservable;
        }

        /// <summary>Gets the board-local textual instance ID; complete identity includes BoardAddress.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the matching item-layer entity ID, never a Unity InstanceID.</summary>
        public EntityId EntityId { get; }
        /// <summary>Gets the immutable item definition key.</summary>
        public string ItemDefinitionId { get; }
        /// <summary>Gets the immutable resource-family key.</summary>
        public string ResourceFamilyId { get; }
        /// <summary>Gets the authored source key, or null for dynamic content.</summary>
        public string SourceId { get; }
        /// <summary>Gets whether the item can contribute to the initial observable supply.</summary>
        public bool InitiallyObservable { get; }
    }

    /// <summary>Reports counts derived from initial item spawns, never mutable resource balances.</summary>
    public sealed class InitialResourceManifestEntry
    {
        internal InitialResourceManifestEntry(string resourceFamilyId, int itemCount, int observableItemCount)
        {
            ResourceFamilyId = resourceFamilyId;
            ItemCount = itemCount;
            ObservableItemCount = observableItemCount;
        }

        /// <summary>Gets the stable resource family.</summary>
        public string ResourceFamilyId { get; }
        /// <summary>Gets the number of initial item instances, including hidden content, not their use-effect magnitude.</summary>
        public int ItemCount { get; }
        /// <summary>Gets the number of initially observable item instances; later reveals belong to future observation logic.</summary>
        public int ObservableItemCount { get; }
    }
}