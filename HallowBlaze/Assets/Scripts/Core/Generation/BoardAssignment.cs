using System;
using System.Linq;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Core.Generation
{
    /// <summary>Binds a versioned board snapshot to one real world/route/segment; world role is derived, never invented by a board file.</summary>
    public sealed class BoardAssignment
    {
        private readonly BoardTemplate template;
        private readonly RouteSegmentDefinition segment;
        /// <summary>Validates the exact segment and variant; a shared snapshot may be placed on intermediate or final segments.</summary>
        public BoardAssignment(WorldDefinition world, string segmentId, LandmarkTemplate template, string variantId)
        {
            if (world == null || template == null) throw new ArgumentNullException(nameof(world));
            if (!world.TryGetSegment(segmentId, out segment, out WorldEdgeDefinition edge) || !template.VariantIds.Contains(variantId))
                throw new ArgumentException("Unknown route segment or template variant.");
            if (!world.TryGetNode(edge.FromNodeId, out WorldNodeDefinition source) || !world.TryGetNode(edge.ToNodeId, out WorldNodeDefinition destination) ||
                !KnownPrototypeBiome(source.BiomeFamily) || !KnownPrototypeBiome(destination.BiomeFamily))
                throw new NotSupportedException("The selected route has no supported prototype content catalog.");
            WorldId = world.WorldDefinitionId;
            WorldVersion = world.Version;
            EdgeId = edge.EdgeId;
            SegmentId = segmentId;
            VariantId = variantId;
            IsLandmark = destination.PlaceKind == "landmark" && edge.Segments[edge.SegmentCount - 1].SegmentId == segmentId;
            this.template = template.AsShared();
        }
        private static bool KnownPrototypeBiome(string biome) => biome == "temperate-forest" || biome == "wet-forest" || biome == "rocky-foothills";
        /// <summary>Gets the exact world publication ID.</summary>
        public string WorldId { get; }
        /// <summary>Gets the exact world publication version.</summary>
        public int WorldVersion { get; }
        /// <summary>Gets the owning route ID derived from segment membership.</summary>
        public string EdgeId { get; }
        /// <summary>Gets the globally stable segment ID.</summary>
        public string SegmentId { get; }
        /// <summary>Gets the explicit stable variant, never a random pool choice.</summary>
        public string VariantId { get; }
        /// <summary>Gets whether the target is the final inbound segment of a concrete landmark node.</summary>
        public bool IsLandmark { get; }
        /// <summary>Builds a fresh snapshot at the original saved segment address, retaining all address/configuration fields.</summary>
        public LandmarkBoard CreateBoard(BoardAddress address)
        {
            if (address == null || address.EdgeId != EdgeId || address.SegmentId != SegmentId ||
                address.GenerationConfigurationId != segment.GenerationConfigurationId || address.GenerationVersion != segment.GenerationVersion)
                throw new ArgumentException("The address does not match the assigned segment publication.");
            return template.CreateBoard(address, VariantId);
        }
        /// <summary>Serializes the exact binding only, without publishing content or serializing mutable run state.</summary>
        public string ToJson() => new JObject
        {
            ["schemaVersion"] = 2, ["worldDefinitionId"] = WorldId, ["worldVersion"] = WorldVersion, ["edgeId"] = EdgeId,
            ["segmentId"] = SegmentId, ["generationConfigurationId"] = segment.GenerationConfigurationId,
            ["generationVersion"] = segment.GenerationVersion, ["templateId"] = template.Id, ["templateVersion"] = template.Version,
            ["variantId"] = VariantId
        }.ToString(Formatting.Indented);
        /// <summary>Rejects unknown fields, changed versions or configuration drift rather than silently reassigning a board.</summary>
        public static BoardAssignment Read(string json, WorldDefinition world, LandmarkTemplate template)
        {
            JObject data;
            using (var text = new System.IO.StringReader(json))
            using (var reader = new JsonTextReader(text))
            {
                data = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new JsonSerializationException("Only one assignment object is permitted.");
            }
            var assignment = new BoardAssignment(world, (string)data["segmentId"], template, (string)data["variantId"]);
            if (!JToken.DeepEquals(data, JObject.Parse(assignment.ToJson())))
                throw new JsonSerializationException("Assignment identity, version, fields or scalar types do not match current publications.");
            return assignment;
        }
    }
}