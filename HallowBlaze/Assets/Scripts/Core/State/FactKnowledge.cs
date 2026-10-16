using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.State
{
    /// <summary>A detached, spoiler-safe notes entry; it never owns mutable discovery state.</summary>
    public sealed class FieldNoteEntry
    {
        internal FieldNoteEntry(string id, bool discovered, FactDefinition definition)
        {
            FactId = discovered ? id : null;
            IsDiscovered = discovered;
            IsMissingDefinition = discovered && definition == null;
            Title = !discovered ? "Unknown observation" : definition?.Title ?? "Uncatalogued observation";
            Description = !discovered ? "" : definition?.Description ?? "This recorded observation has no available text.";
            IconId = discovered ? definition?.IconId ?? "missing" : "unknown";
            SourceSystem = discovered ? definition?.SourceSystem : null;
        }
        /// <summary>Gets a recorded profile key, or null while unknown to avoid exposing a rule through its identifier.</summary>
        public string FactId { get; }
        /// <summary>Gets whether the profile, not the UI, owns this fact.</summary>
        public bool IsDiscovered { get; }
        /// <summary>Gets whether a saved ID requires a controlled placeholder because content is unavailable.</summary>
        public bool IsMissingDefinition { get; }
        /// <summary>Gets the known title or a generic placeholder without hints about an unknown rule.</summary>
        public string Title { get; }
        /// <summary>Gets the recorded explanation; unknown entries contain no explanation.</summary>
        public string Description { get; }
        /// <summary>Gets an icon key; unknown entries never reveal the fact-specific icon.</summary>
        public string IconId { get; }
        /// <summary>Gets known mechanical provenance, or null for unknown/missing content.</summary>
        public string SourceSystem { get; }
    }

    /// <summary>Maps explicit presented observations to durable profile facts, without unlocking or consulting gameplay rules.</summary>
    public sealed class FactKnowledge
    {
        private readonly Dictionary<string, FactDefinition> definitions = new Dictionary<string, FactDefinition>(StringComparer.Ordinal);
        private readonly List<FactDefinition> ordered = new List<FactDefinition>();

        /// <summary>Freezes a validated authored registry. Duplicate IDs or null definitions are rejected before use.</summary>
        public FactKnowledge(IEnumerable<FactDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (FactDefinition definition in definitions)
            {
                if (definition == null || this.definitions.ContainsKey(definition.Id))
                    throw new ArgumentException("Facts must have distinct definitions.", nameof(definitions));
                this.definitions.Add(definition.Id, definition);
                ordered.Add(definition);
            }
        }

        /// <summary>Records only facts matching a supported actual observation; duplicate observations create no event.</summary>
        /// <returns>Newly recorded discoveries in authored order. Persistence and feedback consume these, never previews.</returns>
        public IReadOnlyList<FactDiscovery> Observe(ProfileState profile, FactObservation observation)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var discoveries = new List<FactDiscovery>();
            foreach (FactDefinition definition in ordered)
                if (definition.Observation == observation && profile.DiscoverFact(definition.Id)) discoveries.Add(new FactDiscovery(definition));
            return discoveries.AsReadOnly();
        }

        /// <summary>Projects one fact through profile knowledge; no unknown rule ID, title, explanation or icon can leak through this entry.</summary>
        public FieldNoteEntry GetEntry(ProfileState profile, string factId)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrWhiteSpace(factId)) throw new ArgumentException("A fact ID is required.", nameof(factId));
            bool discovered = false;
            foreach (string known in profile.DiscoveredFactIds) discovered |= known == factId;
            definitions.TryGetValue(factId, out FactDefinition definition);
            return new FieldNoteEntry(factId, discovered, definition);
        }

        /// <summary>Reads only recorded entries in durable discovery order, retaining placeholders for unavailable definitions.</summary>
        /// <returns>No unknown checklist, total, completion percentage or source-location knowledge.</returns>
        public IReadOnlyList<FieldNoteEntry> GetEntries(ProfileState profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var entries = new List<FieldNoteEntry>();
            foreach (string id in profile.DiscoveredFactIds) entries.Add(GetEntry(profile, id));
            return entries.AsReadOnly();
        }
    }
}