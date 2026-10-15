using System;
using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    /// <summary>Frozen v2 travel fields, also inherited by the RNG-versioned v3 schema; no mid-board snapshot.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class RunStateV2Dto
    {
        /// <summary>Gets the supported travel schema version; v1 remains a separate migration input.</summary>
        public const int CurrentSchemaVersion = 2;
        /// <summary>Gets or sets the explicit document version.</summary>
        [JsonProperty("schemaVersion", Required = Required.Always, Order = 1)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>Gets or sets the owning run identity.</summary>
        [JsonProperty("runId", Required = Required.Always, Order = 2)]
        public string RunId { get; set; }
        /// <summary>Gets or sets the deterministic root seed.</summary>
        [JsonProperty("runSeed", Required = Required.Always, Order = 3)]
        public int RunSeed { get; set; }
        /// <summary>Gets or sets completed days, independent of active-segment progress.</summary>
        [JsonProperty("completedDayCount", Required = Required.Always, Order = 4)]
        public int CompletedDayCount { get; set; }
        /// <summary>Gets or sets the last reached node, not the active destination.</summary>
        [JsonProperty("reachedCheckpointNodeId", Required = Required.Always, Order = 5)]
        public string ReachedCheckpointNodeId { get; set; }
        /// <summary>Gets or sets the monotonic run-local traversal allocator.</summary>
        [JsonProperty("traversalSequence", Required = Required.Always, Order = 6)]
        public int TraversalSequence { get; set; }
        /// <summary>Gets or sets current health.</summary>
        [JsonProperty("health", Required = Required.Always, Order = 7)]
        public int Health { get; set; }
        /// <summary>Gets or sets the existing Food resource without rebalance.</summary>
        [JsonProperty("food", Required = Required.Always, Order = 8)]
        public int Food { get; set; }
        /// <summary>Gets or sets the existing active/dead/won status ID.</summary>
        [JsonProperty("status", Required = Required.Always, Order = 9)]
        public string Status { get; set; }
        /// <summary>Gets or sets the existing two tool slots.</summary>
        [JsonProperty("toolSlots", Required = Required.Always, Order = 10)]
        public ToolSlotDto[] ToolSlots { get; set; } = Array.Empty<ToolSlotDto>();
        /// <summary>Gets or sets the sole ordered travel history.</summary>
        [JsonProperty("travelHistory", Required = Required.Always, Order = 11)]
        public TravelHistoryDto[] TravelHistory { get; set; } = Array.Empty<TravelHistoryDto>();
        /// <summary>Gets or sets the optional leg; the field is required even when explicitly null.</summary>
        [JsonProperty("activeLeg", Required = Required.AllowNull, Order = 12)]
        public ActiveRouteLegDto ActiveLeg { get; set; }
    }

    /// <summary>Tagged history with explicit nulls for information absent from legacy evidence.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TravelHistoryDto
    {
        /// <summary>Gets or sets legacyCheckpoint or completedTraversal.</summary>
        [JsonProperty("kind", Required = Required.Always)]
        public string Kind { get; set; }
        /// <summary>Gets or sets the reached node.</summary>
        [JsonProperty("toNodeId", Required = Required.Always)]
        public string ToNodeId { get; set; }
        /// <summary>Gets or sets the leg ID, null for legacy checkpoints.</summary>
        [JsonProperty("legId", Required = Required.AllowNull)]
        public string LegId { get; set; }
        /// <summary>Gets or sets the actual road, null for legacy checkpoints.</summary>
        [JsonProperty("edgeId", Required = Required.AllowNull)]
        public string EdgeId { get; set; }
        /// <summary>Gets or sets the source node, null for legacy checkpoints.</summary>
        [JsonProperty("fromNodeId", Required = Required.AllowNull)]
        public string FromNodeId { get; set; }
        /// <summary>Gets or sets the completed day, null for legacy checkpoints.</summary>
        [JsonProperty("dayNumber", Required = Required.AllowNull)]
        public int? DayNumber { get; set; }
        /// <summary>Gets or sets the total segments, null for legacy checkpoints.</summary>
        [JsonProperty("segmentCount", Required = Required.AllowNull)]
        public int? SegmentCount { get; set; }
        /// <summary>Gets or sets completed segments; addressed history requires full completion.</summary>
        [JsonProperty("completedSegmentCount", Required = Required.AllowNull)]
        public int? CompletedSegmentCount { get; set; }
    }

    /// <summary>Persisted inputs for reconstructing an active traversal and its deterministic address.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ActiveRouteLegDto
    {
        /// <summary>Gets or sets the allocated traversal ID.</summary>
        [JsonProperty("legId", Required = Required.Always)]
        public string LegId { get; set; }
        /// <summary>Gets or sets the directed road ID.</summary>
        [JsonProperty("edgeId", Required = Required.Always)]
        public string EdgeId { get; set; }
        /// <summary>Gets or sets the reached source checkpoint.</summary>
        [JsonProperty("fromNodeId", Required = Required.Always)]
        public string FromNodeId { get; set; }
        /// <summary>Gets or sets the unreached destination.</summary>
        [JsonProperty("toNodeId", Required = Required.Always)]
        public string ToNodeId { get; set; }
        /// <summary>Gets or sets the one-based day.</summary>
        [JsonProperty("dayNumber", Required = Required.Always)]
        public int DayNumber { get; set; }
        /// <summary>Gets or sets the zero-based cursor, not board identity.</summary>
        [JsonProperty("segmentIndex", Required = Required.Always)]
        public int SegmentIndex { get; set; }
        /// <summary>Gets or sets progress, which must equal the cursor.</summary>
        [JsonProperty("completedSegmentCount", Required = Required.Always)]
        public int CompletedSegmentCount { get; set; }
        /// <summary>Gets or sets pending or consumed; presented is not supported.</summary>
        [JsonProperty("introState", Required = Required.Always)]
        public string IntroState { get; set; }
        /// <summary>Gets or sets detached ordered segment generation identities.</summary>
        [JsonProperty("segments", Required = Required.Always)]
        public RouteSegmentDto[] Segments { get; set; }
    }

    /// <summary>Published scalar segment identity; never stores a Unity asset or position.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RouteSegmentDto
    {
        /// <summary>Gets or sets the migration-sensitive segment ID.</summary>
        [JsonProperty("segmentId", Required = Required.Always)]
        public string SegmentId { get; set; }
        /// <summary>Gets or sets the stable generation configuration.</summary>
        [JsonProperty("generationConfigurationId", Required = Required.Always)]
        public string GenerationConfigurationId { get; set; }
        /// <summary>Gets or sets the positive generation version.</summary>
        [JsonProperty("generationVersion", Required = Required.Always)]
        public int GenerationVersion { get; set; }
    }
}