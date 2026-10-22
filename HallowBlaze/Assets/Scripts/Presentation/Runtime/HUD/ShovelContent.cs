using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Resolution;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Loads explicit cache and diggable terrain content without changing equipment or versioned generator placement.</summary>
    public static class ShovelContent
    {
        /// <summary>Loads the required shared manual/shovel rules and concrete transitional reward definition.</summary>
        public static ShovelRules LoadRules()
        {
            TextAsset resource = Resources.Load<TextAsset>("ShovelDigging");
            if (resource == null) throw new InvalidOperationException("Shovel digging content is missing.");
            return ReadRules(resource.text);
        }

        /// <summary>Rejects malformed content before a runtime is published; no tool-only or partial rules are installed.</summary>
        public static ShovelRules ReadRules(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Digging content is required.", nameof(json));
            DiggingDocument content = JsonUtility.FromJson<DiggingDocument>(json);
            if (content == null) throw new ArgumentException("Digging content is required.", nameof(json));
            return new ShovelRules(content.cacheContentId, content.diggableTerrainId, content.cacheWork,
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, content.rewardDefinitionId,
                    new BoardEntityTraits(false, false, true), content.rewardFood), content.pitLifetimeTurns,
                content.noiseStrength, content.noiseLifetimeTurns);
        }

        /// <summary>Registers clearing, cache work and explicit boulder pushing without paid contextual no-op alternatives.</summary>
        public static InteractionQuery CreateInteractions(ObstacleClearingRules axe, ShovelRules shovel) => new InteractionQuery(new IInteractionAction[]
        {
            new ContextInteractionAction(shovel.CacheContentId, BoulderPushAction.ContentId), new CollectInteractionAction(),
            new ClearObstacleAction(axe, false), new CacheDigAction(shovel, false), new BoulderPushAction()
        });

        /// <summary>Registers independent definition-specific axe and shovel effects; no item is automatically equipped.</summary>
        public static ToolUseQuery CreateTools(ObstacleClearingRules axe, ShovelRules shovel) => new ToolUseQuery(new[]
        {
            new ToolAction(ObstacleClearingRules.AxeDefinitionId, new ClearObstacleAction(axe, true), "Chop: clear obstacle",
                axe.AxeNoiseStrength, axe.NoiseLifetimeTurns, ObstacleClearingRules.AxeNoiseFactId),
            new ToolAction(ShovelRules.DefinitionId, new CacheDigAction(shovel, true), "Unearth cache", shovel.NoiseStrength, shovel.NoiseLifetimeTurns),
            new ToolAction(ShovelRules.DefinitionId, new PitDigAction(shovel), "Dig pit", shovel.NoiseStrength, shovel.NoiseLifetimeTurns)
        });

        [Serializable]
        private sealed class DiggingDocument
        {
            public string cacheContentId;
            public string diggableTerrainId;
            public int cacheWork;
            public string rewardDefinitionId;
            public int rewardFood;
            public int pitLifetimeTurns;
            public int noiseStrength;
            public int noiseLifetimeTurns;
        }
    }
}