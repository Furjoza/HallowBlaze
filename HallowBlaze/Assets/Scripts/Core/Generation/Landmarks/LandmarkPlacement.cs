using System;
using System.Globalization;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;

namespace HallowBlaze.Core.Generation.Landmarks
{
    /// <summary>Assigns one explicit template variant to the final segment of an inbound landmark road, never one board per node or one extra day.</summary>
    public sealed class LandmarkPlacement
    {
        private readonly LandmarkTemplate template;

        /// <summary>Validates the world, terminal segment and matching generation identity before making an assignment available.</summary>
        /// <exception cref="ArgumentException">The segment, destination, generation contract or variant is invalid.</exception>
        public LandmarkPlacement(WorldDefinition world, string segmentId, LandmarkTemplate template, string variantId)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            this.template = template ?? throw new ArgumentNullException(nameof(template));
            GenerationContract.RequireId(segmentId, nameof(segmentId));
            GenerationContract.RequireId(variantId, nameof(variantId));
            if (!world.TryGetSegment(segmentId, out RouteSegmentDefinition segment, out WorldEdgeDefinition edge) ||
                edge.Segments[edge.SegmentCount - 1].SegmentId != segmentId ||
                !world.TryGetNode(edge.FromNodeId, out _) ||
                !world.TryGetNode(edge.ToNodeId, out WorldNodeDefinition destination) || destination.PlaceKind != "landmark")
                throw new ArgumentException("Landmark assignment requires the final segment of an inbound landmark edge: " + segmentId + ".", nameof(segmentId));
            if (segment.GenerationConfigurationId != template.Config.Id || segment.GenerationVersion != template.Config.Version)
                throw new ArgumentException("Landmark segment and template generation configuration must match: " + segmentId + ".", nameof(template));
            bool known = false;
            foreach (string candidate in template.VariantIds) known |= candidate == variantId;
            if (!known) throw new ArgumentException("Unknown variant " + variantId + " in " + template.Id + ".", nameof(variantId));
            WorldDefinitionId = world.WorldDefinitionId;
            WorldVersion = world.Version;
            EdgeId = edge.EdgeId;
            SegmentId = segmentId;
            VariantId = variantId;
        }

        /// <summary>Gets the published world key for assignment validation, not a board identity.</summary>
        public string WorldDefinitionId { get; }
        /// <summary>Gets the exact world publication version used to validate the assignment.</summary>
        public int WorldVersion { get; }
        /// <summary>Gets the inbound road key; all preceding segment addresses remain distinct and ordinary.</summary>
        public string EdgeId { get; }
        /// <summary>Gets the exact globally unique segment key; no node/day convention selects the landmark.</summary>
        public string SegmentId { get; }
        /// <summary>Gets the migration-sensitive template key retained alongside the runtime board address.</summary>
        public string TemplateId => template.Id;
        /// <summary>Gets the selected published template version.</summary>
        public int TemplateVersion => template.Version;
        /// <summary>Gets the explicit authoring/default variant used by direct construction, not a saved random selection.</summary>
        public string VariantId { get; }

        /// <summary>Creates a fresh addressed selection from the ordinal authored variant set using an independent landmark PCG32 stream.</summary>
        /// <param name="address">The complete assigned board identity; no node/day-only or seed-only substitution is allowed.</param>
        /// <param name="runSeed">The persisted run root seed, independent of cosmetic/layout stream cursors.</param>
        /// <returns>An immutable decision that Session must durably checkpoint before rendering. Existing saved selections must be reused instead.</returns>
        /// <exception cref="ArgumentException">The address does not belong to this assignment or generation configuration.</exception>
        public LandmarkRecord SelectVariant(BoardAddress address, int runSeed)
        {
            ValidateAddress(address);
            string streamId = RandomStreamProvider.StreamId("landmark-variant-v1", WorldDefinitionId,
                WorldVersion.ToString(CultureInfo.InvariantCulture), TemplateId, TemplateVersion.ToString(CultureInfo.InvariantCulture),
                address.RunId, address.LegId, address.EdgeId, address.SegmentId,
                address.DayNumber.ToString(CultureInfo.InvariantCulture), address.GenerationConfigurationId,
                address.GenerationVersion.ToString(CultureInfo.InvariantCulture), address.BoardSeed.ToString(CultureInfo.InvariantCulture));
            int index = new RandomStreamProvider(runSeed).Create(streamId).NextInt(0, template.VariantIds.Count);
            return new LandmarkRecord(address, TemplateId, TemplateVersion, template.VariantIds[index]);
        }

        /// <summary>Builds only for the assigned active edge/segment; preserves all address fields including run, leg, day and seed.</summary>
        public LandmarkBoard CreateBoard(BoardAddress address)
        {
            ValidateAddress(address);
            return template.CreateBoard(address, VariantId);
        }

        private void ValidateAddress(BoardAddress address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (address.EdgeId != EdgeId || address.SegmentId != SegmentId ||
                address.GenerationConfigurationId != template.Config.Id || address.GenerationVersion != template.Config.Version)
                throw new ArgumentException("BoardAddress does not identify the assigned landmark segment.", nameof(address));
        }
    }
}