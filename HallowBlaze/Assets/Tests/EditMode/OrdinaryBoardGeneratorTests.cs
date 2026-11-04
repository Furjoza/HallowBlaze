using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies single-candidate generation independently of Unity and presentation.</summary>
    public class OrdinaryBoardGeneratorTests
    {
        /// <summary>Food types have authored fixed rewards; old bush identities retain their original effect.</summary>
        [TestCase("legacy.food", 10)]
        [TestCase("legacy.soda", 20)]
        [TestCase("legacy.bush-food", 10)]
        [TestCase(OrdinaryBoardContent.BerryFoodId, 5)]
        [TestCase(OrdinaryBoardContent.CarrotFoodId, 15)]
        public void ProductionCatalogRetainsFixedFoodRewards(string contentId, int reward)
        {
            BoardValidationCatalog catalog = OrdinaryBoardContent.CreateCatalog();
            var address = new BoardAddress("berries", "leg", "road", "segment", 1,
                OrdinaryBoardGenerator.ConfigurationId, 1, 42);
            var config = new BoardGenerationConfig(address.GenerationConfigurationId, address.GenerationVersion,
                new GenerationRange(8, 14), new GenerationRange(20, 58), new GenerationRange(5, 7),
                new GenerationRange(0, 0), 0, 2,
                new[] { "legacy.player", "legacy.floor", "legacy.wall", contentId, OrdinaryBoardContent.ExitTerrainId },
                new[] { new ResourceGenerationBudget("food", new GenerationRange(2, 2)) });
            BoardBlueprint blueprint = OrdinaryBoardGenerator.Generate(address, config, catalog, 77);
            BoardEntityState[] food = blueprint.Entities.Where(entity => entity.Definition.Layer == BoardLayer.Item).ToArray();
            Assert.That(food.Length, Is.EqualTo(2));
            Assert.That(food.All(entity => entity.Definition.ContentId == contentId &&
                entity.Definition.AutomaticFoodReward == reward), Is.True);
            Assert.That(blueprint.InitialResources[0].ItemCount, Is.EqualTo(2));
        }

        /// <summary>Known seeds produce validated, reproducible topology, safe openings and reachable supply.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-1)]
        [TestCase(42)]
        [TestCase(123456)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void KnownSeedsProduceIdenticalValidatedBlueprints(int seed)
        {
            BoardGenerationConfig config = Config();
            BoardAddress address = Address(seed);
            BoardValidationCatalog catalog = Catalog();
            BoardBlueprint first = OrdinaryBoardGenerator.Generate(address, config, catalog, 77);
            BoardBlueprint second = OrdinaryBoardGenerator.Generate(address, config, catalog, 77);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(second.ContentHash, Is.EqualTo(first.ContentHash));
            BoardValidationReport report = BoardValidator.Validate(first, catalog);
            Assert.That(report.IsValid, Is.True);
            Assert.That(report.Metrics.SafeStartMoves, Is.GreaterThanOrEqualTo(2));
            Assert.That(report.Metrics.MaximumConcurrentThreats, Is.LessThanOrEqualTo(1));
            Assert.That(first.InitialResources[0].ItemCount, Is.InRange(2, 5));
            Assert.That(first.InitialResources[0].ObservableItemCount, Is.EqualTo(first.Items.Count));
            Assert.That(first.Exits[0].Y, Is.EqualTo(7));
            Assert.That(first.Start.Y, Is.Zero);
            bool perimeterObstacle = false;
            foreach (BoardEntityState entity in first.Entities)
                if (entity.Definition.Layer == BoardLayer.Obstacle &&
                    (entity.Position.X == 0 || entity.Position.X == 7 || entity.Position.Y == 0 || entity.Position.Y == 7))
                    perimeterObstacle = true;
            Assert.That(perimeterObstacle, Is.True);
            if (seed == 42) Assert.That(first.ContentHash, Is.EqualTo(
                "4bfe146220c8821b722633897904772339e472148c765b534263f1469a941fbd"));
        }

        /// <summary>Address changes, including equal board seeds on distinct segments, change placement rather than only metadata.</summary>
        [TestCase("run")]
        [TestCase("leg")]
        [TestCase("edge")]
        [TestCase("segment")]
        [TestCase("day")]
        [TestCase("root")]
        public void FullIdentityParticipatesInLayout(string changed)
        {
            BoardBlueprint first = OrdinaryBoardGenerator.Generate(Address(42), Config(), Catalog(), 77);
            var address = new BoardAddress(changed == "run" ? "other" : "run", changed == "leg" ? "other" : "leg",
                changed == "edge" ? "other" : "road", changed == "segment" ? "other" : "segment",
                changed == "day" ? 4 : 3, "ordinary", 1, 42);
            BoardBlueprint second = OrdinaryBoardGenerator.Generate(address, Config(), Catalog(), changed == "root" ? 78 : 77);
            Assert.That(Layout(second), Is.Not.EqualTo(Layout(first)));
        }

        /// <summary>Catalog enumeration and current culture cannot change seeded generation.</summary>
        [Test]
        public void CatalogOrderAndCultureAreIrrelevant()
        {
            BoardBlueprint first = OrdinaryBoardGenerator.Generate(Address(42), Config(), Catalog(), 77);
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
                Assert.That(OrdinaryBoardGenerator.Generate(Address(42), Config(), Catalog(true), 77), Is.EqualTo(first));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        /// <summary>Impossible budgets and mismatched identities never publish a partially valid board or silently retry.</summary>
        [TestCase("obstacles", "ObstacleBudget")]
        [TestCase("branches", "BranchPointBudget")]
        [TestCase("path", "PathLengthBudget")]
        [TestCase("resources", "ResourceBudget")]
        [TestCase("identity", "ConfigurationMismatch")]
        public void InvalidCandidateHasReproducibleRejection(string scenario, string code)
        {
            BoardGenerationConfig config = Config(scenario);
            BoardGenerationException failure = Assert.Throws<BoardGenerationException>(() =>
                OrdinaryBoardGenerator.Generate(Address(42), config, Catalog(), 77));
            Assert.That(failure.Report.IsValid, Is.False);
            var codes = new List<string>();
            foreach (BoardValidationError error in failure.Report.Errors) codes.Add(error.Code);
            Assert.That(codes, Does.Contain(code));
            Assert.That(failure.Message, Does.Contain("seed=42"));
            Assert.That(failure.Message, Does.Contain("segment=segment"));
        }

        /// <summary>Input misuse is rejected before generation and mutable runtime state never changes the initial blueprint.</summary>
        [Test]
        public void NullInputsAndStateIsolation()
        {
            Assert.Throws<ArgumentNullException>(() => OrdinaryBoardGenerator.Generate(null, Config(), Catalog(), 77));
            Assert.Throws<ArgumentNullException>(() => OrdinaryBoardGenerator.Generate(Address(42), null, Catalog(), 77));
            Assert.Throws<ArgumentNullException>(() => OrdinaryBoardGenerator.Generate(Address(42), Config(), null, 77));
            BoardBlueprint blueprint = OrdinaryBoardGenerator.Generate(Address(42), Config(), Catalog(), 77);
            string fingerprint = blueprint.ContentHash;
            BoardState state = blueprint.CreateState();
            state.TryRemove(blueprint.Items[0].EntityId);
            Assert.That(blueprint.ContentHash, Is.EqualTo(fingerprint));
            Assert.That(OrdinaryBoardGenerator.Generate(Address(42), Config(), Catalog(), 77), Is.EqualTo(blueprint));
        }

        /// <summary>Production presets cover no enemies, mixed enemies and maximum first-family pressure on representative seeds.</summary>
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(10)]
        public void ProductionPresetsValidateKnownSeedsAndResourceCounts(int day)
        {
            foreach (int seed in new[] { 0, 1, -1, 42, 123456, int.MinValue, int.MaxValue })
            {
                var address = new BoardAddress("run", "leg", "road", "segment", day,
                    OrdinaryBoardGenerator.ConfigurationId, OrdinaryBoardGenerator.ConfigurationVersion, seed);
                BoardGenerationConfig config = OrdinaryBoardContent.CreateConfiguration(address);
                BoardValidationCatalog catalog = OrdinaryBoardContent.CreateCatalog();
                BoardBlueprint blueprint = OrdinaryBoardGenerator.Generate(address, config, catalog, 77);
                BoardValidationReport report = BoardValidator.Validate(blueprint, catalog);
                Assert.That(report.IsValid, Is.True, "day=" + day + ", seed=" + seed);
                Assert.That(report.Metrics.EnemyCount, Is.EqualTo(config.EnemyCount.Minimum));
                Assert.That(blueprint.InitialResources[0].ItemCount, Is.InRange(2, 5));
                Assert.That(OrdinaryBoardGenerator.Generate(address, config, catalog, 77), Is.EqualTo(blueprint));
                if (seed == 42) TestContext.WriteLine("day=" + day + ", hash=" + blueprint.ContentHash + "\n" + Draw(blueprint));
            }
            Assert.Throws<ArgumentException>(() => OrdinaryBoardContent.CreateConfiguration(Address(42)));
        }

        /// <summary>New configuration seeds expose every fixed-value food and retain exact family-count manifests.</summary>
        [Test]
        public void PublishedFoodVarietiesHaveDeterministicSupplyAndFixedEffects()
        {
            var counts = new Dictionary<string, int>();
            var rewards = new Dictionary<string, int> { { "legacy.food", 10 }, { "legacy.soda", 20 },
                { OrdinaryBoardContent.BerryFoodId, 5 }, { OrdinaryBoardContent.CarrotFoodId, 15 } };
            for (int seed = 0; seed < 100; seed++)
            {
                var address = new BoardAddress("food-supply", "leg", "road", "segment", 4,
                    OrdinaryBoardGenerator.ConfigurationId, 4, seed);
                var request = new BoardGenerationRequest(address, OrdinaryBoardContent.CreateConfiguration(address),
                    OrdinaryBoardContent.CreateCatalog(), seed);
                BoardGenerationResult first = BoardGenerationPipeline.GenerateOrdinary(request);
                Assert.That(first.IsSuccess, Is.True, "seed=" + seed + "; " + first);
                Assert.That(BoardGenerationPipeline.GenerateOrdinary(request).Blueprint, Is.EqualTo(first.Blueprint));
                BoardItemSpawn[] food = first.Blueprint.Items.Where(item => item.ResourceFamilyId == "food").ToArray();
                Assert.That(food.Length, Is.InRange(2, 5));
                Assert.That(first.Blueprint.InitialResources.Single(entry => entry.ResourceFamilyId == "food").ItemCount,
                    Is.EqualTo(food.Length));
                foreach (BoardItemSpawn item in food)
                {
                    BoardEntityState entity = first.Blueprint.Entities.Single(candidate => candidate.Id.Equals(item.EntityId));
                    Assert.That(entity.Definition.AutomaticFoodReward, Is.EqualTo(rewards[item.ItemDefinitionId]));
                    counts[item.ItemDefinitionId] = counts.TryGetValue(item.ItemDefinitionId, out int count) ? count + 1 : 1;
                }
            }
            CollectionAssert.AreEquivalent(rewards.Keys, counts.Keys);
            foreach (KeyValuePair<string, int> count in counts)
                TestContext.WriteLine(count.Key + ": " + count.Value + " items; fixed reward=" + rewards[count.Key]);
        }

        /// <summary>Extra catalog definitions cannot alter pinned content, IDs, positions, supply or effects in versions 1-3.</summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void PinnedFoodVersionsMatchTheOriginalCatalog(int version)
        {
            var definitions = new List<BoardEntityDefinition>
            {
                new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "legacy.player"),
                new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "legacy.floor", new BoardEntityTraits(true, false, false)),
                new BoardEntityDefinition(BoardLayer.Obstacle, EntityKind.Obstacle, "legacy.wall"),
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "legacy.food", BoardEntityTraits.Default, 10),
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "legacy.soda", BoardEntityTraits.Default, 20)
            };
            definitions.Add(new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), OrdinaryBoardContent.ExitTerrainId,
                new BoardEntityTraits(true, true, false)));
            definitions.Add(new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, OrdinaryBoardContent.Enemy10Id));
            definitions.Add(new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, OrdinaryBoardContent.Enemy20Id));
            var original = new BoardValidationCatalog(definitions,
                new Dictionary<string, string> { { "legacy.food", "food" }, { "legacy.soda", "food" } },
                new Dictionary<string, string>(), new Dictionary<string, ShamblerDefinition>
                {
                    { OrdinaryBoardContent.Enemy10Id, new ShamblerDefinition(10) },
                    { OrdinaryBoardContent.Enemy20Id, new ShamblerDefinition(20) }
                });
            foreach (int seed in new[] { 0, 42, -1, 123456 })
            {
                var address = new BoardAddress("pinned-food", "leg", "road", "segment", 4,
                    OrdinaryBoardGenerator.ConfigurationId, version, seed);
                BoardGenerationConfig config = OrdinaryBoardContent.CreateConfiguration(address);
                BoardBlueprint expected = version == 1 ? OrdinaryBoardGenerator.Generate(address, config, original, 77) :
                    BoardGenerationPipeline.GenerateOrdinary(new BoardGenerationRequest(address, config, original, 77)).Blueprint;
                BoardBlueprint actual = version == 1 ? OrdinaryBoardGenerator.Generate(address, config, OrdinaryBoardContent.CreateCatalog(), 77) :
                    BoardGenerationPipeline.GenerateOrdinary(new BoardGenerationRequest(address, config, OrdinaryBoardContent.CreateCatalog(), 77)).Blueprint;
                Assert.That(actual, Is.EqualTo(expected), "version=" + version + "; seed=" + seed);
            }
        }

        private static string Draw(BoardBlueprint blueprint)
        {
            var rows = new List<string>();
            for (int coordinateY = 7; coordinateY >= 0; coordinateY--)
            {
                var row = new char[8];
                for (int coordinateX = 0; coordinateX < 8; coordinateX++) row[coordinateX] = '.';
                foreach (BoardEntityState entity in blueprint.Entities)
                    if (entity.Position.Y == coordinateY)
                    {
                        char marker = entity.Definition.Traits.IsExit ? 'E' : entity.Definition.Layer == BoardLayer.Obstacle ? '#' :
                            entity.Definition.Kind.Equals(EntityKind.Player) ? 'P' : entity.Definition.Kind.Equals(EntityKind.Enemy) ? 'Z' :
                            entity.Definition.Layer == BoardLayer.Item ? 'f' : '.';
                        if (marker != '.') row[entity.Position.X] = marker;
                    }
                rows.Add(new string(row));
            }
            return string.Join("\n", rows);
        }

        private static BoardGenerationConfig Config(string scenario = null) => new BoardGenerationConfig(
            "ordinary", scenario == "identity" ? 2 : 1,
            scenario == "path" ? new GenerationRange(63, 63) : new GenerationRange(8, 24),
            scenario == "branches" ? new GenerationRange(64, 64) : new GenerationRange(0, 64),
            scenario == "obstacles" ? new GenerationRange(64, 64) : new GenerationRange(5, 9),
            new GenerationRange(1, 3), 1, 2,
            new[] { "ground", "exit", "player", "wall", "enemy", "berries" },
            new[] { new ResourceGenerationBudget("food", scenario == "resources" ? new GenerationRange(64, 64) : new GenerationRange(2, 5)) });

        private static BoardAddress Address(int seed) => new BoardAddress("run", "leg", "road", "segment", 3, "ordinary", 1, seed);

        private static BoardValidationCatalog Catalog(bool reversed = false)
        {
            var definitions = new List<BoardEntityDefinition>
            {
                new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("ground"), "ground", new BoardEntityTraits(true, false, false)),
                new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("exit"), "exit", new BoardEntityTraits(true, true, false)),
                new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"),
                new BoardEntityDefinition(BoardLayer.Obstacle, EntityKind.Obstacle, "wall", new BoardEntityTraits(false, false, false)),
                new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy"),
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "berries", BoardEntityTraits.Default, 10)
            };
            if (reversed) definitions.Reverse();
            return new BoardValidationCatalog(definitions, new Dictionary<string, string> { { "berries", "food" } },
                new Dictionary<string, string>(), new Dictionary<string, ShamblerDefinition> { { "enemy", new ShamblerDefinition(10) } });
        }

        private static string Layout(BoardBlueprint blueprint)
        {
            var parts = new List<string>();
            foreach (BoardEntityState entity in blueprint.Entities)
                parts.Add(entity.Definition.ContentId + "@" + entity.Position);
            return string.Join(";", parts);
        }
    }
}