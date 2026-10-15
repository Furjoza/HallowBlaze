using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    /// <summary>Extends the stable travel boundary with explicit layout RNG compatibility metadata.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RunStateV3Dto : RunStateV2Dto
    {
        /// <summary>Gets the run-only schema version; profile and historical DTO versions are unchanged.</summary>
        public new const int CurrentSchemaVersion = 3;

        /// <summary>Initializes a v3 document; both RNG fields remain required on input.</summary>
        public RunStateV3Dto()
        {
            SchemaVersion = CurrentSchemaVersion;
        }

        internal RunStateV3Dto(RunStateV2Dto travel) : this()
        {
            RunId = travel.RunId;
            RunSeed = travel.RunSeed;
            CompletedDayCount = travel.CompletedDayCount;
            ReachedCheckpointNodeId = travel.ReachedCheckpointNodeId;
            TraversalSequence = travel.TraversalSequence;
            Health = travel.Health;
            Food = travel.Food;
            Status = travel.Status;
            ToolSlots = travel.ToolSlots;
            TravelHistory = travel.TravelHistory;
            ActiveLeg = travel.ActiveLeg;
        }

        /// <summary>Gets or sets the exact layout RNG identifier, including the explicit legacy adapter.</summary>
        [JsonProperty("randomAlgorithmId", Required = Required.Always, Order = 13)]
        public string RandomAlgorithmId { get; set; }

        /// <summary>Gets or sets the positive generator/derivation version; unknown versions are rejected.</summary>
        [JsonProperty("randomAlgorithmVersion", Required = Required.Always, Order = 14)]
        public int RandomAlgorithmVersion { get; set; }
    }
}