using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Resolution;

namespace HallowBlaze.Core.Generation.Validation
{
    /// <summary>Owns detached authoritative definitions and fixed-source bindings used to resolve candidate IDs.</summary>
    public sealed class BoardValidationCatalog
    {
        private readonly Dictionary<(BoardLayer, string), BoardEntityDefinition> definitions =
            new Dictionary<(BoardLayer, string), BoardEntityDefinition>();
        private readonly HashSet<string> contentIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> itemFamilies = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> fixedSources = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ShamblerDefinition> shamblers = new Dictionary<string, ShamblerDefinition>(StringComparer.Ordinal);

        /// <summary>Copies known content and authored source identities without accessing prefabs, inventory or profile data.</summary>
        /// <param name="content">Unique definitions per layer/content ID; a legacy exit may use one ID on two layers.</param>
        /// <param name="itemResourceFamilies">Item definition ID to resource family; each key must resolve to a non-exit item.</param>
        /// <param name="sourceItemDefinitions">Board-local fixed-source ID to known item definition ID.</param>
        /// <param name="shamblerDefinitions">Optional enemy content ID to current Shambler rule; an unmapped spawned enemy is rejected.</param>
        /// <exception cref="ArgumentNullException">A collection is null.</exception>
        /// <exception cref="ArgumentException">Entries are null, duplicated, malformed or refer to unknown definitions.</exception>
        public BoardValidationCatalog(IEnumerable<BoardEntityDefinition> content,
            IEnumerable<KeyValuePair<string, string>> itemResourceFamilies,
            IEnumerable<KeyValuePair<string, string>> sourceItemDefinitions,
            IEnumerable<KeyValuePair<string, ShamblerDefinition>> shamblerDefinitions = null)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (itemResourceFamilies == null) throw new ArgumentNullException(nameof(itemResourceFamilies));
            if (sourceItemDefinitions == null) throw new ArgumentNullException(nameof(sourceItemDefinitions));
            foreach (BoardEntityDefinition definition in content)
            {
                if (definition == null || definitions.ContainsKey((definition.Layer, definition.ContentId)))
                    throw new ArgumentException("Catalog definitions must be non-null and unique per layer/content ID.", nameof(content));
                definitions.Add((definition.Layer, definition.ContentId), definition);
                contentIds.Add(definition.ContentId);
            }
            foreach (KeyValuePair<string, string> pair in itemResourceFamilies)
            {
                GenerationContract.RequireId(pair.Key, nameof(itemResourceFamilies));
                GenerationContract.RequireId(pair.Value, nameof(itemResourceFamilies));
                if (itemFamilies.ContainsKey(pair.Key) || !definitions.TryGetValue((BoardLayer.Item, pair.Key), out BoardEntityDefinition definition) || definition.Traits.IsExit)
                    throw new ArgumentException("Item families require unique known non-exit item definitions.", nameof(itemResourceFamilies));
                itemFamilies.Add(pair.Key, pair.Value);
            }
            foreach (KeyValuePair<string, string> pair in sourceItemDefinitions)
            {
                GenerationContract.RequireId(pair.Key, nameof(sourceItemDefinitions));
                GenerationContract.RequireId(pair.Value, nameof(sourceItemDefinitions));
                if (fixedSources.ContainsKey(pair.Key) || !itemFamilies.ContainsKey(pair.Value))
                    throw new ArgumentException("Sources require unique IDs and known item definitions.", nameof(sourceItemDefinitions));
                fixedSources.Add(pair.Key, pair.Value);
            }
            foreach (KeyValuePair<string, ShamblerDefinition> pair in shamblerDefinitions ?? Array.Empty<KeyValuePair<string, ShamblerDefinition>>())
            {
                GenerationContract.RequireId(pair.Key, nameof(shamblerDefinitions));
                if (pair.Value == null || shamblers.ContainsKey(pair.Key) ||
                    !definitions.TryGetValue((BoardLayer.Actor, pair.Key), out BoardEntityDefinition definition) || !definition.Kind.Equals(EntityKind.Enemy))
                    throw new ArgumentException("Shambler rules require unique known enemy content IDs and non-null definitions.", nameof(shamblerDefinitions));
                shamblers.Add(pair.Key, pair.Value);
            }
        }

        internal bool ContainsContent(string identifier) => contentIds.Contains(identifier);
        internal IReadOnlyList<BoardEntityDefinition> GetDefinitions()
        {
            var ordered = new List<BoardEntityDefinition>(definitions.Values);
            ordered.Sort((left, right) =>
            {
                int layer = left.Layer.CompareTo(right.Layer);
                return layer != 0 ? layer : string.CompareOrdinal(left.ContentId, right.ContentId);
            });
            return ordered.AsReadOnly();
        }
        internal bool TryGetDefinition(BoardEntityDefinition candidate, out BoardEntityDefinition definition) =>
            definitions.TryGetValue((candidate.Layer, candidate.ContentId), out definition);
        internal bool TryGetItemFamily(string identifier, out string family) => itemFamilies.TryGetValue(identifier, out family);
        internal bool TryGetSourceDefinition(string identifier, out string definition) => fixedSources.TryGetValue(identifier, out definition);
        internal bool ContainsFamily(string family) => itemFamilies.ContainsValue(family);
        /// <summary>Resolves the authoritative enemy rule for runtime construction without reading a prefab's gameplay data.</summary>
        /// <param name="identifier">Stable enemy content ID.</param>
        /// <param name="definition">The registered immutable rule, or null when no supported rule is registered.</param>
        /// <returns>True only for an explicitly registered current Shambler rule.</returns>
        public bool TryGetShambler(string identifier, out ShamblerDefinition definition) => shamblers.TryGetValue(identifier, out definition);
    }
}