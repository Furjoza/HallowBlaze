using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    /// <summary>Adds an exact whole-leg authored selection plan without changing earlier document semantics.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RunStateV6Dto : RunStateV5Dto
    {
        /// <summary>Versions durable pooled/fixed authored choices; profile schema remains unchanged.</summary>
        public new const int CurrentSchemaVersion = 6;
        /// <summary>Initializes the pool-aware schema while keeping the plan explicitly nullable.</summary>
        public RunStateV6Dto() { SchemaVersion = CurrentSchemaVersion; }
        /// <summary>Gets or sets the full active leg's immutable plan, or null for generated/legacy travel and checkpoints.</summary>
        [JsonProperty("boardSelectionPlan", Required = Required.AllowNull, Order = 18)]
        public RouteBoardPlanDto BoardSelectionPlan { get; set; }
    }

    /// <summary>Scalar publication provenance and all choices, including segments not visited yet.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RouteBoardPlanDto
    {
        /// <summary>Gets or sets the exact owning run.</summary>
        [JsonProperty("runId", Required = Required.Always)] public string RunId { get; set; }
        /// <summary>Gets or sets its original root seed.</summary>
        [JsonProperty("runSeed", Required = Required.Always)] public int RunSeed { get; set; }
        /// <summary>Gets or sets the owning traversal identity.</summary>
        [JsonProperty("legId", Required = Required.Always)] public string LegId { get; set; }
        /// <summary>Gets or sets the directed route identity.</summary>
        [JsonProperty("edgeId", Required = Required.Always)] public string EdgeId { get; set; }
        /// <summary>Gets or sets the captured world identity.</summary>
        [JsonProperty("worldId", Required = Required.Always)] public string WorldId { get; set; }
        /// <summary>Gets or sets its captured content version.</summary>
        [JsonProperty("worldVersion", Required = Required.Always)] public int WorldVersion { get; set; }
        /// <summary>Gets or sets the route publication identity.</summary>
        [JsonProperty("publicationId", Required = Required.Always)] public string PublicationId { get; set; }
        /// <summary>Gets or sets its exact content version.</summary>
        [JsonProperty("publicationVersion", Required = Required.Always)] public int PublicationVersion { get; set; }
        /// <summary>Gets or sets the complete immutable publication hash.</summary>
        [JsonProperty("publicationHash", Required = Required.Always)] public string PublicationHash { get; set; }
        /// <summary>Gets or sets the supported selection-policy identity.</summary>
        [JsonProperty("policyId", Required = Required.Always)] public string PolicyId { get; set; }
        /// <summary>Gets or sets its frozen ordering/stream/mapping version.</summary>
        [JsonProperty("policyVersion", Required = Required.Always)] public int PolicyVersion { get; set; }
        /// <summary>Gets or sets exact choices in retained segment order; empty means no authored segments.</summary>
        [JsonProperty("selections", Required = Required.Always)] public RouteBoardSelectionDto[] Selections { get; set; }
    }

    /// <summary>Exact authored reference and source-pool provenance, never mutable board or Unity data.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RouteBoardSelectionDto
    {
        /// <summary>Gets or sets the stable segment identity.</summary>
        [JsonProperty("segmentId", Required = Required.Always)] public string SegmentId { get; set; }
        /// <summary>Gets or sets the retained segment generation family.</summary>
        [JsonProperty("configurationId", Required = Required.Always)] public string ConfigurationId { get; set; }
        /// <summary>Gets or sets the retained generation version.</summary>
        [JsonProperty("configurationVersion", Required = Required.Always)] public int ConfigurationVersion { get; set; }
        /// <summary>Gets or sets the selected template identity.</summary>
        [JsonProperty("templateId", Required = Required.Always)] public string TemplateId { get; set; }
        /// <summary>Gets or sets its selected content version.</summary>
        [JsonProperty("templateVersion", Required = Required.Always)] public int TemplateVersion { get; set; }
        /// <summary>Gets or sets the explicit selected variant.</summary>
        [JsonProperty("variantId", Required = Required.Always)] public string VariantId { get; set; }
        /// <summary>Gets or sets the immutable selected template hash.</summary>
        [JsonProperty("templateHash", Required = Required.Always)] public string TemplateHash { get; set; }
        /// <summary>Gets or sets the source pool ID; null identifies a fixed source.</summary>
        [JsonProperty("poolId", Required = Required.AllowNull)] public string PoolId { get; set; }
        /// <summary>Gets or sets its positive version, or zero for a fixed source.</summary>
        [JsonProperty("poolVersion", Required = Required.Always)] public int PoolVersion { get; set; }
        /// <summary>Gets or sets its content hash, or null for a fixed source.</summary>
        [JsonProperty("poolHash", Required = Required.AllowNull)] public string PoolHash { get; set; }
    }
}