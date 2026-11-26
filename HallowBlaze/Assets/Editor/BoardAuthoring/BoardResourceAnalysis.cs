using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Counts detached authored supply, never reachability, collected rewards or action costs.</summary>
    public static class BoardResourceAnalysis
    {
        /// <summary>One content definition's physical spawns and nominal rewards; reusable tools have no charge total.</summary>
        public sealed class Resource
        {
            internal Resource(string id, int count, int food, int health, bool tool, int? charges)
            { Id = id; Count = count; Food = food; Health = health; IsTool = tool; Charges = charges; }
            /// <summary>Gets the exact content ID, not a prefab name or fixed-source metadata ID.</summary>
            public string Id { get; }
            /// <summary>Gets the number of actual spawned entities of this definition.</summary>
            public int Count { get; }
            /// <summary>Gets gross authored Food, without subtracting turns or assuming collection.</summary>
            public int Food { get; }
            /// <summary>Gets nominal restoration before health caps or player condition.</summary>
            public int Health { get; }
            /// <summary>Gets whether the content resolves to a known tool definition.</summary>
            public bool IsTool { get; }
            /// <summary>Gets initial charges for finite tools, or null for reusable tools and non-tools.</summary>
            public int? Charges { get; }
        }

        /// <summary>A detached per-variant count; source documents and gameplay state are not retained.</summary>
        public sealed class Supply
        {
            internal Supply(IEnumerable<Resource> resources, IEnumerable<string> issues, IEnumerable<string> notes)
            { Resources = resources.ToList().AsReadOnly(); Issues = issues.ToList().AsReadOnly(); Notes = notes.ToList().AsReadOnly(); }
            /// <summary>Gets one row per resource definition, ordered by stable ID.</summary>
            public IReadOnlyList<Resource> Resources { get; }
            /// <summary>Gets the gross Food across actual spawns, counting item metadata only once.</summary>
            public int Food => Resources.Sum(resource => resource.Food);
            /// <summary>Gets nominal Health restoration, not effective healing.</summary>
            public int Health => Resources.Sum(resource => resource.Health);
            /// <summary>Gets unresolved reward definitions; any such issue makes totals partial, never a complete zero.</summary>
            public IReadOnlyList<string> Issues { get; }
            /// <summary>Gets conditional or hidden supply annotations; totals remain gross authored supply.</summary>
            public IReadOnlyList<string> Notes { get; }
            internal Dictionary<string, decimal> Values()
            {
                var values = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["Food"] = Food, ["Health (nominal)"] = Health };
                foreach (Resource resource in Resources)
                {
                    values[(resource.IsTool ? "Tools: " : "Items: ") + resource.Id] = resource.Count;
                    if (resource.Charges.HasValue) values["Charges: " + resource.Id] = resource.Charges.Value;
                }
                return values;
            }
        }

        /// <summary>One-resource extrema and expectation; extrema across resources need not describe the same candidate.</summary>
        public sealed class Range
        {
            internal Range(decimal minimum, decimal mean, decimal maximum) { Minimum = minimum; Mean = mean; Maximum = maximum; }
            /// <summary>Gets the least authored amount from one choice per known segment.</summary>
            public decimal Minimum { get; }
            /// <summary>Gets the equal-probability arithmetic mean, retaining fractional units.</summary>
            public decimal Mean { get; }
            /// <summary>Gets the greatest authored amount from one choice per known segment.</summary>
            public decimal Maximum { get; }
        }

        /// <summary>Detached totals or partial authored subtotals; omitted segments are never silently counted as zero.</summary>
        public sealed class Estimate
        {
            internal Estimate(IDictionary<string, Range> values, bool complete)
            { Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Range>(values); IsComplete = complete; }
            /// <summary>Gets ordered resource metrics; tool counts and finite-use charges have distinct units.</summary>
            public IReadOnlyDictionary<string, Range> Values { get; }
            /// <summary>Gets whether every segment and candidate contributed a known supported value.</summary>
            public bool IsComplete { get; }
        }

        /// <summary>A single exact template/variant alternative, counted once regardless of library file copies.</summary>
        public sealed class Candidate
        {
            internal Candidate(string templateId, int version, string variantId, Supply supply)
            { TemplateId = templateId; Version = version; VariantId = variantId; Supply = supply; }
            /// <summary>Gets the retained template identity, or null for an addressed generated snapshot.</summary>
            public string TemplateId { get; }
            /// <summary>Gets the retained template version; zero denotes generated content.</summary>
            public int Version { get; }
            /// <summary>Gets the exact variant; null denotes generated content.</summary>
            public string VariantId { get; }
            /// <summary>Gets nominal supply for this candidate without selection probabilities applied.</summary>
            public Supply Supply { get; }
        }

        /// <summary>One stable segment's alternatives, estimate and independently addressed seed choice.</summary>
        public sealed class Segment
        {
            internal Segment(string id, string mode, Candidate[] candidates, Candidate selected)
            { Id = id; Mode = mode; Candidates = Array.AsReadOnly(candidates); Selected = selected; Estimate = Choices(candidates.Select(candidate => candidate.Supply)); }
            /// <summary>Gets the stable segment identity, never an array-index identity.</summary>
            public string Id { get; }
            /// <summary>Gets Generated, Fixed or Pool.</summary>
            public string Mode { get; }
            /// <summary>Gets every exact eligible alternative, or no entries for unknown generated content.</summary>
            public IReadOnlyList<Candidate> Candidates { get; }
            /// <summary>Gets the exact seed choice, or null when the generated snapshot is unavailable.</summary>
            public Candidate Selected { get; }
            /// <summary>Gets one-draw equal-probability values, not total stock across alternative maps.</summary>
            public Estimate Estimate { get; }
        }

        /// <summary>Frozen route-level supply; no run, RNG, file handle or mutable editor document is retained.</summary>
        public sealed class RouteSupply
        {
            internal RouteSupply(Segment[] segments)
            {
                Segments = Array.AsReadOnly(segments);
                Estimate = Sum(segments.Select(segment => segment.Estimate));
                Selected = Sum(segments.Select(segment => Choices(segment.Selected == null ? Array.Empty<Supply>() : new[] { segment.Selected.Supply })));
            }
            /// <summary>Gets ordered segment reports, including explicit unknown generated segments.</summary>
            public IReadOnlyList<Segment> Segments { get; }
            /// <summary>Gets route extrema and expectation from one choice per segment.</summary>
            public Estimate Estimate { get; }
            /// <summary>Gets exact seed-selected supply, or a partial subtotal when a segment is unknown.</summary>
            public Estimate Selected { get; }
        }

        /// <summary>Counts one exact variant from compatible schema 1 or 2 data without generating or resolving a turn.</summary>
        /// <param name="json">A detached template snapshot decoded using the current authoritative authoring catalog.</param>
        /// <param name="variantId">Exact variant identity; missing variants are rejected rather than substituted.</param>
        /// <returns>One count per spawned resource; item/source bindings do not create extra inventory.</returns>
        public static Supply Board(string json, string variantId)
        {
            var template = BoardTemplateJson.ReadCompatible(json, OrdinaryBoardContent.CreateAuthoringCatalog());
            var address = new BoardAddress("resource-inspection", "resource-leg", "resource-route", "resource-segment", 1,
                template.Config.Id, template.Config.Version, 0);
            return Blueprint(template.CreateBoard(address, variantId).Blueprint);
        }

        /// <summary>Counts a concrete generated/addressed snapshot, not estimates inferred from generation budgets.</summary>
        public static Supply Blueprint(BoardBlueprint blueprint)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            var tools = ToolContentCatalog.Load();
            var digging = ShovelContent.LoadRules();
            var issues = new List<string>();
            var notes = new List<string>();
            var items = blueprint.Items.ToDictionary(item => item.EntityId);
            var resources = blueprint.Entities.Where(entity => entity.Definition.Layer == BoardLayer.Item && !entity.Definition.Traits.IsExit || entity.Definition.AutomaticFoodReward != 0)
                .GroupBy(entity => entity.Definition.ContentId).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    int count = group.Count();
                    int food = group.Sum(entity => entity.Definition.AutomaticFoodReward);
                    if (group.Key == digging.CacheContentId)
                    {
                        food = checked(count * digging.RewardDefinition.AutomaticFoodReward);
                        notes.Add("Conditional cache reward (digging required): " + group.Key);
                    }
                    int health = group.Key == FirstAidInteractionAction.ContentId ? checked(count * FirstAidInteractionAction.HealAmount) : 0;
                    bool tool = tools.TryGetValue(group.Key, out ToolDefinition definition);
                    if (food == 0 && health == 0 && !tool) issues.Add("Unsupported reward: " + group.Key);
                    if (group.Any(entity => items.TryGetValue(entity.Id, out var item) && !item.InitiallyObservable))
                        notes.Add("Hidden supply included: " + group.Key);
                    if (health > 0) notes.Add("Health-capped restoration: " + group.Key);
                    return new Resource(group.Key, count, food, health, tool,
                        tool && !definition.IsReusable ? (int?)checked(count * definition.MaximumCharges) : null);
                }).ToArray();
            return new Supply(resources, issues, notes);
        }

        /// <summary>Computes a one-choice distribution; unknown candidates make the segment unknown, not a narrower or zero-valued pool.</summary>
        public static Estimate Choices(IEnumerable<Supply> candidates)
        {
            var entries = candidates.ToArray();
            if (entries.Length == 0 || entries.Any(entry => entry == null || entry.Issues.Count > 0))
                return new Estimate(new SortedDictionary<string, Range>(StringComparer.Ordinal), false);
            var values = entries.Select(entry => entry.Values()).ToArray();
            var result = new SortedDictionary<string, Range>(StringComparer.Ordinal);
            foreach (string key in values.SelectMany(value => value.Keys).Distinct(StringComparer.Ordinal))
            {
                decimal[] amounts = values.Select(value => value.TryGetValue(key, out decimal amount) ? amount : 0m).ToArray();
                result[key] = new Range(amounts.Min(), amounts.Average(), amounts.Max());
            }
            return new Estimate(result, true);
        }

        /// <summary>Sums one contribution per segment; an unknown segment leaves an explicitly partial authored subtotal.</summary>
        public static Estimate Sum(IEnumerable<Estimate> segments)
        {
            var entries = segments.ToArray();
            var result = new SortedDictionary<string, Range>(StringComparer.Ordinal);
            foreach (string key in entries.SelectMany(entry => entry.Values.Keys).Distinct(StringComparer.Ordinal))
            {
                var amounts = entries.Where(entry => entry.Values.ContainsKey(key)).Select(entry => entry.Values[key]).ToArray();
                result[key] = new Range(amounts.Sum(value => value.Minimum), amounts.Sum(value => value.Mean), amounts.Sum(value => value.Maximum));
            }
            return new Estimate(result, entries.Length > 0 && entries.All(entry => entry.IsComplete));
        }

        /// <summary>Reads exact captured route sources; seeded selection is pure and generated values require matching full-address snapshots.</summary>
        /// <param name="generated">Optional concrete snapshots; stale or foreign addresses are rejected, never relabelled for the current seed.</param>
        public static RouteSupply Route(string worldJson, string manifestJson, string[] templates, int seed,
            IReadOnlyDictionary<string, BoardBlueprint> generated = null)
        {
            var catalog = OrdinaryBoardContent.CreateAuthoringCatalog();
            var publication = BoardPoolPublication.Read(worldJson, manifestJson, templates, catalog);
            var world = WorldDefinitionJson.Deserialize(worldJson);
            JObject manifest = JObject.Parse(publication.ToJson());
            var sources = manifest["sources"].ToDictionary(source => (string)source["segmentId"], StringComparer.Ordinal);
            var byTemplate = templates.Select(json => (Json: json, Template: BoardTemplateJson.ReadCompatible(json, catalog)))
                .GroupBy(entry => (entry.Template.Id, entry.Template.Version)).ToDictionary(group => group.Key, group => group.First().Json);
            RunState run = BoardAuthoringTestContext.PreviewPoolRoute(worldJson, publication.ToJson(), templates, seed, 100, 100, false);
            var selections = run.ActiveLeg.BoardPlan.Selections.ToDictionary(selection => selection.SegmentId, StringComparer.Ordinal);
            var result = new List<Segment>();
            string legId = run.ActiveLeg.LegId;
            run.TryConsumeIntro(legId);
            foreach (RouteSegmentState segment in run.ActiveLeg.Segments)
            {
                Candidate[] candidates;
                Candidate selected = null;
                string mode = "Generated";
                if (sources.TryGetValue(segment.SegmentId, out JToken source))
                {
                    mode = source["poolId"].Type == JTokenType.Null ? "Fixed" : "Pool";
                    candidates = source["entries"].Select(entry => new Candidate((string)entry["templateId"], (int)entry["templateVersion"],
                        (string)entry["variantId"], Board(byTemplate[((string)entry["templateId"], (int)entry["templateVersion"])], (string)entry["variantId"]))).ToArray();
                    var choice = selections[segment.SegmentId];
                    selected = candidates.Single(candidate => candidate.TemplateId == choice.TemplateId && candidate.Version == choice.TemplateVersion && candidate.VariantId == choice.VariantId);
                }
                else if (generated != null && generated.TryGetValue(segment.SegmentId, out BoardBlueprint snapshot))
                {
                    if (!snapshot.Address.Equals(BoardAddressFactory.Create(run, world))) throw new ArgumentException("Generated snapshot address is stale: " + segment.SegmentId);
                    selected = new Candidate(null, 0, null, Blueprint(snapshot));
                    candidates = new[] { selected };
                }
                else candidates = Array.Empty<Candidate>();
                result.Add(new Segment(segment.SegmentId, mode, candidates, selected));
                if (!run.ActiveLeg.IsFinalSegment && !run.TryAdvanceSegment(legId, segment.SegmentId)) throw new InvalidOperationException("Cannot inspect next route segment.");
            }
            if (generated != null && generated.Keys.Any(key => result.All(segment => segment.Id != key || segment.Mode != "Generated")))
                throw new ArgumentException("Generated snapshot belongs to another segment.");
            return new RouteSupply(result.ToArray());
        }

        /// <summary>Explicitly captures actual production-generated snapshots for the sandbox's full seed/address; never runs from a repaint.</summary>
        public static IReadOnlyDictionary<string, BoardBlueprint> CaptureGenerated(string worldJson, string manifestJson, string[] templates, int seed)
        {
            RunState run = BoardAuthoringTestContext.PreviewPoolRoute(worldJson, manifestJson, templates, seed, 100, 100, false);
            var world = WorldDefinitionJson.Deserialize(worldJson);
            string legId = run.ActiveLeg.LegId;
            run.TryConsumeIntro(legId);
            var result = new Dictionary<string, BoardBlueprint>(StringComparer.Ordinal);
            foreach (RouteSegmentState segment in run.ActiveLeg.Segments)
            {
                if (run.ActiveLeg.BoardPlan.Selections.All(choice => choice.SegmentId != segment.SegmentId))
                {
                    var address = BoardAddressFactory.Create(run, world);
                    var configuration = OrdinaryBoardContent.CreateConfiguration(address);
                    if (configuration.Version >= 2 && configuration.Version <= BoardGenerationPipeline.OrdinaryConfigurationVersion)
                    {
                        var generated = BoardGenerationPipeline.GenerateOrdinary(new BoardGenerationRequest(address, configuration, OrdinaryBoardContent.CreateCatalog(), run.RunSeed));
                        if (!generated.IsSuccess) throw new InvalidOperationException(generated.ToString());
                        result.Add(segment.SegmentId, generated.Blueprint);
                    }
                    else result.Add(segment.SegmentId, OrdinaryBoardGenerator.Generate(address, configuration, OrdinaryBoardContent.CreateCatalog(), run.RunSeed));
                }
                if (!run.ActiveLeg.IsFinalSegment) run.TryAdvanceSegment(legId, segment.SegmentId);
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, BoardBlueprint>(result);
        }
    }
}