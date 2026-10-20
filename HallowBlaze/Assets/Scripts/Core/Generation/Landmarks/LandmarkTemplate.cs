using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Generation.Landmarks
{
    /// <summary>Names a future completed-board result, not an executable puzzle rule or an already earned reward.</summary>
    public sealed class LandmarkOutcomeDefinition
    {
        /// <summary>Declares an exit goal and optional tool requirement; a mandatory tool must have an explicit initial instance guarantee.</summary>
        public LandmarkOutcomeDefinition(string id, string name, EntityId exitEntityId, bool optional = false,
            string requiredToolDefinitionId = null, string guaranteedToolInstanceId = null)
        {
            GenerationContract.RequireId(id, nameof(id));
            GenerationContract.RequireId(name, nameof(name));
            if (requiredToolDefinitionId != null) GenerationContract.RequireId(requiredToolDefinitionId, nameof(requiredToolDefinitionId));
            if (guaranteedToolInstanceId != null) GenerationContract.RequireId(guaranteedToolInstanceId, nameof(guaranteedToolInstanceId));
            if (requiredToolDefinitionId == null && guaranteedToolInstanceId != null ||
                !optional && requiredToolDefinitionId != null && guaranteedToolInstanceId == null)
                throw new ArgumentException("A mandatory tool requires a concrete initial item guarantee; random loot cannot be required.", nameof(guaranteedToolInstanceId));
            Id = id;
            Name = name;
            ExitEntityId = exitEntityId;
            IsOptional = optional;
            RequiredToolDefinitionId = requiredToolDefinitionId;
            GuaranteedToolInstanceId = guaranteedToolInstanceId;
        }

        /// <summary>Gets the migration-sensitive result key; it is not an atlas node or entity ID.</summary>
        public string Id { get; }
        /// <summary>Gets the working authored name, not a solution instruction.</summary>
        public string Name { get; }
        /// <summary>Gets the terrain exit marker that must exist in every supported variant.</summary>
        public EntityId ExitEntityId { get; }
        /// <summary>Gets whether this outcome may be skipped without blocking the mandatory objective.</summary>
        public bool IsOptional { get; }
        /// <summary>Gets a stable tool definition requirement, or null for a tool-free result.</summary>
        public string RequiredToolDefinitionId { get; }
        /// <summary>Gets the concrete initial spawn reachable without a tool or prior exit in every variant, or null when no guarantee is needed.</summary>
        public string GuaranteedToolInstanceId { get; }
    }

    /// <summary>Owns one detached authored layout; selecting a variant never edits the template or consumes RNG.</summary>
    public sealed class LandmarkVariant
    {
        /// <summary>Copies all initial entities/items, preserving board-local IDs across reconstruction.</summary>
        public LandmarkVariant(string id, IEnumerable<BoardEntityState> entities, IEnumerable<BoardItemSpawn> items)
        {
            GenerationContract.RequireId(id, nameof(id));
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            if (items == null) throw new ArgumentNullException(nameof(items));
            Id = id;
            Entities = new List<BoardEntityState>(entities).AsReadOnly();
            Items = new List<BoardItemSpawn>(items).AsReadOnly();
        }

        /// <summary>Gets the stable variant key, independent of its position in a JSON array.</summary>
        public string Id { get; }
        /// <summary>Gets immutable entity snapshots; runtime mutation belongs to a fresh BoardState.</summary>
        public IReadOnlyList<BoardEntityState> Entities { get; }
        /// <summary>Gets concrete generic item/source bindings, including any promised tool.</summary>
        public IReadOnlyList<BoardItemSpawn> Items { get; }
    }

    /// <summary>Pairs an ordinary blueprint with explicit authored identity without replacing its full segment address.</summary>
    public sealed class LandmarkBoard
    {
        internal LandmarkBoard(LandmarkTemplate template, string variantId, BoardBlueprint blueprint)
        {
            TemplateId = template.Id;
            TemplateVersion = template.Version;
            VariantId = variantId;
            Blueprint = blueprint;
        }

        /// <summary>Gets the stable template key for future completed-board persistence.</summary>
        public string TemplateId { get; }
        /// <summary>Gets the positive authored schema/content version, not the world-node version.</summary>
        public int TemplateVersion { get; }
        /// <summary>Gets the selected authored variant key; no array-index identity or cosmetic RNG is used.</summary>
        public string VariantId { get; }
        /// <summary>Gets the same blueprint/state construction contract used by ordinary boards.</summary>
        public BoardBlueprint Blueprint { get; }
    }

    /// <summary>Defines a validated authored 8x8 landmark using existing board layers; it neither solves nor executes outcomes.</summary>
    public sealed class LandmarkTemplate
    {
        private readonly Dictionary<string, LandmarkVariant> variants = new Dictionary<string, LandmarkVariant>(StringComparer.Ordinal);

        /// <summary>Freezes a versioned template, rejecting invalid goals, occupancy and missing mandatory guarantees before publication.</summary>
        /// <remarks>Structural validation uses a local diagnostic address only. Gameplay construction always requires the actual BoardAddress.</remarks>
        public LandmarkTemplate(string id, int version, BoardGenerationConfig config,
            IEnumerable<LandmarkVariant> variants, IEnumerable<LandmarkOutcomeDefinition> outcomes)
        {
            GenerationContract.RequireId(id, nameof(id));
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
            Id = id;
            Version = version;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            if (variants == null) throw new ArgumentNullException(nameof(variants));
            if (outcomes == null) throw new ArgumentNullException(nameof(outcomes));
            var orderedOutcomes = new SortedDictionary<string, LandmarkOutcomeDefinition>(StringComparer.Ordinal);
            bool mandatory = false;
            foreach (LandmarkOutcomeDefinition outcome in outcomes)
            {
                if (outcome == null || orderedOutcomes.ContainsKey(outcome.Id))
                    throw new ArgumentException("Landmark " + id + " requires distinct non-null outcomes.", nameof(outcomes));
                orderedOutcomes.Add(outcome.Id, outcome);
                mandatory |= !outcome.IsOptional;
            }
            if (orderedOutcomes.Count < 2 || !mandatory)
                throw new ArgumentException("Landmark " + id + " requires at least two named outcomes and a mandatory goal.", nameof(outcomes));
            Outcomes = new List<LandmarkOutcomeDefinition>(orderedOutcomes.Values).AsReadOnly();
            foreach (LandmarkVariant variant in variants)
            {
                if (variant == null || this.variants.ContainsKey(variant.Id))
                    throw new ArgumentException("Landmark " + id + " requires distinct non-null variants.", nameof(variants));
                ValidateVariant(variant);
                this.variants.Add(variant.Id, variant);
            }
            if (this.variants.Count == 0) throw new ArgumentException("At least one authored variant is required.", nameof(variants));
            var keys = new List<string>(this.variants.Keys);
            keys.Sort(StringComparer.Ordinal);
            VariantIds = keys.AsReadOnly();
        }

        /// <summary>Gets a stable template ID independent of atlas locations and route-segment IDs.</summary>
        public string Id { get; }
        /// <summary>Gets the positive authored version; changing published identities requires an explicit compatibility decision.</summary>
        public int Version { get; }
        /// <summary>Gets explicit shared board targets and allowed content; ordinary generation is not invoked.</summary>
        public BoardGenerationConfig Config { get; }
        /// <summary>Gets permitted variant keys in ordinal order; random variant selection belongs to a later task.</summary>
        public IReadOnlyList<string> VariantIds { get; }
        /// <summary>Gets named goal declarations in ordinal order, not earned results or solution sequences.</summary>
        public IReadOnlyList<LandmarkOutcomeDefinition> Outcomes { get; }

        /// <summary>Creates a fresh blueprint for one explicit variant at the real active segment address; it never changes run/profile state.</summary>
        /// <exception cref="ArgumentException">The variant is unknown or the address uses a different configuration ID/version.</exception>
        public LandmarkBoard CreateBoard(BoardAddress address, string variantId)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            GenerationContract.RequireId(variantId, nameof(variantId));
            if (!variants.TryGetValue(variantId, out LandmarkVariant variant))
                throw new ArgumentException("Unknown variant " + variantId + " in landmark " + Id + ".", nameof(variantId));
            return new LandmarkBoard(this, variantId, new BoardBlueprint(address, Config, variant.Entities, variant.Items));
        }

        private void ValidateVariant(LandmarkVariant variant)
        {
            string context = "Landmark " + Id + "/" + Version + " variant " + variant.Id + ": ";
            BoardState state;
            try
            {
                var address = new BoardAddress("validation", "validation", "validation", "validation", 1, Config.Id, Config.Version, 0);
                state = new BoardBlueprint(address, Config, variant.Entities, variant.Items).CreateState();
            }
            catch (ArgumentException exception) { throw new ArgumentException(context + exception.Message, nameof(variant), exception); }
            foreach (BoardEntityState entity in variant.Entities)
                if (!Contains(Config.AllowedContentIds, entity.Definition.ContentId))
                    throw new ArgumentException(context + "entity " + entity.Id + " references disallowed content " + entity.Definition.ContentId + ".");
            foreach (LandmarkOutcomeDefinition outcome in Outcomes)
            {
                if (!state.TryGetEntity(outcome.ExitEntityId, out BoardEntityState exit) ||
                    exit.Definition.Layer != BoardLayer.Terrain || !exit.Definition.Traits.IsExit || !exit.Definition.Traits.IsWalkable)
                    throw new ArgumentException(context + "outcome " + outcome.Id + " references a missing or non-terrain exit.");
                if (outcome.RequiredToolDefinitionId != null && !Contains(Config.AllowedContentIds, outcome.RequiredToolDefinitionId))
                    throw new ArgumentException(context + "outcome " + outcome.Id + " requires a disallowed tool definition.");
                if (outcome.GuaranteedToolInstanceId == null) continue;
                bool provided = false;
                foreach (BoardItemSpawn item in variant.Items)
                    if (item.InstanceId == outcome.GuaranteedToolInstanceId && item.ItemDefinitionId == outcome.RequiredToolDefinitionId &&
                        item.ResourceFamilyId == "tools" && item.InitiallyObservable && state.TryGetEntity(item.EntityId, out BoardEntityState tool) &&
                        CanReachBeforeExit(state, tool.Position)) provided = true;
                if (!provided)
                    throw new ArgumentException(context + "outcome " + outcome.Id + " is missing an accessible guaranteed tool instance " + outcome.GuaranteedToolInstanceId + ".");
            }
        }

        private static bool CanReachBeforeExit(BoardState state, GridPosition target)
        {
            var frontier = new Queue<GridPosition>();
            var visited = new HashSet<GridPosition>();
            foreach (BoardEntityState entity in state.GetEntities())
                if (entity.Definition.Kind.Equals(EntityKind.Player)) frontier.Enqueue(entity.Position);
            while (frontier.Count > 0)
            {
                GridPosition current = frontier.Dequeue();
                if (!visited.Add(current) || !state.TryGetEntity(BoardLayer.Terrain, current, out BoardEntityState terrain) ||
                    !terrain.Definition.Traits.IsWalkable || terrain.Definition.Traits.IsExit ||
                    state.TryGetEntity(BoardLayer.Obstacle, current, out BoardEntityState obstacle) && !obstacle.Definition.Traits.IsWalkable) continue;
                if (current.Equals(target)) return true;
                foreach (Direction direction in new[] { Direction.North, Direction.East, Direction.West, Direction.South })
                {
                    GridPosition next = current.Move(direction);
                    if (state.Bounds.Contains(next) && !visited.Contains(next)) frontier.Enqueue(next);
                }
            }
            return false;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            foreach (string candidate in values) if (candidate == value) return true;
            return false;
        }
    }
}