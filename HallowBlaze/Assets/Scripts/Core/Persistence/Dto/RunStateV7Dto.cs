using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    /// <summary>Resource-aware v7 boundary retained for migration and the inventory-aware extension, without Food inheritance.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class RunStateV7Dto
    {
        /// <summary>Current resource-aware schema; profile and historical schemas are unchanged.</summary>
        public const int CurrentSchemaVersion = 7;
        /// <summary>Gets or sets the required document version.</summary>
        [JsonProperty("schemaVersion", Required = Required.Always, Order = 1)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>Gets or sets the stable owning run identity.</summary>
        [JsonProperty("runId", Required = Required.Always, Order = 2)] public string RunId { get; set; }
        /// <summary>Gets or sets the original root seed.</summary>
        [JsonProperty("runSeed", Required = Required.Always, Order = 3)] public int RunSeed { get; set; }
        /// <summary>Gets or sets completed days independently of segment progress.</summary>
        [JsonProperty("completedDayCount", Required = Required.Always, Order = 4)] public int CompletedDayCount { get; set; }
        /// <summary>Gets or sets the reached checkpoint.</summary>
        [JsonProperty("reachedCheckpointNodeId", Required = Required.Always, Order = 5)] public string ReachedCheckpointNodeId { get; set; }
        /// <summary>Gets or sets the durable traversal allocator.</summary>
        [JsonProperty("traversalSequence", Required = Required.Always, Order = 6)] public int TraversalSequence { get; set; }
        /// <summary>Gets or sets bounded health.</summary>
        [JsonProperty("health", Required = Required.Always, Order = 7)] public int Health { get; set; }
        /// <summary>Gets or sets the only persisted satiety value; no food field is accepted in this schema.</summary>
        [JsonProperty("satiety", Required = Required.Always, Order = 8)] public int Satiety { get; set; }
        /// <summary>Gets or sets active, dead, or won status.</summary>
        [JsonProperty("status", Required = Required.Always, Order = 9)] public string Status { get; set; }
        /// <summary>Gets or sets the two separate generic tool slots.</summary>
        [JsonProperty("toolSlots", Required = Required.Always, Order = 10)] public ToolInstanceDto[] ToolSlots { get; set; }
        /// <summary>Gets or sets ordered checkpoint/traversal history.</summary>
        [JsonProperty("travelHistory", Required = Required.Always, Order = 11)] public TravelHistoryDto[] TravelHistory { get; set; }
        /// <summary>Gets or sets the optional active leg; an explicit null is required at checkpoints.</summary>
        [JsonProperty("activeLeg", Required = Required.AllowNull, Order = 12)] public ActiveRouteLegDto ActiveLeg { get; set; }
        /// <summary>Gets or sets the retained layout RNG algorithm identity.</summary>
        [JsonProperty("randomAlgorithmId", Required = Required.Always, Order = 13)] public string RandomAlgorithmId { get; set; }
        /// <summary>Gets or sets the retained layout RNG version.</summary>
        [JsonProperty("randomAlgorithmVersion", Required = Required.Always, Order = 14)] public int RandomAlgorithmVersion { get; set; }
        /// <summary>Gets or sets the monotonic tool allocator.</summary>
        [JsonProperty("toolInstanceSequence", Required = Required.Always, Order = 15)] public int ToolInstanceSequence { get; set; }
        /// <summary>Gets or sets the current landmark selection, explicitly null when absent.</summary>
        [JsonProperty("activeLandmark", Required = Required.AllowNull, Order = 16)] public LandmarkRecordDto ActiveLandmark { get; set; }
        /// <summary>Gets or sets completed landmark results.</summary>
        [JsonProperty("completedLandmarks", Required = Required.Always, Order = 17)] public LandmarkRecordDto[] CompletedLandmarks { get; set; }
        /// <summary>Gets or sets the exact immutable whole-leg authored choices, explicitly null when absent.</summary>
        [JsonProperty("boardSelectionPlan", Required = Required.AllowNull, Order = 18)] public RouteBoardPlanDto BoardSelectionPlan { get; set; }
        /// <summary>Gets or sets the resource-rule version, caps, threshold, and costs.</summary>
        [JsonProperty("resourceRules", Required = Required.Always, Order = 19)] public ResourceRulesDto ResourceRules { get; set; }
    }

    /// <summary>Required scalar configuration prevents Continue from silently changing resource semantics.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ResourceRulesDto
    {
        /// <summary>Gets or sets the supported rule-interpretation version.</summary>
        [JsonProperty("version", Required = Required.Always)] public int Version { get; set; }
        /// <summary>Gets or sets the positive health cap.</summary>
        [JsonProperty("maximumHealth", Required = Required.Always)] public int MaximumHealth { get; set; }
        /// <summary>Gets or sets the positive satiety cap.</summary>
        [JsonProperty("maximumSatiety", Required = Required.Always)] public int MaximumSatiety { get; set; }
        /// <summary>Gets or sets the inclusive normal-cost threshold.</summary>
        [JsonProperty("normalSatietyThreshold", Required = Required.Always)] public int NormalSatietyThreshold { get; set; }
        /// <summary>Gets or sets the positive cost at or below the threshold.</summary>
        [JsonProperty("normalActionCost", Required = Required.Always)] public int NormalActionCost { get; set; }
        /// <summary>Gets or sets the positive cost above the threshold.</summary>
        [JsonProperty("satedActionCost", Required = Required.Always)] public int SatedActionCost { get; set; }
    }
}