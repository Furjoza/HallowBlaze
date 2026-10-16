using System;
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
        /// <summary>Gets the explicitly selected variant key; random selection is not implemented here.</summary>
        public string VariantId { get; }

        /// <summary>Builds only for the assigned active edge/segment; preserves all address fields including run, leg, day and seed.</summary>
        public LandmarkBoard CreateBoard(BoardAddress address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (address.EdgeId != EdgeId || address.SegmentId != SegmentId)
                throw new ArgumentException("BoardAddress does not identify the assigned landmark segment.", nameof(address));
            return template.CreateBoard(address, VariantId);
        }
    }
}