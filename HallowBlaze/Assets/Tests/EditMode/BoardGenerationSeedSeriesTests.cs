using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Checks the batch measurement and failure recorder independently of a large production sweep.</summary>
    public sealed class SeedSeriesHarnessTests
    {
        /// <summary>Histogram bounds and population variance retain outliers rather than reporting only a mean.</summary>
        [Test]
        public void DistributionReportsExactHistogramAndVariance()
        {
            var values = new SeedSeriesDistribution();
            foreach (int value in new[] { 2, 4, 4, 6 }) values.Add(value);
            JObject json = values.ToJson();
            Assert.That((int)json["observations"], Is.EqualTo(4));
            Assert.That((decimal)json["minimum"], Is.EqualTo(2));
            Assert.That((decimal)json["maximum"], Is.EqualTo(6));
            Assert.That((decimal)json["mean"], Is.EqualTo(4));
            Assert.That((decimal)json["populationVariance"], Is.EqualTo(2));
            Assert.That((int)json["histogram"]["4"], Is.EqualTo(2));
            var density = new SeedSeriesDistribution(64);
            density.Add(8);
            Assert.That((decimal)density.ToJson()["mean"], Is.EqualTo(0.125m));
        }

        /// <summary>The same cases give equal canonical summaries and hashes, independent of runtime and current culture.</summary>
        [Test]
        public void RepeatedSeriesHasIdenticalDeterministicSummary()
        {
            var cases = new List<SeedSeriesCase>();
            for (int index = 0; index < 12; index++) cases.Add(Sample(index));
            SeedSeriesResult first = SeedSeriesHarness.Run(cases, BoardGenerationPipeline.GenerateOrdinary, 0.01m, TimeSpan.FromSeconds(10));
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
                SeedSeriesResult second = SeedSeriesHarness.Run(cases, BoardGenerationPipeline.GenerateOrdinary, 0.01m, TimeSpan.FromSeconds(10));
                Assert.That(first.Passed, Is.True, first.Summary.ToString());
                Assert.That(second.Passed, Is.True, second.Summary.ToString());
                Assert.That(second.Fingerprint, Is.EqualTo(first.Fingerprint));
                Assert.That(JToken.DeepEquals(second.Summary, first.Summary), Is.True);
                Assert.That((int)first.Summary["executed"], Is.EqualTo(12));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        /// <summary>Every intentionally broken configuration is retained with its address, seed, budgets and complete attempt history.</summary>
        [Test]
        public void CorruptConfigurationRecordsAllSeedsOutsideProject()
        {
            var cases = new List<SeedSeriesCase>();
            for (int index = 0; index < 3; index++) cases.Add(Sample(index, true));
            SeedSeriesResult result = SeedSeriesHarness.Run(cases, BoardGenerationPipeline.GenerateOrdinary, 0.01m, TimeSpan.FromSeconds(10));
            Assert.That(result.Passed, Is.False);
            Assert.That((int)result.Summary["executed"], Is.EqualTo(3));
            Assert.That(result.Failures.Count, Is.EqualTo(3));
            string directory = result.WriteArtifacts("intentional-corrupt-configuration");
            JArray persisted = JArray.Parse(File.ReadAllText(Path.Combine(directory, "failures.json")));
            Assert.That(persisted.Count, Is.EqualTo(3));
            for (int index = 0; index < 3; index++)
            {
                Assert.That((int)persisted[index]["request"]["address"]["BoardSeed"], Is.EqualTo(index));
                Assert.That((string)persisted[index]["history"], Does.Contain("PathLengthBudget"));
                Assert.That((string)persisted[index]["request"]["address"]["SegmentId"], Is.EqualTo("segment"));
            }
            Assert.That(Path.GetFullPath(directory), Does.StartWith(Path.GetFullPath(Path.GetTempPath())));
            TestContext.WriteLine("Expected failures and report: " + directory);
        }

        /// <summary>Valid fallback boards still fail a predeclared frequency threshold instead of concealing generator collapse.</summary>
        [Test]
        public void FallbackThresholdIsEnforcedWithoutInvalidBoards()
        {
            var cases = new[] { Sample(1), Sample(2) };
            SeedSeriesResult result = SeedSeriesHarness.Run(cases,
                request => BoardGenerationPipeline.Generate(request, (_, __) => null, OrdinaryBoardFallbacks.CreateLibrary(), 1),
                0.01m, TimeSpan.FromSeconds(10));
            Assert.That((int)result.Summary["invalid"], Is.Zero);
            Assert.That((decimal)result.Summary["fallbackRate"], Is.EqualTo(1));
            Assert.That(result.Summary["violations"].Values<string>(), Does.Contain("FallbackRate"));
            Assert.That(result.Passed, Is.False);
        }

        /// <summary>Whole-edge totals and population variance use complete traversals, never pooled individual-board counts.</summary>
        [Test]
        public void EdgeSupplyUsesCompleteTraversalTotals()
        {
            var cases = new List<SeedSeriesCase>();
            for (int index = 0; index < 4; index++)
            {
                SeedSeriesCase original = Sample(index);
                var address = new BoardAddress("supply-" + (index / 2).ToString(CultureInfo.InvariantCulture), "leg.1", "road",
                    "segment-" + (index % 2).ToString(CultureInfo.InvariantCulture), 1, OrdinaryBoardGenerator.ConfigurationId,
                    BoardGenerationPipeline.OrdinaryConfigurationVersion, index);
                BoardGenerationConfig config = original.Request.Configuration;
                config = new BoardGenerationConfig(config.Id, config.Version, config.PathLength, config.BranchPoints, config.ObstacleCount,
                    config.EnemyCount, config.MaximumConcurrentThreats, config.MinimumSafeStartMoves, config.AllowedContentIds,
                    new[] { new ResourceGenerationBudget("food", new GenerationRange(index + 2, index + 2)) });
                cases.Add(new SeedSeriesCase(new BoardGenerationRequest(address, config, original.Request.Catalog, index), "supply-control", index % 2, 2));
            }
            SeedSeriesResult result = SeedSeriesHarness.Run(cases, BoardGenerationPipeline.GenerateOrdinary, 0.01m, TimeSpan.FromSeconds(10));
            Assert.That(result.Passed, Is.True, result.Summary.ToString());
            Assert.That(((JObject)result.Summary["edgeSupply"]).Count, Is.EqualTo(2));
            foreach (JProperty metric in ((JObject)result.Summary["edgeSupply"]).Properties())
            {
                Assert.That((int)metric.Value["observations"], Is.EqualTo(2));
                Assert.That((decimal)metric.Value["minimum"], Is.EqualTo(5));
                Assert.That((decimal)metric.Value["maximum"], Is.EqualTo(9));
                Assert.That((decimal)metric.Value["mean"], Is.EqualTo(7));
                Assert.That((decimal)metric.Value["populationVariance"], Is.EqualTo(4));
            }
            cases.RemoveAt(3);
            SeedSeriesResult partial = SeedSeriesHarness.Run(cases, BoardGenerationPipeline.GenerateOrdinary, 0.01m, TimeSpan.FromSeconds(10));
            Assert.That(partial.Passed, Is.False);
            Assert.That((int)partial.Summary["incompleteTraversals"], Is.EqualTo(1));
            foreach (JProperty metric in ((JObject)partial.Summary["edgeSupply"]).Properties())
                Assert.That((int)metric.Value["observations"], Is.EqualTo(1));
        }

        /// <summary>An exhausted time budget reports every unexecuted case instead of declaring an incomplete sweep successful.</summary>
        [Test]
        public void ExpiredBudgetCannotSilentlyTruncateTheSeries()
        {
            var cases = new[] { Sample(1), Sample(2), Sample(3) };
            SeedSeriesResult result = SeedSeriesHarness.Run(cases, BoardGenerationPipeline.GenerateOrdinary, 0.01m, TimeSpan.FromTicks(1));
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failures.Count + (int)result.Summary["accepted"], Is.EqualTo(3));
            Assert.That(result.Summary["violations"].Values<string>(), Does.Contain("TimeLimitExceeded"));
            Assert.That((int)result.Summary["executed"], Is.LessThan(3));
        }

        /// <summary>Two- and five-segment authored fixtures retain exact family bands and a hidden fixed source once per traversal.</summary>
        [Test]
        public void AuthoredSupplyAndFixedSourceHaveExplicitSeriesEvidence()
        {
            List<SeedSeriesCase> cases = ResourceCases();
            SeedSeriesResult result = SeedSeriesHarness.Run(cases, BoardGenerationPipeline.GenerateOrdinary, 1m, TimeSpan.FromSeconds(20));
            string directory = result.WriteArtifacts("authored-resource-fixtures");
            TestContext.WriteLine("Synthetic authored fixtures (not production fallback-rate gate): " + directory);
            Assert.That(result.Passed, Is.True, result.Summary.ToString());
            Assert.That((int)result.Summary["executed"], Is.EqualTo(56));
            JObject sourceMetrics = (JObject)result.Summary["segmentSupply"];
            JProperty[] sources = sourceMetrics.Properties().Where(metric => metric.Name.Contains("required-source")).ToArray();
            Assert.That(sources.Length, Is.EqualTo(2));
            Assert.That(sources.All(metric => (int)metric.Value["observations"] == 8 && (decimal)metric.Value["minimum"] == 1), Is.True);
            foreach (JProperty metric in ((JObject)result.Summary["edgeSupply"]).Properties())
            {
                bool shortRoad = metric.Name.Contains("fixture.edge.2");
                bool food = metric.Name.Contains("food:");
                bool observed = metric.Name.Contains(":observable");
                int expected = food ? (shortRoad ? 8 : 20) : observed ? 0 : 1;
                Assert.That((int)metric.Value["observations"], Is.EqualTo(8));
                Assert.That((decimal)metric.Value["minimum"], Is.EqualTo(expected), metric.Name);
                Assert.That((decimal)metric.Value["maximum"], Is.EqualTo(expected), metric.Name);
                Assert.That((decimal)metric.Value["populationVariance"], Is.Zero);
            }
        }

        /// <summary>Independent series checks catch a builder that returns a different supply band or silently drops a required source.</summary>
        [TestCase("band", "ResourceBudget")]
        [TestCase("source", "RequiredFeature")]
        public void AuthoredRequirementViolationsCannotHideInAPassingPipeline(string mutation, string expected)
        {
            SeedSeriesCase sample = ResourceCases()[0];
            SeedSeriesResult result = SeedSeriesHarness.Run(new[] { sample }, request =>
            {
                BoardGenerationConfig config = request.Configuration;
                if (mutation == "band") config = new BoardGenerationConfig(config.Id, config.Version, config.PathLength, config.BranchPoints,
                    config.ObstacleCount, config.EnemyCount, config.MaximumConcurrentThreats, config.MinimumSafeStartMoves,
                    config.AllowedContentIds, new[] { new ResourceGenerationBudget("food", new GenerationRange(2, 2)),
                        new ResourceGenerationBudget("tools", new GenerationRange(1, 1)) });
                var altered = new BoardGenerationRequest(request.Address, config, request.Catalog, request.RunSeed,
                    mutation == "source" ? null : request.RequiredFeatures);
                return BoardGenerationPipeline.GenerateOrdinary(altered);
            }, 1m, TimeSpan.FromSeconds(10));
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failures.Count, Is.EqualTo(1));
            Assert.That(result.Failures[0]["reasons"].Values<string>().Any(reason => reason.StartsWith(expected, StringComparison.Ordinal)), Is.True);
            Assert.That((string)result.Failures[0]["request"]["requiredFeatures"][0]["SourceId"], Is.EqualTo("fixture.fixed-axe"));
            string directory = result.WriteArtifacts("intentional-authored-" + mutation + "-violation");
            TestContext.WriteLine("Expected " + expected + ": " + directory);
        }

        private static List<SeedSeriesCase> ResourceCases()
        {
            var definitions = new List<BoardEntityDefinition>();
            foreach (LegacyBoardContentDefinition content in LegacyBoardContentCatalog.Definitions)
                if (content.Kind == LegacyBoardContentKind.Player || content.Kind == LegacyBoardContentKind.Floor ||
                    content.Kind == LegacyBoardContentKind.Wall || content.Kind == LegacyBoardContentKind.Food || content.Kind == LegacyBoardContentKind.Soda)
                    definitions.Add(new BoardEntityDefinition(content.Layer.Value, content.EntityKind, content.ContentId, content.Traits, content.AutomaticFoodReward));
            definitions.Add(new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), OrdinaryBoardContent.ExitTerrainId, new BoardEntityTraits(true, true, false)));
            definitions.Add(new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, OrdinaryBoardContent.Enemy10Id));
            definitions.Add(new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, OrdinaryBoardContent.Enemy20Id));
            var axe = new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "fixture.tool.axe");
            definitions.Add(axe);
            var catalog = new BoardValidationCatalog(definitions,
                new Dictionary<string, string> { { "legacy.food", "food" }, { "legacy.soda", "food" }, { "fixture.tool.axe", "tools" } },
                new Dictionary<string, string> { { "fixture.fixed-axe", "fixture.tool.axe" } },
                new Dictionary<string, ShamblerDefinition> { { OrdinaryBoardContent.Enemy10Id, new ShamblerDefinition(10) },
                    { OrdinaryBoardContent.Enemy20Id, new ShamblerDefinition(20) } });
            var cases = new List<SeedSeriesCase>();
            foreach (int length in new[] { 2, 5 })
                for (int traversal = 0; traversal < 8; traversal++)
                    for (int index = 0; index < length; index++)
                    {
                        string fixture = "authored-resource-" + length.ToString(CultureInfo.InvariantCulture);
                        var address = new BoardAddress(fixture + "-run-" + traversal.ToString(CultureInfo.InvariantCulture), "leg.1",
                            "fixture.edge." + length.ToString(CultureInfo.InvariantCulture), "fixture.segment." + index.ToString(CultureInfo.InvariantCulture),
                            1 + traversal, OrdinaryBoardGenerator.ConfigurationId, 2, cases.Count);
                        BoardGenerationConfig config = OrdinaryBoardContent.CreateConfiguration(address);
                        config = new BoardGenerationConfig(config.Id, config.Version, config.PathLength, config.BranchPoints, config.ObstacleCount,
                            config.EnemyCount, config.MaximumConcurrentThreats, config.MinimumSafeStartMoves,
                            config.AllowedContentIds.Concat(new[] { axe.ContentId }), new[]
                            {
                                new ResourceGenerationBudget("food", new GenerationRange(4, 4)),
                                new ResourceGenerationBudget("tools", new GenerationRange(index == 0 ? 1 : 0, index == 0 ? 1 : 0))
                            });
                        var features = index == 0 ? new[] { new BoardRequiredFeature(axe, new GridPosition(2, 1), "fixture.fixed-axe", false) } :
                            Array.Empty<BoardRequiredFeature>();
                        cases.Add(new SeedSeriesCase(new BoardGenerationRequest(address, config, catalog, traversal, features), fixture, index, length));
                    }
            return cases;
        }

        internal static SeedSeriesCase Sample(int seed, bool broken = false)
        {
            var address = new BoardAddress("series-" + seed.ToString(CultureInfo.InvariantCulture), "leg.1", "road", "segment", 1,
                OrdinaryBoardGenerator.ConfigurationId, BoardGenerationPipeline.OrdinaryConfigurationVersion, seed);
            BoardGenerationConfig config = OrdinaryBoardContent.CreateConfiguration(address);
            if (broken) config = new BoardGenerationConfig(config.Id, config.Version, new GenerationRange(63, 63), config.BranchPoints,
                config.ObstacleCount, config.EnemyCount, config.MaximumConcurrentThreats, config.MinimumSafeStartMoves,
                config.AllowedContentIds, config.ResourceBudgets);
            return new SeedSeriesCase(new BoardGenerationRequest(address, config, OrdinaryBoardContent.CreateCatalog(), seed), "control");
        }
    }

    /// <summary>Runs headless production-catalog daily and milestone sweeps with thresholds fixed before generation.</summary>
    public sealed class BoardGenerationSeedSeriesTests
    {
        private const decimal MaximumFallbackRate = 0.01m;

        /// <summary>The default daily gate covers 1,000 distinct addressed boards twice and compares complete deterministic summaries.</summary>
        [Test]
        [Category("GenerationDaily")]
        [Timeout(150000)]
        public void Daily1000SeedsHaveNoInvalidBoardsAndRepeatExactly() => RunGate(1000, TimeSpan.FromSeconds(60));

        /// <summary>The explicitly selected milestone gate covers 10,000 boards twice without extending the ordinary daily suite.</summary>
        [Test]
        [Category("GenerationMilestone")]
        [Explicit("Run this 10,000-seed gate explicitly before accepting a generation milestone.")]
        [Timeout(650000)]
        public void Milestone10000SeedsHaveNoInvalidBoardsAndRepeatExactly() => RunGate(10000, TimeSpan.FromSeconds(300));

        /// <summary>Audits real ordinary layouts at explicitly low resources without changing their pinned generation or pretending to recover the reported seed.</summary>
        [Test]
        [Timeout(300000)]
        public void Published1000BoardsHaveAFourCommandEntryEscape()
        {
            foreach (SeedSeriesCase sample in ProductionCases(1000))
            {
                BoardGenerationResult generated = BoardGenerationPipeline.GenerateOrdinary(sample.Request);
                Assert.That(generated.IsSuccess, Is.True, generated.ToString());
                BoardEntrySafetyResult audit = BoardEntrySafety.Check(generated.Blueprint, sample.Request.Catalog);
                Assert.That(audit.HasEscape, Is.True,
                    "Conservative one HP / five Food entry audit, four accepted-command horizon: " + sample.Request.Address);
                Assert.That(audit.EvaluatedCommands, Is.LessThanOrEqualTo(780));
            }
        }

        /// <summary>The pinned v2 diagnostic remains unchanged and is not mislabeled as the owner's unrecovered day-four seed.</summary>
        [Test]
        public void RetainedVersionTwoDiagnosticKeepsItsLayoutAndBoundedFailure()
        {
            var address = new BoardAddress("series-run-188", "leg.1", "road.west-grove-ridge",
                "segment.west-grove-ridge.stretch", 9, OrdinaryBoardGenerator.ConfigurationId, 2, 2125458236);
            BoardValidationCatalog catalog = OrdinaryBoardContent.CreateCatalog();
            var request = new BoardGenerationRequest(address, OrdinaryBoardContent.CreateConfiguration(address), catalog, 94);
            BoardGenerationResult first = BoardGenerationPipeline.GenerateOrdinary(request);
            BoardGenerationResult repeat = BoardGenerationPipeline.GenerateOrdinary(request);
            Assert.That(first.IsSuccess, Is.True, first.ToString());
            Assert.That(repeat.Blueprint.ContentHash, Is.EqualTo(first.Blueprint.ContentHash));
            Assert.That(first.Blueprint.Address, Is.EqualTo(address));
            Assert.That(BoardEntrySafety.Check(first.Blueprint, catalog, 10, 20).HasEscape, Is.False);
        }

        private static void RunGate(int count, TimeSpan timeLimit)
        {
            List<SeedSeriesCase> cases = ProductionCases(count);
            SeedSeriesResult first = SeedSeriesHarness.Run(cases, BoardGenerationPipeline.GenerateOrdinary, MaximumFallbackRate, timeLimit);
            string firstDirectory = first.WriteArtifacts("production-" + count.ToString(CultureInfo.InvariantCulture) + "-first");
            TestContext.WriteLine("First series: " + firstDirectory);
            TestContext.WriteLine("Count=" + count + "; elapsedMs=" + first.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
                "; hash=" + first.Fingerprint + "; invalid=" + first.Summary["invalid"] + "; fallbacks=" + first.Summary["fallbacks"]);
            SeedSeriesResult second = SeedSeriesHarness.Run(cases, BoardGenerationPipeline.GenerateOrdinary, MaximumFallbackRate, timeLimit);
            string secondDirectory = second.WriteArtifacts("production-" + count.ToString(CultureInfo.InvariantCulture) + "-repeat");
            TestContext.WriteLine("Repeated series: " + secondDirectory);
            TestContext.WriteLine("Repeated elapsedMs=" + second.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
                "; hash=" + second.Fingerprint);
            Assert.That(first.Passed, Is.True, "First series: " + firstDirectory + "\n" + first.Summary);
            Assert.That(second.Passed, Is.True, "Repeated series: " + secondDirectory + "\n" + second.Summary);
            Assert.That((int)first.Summary["executed"], Is.EqualTo(count));
            Assert.That((int)first.Summary["distinctBoardSeeds"], Is.EqualTo(count));
            Assert.That((int)first.Summary["invalid"], Is.Zero);
            Assert.That((int)first.Summary["staticSoftlocks"], Is.Zero);
            Assert.That(second.Fingerprint, Is.EqualTo(first.Fingerprint));
            Assert.That(JToken.DeepEquals(second.Summary, first.Summary), Is.True);
        }

        private static List<SeedSeriesCase> ProductionCases(int count)
        {
            string project = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(typeof(BoardGenerationSeedSeriesTests).Assembly.Location)));
            WorldDefinition world = WorldDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(project, "Assets", "GameData", "World", "prototype-world.json")));
            var catalog = OrdinaryBoardContent.CreateCatalog();
            var cases = new List<SeedSeriesCase>();
            int inboundPrefixSweeps = 0;
            for (int traversal = 0; cases.Count < count; traversal++)
            {
                WorldEdgeDefinition edge = world.Edges[traversal % world.Edges.Count];
                int ordinaryCount = edge.Segments.Count(segment => segment.GenerationConfigurationId == OrdinaryBoardGenerator.ConfigurationId);
                bool inboundPrefix = ordinaryCount != edge.SegmentCount;
                if (inboundPrefix && inboundPrefixSweeps >= 5) continue;
                if (cases.Count + ordinaryCount > count) continue;
                if (inboundPrefix) inboundPrefixSweeps++;
                int day = edge.FromNodeId == world.StartNodeId ? 1 : 1 + traversal / world.Edges.Count % 10;
                int rootSeed = traversal % 2 == 0 ? traversal / 2 : -(traversal / 2) - 1;
                var run = new RunState("series-run-" + traversal.ToString(CultureInfo.InvariantCulture), rootSeed,
                    new RunStateConfiguration(100, 100, day - 1, edge.FromNodeId));
                run.StartLeg(edge.EdgeId, edge.FromNodeId, edge.ToNodeId, edge.Segments.Select(segment =>
                    new RouteSegmentState(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion)));
                run.TryConsumeIntro(run.ActiveLeg.LegId);
                for (int index = 0; index < edge.SegmentCount && cases.Count < count; index++)
                {
                    BoardAddress address = BoardAddressFactory.Create(run, world);
                    if (address.GenerationConfigurationId == OrdinaryBoardGenerator.ConfigurationId)
                    {
                        Assert.That(address.GenerationVersion, Is.EqualTo(BoardGenerationPipeline.OrdinaryConfigurationVersion));
                        cases.Add(new SeedSeriesCase(new BoardGenerationRequest(address, OrdinaryBoardContent.CreateConfiguration(address), catalog, rootSeed),
                            inboundPrefix ? "published-inbound-ordinary-prefix" : "published", index, ordinaryCount));
                    }
                    if (index + 1 < edge.SegmentCount)
                        Assert.That(run.TryAdvanceSegment(run.ActiveLeg.LegId, address.SegmentId), Is.True);
                }
            }
            Assert.That(cases.Select(sample => sample.Request.Address.SegmentId).Distinct().Count(), Is.EqualTo(45));
            Assert.That(cases.Select(sample => sample.Request.Address.DayNumber).Distinct().Count(), Is.EqualTo(10));
            return cases;
        }
    }
}