using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Random;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Tests.EditMode
{
    internal sealed class SeedSeriesCase
    {
        internal SeedSeriesCase(BoardGenerationRequest request, string fixtureId, int segmentIndex = 0, int segmentCount = 1)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(fixtureId)) throw new ArgumentException("A fixture ID is required.", nameof(fixtureId));
            if (segmentIndex < 0 || segmentCount < 1 || segmentIndex >= segmentCount)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            FixtureId = fixtureId;
            SegmentIndex = segmentIndex;
            SegmentCount = segmentCount;
        }

        internal BoardGenerationRequest Request { get; }
        internal string FixtureId { get; }
        internal int SegmentIndex { get; }
        internal int SegmentCount { get; }
    }

    internal sealed class SeedSeriesDistribution
    {
        private readonly SortedDictionary<int, int> histogram = new SortedDictionary<int, int>();
        private readonly int denominator;
        private long sum;
        private long squareSum;
        internal int Count { get; private set; }

        internal SeedSeriesDistribution(int denominator = 1)
        {
            if (denominator < 1) throw new ArgumentOutOfRangeException(nameof(denominator));
            this.denominator = denominator;
        }

        internal void Add(int value)
        {
            histogram.TryGetValue(value, out int count);
            histogram[value] = count + 1;
            Count++;
            sum += value;
            squareSum += (long)value * value;
        }

        internal JObject ToJson()
        {
            var bins = new JObject();
            foreach (KeyValuePair<int, int> bin in histogram)
                bins.Add(((decimal)bin.Key / denominator).ToString(CultureInfo.InvariantCulture), bin.Value);
            decimal mean = Count == 0 ? 0 : (decimal)sum / Count;
            return new JObject
            {
                ["observations"] = Count,
                ["minimum"] = Count == 0 ? JValue.CreateNull() : new JValue((decimal)histogram.First().Key / denominator),
                ["maximum"] = Count == 0 ? JValue.CreateNull() : new JValue((decimal)histogram.Last().Key / denominator),
                ["mean"] = mean / denominator,
                ["populationVariance"] = Count == 0 ? 0 : ((decimal)squareSum / Count - mean * mean) / denominator / denominator,
                ["histogram"] = bins
            };
        }
    }

    internal sealed class SeedSeriesResult
    {
        internal SeedSeriesResult(JObject summary, List<JObject> failures, TimeSpan elapsed)
        {
            Summary = summary;
            Failures = failures.AsReadOnly();
            Elapsed = elapsed;
            Fingerprint = SeedSeriesHarness.Hash(summary.ToString(Formatting.None));
        }

        internal JObject Summary { get; }
        internal IReadOnlyList<JObject> Failures { get; }
        internal TimeSpan Elapsed { get; }
        internal string Fingerprint { get; }
        internal bool Passed => (bool)Summary["passed"];

        internal string WriteArtifacts(string label)
        {
            string directory = Path.Combine(Path.GetTempPath(), "HB-M46-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var report = new JObject
            {
                ["label"] = label,
                ["elapsedMilliseconds"] = Elapsed.TotalMilliseconds,
                ["fingerprint"] = Fingerprint,
                ["summary"] = Summary.DeepClone()
            };
            File.WriteAllText(Path.Combine(directory, "summary.json"), report.ToString(Formatting.Indented), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "failures.json"), new JArray(Failures).ToString(Formatting.Indented), new UTF8Encoding(false));
            return directory;
        }
    }

    internal static class SeedSeriesHarness
    {
        internal const int MaximumBoards = 10000;

        internal static SeedSeriesResult Run(IReadOnlyList<SeedSeriesCase> cases,
            Func<BoardGenerationRequest, BoardGenerationResult> generate, decimal maximumFallbackRate, TimeSpan timeLimit)
        {
            if (cases == null) throw new ArgumentNullException(nameof(cases));
            if (generate == null) throw new ArgumentNullException(nameof(generate));
            if (cases.Count < 1 || cases.Count > MaximumBoards) throw new ArgumentOutOfRangeException(nameof(cases));
            if (maximumFallbackRate < 0 || maximumFallbackRate > 1) throw new ArgumentOutOfRangeException(nameof(maximumFallbackRate));
            if (timeLimit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeLimit));
            if (cases.Any(sample => sample == null)) throw new ArgumentException("Null series case.", nameof(cases));

            var elapsed = Stopwatch.StartNew();
            var metrics = new SortedDictionary<string, SeedSeriesDistribution>(StringComparer.Ordinal);
            var segmentSupply = new SortedDictionary<string, SeedSeriesDistribution>(StringComparer.Ordinal);
            var edgeSupply = new SortedDictionary<string, SeedSeriesDistribution>(StringComparer.Ordinal);
            var groups = new SortedDictionary<string, TraversalSupply>(StringComparer.Ordinal);
            var failures = new List<JObject>();
            var addresses = new HashSet<HallowBlaze.Core.State.BoardAddress>();
            var seeds = new HashSet<int>();
            var rejectionCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var choices = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var trace = new StringBuilder();
            int executed = 0;
            int accepted = 0;
            int fallbacks = 0;
            int staticSoftlocks = 0;
            bool timedOut = false;
            foreach (SeedSeriesCase sample in cases)
            {
                BoardGenerationRequest request = sample.Request;
                var reasons = new SortedSet<string>(StringComparer.Ordinal);
                BoardGenerationResult result = null;
                BoardValidationReport validation = null;
                if (!addresses.Add(request.Address)) reasons.Add("DuplicateAddress");
                if (elapsed.Elapsed > timeLimit)
                {
                    reasons.Add("NotExecuted:TimeLimitExceeded");
                    timedOut = true;
                }
                else
                {
                    executed++;
                    seeds.Add(request.Address.BoardSeed);
                    try
                    {
                        result = generate(request);
                        if (result == null || !result.IsSuccess) reasons.Add("GenerationExhausted");
                        if (result != null)
                            foreach (BoardGenerationAttempt attempt in result.Attempts)
                                foreach (BoardValidationError error in attempt.Report.Errors) Increment(rejectionCodes, error.Code);
                        if (result?.Blueprint != null)
                        {
                            validation = BoardValidator.Validate(request.Address, request.Configuration,
                                result.Blueprint.Entities, result.Blueprint.Items, request.Catalog);
                            foreach (BoardValidationError error in validation.Errors) reasons.Add(error.Code);
                            if (!request.Address.Equals(result.Blueprint.Address) || !result.Blueprint.Equals(new BoardBlueprint(
                                request.Address, request.Configuration, result.Blueprint.Entities, result.Blueprint.Items)))
                                reasons.Add("ResultIdentityMismatch");
                            CheckFeatures(request, result.Blueprint, reasons);
                            if (validation.Metrics != null && validation.Metrics.ExitDistances.Values.Any(distance => distance < 1))
                            {
                                reasons.Add("StaticSoftlock");
                                staticSoftlocks++;
                            }
                        }
                    }
                    catch (Exception failure)
                    {
                        reasons.Add(failure.GetType().FullName + ":" + failure.Message);
                    }
                }
                if (result?.UsedFallback == true) fallbacks++;
                string selection = result == null || !result.IsSuccess ? "failure" : result.UsedFallback ?
                    "fallback:" + result.FallbackId : "attempt:" + result.SelectedAttempt;
                Increment(choices, selection);
                trace.Append(new JObject
                {
                    ["fixture"] = sample.FixtureId,
                    ["segmentIndex"] = sample.SegmentIndex,
                    ["segmentCount"] = sample.SegmentCount,
                    ["request"] = Input(request),
                    ["blueprint"] = result?.Blueprint?.ContentHash,
                    ["history"] = result?.ToString(),
                    ["errors"] = new JArray(reasons)
                }.ToString(Formatting.None)).Append('\n');
                if (reasons.Count > 0)
                {
                    failures.Add(new JObject
                    {
                        ["caseIndex"] = failures.Count + accepted,
                        ["fixture"] = sample.FixtureId,
                        ["segmentIndex"] = sample.SegmentIndex,
                        ["segmentCount"] = sample.SegmentCount,
                        ["request"] = Input(request),
                        ["reasons"] = new JArray(reasons),
                        ["history"] = result?.ToString(),
                        ["blueprintHash"] = result?.Blueprint?.ContentHash
                    });
                    continue;
                }
                accepted++;
                BoardValidationMetrics measured = validation.Metrics;
                foreach (string prefix in new[] { string.Empty, "day-" + request.Address.DayNumber.ToString(CultureInfo.InvariantCulture) + ":" })
                {
                    Add(metrics, prefix + "reachableCells", measured.ReachableCells);
                    Add(metrics, prefix + "branchPoints", measured.BranchPoints);
                    Add(metrics, prefix + "obstacleDensity", measured.ObstacleCount, 64);
                    Add(metrics, prefix + "enemyCount", measured.EnemyCount);
                    Add(metrics, prefix + "safeStartMoves", measured.SafeStartMoves.Value);
                    Add(metrics, prefix + "maximumConcurrentThreats", measured.MaximumConcurrentThreats.Value);
                    Add(metrics, prefix + "builderCalls", result.Attempts.Count);
                    Add(metrics, prefix + "fallback", result.UsedFallback ? 1 : 0);
                    foreach (int distance in measured.ExitDistances.Values) Add(metrics, prefix + "exitDistance", distance);
                    if (measured.MinimumEnemyStartDistance.HasValue) Add(metrics, prefix + "minimumEnemyStartDistance", measured.MinimumEnemyStartDistance.Value);
                }
                string groupId = RandomStreamProvider.StreamId("traversal", sample.FixtureId,
                    request.Address.RunId, request.Address.LegId, request.Address.EdgeId);
                if (!groups.TryGetValue(groupId, out TraversalSupply group))
                {
                    group = new TraversalSupply(sample);
                    groups.Add(groupId, group);
                }
                if (group.Expected != sample.SegmentCount || !group.Segments.Add(sample.SegmentIndex))
                    group.Conflict = true;
                foreach (ResourceGenerationBudget budget in request.Configuration.ResourceBudgets)
                {
                    InitialResourceManifestEntry manifest = result.Blueprint.InitialResources.FirstOrDefault(entry => entry.ResourceFamilyId == budget.ResourceFamilyId);
                    int count = manifest?.ItemCount ?? 0;
                    int observable = manifest?.ObservableItemCount ?? 0;
                    string segmentKey = RandomStreamProvider.StreamId("segment", sample.FixtureId, request.Address.EdgeId,
                        request.Address.SegmentId, budget.ResourceFamilyId);
                    Add(segmentSupply, segmentKey + ":total", count);
                    Add(segmentSupply, segmentKey + ":observable", observable);
                    group.Add(budget.ResourceFamilyId + ":total", count);
                    group.Add(budget.ResourceFamilyId + ":observable", observable);
                }
                foreach (BoardRequiredFeature feature in request.RequiredFeatures)
                    if (feature.SourceId != null)
                        Add(segmentSupply, RandomStreamProvider.StreamId("required-source", sample.FixtureId,
                            request.Address.EdgeId, request.Address.SegmentId, feature.SourceId), 1);
            }
            int incompleteTraversals = 0;
            foreach (TraversalSupply group in groups.Values)
            {
                if (group.Conflict || group.Segments.Count != group.Expected)
                {
                    incompleteTraversals++;
                    continue;
                }
                foreach (KeyValuePair<string, int> family in group.Totals)
                    Add(edgeSupply, RandomStreamProvider.StreamId("edge", group.FixtureId, group.EdgeId,
                        group.Expected.ToString(CultureInfo.InvariantCulture), family.Key), family.Value);
            }
            elapsed.Stop();
            timedOut |= elapsed.Elapsed > timeLimit;
            decimal fallbackRate = (decimal)fallbacks / cases.Count;
            var violations = new JArray();
            if (failures.Count > 0) violations.Add("InvalidBoards");
            if (incompleteTraversals > 0) violations.Add("IncompleteTraversals");
            if (fallbackRate > maximumFallbackRate) violations.Add("FallbackRate");
            if (timedOut) violations.Add("TimeLimitExceeded");
            return new SeedSeriesResult(new JObject
            {
                ["format"] = "hallowblaze.seed-series.v1",
                ["requested"] = cases.Count,
                ["executed"] = executed,
                ["accepted"] = accepted,
                ["invalid"] = failures.Count,
                ["staticSoftlocks"] = staticSoftlocks,
                ["distinctBoardSeeds"] = seeds.Count,
                ["fallbacks"] = fallbacks,
                ["fallbackRate"] = fallbackRate,
                ["maximumFallbackRate"] = maximumFallbackRate,
                ["timeLimitSeconds"] = timeLimit.TotalSeconds,
                ["incompleteTraversals"] = incompleteTraversals,
                ["violations"] = violations,
                ["passed"] = violations.Count == 0,
                ["metrics"] = Distributions(metrics),
                ["segmentSupply"] = Distributions(segmentSupply),
                ["edgeSupply"] = Distributions(edgeSupply),
                ["selectionCounts"] = JObject.FromObject(choices),
                ["rejectionCodes"] = JObject.FromObject(rejectionCodes),
                ["boardSequenceHash"] = Hash(trace.ToString())
            }, failures, elapsed.Elapsed);
        }

        internal static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static JObject Input(BoardGenerationRequest request) => new JObject
        {
            ["address"] = JObject.FromObject(request.Address),
            ["runSeed"] = request.RunSeed,
            ["configuration"] = JObject.FromObject(request.Configuration),
            ["requiredFeatures"] = JArray.FromObject(request.RequiredFeatures)
        };

        private static void CheckFeatures(BoardGenerationRequest request, BoardBlueprint blueprint, SortedSet<string> reasons)
        {
            foreach (BoardRequiredFeature feature in request.RequiredFeatures)
            {
                BoardEntityState entity = blueprint.Entities.FirstOrDefault(candidate =>
                    candidate.Definition.Equals(feature.Definition) && candidate.Position.Equals(feature.Position));
                if (entity == null || (feature.Definition.Layer == BoardLayer.Item && !feature.Definition.Traits.IsExit &&
                    !blueprint.Items.Any(item => item.EntityId.Equals(entity.Id) && item.SourceId == feature.SourceId &&
                        item.InitiallyObservable == feature.InitiallyObservable)))
                    reasons.Add("RequiredFeature:" + feature.Definition.ContentId + ":" + feature.SourceId);
            }
        }

        private static void Increment(SortedDictionary<string, int> counters, string key)
        {
            counters.TryGetValue(key, out int value);
            counters[key] = value + 1;
        }

        private static void Add(SortedDictionary<string, SeedSeriesDistribution> metrics, string key, int value, int denominator = 1)
        {
            if (!metrics.TryGetValue(key, out SeedSeriesDistribution distribution))
            {
                distribution = new SeedSeriesDistribution(denominator);
                metrics.Add(key, distribution);
            }
            distribution.Add(value);
        }

        private static JObject Distributions(SortedDictionary<string, SeedSeriesDistribution> metrics)
        {
            var result = new JObject();
            foreach (KeyValuePair<string, SeedSeriesDistribution> metric in metrics) result.Add(metric.Key, metric.Value.ToJson());
            return result;
        }

        private sealed class TraversalSupply
        {
            internal TraversalSupply(SeedSeriesCase sample)
            {
                FixtureId = sample.FixtureId;
                EdgeId = sample.Request.Address.EdgeId;
                Expected = sample.SegmentCount;
            }

            internal string FixtureId { get; }
            internal string EdgeId { get; }
            internal int Expected { get; }
            internal bool Conflict { get; set; }
            internal HashSet<int> Segments { get; } = new HashSet<int>();
            internal SortedDictionary<string, int> Totals { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

            internal void Add(string family, int count)
            {
                Totals.TryGetValue(family, out int value);
                Totals[family] = value + count;
            }
        }
    }
}