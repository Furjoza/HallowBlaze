using System;
using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RunStateDto
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty("schemaVersion", Required = Required.Always, Order = 1)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("runId", Required = Required.Always, Order = 2)]
        public string RunId { get; set; } = string.Empty;

        [JsonProperty("runSeed", Required = Required.Always, Order = 3)]
        public int RunSeed { get; set; }

        [JsonProperty("currentDay", Required = Required.Always, Order = 4)]
        public int CurrentDay { get; set; }

        [JsonProperty("worldNodeId", Required = Required.Always, Order = 5)]
        public string WorldNodeId { get; set; } = string.Empty;

        [JsonProperty("health", Required = Required.Always, Order = 6)]
        public int Health { get; set; }

        [JsonProperty("food", Required = Required.Always, Order = 7)]
        public int Food { get; set; }

        [JsonProperty("status", Required = Required.Always, Order = 8)]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("toolSlots", Required = Required.Always, Order = 9)]
        public ToolSlotDto[] ToolSlots { get; set; } = Array.Empty<ToolSlotDto>();

        [JsonProperty("route", Required = Required.Always, Order = 10)]
        public string[] Route { get; set; } = Array.Empty<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ToolSlotDto
    {
        [JsonProperty("toolId", Required = Required.Always, Order = 1)]
        public string ToolId { get; set; } = string.Empty;

        [JsonProperty("remainingUses", Required = Required.Always, Order = 2)]
        public int RemainingUses { get; set; }
    }

    public static class RunStatusIds
    {
        public const string Active = "active";
        public const string Dead = "dead";
        public const string Won = "won";
    }
}