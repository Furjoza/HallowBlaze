using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Core.Generation
{
    /// <summary>Preflights one immutable route publication and resolves only its exact retained authored selections.</summary>
    public sealed class BoardPoolPublication
    {
        private readonly WorldDefinition world;
        private readonly WorldEdgeDefinition edge;
        private readonly Dictionary<string, Source> sources = new Dictionary<string, Source>(StringComparer.Ordinal);
        private readonly JObject manifest;

        private sealed class Candidate
        {
            internal LandmarkTemplate Template;
            internal string Variant;
            internal string Hash;
        }

        private sealed class Source
        {
            internal string PoolId;
            internal int PoolVersion;
            internal string PoolHash;
            internal Candidate[] Candidates;
        }

        private BoardPoolPublication(string worldJson, string publicationJson, IEnumerable<string> templateJsons, BoardValidationCatalog catalog)
        {
            if (catalog == null || templateJsons == null) throw new ArgumentNullException(nameof(catalog));
            world = WorldDefinitionJson.Deserialize(worldJson);
            JObject data = ReadObject(publicationJson);
            Fields(data, "schemaVersion", "publicationId", "publicationVersion", "worldDefinitionId", "worldVersion", "edgeId", "sources");
            if (Integer(data, "schemaVersion") != 1 || Text(data, "worldDefinitionId") != world.WorldDefinitionId || Integer(data, "worldVersion") != world.Version)
                throw new ArgumentException("Pool schema or world publication mismatch.");
            Id = Text(data, "publicationId");
            Version = Integer(data, "publicationVersion");
            GenerationContract.RequireId(Id, nameof(Id));
            if (Version < 1) throw new ArgumentException("A positive route publication version is required.");
            edge = world.Edges.SingleOrDefault(candidate => candidate.EdgeId == Text(data, "edgeId")) ?? throw new ArgumentException("Unknown route.");
            var templates = new Dictionary<(string, int), (LandmarkTemplate Template, string Hash)>();
            foreach (string json in templateJsons)
            {
                LandmarkTemplate template = BoardTemplateJson.ReadCompatible(json, catalog);
                if (!template.IsShared) throw new ArgumentException("Segment pools require shared board snapshots; strict legacy landmark selection is unchanged.");
                string hash = Hash(ReadObject(json));
                var key = (template.Id, template.Version);
                if (templates.TryGetValue(key, out var previous) && previous.Hash != hash)
                    throw new ArgumentException("Conflicting template/version: " + template.Id + "/" + template.Version);
                templates[key] = (template, hash);
            }
            if (!(data["sources"] is JArray sourceRows)) throw new ArgumentException("An explicit source array is required.");
            var normalized = new JArray();
            var poolHashes = new Dictionary<(string, int), string>();
            foreach (JToken token in sourceRows)
            {
                if (!(token is JObject row)) throw new ArgumentException("Segment sources must be objects.");
                Fields(row, "segmentId", "generationConfigurationId", "generationVersion", "poolId", "poolVersion", "entries");
                string segmentId = Text(row, "segmentId");
                if (!world.TryGetSegment(segmentId, out RouteSegmentDefinition segment, out WorldEdgeDefinition owner) || owner.EdgeId != edge.EdgeId ||
                    segment.GenerationConfigurationId != Text(row, "generationConfigurationId") || segment.GenerationVersion != Integer(row, "generationVersion") ||
                    sources.ContainsKey(segmentId)) throw new ArgumentException("Foreign, duplicate or incompatible segment source: " + segmentId);
                string poolId = row["poolId"].Type == JTokenType.Null ? null : Text(row, "poolId");
                int poolVersion = Integer(row, "poolVersion");
                if (poolId == null ? poolVersion != 0 : poolVersion < 1) throw new ArgumentException("Invalid pool version.");
                if (poolId != null) GenerationContract.RequireId(poolId, nameof(poolId));
                if (!(row["entries"] is JArray entries) || entries.Count == 0 || poolId == null && entries.Count != 1)
                    throw new ArgumentException("A pool must be nonempty; a fixed source requires exactly one entry.");
                var candidates = new List<Candidate>();
                var unique = new HashSet<(string, int, string)>();
                foreach (JToken entryToken in entries)
                {
                    if (!(entryToken is JObject entry)) throw new ArgumentException("Pool entries must be exact reference objects.");
                    Fields(entry, "templateId", "templateVersion", "variantId");
                    string templateId = Text(entry, "templateId");
                    int templateVersion = Integer(entry, "templateVersion");
                    string variant = Text(entry, "variantId");
                    if (!unique.Add((templateId, templateVersion, variant)) || !templates.TryGetValue((templateId, templateVersion), out var captured))
                        throw new ArgumentException("Duplicate or missing exact pool entry: " + templateId);
                    var assignment = new BoardAssignment(world, segmentId, captured.Template, variant);
                    var address = new BoardAddress("pool-preflight", "leg.1", edge.EdgeId, segmentId, 1,
                        segment.GenerationConfigurationId, segment.GenerationVersion, 0);
                    if (!assignment.CreateBoard(address).Validate(catalog).IsValid) throw new ArgumentException("Invalid pool candidate: " + templateId + "/" + variant);
                    candidates.Add(new Candidate { Template = captured.Template, Variant = variant, Hash = captured.Hash });
                }
                Candidate[] ordered = candidates.OrderBy(candidate => candidate.Template.Id, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Template.Version).ThenBy(candidate => candidate.Variant, StringComparer.Ordinal).ToArray();
                var normalizedEntries = new JArray(ordered.Select(candidate => new JObject
                {
                    ["templateId"] = candidate.Template.Id, ["templateVersion"] = candidate.Template.Version,
                    ["variantId"] = candidate.Variant, ["templateHash"] = candidate.Hash
                }));
                string poolHash = poolId == null ? null : Hash(new JObject
                { ["poolId"] = poolId, ["poolVersion"] = poolVersion, ["entries"] = normalizedEntries.DeepClone() });
                if (poolId != null)
                {
                    if (poolHashes.TryGetValue((poolId, poolVersion), out string previous) && previous != poolHash)
                        throw new ArgumentException("Conflicting pool identity/version: " + poolId);
                    poolHashes[(poolId, poolVersion)] = poolHash;
                }
                sources.Add(segmentId, new Source { PoolId = poolId, PoolVersion = poolVersion, PoolHash = poolHash, Candidates = ordered });
                JObject copy = (JObject)row.DeepClone();
                copy["entries"] = new JArray(ordered.Select(candidate => new JObject
                { ["templateId"] = candidate.Template.Id, ["templateVersion"] = candidate.Template.Version, ["variantId"] = candidate.Variant }));
                normalized.Add(copy);
            }
            foreach (RouteSegmentDefinition segment in edge.Segments)
                if (!sources.ContainsKey(segment.SegmentId) && (segment.GenerationConfigurationId != OrdinaryBoardGenerator.ConfigurationId ||
                    segment.GenerationVersion < 1 || segment.GenerationVersion > 6))
                    throw new ArgumentException("Unsupported unassigned generator: " + segment.SegmentId);
            manifest = (JObject)data.DeepClone();
            manifest["sources"] = new JArray(normalized.OfType<JObject>().OrderBy(row => (string)row["segmentId"], StringComparer.Ordinal));
            ContentHash = Hash(new JObject
            {
                ["world"] = ReadObject(worldJson), ["publication"] = manifest.DeepClone(),
                ["templates"] = new JArray(sources.Values.SelectMany(source => source.Candidates)
                    .GroupBy(candidate => (candidate.Template.Id, candidate.Template.Version)).Select(group => group.First())
                    .OrderBy(candidate => candidate.Template.Id, StringComparer.Ordinal).ThenBy(candidate => candidate.Template.Version)
                    .Select(candidate => new JObject { ["id"] = candidate.Template.Id, ["version"] = candidate.Template.Version, ["hash"] = candidate.Hash }))
            });
        }

        /// <summary>Gets the immutable route publication identity.</summary>
        public string Id { get; }
        /// <summary>Gets the positive route publication version.</summary>
        public int Version { get; }
        /// <summary>Gets the complete canonical publication/content fingerprint.</summary>
        public string ContentHash { get; }

        /// <summary>Reads schema 1, validates every source/candidate and detaches all data before any selection or I/O.</summary>
        /// <param name="worldJson">The exact existing world publication; no topology is created or rewritten.</param>
        /// <param name="publicationJson">One explicit route manifest, not a directory of implicit assignment files.</param>
        /// <param name="templateJsons">Exact shared templates; identical copies do not multiply entries.</param>
        /// <param name="catalog">The authoritative supported content catalog.</param>
        public static BoardPoolPublication Read(string worldJson, string publicationJson, IEnumerable<string> templateJsons, BoardValidationCatalog catalog) =>
            new BoardPoolPublication(worldJson, publicationJson, templateJsons, catalog);

        /// <summary>Writes the normalized schema-1 manifest; snapshots remain separate exact artifacts.</summary>
        public string ToJson() => manifest.ToString(Formatting.Indented);

        /// <summary>Derives the whole leg's exact plan with a fresh addressed stream per pool; caller-owned RNG is never consumed.</summary>
        /// <returns>A detached plan for fixed and pooled sources; generated segments remain absent.</returns>
        /// <exception cref="ArgumentException">The leg does not match this route and its exact published configurations.</exception>
        public RouteBoardPlan Select(string runId, int runSeed, ActiveRouteLegState leg)
        {
            ValidateLeg(leg);
            var selections = new List<RouteBoardSelection>();
            foreach (RouteSegmentState segment in leg.Segments)
            {
                if (!sources.TryGetValue(segment.SegmentId, out Source source)) continue;
                string streamId = RandomStreamProvider.StreamId(RouteBoardPlan.PolicyId,
                    RouteBoardPlan.PolicyVersion.ToString(CultureInfo.InvariantCulture), runId, leg.LegId, edge.EdgeId,
                    leg.DayNumber.ToString(CultureInfo.InvariantCulture), segment.SegmentId, Id, Version.ToString(CultureInfo.InvariantCulture),
                    ContentHash, source.PoolId ?? "fixed", source.PoolVersion.ToString(CultureInfo.InvariantCulture));
                int index = source.Candidates.Length == 1 ? 0 : new RandomStreamProvider(runSeed).Create(streamId).NextInt(0, source.Candidates.Length);
                Candidate candidate = source.Candidates[index];
                selections.Add(new RouteBoardSelection(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion,
                    candidate.Template.Id, candidate.Template.Version, candidate.Variant, candidate.Hash, source.PoolId, source.PoolVersion, source.PoolHash));
            }
            return new RouteBoardPlan(runId, runSeed, leg.LegId, edge.EdgeId, world.WorldDefinitionId, world.Version, Id, Version, ContentHash, selections);
        }

        /// <summary>Recreates only the retained exact authored board; generated segments return null, and missing/changed archives refuse resolution.</summary>
        /// <param name="run">The restored active run and its complete pinned plan.</param>
        /// <param name="address">The current full address from the Session BoardAddressFactory, which owns seed derivation and request validation.</param>
        /// <exception cref="InvalidOperationException">The run has no matching plan or this is not its captured publication.</exception>
        public LandmarkBoard Resolve(RunState run, BoardAddress address)
        {
            RouteBoardPlan plan = run?.ActiveLeg?.BoardPlan;
            if (plan == null || plan.PublicationId != Id || plan.PublicationVersion != Version || plan.PublicationHash != ContentHash ||
                plan.WorldId != world.WorldDefinitionId || plan.WorldVersion != world.Version || plan.RunId != run.RunId || plan.RunSeed != run.RunSeed)
                throw new InvalidOperationException("The exact captured route publication is required; no reroll or fallback is permitted.");
            RouteBoardPlan expected = Select(run.RunId, run.RunSeed, run.ActiveLeg);
            if (plan.Selections.Count != expected.Selections.Count || plan.Selections.Where((selection, index) => !Same(selection, expected.Selections[index])).Any())
                throw new InvalidOperationException("Retained choices do not match their immutable publication and selection policy.");
            RouteSegmentState segment = run.ActiveLeg.CurrentSegment;
            if (address == null || address.RunId != run.RunId || address.LegId != run.ActiveLeg.LegId || address.EdgeId != edge.EdgeId ||
                address.DayNumber != run.ActiveLeg.DayNumber || address.SegmentId != segment.SegmentId ||
                address.GenerationConfigurationId != segment.GenerationConfigurationId || address.GenerationVersion != segment.GenerationVersion)
                throw new ArgumentException("The request does not match current retained travel.");
            RouteBoardSelection chosen = plan.Selections.SingleOrDefault(selection => selection.SegmentId == address.SegmentId);
            if (chosen == null) return null;
            Source source = sources[chosen.SegmentId];
            Candidate candidate = source.Candidates.Single(entry => entry.Template.Id == chosen.TemplateId && entry.Template.Version == chosen.TemplateVersion && entry.Variant == chosen.VariantId);
            return new BoardAssignment(world, chosen.SegmentId, candidate.Template, chosen.VariantId).CreateBoard(address);
        }

        /// <summary>Append-only in-memory index of fully validated publications; active legs resolve their exact old version, not a current default.</summary>
        public sealed class Archive
        {
            private readonly Dictionary<(string, int, string, string, int), BoardPoolPublication> publications =
                new Dictionary<(string, int, string, string, int), BoardPoolPublication>();
            private readonly Dictionary<(string, int), string> templateHashes = new Dictionary<(string, int), string>();
            private readonly Dictionary<(string, int), string> poolHashes = new Dictionary<(string, int), string>();

            /// <summary>Gets the number of distinct exact route publications retained; identical registration is idempotent.</summary>
            public int Count => publications.Count;

            /// <summary>Registers one detached preflighted publication only after all existing identity/content pairs agree.</summary>
            /// <exception cref="ArgumentException">An existing publication, template or pool identity/version would change content.</exception>
            public void Add(BoardPoolPublication publication)
            {
                if (publication == null) throw new ArgumentNullException(nameof(publication));
                var key = (publication.world.WorldDefinitionId, publication.world.Version, publication.edge.EdgeId, publication.Id, publication.Version);
                if (publications.TryGetValue(key, out BoardPoolPublication existing) && existing.ContentHash != publication.ContentHash)
                    throw new ArgumentException("An archived publication identity/version is immutable.");
                Candidate[] candidates = publication.sources.Values.SelectMany(source => source.Candidates).ToArray();
                Source[] pools = publication.sources.Values.Where(source => source.PoolId != null).ToArray();
                foreach (Candidate candidate in candidates)
                    if (templateHashes.TryGetValue((candidate.Template.Id, candidate.Template.Version), out string hash) && hash != candidate.Hash)
                        throw new ArgumentException("An archived template identity/version is immutable.");
                foreach (Source pool in pools)
                    if (poolHashes.TryGetValue((pool.PoolId, pool.PoolVersion), out string hash) && hash != pool.PoolHash)
                        throw new ArgumentException("An archived pool identity/version is immutable.");
                foreach (Candidate candidate in candidates) templateHashes[(candidate.Template.Id, candidate.Template.Version)] = candidate.Hash;
                foreach (Source pool in pools) poolHashes[(pool.PoolId, pool.PoolVersion)] = pool.PoolHash;
                publications[key] = publication;
            }

            /// <summary>Resolves the exact saved publication; a missing archive never falls back to a newer publication or generator.</summary>
            /// <exception cref="InvalidOperationException">No plan or matching archived publication is available.</exception>
            public LandmarkBoard Resolve(RunState run, BoardAddress address)
            {
                RouteBoardPlan plan = run?.ActiveLeg?.BoardPlan ?? throw new InvalidOperationException("No retained board plan is available.");
                if (!publications.TryGetValue((plan.WorldId, plan.WorldVersion, plan.EdgeId, plan.PublicationId, plan.PublicationVersion), out BoardPoolPublication publication))
                    throw new InvalidOperationException("The exact archived route publication is missing.");
                return publication.Resolve(run, address);
            }
        }

        private void ValidateLeg(ActiveRouteLegState leg)
        {
            if (leg == null || leg.EdgeId != edge.EdgeId || leg.FromNodeId != edge.FromNodeId || leg.ToNodeId != edge.ToNodeId || leg.Segments.Count != edge.SegmentCount ||
                leg.Segments.Where((segment, index) => segment.SegmentId != edge.Segments[index].SegmentId ||
                    segment.GenerationConfigurationId != edge.Segments[index].GenerationConfigurationId || segment.GenerationVersion != edge.Segments[index].GenerationVersion).Any())
                throw new ArgumentException("The active leg does not match this route publication.");
        }

        private static bool Same(RouteBoardSelection left, RouteBoardSelection right) => left.SegmentId == right.SegmentId &&
            left.ConfigurationId == right.ConfigurationId && left.ConfigurationVersion == right.ConfigurationVersion && left.TemplateId == right.TemplateId &&
            left.TemplateVersion == right.TemplateVersion && left.VariantId == right.VariantId && left.TemplateHash == right.TemplateHash &&
            left.PoolId == right.PoolId && left.PoolVersion == right.PoolVersion && left.PoolHash == right.PoolHash;

        private static JObject ReadObject(string json)
        {
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text))
            {
                reader.DateParseHandling = DateParseHandling.None;
                var document = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new ArgumentException("Only one JSON object is permitted.");
                return document;
            }
        }

        private static void Fields(JObject data, params string[] names)
        {
            if (data.Properties().Count() != names.Length || names.Any(name => data.Property(name, StringComparison.Ordinal) == null))
                throw new ArgumentException("Missing, unknown or incorrectly cased publication fields.");
        }

        private static string Text(JObject data, string field) => data[field]?.Type == JTokenType.String
            ? (string)data[field] : throw new ArgumentException("Expected string: " + field);
        private static int Integer(JObject data, string field) => data[field]?.Type == JTokenType.Integer
            ? data[field].Value<int>() : throw new ArgumentException("Expected integer: " + field);

        private static JToken Canonical(JToken token)
        {
            if (token is JObject document) return new JObject(document.Properties().OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => new JProperty(property.Name, Canonical(property.Value))));
            if (token is JArray array) return new JArray(array.Select(Canonical));
            return token.DeepClone();
        }

        private static string Hash(JToken token)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Canonical(token).ToString(Formatting.None)))).Replace("-", "").ToLowerInvariant();
        }
    }
}