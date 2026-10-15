using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Generation
{
    /// <summary>
    /// Owns an immutable initial 8x8 layout, independent of scene objects and runtime mutations.
    /// Structural checks do not prove reachability or tactical fairness.
    /// </summary>
    public sealed class BoardBlueprint : IEquatable<BoardBlueprint>
    {
        private readonly byte[] canonicalData;
        private readonly int deterministicHashCode;

        /// <summary>Creates a detached initial layout with exactly one player and distinct exits.</summary>
        /// <param name="address">Full stable segment identity, including seed and configuration version.</param>
        /// <param name="config">Immutable generation targets matching the address's configuration key/version.</param>
        /// <param name="entities">Initial entities across the four independent occupancy layers.</param>
        /// <param name="items">Exactly one generic spawn descriptor for every non-exit item-layer entity.</param>
        /// <exception cref="ArgumentException">A marker or entity violates bounds, identity, or occupancy.</exception>
        /// <exception cref="ArgumentNullException">A required input is null.</exception>
        public BoardBlueprint(BoardAddress address, BoardGenerationConfig config,
            IEnumerable<BoardEntityState> entities, IEnumerable<BoardItemSpawn> items)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (address.GenerationConfigurationId != config.Id || address.GenerationVersion != config.Version)
                throw new ArgumentException("Address and configuration identity/version must agree.", nameof(config));

            var initialState = new BoardState(Bounds);
            BoardEntityState player = null;
            var exitPositions = new SortedSet<GridPosition>();
            foreach (BoardEntityState entity in entities)
            {
                if (!initialState.TryAdd(entity))
                    throw new ArgumentException("Entities must have unique IDs and in-bounds, unoccupied layer cells.", nameof(entities));
                if (entity.Definition.Kind.Equals(EntityKind.Player))
                {
                    if (player != null || entity.Definition.Layer != BoardLayer.Actor)
                        throw new ArgumentException("Exactly one player actor is required.", nameof(entities));
                    player = entity;
                }
                if (entity.Definition.Traits.IsExit)
                    exitPositions.Add(entity.Position);
            }
            if (player == null || exitPositions.Count == 0 || exitPositions.Contains(player.Position))
                throw new ArgumentException("One player start and at least one distinct exit are required.", nameof(entities));
            Start = player.Position;
            Exits = new List<GridPosition>(exitPositions).AsReadOnly();
            Entities = initialState.GetEntities();
            Items = CopyItems(items, initialState);
            InitialResources = BuildManifest(Items);
            canonicalData = EncodeCanonicalData();
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(canonicalData);
                ContentHash = BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
                deterministicHashCode = unchecked(digest[0] | digest[1] << 8 | digest[2] << 16 | digest[3] << 24);
            }
        }

        /// <summary>Gets the full stable address; a seed alone is not a board identity.</summary>
        public BoardAddress Address { get; }

        /// <summary>Gets the immutable targets used to describe this candidate; target compliance requires validation.</summary>
        public BoardGenerationConfig Config { get; }

        /// <summary>Gets the fixed inclusive 8x8 bounds for ordinary vertical-slice boards.</summary>
        public GridBounds Bounds => GridPosition.DefaultBoardBounds;

        /// <summary>Gets the initial player position, not a mutable runtime cursor.</summary>
        public GridPosition Start { get; }

        /// <summary>
        /// Gets distinct logical exit positions in deterministic order.
        /// Multiple layers may mark the same exit cell, as in the transitional legacy board.
        /// </summary>
        public IReadOnlyList<GridPosition> Exits { get; }

        /// <summary>Gets immutable initial entities in deterministic identifier order.</summary>
        public IReadOnlyList<BoardEntityState> Entities { get; }

        /// <summary>Gets generic item instances in ordinal textual instance-ID order.</summary>
        public IReadOnlyList<BoardItemSpawn> Items { get; }

        /// <summary>Gets initial item counts derived solely from Items, ordered by ordinal resource-family ID.</summary>
        public IReadOnlyList<InitialResourceManifestEntry> InitialResources { get; }

        /// <summary>
        /// Gets a deterministic lowercase SHA-256 fingerprint of all address, config, entity, and spawn values.
        /// This versioned comparison encoding is not a save or portable replay format.
        /// </summary>
        public string ContentHash { get; }

        /// <summary>Builds a fresh authoritative board without sharing mutable collections.</summary>
        /// <returns>An independent state; changing it never edits this initial layout.</returns>
        public BoardState CreateState()
        {
            var state = new BoardState(Bounds);
            foreach (BoardEntityState entity in Entities)
                state.TryAdd(entity);
            return state;
        }

        /// <summary>
        /// Exports an existing authoritative board for transitional layout comparisons.
        /// Address, config and generic item metadata are explicit because BoardState does not own them.
        /// </summary>
        /// <param name="address">The actual segment identity, never synthesized from node/day alone.</param>
        /// <param name="config">The actual immutable generation targets.</param>
        /// <param name="state">An 8x8 state to snapshot; use before gameplay for an initial blueprint.</param>
        /// <param name="items">Explicit generic item identities; legacy food rewards are not inferred as item definitions.</param>
        /// <returns>A detached blueprint of the supplied state.</returns>
        /// <exception cref="ArgumentException">The state uses unsupported bounds or invalid markers.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
        public static BoardBlueprint FromState(BoardAddress address, BoardGenerationConfig config,
            BoardState state, IEnumerable<BoardItemSpawn> items)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!state.Bounds.Equals(GridPosition.DefaultBoardBounds))
                throw new ArgumentException("Only ordinary 8x8 boards are supported.", nameof(state));
            return new BoardBlueprint(address, config, state.GetEntities(), items);
        }

        /// <summary>Compares all canonical values, not just the fingerprint, independent of input enumeration order.</summary>
        /// <param name="other">The initial layout to compare.</param>
        /// <returns>True only when all address, config, entity, and item-spawn values match.</returns>
        public bool Equals(BoardBlueprint other)
        {
            if (other == null || canonicalData.Length != other.canonicalData.Length) return false;
            for (int index = 0; index < canonicalData.Length; index++)
                if (canonicalData[index] != other.canonicalData[index]) return false;
            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => Equals(obj as BoardBlueprint);

        /// <summary>Returns the first four SHA-256 bytes as a deterministic little-endian signed dictionary hash.</summary>
        public override int GetHashCode() => deterministicHashCode;

        private static IReadOnlyList<BoardItemSpawn> CopyItems(IEnumerable<BoardItemSpawn> items, BoardState state)
        {
            var ordered = new SortedDictionary<string, BoardItemSpawn>(StringComparer.Ordinal);
            var entityIds = new HashSet<EntityId>();
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            var familiesByDefinition = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (BoardItemSpawn item in items)
            {
                if (item == null || ordered.ContainsKey(item.InstanceId) || !entityIds.Add(item.EntityId) ||
                    !state.TryGetEntity(item.EntityId, out BoardEntityState entity) ||
                    entity.Definition.Layer != BoardLayer.Item || entity.Definition.Traits.IsExit ||
                    entity.Definition.ContentId != item.ItemDefinitionId ||
                    item.SourceId != null && !sourceIds.Add(item.SourceId))
                    throw new ArgumentException("Each item needs one unique spawn, matching item entity/definition, and unique optional source.", nameof(items));
                if (familiesByDefinition.TryGetValue(item.ItemDefinitionId, out string family) && family != item.ResourceFamilyId)
                    throw new ArgumentException("An item definition cannot belong to multiple resource families.", nameof(items));
                familiesByDefinition[item.ItemDefinitionId] = item.ResourceFamilyId;
                ordered.Add(item.InstanceId, item);
            }
            foreach (BoardEntityState entity in state.GetEntities())
                if (entity.Definition.Layer == BoardLayer.Item && !entity.Definition.Traits.IsExit && !entityIds.Contains(entity.Id))
                    throw new ArgumentException("Every item-layer entity requires generic spawn metadata.", nameof(items));
            return new List<BoardItemSpawn>(ordered.Values).AsReadOnly();
        }

        private static IReadOnlyList<InitialResourceManifestEntry> BuildManifest(IReadOnlyList<BoardItemSpawn> items)
        {
            var counts = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            foreach (BoardItemSpawn item in items)
            {
                if (!counts.TryGetValue(item.ResourceFamilyId, out int[] count))
                {
                    count = new int[2];
                    counts.Add(item.ResourceFamilyId, count);
                }
                count[0]++;
                if (item.InitiallyObservable) count[1]++;
            }
            var manifest = new List<InitialResourceManifestEntry>();
            foreach (KeyValuePair<string, int[]> count in counts)
                manifest.Add(new InitialResourceManifestEntry(count.Key, count.Value[0], count.Value[1]));
            return manifest.AsReadOnly();
        }

        private byte[] EncodeCanonicalData()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true))
            {
                writer.Write("hallowblaze.board-blueprint.v1");
                writer.Write(Address.RunId);
                writer.Write(Address.LegId);
                writer.Write(Address.EdgeId);
                writer.Write(Address.SegmentId);
                writer.Write(Address.DayNumber);
                writer.Write(Address.GenerationConfigurationId);
                writer.Write(Address.GenerationVersion);
                writer.Write(Address.BoardSeed);
                writer.Write(Config.Id);
                writer.Write(Config.Version);
                WriteRange(writer, Config.PathLength);
                WriteRange(writer, Config.BranchPoints);
                WriteRange(writer, Config.ObstacleCount);
                WriteRange(writer, Config.EnemyCount);
                writer.Write(Config.MaximumConcurrentThreats);
                writer.Write(Config.MinimumSafeStartMoves);
                writer.Write(Config.AllowedContentIds.Count);
                foreach (string contentId in Config.AllowedContentIds) writer.Write(contentId);
                writer.Write(Config.ResourceBudgets.Count);
                foreach (ResourceGenerationBudget budget in Config.ResourceBudgets)
                {
                    writer.Write(budget.ResourceFamilyId);
                    WriteRange(writer, budget.ItemCount);
                }
                writer.Write(Entities.Count);
                foreach (BoardEntityState entity in Entities)
                {
                    writer.Write(entity.Id.Value);
                    writer.Write(entity.Position.X);
                    writer.Write(entity.Position.Y);
                    writer.Write((int)entity.Definition.Layer);
                    writer.Write(entity.Definition.Kind.Id);
                    writer.Write(entity.Definition.ContentId);
                    writer.Write(entity.Definition.Traits.IsWalkable);
                    writer.Write(entity.Definition.Traits.IsExit);
                    writer.Write(entity.Definition.Traits.IsInteractable);
                    writer.Write(entity.Definition.AutomaticFoodReward);
                }
                writer.Write(Items.Count);
                foreach (BoardItemSpawn item in Items)
                {
                    writer.Write(item.InstanceId);
                    writer.Write(item.EntityId.Value);
                    writer.Write(item.ItemDefinitionId);
                    writer.Write(item.ResourceFamilyId);
                    writer.Write(item.SourceId != null);
                    if (item.SourceId != null) writer.Write(item.SourceId);
                    writer.Write(item.InitiallyObservable);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteRange(BinaryWriter writer, GenerationRange range)
        {
            writer.Write(range.Minimum);
            writer.Write(range.Maximum);
        }
    }
}