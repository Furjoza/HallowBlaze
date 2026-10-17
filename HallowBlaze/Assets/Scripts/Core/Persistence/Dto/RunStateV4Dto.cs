using System;
using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    /// <summary>Extends the frozen travel fields with identity-bearing equipment and its durable allocator.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RunStateV4Dto : RunStateV2Dto
    {
        /// <summary>Gets the equipment-aware run schema; profile and historical schemas remain unchanged.</summary>
        public new const int CurrentSchemaVersion = 4;

        /// <summary>Initializes a v4 document; required fields still must be present when reading JSON.</summary>
        public RunStateV4Dto() { SchemaVersion = CurrentSchemaVersion; }

        internal RunStateV4Dto(RunStateV3Dto travel) : this()
        {
            RunId = travel.RunId;
            RunSeed = travel.RunSeed;
            CompletedDayCount = travel.CompletedDayCount;
            ReachedCheckpointNodeId = travel.ReachedCheckpointNodeId;
            TraversalSequence = travel.TraversalSequence;
            Health = travel.Health;
            Food = travel.Food;
            Status = travel.Status;
            TravelHistory = travel.TravelHistory;
            ActiveLeg = travel.ActiveLeg;
            RandomAlgorithmId = travel.RandomAlgorithmId;
            RandomAlgorithmVersion = travel.RandomAlgorithmVersion;
        }

        /// <summary>Gets or sets exactly two cells; null means empty, not an unknown or depleted tool.</summary>
        [JsonProperty("toolSlots", Required = Required.Always, Order = 10)]
        public new ToolInstanceDto[] ToolSlots { get; set; } = Array.Empty<ToolInstanceDto>();
        /// <summary>Gets or sets the unchanged persisted layout RNG identity.</summary>
        [JsonProperty("randomAlgorithmId", Required = Required.Always, Order = 13)]
        public string RandomAlgorithmId { get; set; }
        /// <summary>Gets or sets the unchanged RNG version.</summary>
        [JsonProperty("randomAlgorithmVersion", Required = Required.Always, Order = 14)]
        public int RandomAlgorithmVersion { get; set; }
        /// <summary>Gets or sets the last allocated tool number, including removed or replaced instances.</summary>
        [JsonProperty("toolInstanceSequence", Required = Required.Always, Order = 15)]
        public int ToolInstanceSequence { get; set; }

        internal RunStateV3Dto CreateTravelDocument() => new RunStateV3Dto(new RunStateV2Dto
        {
            RunId = RunId, RunSeed = RunSeed, CompletedDayCount = CompletedDayCount,
            ReachedCheckpointNodeId = ReachedCheckpointNodeId, TraversalSequence = TraversalSequence,
            Health = Health, Food = Food, Status = Status, TravelHistory = TravelHistory, ActiveLeg = ActiveLeg,
            ToolSlots = new[] { new ToolSlotDto { ToolId = "" }, new ToolSlotDto { ToolId = "" } }
        }) { RandomAlgorithmId = RandomAlgorithmId, RandomAlgorithmVersion = RandomAlgorithmVersion };
    }

    /// <summary>Scalar tool state with no Unity asset, source identity, or shared definition mutation.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ToolInstanceDto
    {
        /// <summary>Gets or sets the run-local instance ID, not an atlas source ID.</summary>
        [JsonProperty("instanceId", Required = Required.Always)]
        public string InstanceId { get; set; }
        /// <summary>Gets or sets the stable content ID; unknown content is retained rather than substituted.</summary>
        [JsonProperty("definitionId", Required = Required.Always)]
        public string DefinitionId { get; set; }
        /// <summary>Gets or sets remaining nonnegative uses; zero still represents an occupied slot.</summary>
        [JsonProperty("charges", Required = Required.Always)]
        public int Charges { get; set; }
    }
}