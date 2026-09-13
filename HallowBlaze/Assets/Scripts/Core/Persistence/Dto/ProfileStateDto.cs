using System;
using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    /// <summary>
    /// Represents the versioned persisted form of durable profile state.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ProfileStateDto
    {
        /// <summary>Identifies the only profile schema accepted by the current mapper.</summary>
        public const int CurrentSchemaVersion = 2;

        [JsonProperty("schemaVersion", Required = Required.Always, Order = 1)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("profileId", Required = Required.Always, Order = 2)]
        public string ProfileId { get; set; } = string.Empty;

        [JsonProperty("worldDefinitionId", Required = Required.Always, Order = 3)]
        public string WorldDefinitionId { get; set; } = string.Empty;

        [JsonProperty("worldDefinitionVersion", Required = Required.Always, Order = 4)]
        public int WorldDefinitionVersion { get; set; }

        /// <summary>Gets or sets persisted node knowledge in first-discovery order.</summary>
        [JsonProperty("nodeDiscoveries", Required = Required.Always, Order = 5)]
        public NodeDiscoveryDto[] NodeDiscoveries { get; set; } =
            Array.Empty<NodeDiscoveryDto>();

        /// <summary>Gets or sets persisted edge knowledge in first-discovery order.</summary>
        [JsonProperty("edgeDiscoveries", Required = Required.Always, Order = 6)]
        public EdgeDiscoveryDto[] EdgeDiscoveries { get; set; } =
            Array.Empty<EdgeDiscoveryDto>();

        [JsonProperty("discoveredFactIds", Required = Required.Always, Order = 7)]
        public string[] DiscoveredFactIds { get; set; } = Array.Empty<string>();

        [JsonProperty("persistentNoteIds", Required = Required.Always, Order = 8)]
        public string[] PersistentNoteIds { get; set; } = Array.Empty<string>();

        [JsonProperty("runSummaries", Required = Required.Always, Order = 9)]
        public ProfileRunSummaryDto[] RunSummaries { get; set; } =
            Array.Empty<ProfileRunSummaryDto>();
    }

    /// <summary>
    /// Persists one known node and its monotonic discovery state.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class NodeDiscoveryDto
    {
        /// <summary>Gets or sets the stable ID of the known world node.</summary>
        [JsonProperty("nodeId", Required = Required.Always, Order = 1)]
        public string NodeId { get; set; } = string.Empty;

        /// <summary>Gets or sets the stable textual node discovery state.</summary>
        [JsonProperty("state", Required = Required.Always, Order = 2)]
        public string State { get; set; } = string.Empty;
    }

    /// <summary>
    /// Persists one known directed edge and its monotonic discovery state.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class EdgeDiscoveryDto
    {
        /// <summary>Gets or sets the stable ID of the known directed edge.</summary>
        [JsonProperty("edgeId", Required = Required.Always, Order = 1)]
        public string EdgeId { get; set; } = string.Empty;

        /// <summary>Gets or sets the stable textual edge discovery state.</summary>
        [JsonProperty("state", Required = Required.Always, Order = 2)]
        public string State { get; set; } = string.Empty;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ProfileRunSummaryDto
    {
        [JsonProperty("runId", Required = Required.Always, Order = 1)]
        public string RunId { get; set; } = string.Empty;

        [JsonProperty("daysSurvived", Required = Required.Always, Order = 2)]
        public int DaysSurvived { get; set; }

        [JsonProperty("status", Required = Required.Always, Order = 3)]
        public string Status { get; set; } = string.Empty;
    }
}