using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HallowBlaze.Core.State;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Loads immutable prototype tool definitions; asset data never stores a run's remaining charges.</summary>
    public static class ToolContentCatalog
    {
        /// <summary>Loads the authored placeholder axe/shovel content without allocating or equipping instances.</summary>
        /// <returns>A detached read-only catalog keyed by stable definition ID.</returns>
        /// <exception cref="InvalidOperationException">The required content resource is missing.</exception>
        public static IReadOnlyDictionary<string, ToolDefinition> Load()
        {
            TextAsset resource = Resources.Load<TextAsset>("ToolDefinitions");
            if (resource == null) throw new InvalidOperationException("The tool definition resource is missing.");
            return Read(resource.text);
        }

        /// <summary>Validates authored placeholder content; malformed or duplicate definitions never publish a partial catalog.</summary>
        /// <param name="json">A document with a nonempty tools array of stable IDs, names and positive capacities.</param>
        /// <returns>Read-only definitions whose values are immutable across all runs.</returns>
        public static IReadOnlyDictionary<string, ToolDefinition> Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Tool content is required.", nameof(json));
            ContentDocument document = JsonUtility.FromJson<ContentDocument>(json);
            if (document?.tools == null || document.tools.Length == 0)
                throw new ArgumentException("Tool content must declare definitions.", nameof(json));
            var definitions = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
            foreach (ContentEntry entry in document.tools)
            {
                if (entry == null) throw new ArgumentException("A tool entry is missing.", nameof(json));
                var definition = new ToolDefinition(entry.id, entry.displayName, entry.maximumCharges);
                definitions.Add(definition.Id, definition);
            }
            return new ReadOnlyDictionary<string, ToolDefinition>(definitions);
        }

        [Serializable]
        private sealed class ContentDocument { public ContentEntry[] tools; }
        [Serializable]
        private sealed class ContentEntry { public string id; public string displayName; public int maximumCharges; }
    }
}