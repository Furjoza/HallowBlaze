using System;
using System.Collections.Generic;
using HallowBlaze.Core.State;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Loads working English fact content separately from durable profile IDs and tool-source knowledge.</summary>
    public static class FactContentCatalog
    {
        /// <summary>Loads the authored registry without discovering a fact or exposing unknown entries through a notes projection.</summary>
        public static FactKnowledge Load()
        {
            TextAsset asset = Resources.Load<TextAsset>("SystemFacts");
            if (asset == null) throw new InvalidOperationException("The system fact registry is missing.");
            return Read(asset.text);
        }

        /// <summary>Rejects malformed content before publication; saved IDs remain independent of this definition registry.</summary>
        public static FactKnowledge Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Fact content is required.", nameof(json));
            FactDocument document = JsonUtility.FromJson<FactDocument>(json);
            if (document?.facts == null || document.facts.Length == 0) throw new ArgumentException("Fact content must declare facts.", nameof(json));
            var definitions = new List<FactDefinition>();
            foreach (FactRecord record in document.facts)
            {
                if (record == null || !Enum.TryParse(record.observation, false, out FactObservation observation) ||
                    observation == FactObservation.None || !Enum.IsDefined(typeof(FactObservation), observation) ||
                    record.iconId != "axe" && record.iconId != "pit" && record.iconId != "hearing")
                    throw new ArgumentException("A fact requires a supported observation and icon.", nameof(json));
                definitions.Add(new FactDefinition(record.id, observation, record.title, record.description, record.iconId, record.sourceSystem));
            }
            return new FactKnowledge(definitions);
        }

        [Serializable]
        private sealed class FactDocument { public FactRecord[] facts; }
        [Serializable]
        private sealed class FactRecord
        {
            public string id;
            public string observation;
            public string title;
            public string description;
            public string iconId;
            public string sourceSystem;
        }
    }
}