using System;
using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    /// <summary>Adds authored selections and completed outcomes to the frozen equipment/travel boundary.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RunStateV5Dto : RunStateV4Dto
    {
        /// <summary>Gets the landmark-aware run schema; profile schema is unchanged.</summary>
        public new const int CurrentSchemaVersion = 5;
        /// <summary>Initializes the current version; required fields remain mandatory on read.</summary>
        public RunStateV5Dto() { SchemaVersion = CurrentSchemaVersion; }
        /// <summary>Gets or sets the exact selected board, or null at ordinary/completed boundaries.</summary>
        [JsonProperty("activeLandmark", Required = Required.AllowNull, Order = 16)]
        public LandmarkRecordDto ActiveLandmark { get; set; }
        /// <summary>Gets or sets completed results in travel order, with no view or mutable board data.</summary>
        [JsonProperty("completedLandmarks", Required = Required.Always, Order = 17)]
        public LandmarkRecordDto[] CompletedLandmarks { get; set; } = Array.Empty<LandmarkRecordDto>();
    }

    /// <summary>Scalar stable identity of an authored board selection/result.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class LandmarkRecordDto
    {
        /// <summary>Gets or sets the original run ID.</summary>
        [JsonProperty("runId", Required = Required.Always)] public string RunId { get; set; }
        /// <summary>Gets or sets the run-local traversal ID.</summary>
        [JsonProperty("legId", Required = Required.Always)] public string LegId { get; set; }
        /// <summary>Gets or sets the authored road ID.</summary>
        [JsonProperty("edgeId", Required = Required.Always)] public string EdgeId { get; set; }
        /// <summary>Gets or sets the exact authored segment ID.</summary>
        [JsonProperty("segmentId", Required = Required.Always)] public string SegmentId { get; set; }
        /// <summary>Gets or sets the one-based travel day.</summary>
        [JsonProperty("day", Required = Required.Always)] public int Day { get; set; }
        /// <summary>Gets or sets the selected generation family.</summary>
        [JsonProperty("configurationId", Required = Required.Always)] public string ConfigurationId { get; set; }
        /// <summary>Gets or sets the selected generation version.</summary>
        [JsonProperty("generationVersion", Required = Required.Always)] public int GenerationVersion { get; set; }
        /// <summary>Gets or sets the unchanged board seed.</summary>
        [JsonProperty("boardSeed", Required = Required.Always)] public int BoardSeed { get; set; }
        /// <summary>Gets or sets the stable template ID.</summary>
        [JsonProperty("templateId", Required = Required.Always)] public string TemplateId { get; set; }
        /// <summary>Gets or sets the selected template content version.</summary>
        [JsonProperty("templateVersion", Required = Required.Always)] public int TemplateVersion { get; set; }
        /// <summary>Gets or sets the stable variant ID.</summary>
        [JsonProperty("variantId", Required = Required.Always)] public string VariantId { get; set; }
        /// <summary>Gets or sets the completed outcome; null is allowed only on the active selection.</summary>
        [JsonProperty("outcomeId", Required = Required.AllowNull)] public string OutcomeId { get; set; }
    }
}