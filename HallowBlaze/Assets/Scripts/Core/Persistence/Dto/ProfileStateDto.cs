using System;
using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ProfileStateDto
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty("schemaVersion", Required = Required.Always, Order = 1)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("profileId", Required = Required.Always, Order = 2)]
        public string ProfileId { get; set; } = string.Empty;

        [JsonProperty("worldDefinitionId", Required = Required.Always, Order = 3)]
        public string WorldDefinitionId { get; set; } = string.Empty;

        [JsonProperty("worldDefinitionVersion", Required = Required.Always, Order = 4)]
        public int WorldDefinitionVersion { get; set; }

        [JsonProperty("discoveredNodeIds", Required = Required.Always, Order = 5)]
        public string[] DiscoveredNodeIds { get; set; } = Array.Empty<string>();

        [JsonProperty("discoveredEdgeIds", Required = Required.Always, Order = 6)]
        public string[] DiscoveredEdgeIds { get; set; } = Array.Empty<string>();

        [JsonProperty("discoveredFactIds", Required = Required.Always, Order = 7)]
        public string[] DiscoveredFactIds { get; set; } = Array.Empty<string>();

        [JsonProperty("persistentNoteIds", Required = Required.Always, Order = 8)]
        public string[] PersistentNoteIds { get; set; } = Array.Empty<string>();

        [JsonProperty("runSummaries", Required = Required.Always, Order = 9)]
        public ProfileRunSummaryDto[] RunSummaries { get; set; } =
            Array.Empty<ProfileRunSummaryDto>();
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