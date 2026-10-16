using System;
using HallowBlaze.Core.Turns.Resolution;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Loads explicit soft-wood clearing behavior without granting equipment or modifying generated layouts.</summary>
    public static class AxeContent
    {
        /// <summary>Loads the required shared rules for manual clearing and the stable axe definition.</summary>
        public static ObstacleClearingRules LoadRules()
        {
            TextAsset resource = Resources.Load<TextAsset>("AxeClearing");
            if (resource == null) throw new InvalidOperationException("Axe clearing content is missing.");
            return ReadRules(resource.text);
        }

        /// <summary>Validates authored numbers; malformed content never publishes a partial or tool-only route.</summary>
        public static ObstacleClearingRules ReadRules(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Clearing content is required.", nameof(json));
            ClearingDocument content = JsonUtility.FromJson<ClearingDocument>(json);
            if (content == null) throw new ArgumentException("Clearing content is required.", nameof(json));
            return new ObstacleClearingRules(content.obstacleContentIds, content.requiredWork,
                content.manualNoiseStrength, content.axeNoiseStrength, content.noiseLifetimeTurns);
        }

        /// <summary>Creates the manual alternative alongside the existing context/collect actions.</summary>
        public static InteractionQuery CreateInteractions(ObstacleClearingRules rules) => new InteractionQuery(new IInteractionAction[]
            { new ContextInteractionAction(), new CollectInteractionAction(), new ClearObstacleAction(rules, false) });

        /// <summary>Registers chopping only for the stable axe definition; the shovel remains without an effect.</summary>
        public static ToolUseQuery CreateTools(ObstacleClearingRules rules) => new ToolUseQuery(new[]
        {
            new ToolAction(ObstacleClearingRules.AxeDefinitionId, new ClearObstacleAction(rules, true), "Chop: clear obstacle",
                rules.AxeNoiseStrength, rules.NoiseLifetimeTurns, ObstacleClearingRules.AxeNoiseFactId)
        });

        [Serializable]
        private sealed class ClearingDocument
        {
            public string[] obstacleContentIds;
            public int requiredWork;
            public int manualNoiseStrength;
            public int axeNoiseStrength;
            public int noiseLifetimeTurns;
        }
    }
}