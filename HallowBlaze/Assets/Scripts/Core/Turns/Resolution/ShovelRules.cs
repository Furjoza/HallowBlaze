using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Immutable opt-in shovel content; ordinary floors, mandatory exits and arbitrary objects are not digging targets.</summary>
    public sealed class ShovelRules
    {
        /// <summary>Stable shovel definition shared by both behaviors and separate from run/source identities.</summary>
        public const string DefinitionId = "shovel.basic";
        /// <summary>Creates manual/cache/pit rules with an explicit concrete reward and published lifetime.</summary>
        public ShovelRules(string cacheContentId, string diggableTerrainId, int cacheWork,
            BoardEntityDefinition rewardDefinition, int pitLifetimeTurns, int noiseStrength, int noiseLifetimeTurns)
        {
            RequireId(cacheContentId);
            RequireId(diggableTerrainId);
            if (cacheWork < 2) throw new ArgumentOutOfRangeException(nameof(cacheWork));
            RewardDefinition = rewardDefinition ?? throw new ArgumentNullException(nameof(rewardDefinition));
            if (rewardDefinition.Layer != BoardLayer.Item || rewardDefinition.Traits.IsExit ||
                !rewardDefinition.Kind.Equals(EntityKind.Item) || rewardDefinition.AutomaticFoodReward <= 0 ||
                rewardDefinition.ContentId == cacheContentId)
                throw new ArgumentException("The transitional reward must be a distinct concrete food item.", nameof(rewardDefinition));
            if (pitLifetimeTurns < 2) throw new ArgumentOutOfRangeException(nameof(pitLifetimeTurns));
            if (noiseStrength < 0) throw new ArgumentOutOfRangeException(nameof(noiseStrength));
            if (noiseLifetimeTurns < 1) throw new ArgumentOutOfRangeException(nameof(noiseLifetimeTurns));
            CacheContentId = cacheContentId;
            DiggableTerrainId = diggableTerrainId;
            CacheWork = cacheWork;
            PitLifetimeTurns = pitLifetimeTurns;
            NoiseStrength = noiseStrength;
            NoiseLifetimeTurns = noiseLifetimeTurns;
        }
        /// <summary>Gets the exact marked item content, not every food or ground object.</summary>
        public string CacheContentId { get; }
        /// <summary>Gets the explicitly authored diggable terrain content, never every walkable cell.</summary>
        public string DiggableTerrainId { get; }
        /// <summary>Gets manual accepted actions for a fresh cache; one shovel use completes the work.</summary>
        public int CacheWork { get; }
        /// <summary>Gets the stable concrete item definition used by the transitional transfer adapter.</summary>
        public BoardEntityDefinition RewardDefinition { get; }
        /// <summary>Gets subsequent accepted commands before the pit and any hold expire.</summary>
        public int PitLifetimeTurns { get; }
        /// <summary>Gets the explicit digging noise strength for both alternatives.</summary>
        public int NoiseStrength { get; }
        /// <summary>Gets the positive lifetime for digging noise.</summary>
        public int NoiseLifetimeTurns { get; }
        /// <summary>Tests exact marked-cache applicability without consulting knowledge or charges.</summary>
        public bool IsCache(BoardEntityState target) => target.Definition.Layer == BoardLayer.Item &&
            target.Definition.ContentId == CacheContentId && !target.Definition.Traits.IsExit;
        /// <summary>Tests exact opt-in terrain; occupancy and exits are revalidated separately.</summary>
        public bool IsDiggable(BoardEntityState target) => target.Definition.Layer == BoardLayer.Terrain &&
            target.Definition.ContentId == DiggableTerrainId;
        private static void RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim()) throw new ArgumentException("A stable digging content ID is required.");
        }
    }
}