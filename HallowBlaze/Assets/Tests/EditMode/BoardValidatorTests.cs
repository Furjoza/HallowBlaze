using System;
using System.Collections.Generic;
using System.Globalization;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies ordinary-board validation against authored, reproducible layouts.</summary>
    public class BoardValidatorTests
    {
        /// <summary>A legal ordinary board has exact cardinal topology measurements without changing its state.</summary>
        [Test]
        public void ValidBoardReportsExactTopologyWithoutMutation()
        {
            BoardBlueprint blueprint = Blueprint(Entities());
            string hash = blueprint.ContentHash;
            BoardValidationReport report = BoardValidator.Validate(blueprint, Catalog());
            Assert.That(report.IsValid, Is.True);
            Assert.That(report.Metrics.ExitDistances[new GridPosition(7, 7)], Is.EqualTo(14));
            Assert.That(report.Metrics.ReachableCells, Is.EqualTo(63));
            Assert.That(report.Metrics.BranchPoints, Is.EqualTo(58));
            Assert.That(report.Metrics.ObstacleCount, Is.EqualTo(1));
            Assert.That(report.Metrics.EnemyCount, Is.Zero);
            Assert.That(report.Metrics.SafeStartMoves, Is.EqualTo(2));
            Assert.That(report.Metrics.MaximumConcurrentThreats, Is.Zero);
            Assert.That(report.Metrics.MinimumEnemyStartDistance, Is.Null);
            Assert.That(blueprint.ContentHash, Is.EqualTo(hash));
            Assert.Throws<NotSupportedException>(() => ((IList<BoardValidationError>)report.Errors).Clear());
        }

        /// <summary>Blocking every cardinal crossing rejects an exit even though diagonal positions and optional loot exist.</summary>
        [Test]
        public void DisconnectedExitIsRejectedWithoutUsingOptionalTools()
        {
            List<BoardEntityState> entities = Entities();
            for (int coordinate = 0; coordinate < 8; coordinate++)
                entities.Add(Entity(200 + coordinate, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", coordinate, 1, false));
            entities.Add(Entity(300, BoardLayer.Item, EntityKind.Item, "axe", 1, 0));
            var items = new[] { new BoardItemSpawn("optional-axe", new EntityId(300), "axe", "tools") };
            BoardValidationReport report = BoardValidator.Validate(Blueprint(entities, items), Catalog());
            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Metrics.ExitDistances[new GridPosition(7, 7)], Is.EqualTo(-1));
            Assert.That(report.Errors.Count, Is.EqualTo(1));
            Assert.That(report.Errors[0].Code, Is.EqualTo("UnreachableExit"));
            foreach (string identity in new[] { "run=run", "leg=leg", "edge=road", "segment=segment", "day=3", "seed=-123", "config=ordinary/1" })
                Assert.That(report.Errors[0].Message, Does.Contain(identity));
        }

        /// <summary>Construction and aggregated inspection share exactly the same structural rejection rules.</summary>
        [TestCase("address", "MissingAddress")]
        [TestCase("config", "MissingConfiguration")]
        [TestCase("config-id", "ConfigurationMismatch")]
        [TestCase("config-version", "ConfigurationMismatch")]
        [TestCase("entities", "MissingEntities")]
        [TestCase("items", "MissingItems")]
        [TestCase("null-entity", "NullEntity")]
        [TestCase("bounds", "OutOfBounds")]
        [TestCase("duplicate-entity", "DuplicateEntity")]
        [TestCase("layer", "LayerConflict")]
        [TestCase("player-layer", "InvalidPlayerLayer")]
        [TestCase("missing-player", "MissingStart")]
        [TestCase("players", "MultipleStarts")]
        [TestCase("missing-exit", "MissingExit")]
        [TestCase("overlap", "StartExitOverlap")]
        [TestCase("null-item", "NullItem")]
        [TestCase("instance", "DuplicateItemInstance")]
        [TestCase("binding", "DuplicateItemEntity")]
        [TestCase("source", "DuplicateSource")]
        [TestCase("orphan", "InvalidItemBinding")]
        [TestCase("wrong-layer", "InvalidItemBinding")]
        [TestCase("definition", "InvalidItemBinding")]
        [TestCase("exit-item", "InvalidItemBinding")]
        [TestCase("family", "ItemFamilyConflict")]
        [TestCase("missing-item", "MissingItemSpawn")]
        public void InvalidStructureIsReportedAndBlueprintStillRejectsIt(string scenario, string expectedCode)
        {
            List<BoardEntityState> entities = Entities();
            entities.Add(Entity(300, BoardLayer.Item, EntityKind.Item, "axe", 1, 0));
            entities.Add(Entity(301, BoardLayer.Item, EntityKind.Item, "axe", 2, 0));
            var items = new List<BoardItemSpawn>
            {
                new BoardItemSpawn("axe-a", new EntityId(300), "axe", "tools", "source-a"),
                new BoardItemSpawn("axe-b", new EntityId(301), "axe", "tools", "source-b")
            };
            BoardGenerationConfig config = Config();
            BoardAddress address = Address(config);
            switch (scenario)
            {
                case "address": address = null; break;
                case "config": config = null; break;
                case "config-id": address = new BoardAddress("run", "leg", "road", "segment", 3, "other", 1, -123); break;
                case "config-version": address = new BoardAddress("run", "leg", "road", "segment", 3, config.Id, 2, -123); break;
                case "entities": entities = null; break;
                case "items": items = null; break;
                case "null-entity": entities.Add(null); break;
                case "bounds": entities.Add(Entity(400, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 8, 0)); break;
                case "duplicate-entity": entities.Add(Entity(101, BoardLayer.Item, EntityKind.Item, "axe", 3, 0)); break;
                case "layer": entities.Add(Entity(400, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 7, 0)); break;
                case "player-layer": entities.RemoveAll(entity => entity.Definition.Kind.Equals(EntityKind.Player));
                    entities.Add(Entity(400, BoardLayer.Item, EntityKind.Player, "player", 0, 0)); break;
                case "missing-player": entities.RemoveAll(entity => entity.Definition.Kind.Equals(EntityKind.Player)); break;
                case "players": entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Player, "player", 1, 1)); break;
                case "missing-exit": entities.RemoveAll(entity => entity.Definition.Traits.IsExit); break;
                case "overlap": entities.Add(Entity(400, BoardLayer.Item, EntityKind.Item, "exit", 0, 0, exit: true)); break;
                case "null-item": items.Add(null); break;
                case "instance": items[1] = new BoardItemSpawn("axe-a", new EntityId(301), "axe", "tools"); break;
                case "binding": items[1] = new BoardItemSpawn("axe-b", new EntityId(300), "axe", "tools"); break;
                case "source": items[1] = new BoardItemSpawn("axe-b", new EntityId(301), "axe", "tools", "source-a"); break;
                case "orphan": items[1] = new BoardItemSpawn("axe-b", new EntityId(999), "axe", "tools"); break;
                case "wrong-layer": items[1] = new BoardItemSpawn("axe-b", new EntityId(100), "player", "tools"); break;
                case "definition": items[1] = new BoardItemSpawn("axe-b", new EntityId(301), "berries", "food"); break;
                case "exit-item": entities.Add(Entity(400, BoardLayer.Item, EntityKind.Item, "exit", 7, 7, exit: true));
                    items[1] = new BoardItemSpawn("axe-b", new EntityId(400), "exit", "tools"); break;
                case "family": items[1] = new BoardItemSpawn("axe-b", new EntityId(301), "axe", "food"); break;
                case "missing-item": items.RemoveAt(1); break;
            }
            BoardValidationReport report = BoardValidator.Validate(address, config, entities, items, Catalog());
            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Metrics, Is.Null);
            Assert.That(Codes(report), Does.Contain(expectedCode));
            if (address == null || config == null || entities == null || items == null)
                Assert.Throws<ArgumentNullException>(() => new BoardBlueprint(address, config, entities, items));
            else
                Assert.Throws<ArgumentException>(() => new BoardBlueprint(address, config, entities, items));
        }

        /// <summary>Independent failures are retained together, including absent markers and multiple entity errors.</summary>
        [Test]
        public void RawCandidateCollectsAllStructuralErrors()
        {
            var entities = new[]
            {
                Entity(1, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", -1, 0, false),
                Entity(1, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", -1, 0, false),
                null
            };
            BoardGenerationConfig config = Config();
            BoardValidationReport report = BoardValidator.Validate(Address(config), config, entities, Array.Empty<BoardItemSpawn>(), Catalog());
            CollectionAssert.AreEquivalent(new[] { "OutOfBounds", "OutOfBounds", "DuplicateEntity", "LayerConflict", "NullEntity", "MissingStart", "MissingExit" }, Codes(report));
        }

        /// <summary>Catalog resolution is separate from configuration allowlists and never trusts candidate metadata.</summary>
        [TestCase("catalog", "MissingCatalog")]
        [TestCase("allowed", "UnknownAllowedContent")]
        [TestCase("disallowed", "DisallowedContent")]
        [TestCase("content", "UnknownContent")]
        [TestCase("traits", "ContentDefinitionMismatch")]
        [TestCase("item", "UnknownItemDefinition")]
        [TestCase("family", "ItemResourceFamilyMismatch")]
        [TestCase("source", "UnknownSource")]
        [TestCase("source-definition", "SourceDefinitionMismatch")]
        [TestCase("budget-family", "UnknownResourceFamily")]
        [TestCase("unbudgeted", "UnbudgetedResource")]
        public void CatalogAndResourceIdentityFailuresAreExplicit(string scenario, string expectedCode)
        {
            List<BoardEntityState> entities = Entities();
            string itemDefinition = scenario == "item" ? "unknown-item" : "axe";
            entities.Add(Entity(300, BoardLayer.Item, EntityKind.Item, itemDefinition, 1, 0));
            var items = new[] { new BoardItemSpawn("axe-a", new EntityId(300), itemDefinition,
                scenario == "family" ? "food" : "tools",
                scenario == "source" ? "unknown-source" : scenario == "source-definition" ? "berry-source" : "source-a") };
            BoardGenerationConfig config = Config();
            if (scenario == "allowed") config = Config(allowed: new[] { "player", "ground", "exit", "wall", "axe", "unknown" });
            if (scenario == "disallowed") config = Config(allowed: new[] { "player", "ground", "exit", "wall" });
            if (scenario == "content") entities.Add(Entity(400, BoardLayer.Obstacle, EntityKind.Obstacle, "unknown", 3, 3));
            if (scenario == "traits") entities[0] = Entity(0, BoardLayer.Terrain, new EntityKind("ground"), "ground", 0, 0, false);
            if (scenario == "budget-family") config = Config(budgets: new[] { new ResourceGenerationBudget("unknown", new GenerationRange(0, 1)) });
            if (scenario == "unbudgeted") config = Config(budgets: Array.Empty<ResourceGenerationBudget>());
            var blueprint = new BoardBlueprint(Address(config), config, entities, items);
            BoardValidationReport report = BoardValidator.Validate(blueprint, scenario == "catalog" ? null : Catalog());
            Assert.That(Codes(report), Does.Contain(expectedCode));
        }

        /// <summary>Both endpoints of every inclusive static budget are enforced, including zero absent resources.</summary>
        [TestCase("path-min", "PathLengthBudget")]
        [TestCase("path-max", "PathLengthBudget")]
        [TestCase("branch-min", "BranchPointBudget")]
        [TestCase("branch-max", "BranchPointBudget")]
        [TestCase("obstacle-min", "ObstacleBudget")]
        [TestCase("obstacle-max", "ObstacleBudget")]
        [TestCase("enemy-min", "EnemyBudget")]
        [TestCase("enemy-max", "EnemyBudget")]
        [TestCase("resource-min", "ResourceBudget")]
        [TestCase("resource-max", "ResourceBudget")]
        public void StaticBudgetViolationsAreRejected(string scenario, string expectedCode)
        {
            List<BoardEntityState> entities = Entities();
            var items = new List<BoardItemSpawn>();
            if (scenario == "enemy-max") entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Enemy, "enemy", 5, 5));
            if (scenario == "resource-max")
            {
                entities.Add(Entity(300, BoardLayer.Item, EntityKind.Item, "axe", 1, 0));
                items.Add(new BoardItemSpawn("axe-a", new EntityId(300), "axe", "tools"));
            }
            BoardGenerationConfig config = Config(
                path: scenario == "path-min" ? new GenerationRange(15, 63) : scenario == "path-max" ? new GenerationRange(1, 13) : (GenerationRange?)null,
                branches: scenario == "branch-min" ? new GenerationRange(59, 64) : scenario == "branch-max" ? new GenerationRange(0, 57) : (GenerationRange?)null,
                obstacles: scenario == "obstacle-min" ? new GenerationRange(2, 64) : scenario == "obstacle-max" ? new GenerationRange(0, 0) : (GenerationRange?)null,
                enemies: scenario == "enemy-min" ? new GenerationRange(1, 8) : scenario == "enemy-max" ? new GenerationRange(0, 0) : (GenerationRange?)null,
                threats: scenario == "enemy-max" ? 0 : 2,
                budgets: new[] { new ResourceGenerationBudget("tools", scenario == "resource-min" ? new GenerationRange(1, 1) : new GenerationRange(0, 0)) });
            BoardValidationReport report = BoardValidator.Validate(new BoardBlueprint(Address(config), config, entities, items), Catalog());
            Assert.That(Codes(report), Does.Contain(expectedCode));
        }

        /// <summary>Exact bounds pass; hidden item counts are instances and the supplied catalog is detached.</summary>
        [Test]
        public void InclusiveBudgetsAndDetachedCatalogPass()
        {
            List<BoardEntityDefinition> definitions = Definitions();
            var families = new Dictionary<string, string> { { "axe", "tools" }, { "berries", "food" } };
            var sources = new Dictionary<string, string> { { "source-a", "axe" } };
            var catalog = new BoardValidationCatalog(definitions, families, sources);
            definitions.Clear();
            families.Clear();
            sources.Clear();
            List<BoardEntityState> entities = Entities();
            entities.Add(Entity(300, BoardLayer.Item, EntityKind.Item, "axe", 1, 0));
            var items = new[] { new BoardItemSpawn("axe-a", new EntityId(300), "axe", "tools", "source-a", false) };
            BoardGenerationConfig config = Config(path: new GenerationRange(14, 14), branches: new GenerationRange(58, 58),
                obstacles: new GenerationRange(1, 1), enemies: new GenerationRange(0, 0), threats: 0,
                budgets: new[] { new ResourceGenerationBudget("tools", new GenerationRange(1, 1)) });
            Assert.That(BoardValidator.Validate(new BoardBlueprint(Address(config), config, entities, items), catalog).IsValid, Is.True);
        }

        /// <summary>Registry construction rejects duplicates, dangling source IDs and malformed registrations.</summary>
        [Test]
        public void InvalidCatalogRegistrationsAreRejected()
        {
            var families = new Dictionary<string, string> { { "axe", "tools" } };
            var sources = new Dictionary<string, string>();
            Assert.Throws<ArgumentNullException>(() => new BoardValidationCatalog(null, families, sources));
            Assert.Throws<ArgumentNullException>(() => new BoardValidationCatalog(Definitions(), null, sources));
            Assert.Throws<ArgumentNullException>(() => new BoardValidationCatalog(Definitions(), families, null));
            List<BoardEntityDefinition> definitions = Definitions();
            definitions.Add(definitions[0]);
            Assert.Throws<ArgumentException>(() => new BoardValidationCatalog(definitions, families, sources));
            definitions = Definitions();
            definitions.Add(null);
            Assert.Throws<ArgumentException>(() => new BoardValidationCatalog(definitions, families, sources));
            Assert.Throws<ArgumentException>(() => new BoardValidationCatalog(Definitions(),
                new[] { new KeyValuePair<string, string>("unknown", "tools") }, sources));
            Assert.Throws<ArgumentException>(() => new BoardValidationCatalog(Definitions(),
                new[] { new KeyValuePair<string, string>("axe", "tools"), new KeyValuePair<string, string>("axe", "food") }, sources));
            Assert.Throws<ArgumentException>(() => new BoardValidationCatalog(Definitions(), families,
                new[] { new KeyValuePair<string, string>("source", "unknown") }));
            Assert.Throws<ArgumentException>(() => new BoardValidationCatalog(Definitions(), families,
                new[] { new KeyValuePair<string, string>("source", "axe"), new KeyValuePair<string, string>("source", "axe") }));
        }

        /// <summary>Conditional movement attacks exclude their locked destination, not every adjacent cell around an enemy.</summary>
        [Test]
        public void OpeningSafetyUsesActualInitialLockedIntent()
        {
            List<BoardEntityState> entities = Entities();
            entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Enemy, "enemy", 2, 0));
            BoardValidationReport report = BoardValidator.Validate(Blueprint(entities), Catalog());
            Assert.That(report.IsValid, Is.True);
            Assert.That(report.Metrics.SafeStartMoves, Is.EqualTo(1));
            Assert.That(report.Metrics.MaximumConcurrentThreats, Is.EqualTo(1));
            Assert.That(report.Metrics.MinimumEnemyStartDistance, Is.EqualTo(2));
            Assert.That(report.Metrics.InitialThreatsByCell[new GridPosition(1, 0)], Is.EqualTo(1));
            Assert.That(report.Metrics.InitialThreatsByCell.ContainsKey(new GridPosition(0, 1)), Is.False);
        }

        /// <summary>A synthetic corner has a safe first move yet no surviving escape through four real accepted commands at low health.</summary>
        [Test]
        public void SyntheticTwoEnemyCornerHasNoFourCommandEscapeDespiteSafeFirstMove()
        {
            List<BoardEntityState> entities = Entities();
            entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Enemy, "enemy", 2, 0));
            entities.Add(Entity(401, BoardLayer.Actor, EntityKind.Enemy, "enemy", 2, 1));
            entities.Add(Entity(402, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 0, 2, false));
            BoardBlueprint blueprint = Blueprint(entities);
            BoardValidationReport report = BoardValidator.Validate(blueprint, Catalog());
            Assert.That(report.IsValid, Is.True);
            Assert.That(report.Metrics.SafeStartMoves, Is.EqualTo(1));
            BoardEntrySafetyResult bounded = BoardEntrySafety.Check(blueprint, Catalog(), 10, 20);
            Assert.That(bounded.HasEscape, Is.False);
            Assert.That(bounded.EvaluatedCommands, Is.LessThanOrEqualTo(780));
            PlayerCommand[] choices = { new MoveCommand(Direction.North), new MoveCommand(Direction.East),
                new MoveCommand(Direction.West), new MoveCommand(Direction.South), new WaitCommand() };
            int escaped = 0;
            int died = 0;
            for (int sequence = 0; sequence < 625; sequence++)
            {
                BoardState board = blueprint.CreateState();
                var run = new RunState("synthetic-entry-trap", 0, new RunStateConfiguration(10, 20, 0, "synthetic"));
                var enemies = new[] { new ShamblerState(new EntityId(400), new ShamblerDefinition(10)),
                    new ShamblerState(new EntityId(401), new ShamblerDefinition(10)) };
                var planner = new ShamblerPlanner();
                foreach (ShamblerState enemy in enemies)
                    enemy.LockIntent(planner.Plan(board, enemy, new EntityId(100)));
                var phases = new EnemyBatchTurnPhases(enemies);
                var controller = new TurnController(board, run, new EntityId(100), new TurnResolver(), phases.Handlers);
                int decisions = sequence;
                for (int step = 0; step < 4 && !controller.IsTerminal; step++)
                {
                    PlayerCommand command = choices[decisions % choices.Length];
                    decisions /= choices.Length;
                    TurnResult result = controller.Resolve(command);
                    if (!result.Accepted) continue;
                    if (run.Health > 0 && board.TryGetEntity(new EntityId(100), out BoardEntityState player) &&
                        player.Position.ManhattanDistance(blueprint.Start) >= 3)
                    {
                        escaped++;
                        break;
                    }
                }
                if (run.Health == 0) died++;
            }
            Assert.That(died, Is.GreaterThan(0));
            Assert.That(escaped, Is.Zero,
                "Synthetic fixture only: two enemies, 10 HP, 20 Food, four accepted-command horizon; not a recovered player seed.");
        }

        /// <summary>The v3 criterion rejects the synthetic trap while preserving a narrow surviving escape and deterministic caller state.</summary>
        [Test]
        public void EntrySafetyRejectsTwoEnemyTrapButAcceptsNarrowSurvivingRoute()
        {
            List<BoardEntityState> entities = Entities();
            entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Enemy, "enemy", 2, 0));
            entities.Add(Entity(401, BoardLayer.Actor, EntityKind.Enemy, "enemy", 2, 1));
            entities.Add(Entity(402, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 0, 2, false));
            BoardGenerationConfig original = Config();
            var config = new BoardGenerationConfig("ordinary.board.v1", 3, original.PathLength, original.BranchPoints,
                original.ObstacleCount, original.EnemyCount, original.MaximumConcurrentThreats, original.MinimumSafeStartMoves,
                original.AllowedContentIds, original.ResourceBudgets);
            var trap = new BoardBlueprint(Address(config), config, entities, Array.Empty<BoardItemSpawn>());
            CollectionAssert.Contains(Codes(BoardValidator.Validate(trap, Catalog())), "EntryEscapeHorizon");
            entities.RemoveAll(entity => entity.Id.Equals(new EntityId(401)));
            var narrow = new BoardBlueprint(Address(config), config, entities, Array.Empty<BoardItemSpawn>());
            string originalHash = narrow.ContentHash;
            Assert.That(BoardValidator.Validate(narrow, Catalog()).IsValid, Is.True);
            BoardEntrySafetyResult first = BoardEntrySafety.Check(narrow, Catalog());
            BoardEntrySafetyResult repeat = BoardEntrySafety.Check(narrow, Catalog());
            Assert.That(first.HasEscape, Is.True);
            Assert.That(first.EscapeCommands.Count, Is.LessThanOrEqualTo(4));
            Assert.That(repeat.EvaluatedCommands, Is.EqualTo(first.EvaluatedCommands));
            Assert.That(narrow.ContentHash, Is.EqualTo(originalHash));
            Assert.That(BoardEntrySafety.Check(narrow, Catalog(), 1, 1).HasEscape, Is.False,
                "Starvation costs still apply; a bounded witness does not promise survival with arbitrary Food.");
            var request = new BoardGenerationRequest(trap.Address, config, Catalog(), 0);
            var fallbacks = new[] { new BoardGenerationFallback("synthetic.narrow-safe", _ => narrow) };
            BoardGenerationResult selected = BoardGenerationPipeline.Generate(request, (_, __) => trap, fallbacks, 2);
            BoardGenerationResult selectedAgain = BoardGenerationPipeline.Generate(request, (_, __) => trap, fallbacks, 2);
            Assert.That(selected.IsSuccess, Is.True);
            Assert.That(selected.UsedFallback, Is.True);
            Assert.That(selected.Attempts.Count, Is.EqualTo(3));
            Assert.That(selected.FallbackId, Is.EqualTo("synthetic.narrow-safe"));
            Assert.That(selectedAgain.Blueprint.ContentHash, Is.EqualTo(selected.Blueprint.ContentHash));
            Assert.That(selectedAgain.ToString(), Is.EqualTo(selected.ToString()));
        }

        /// <summary>Distinct enemy intents sharing one destination count separately even when movement later conflicts.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SharedOpeningThreatsRespectInclusiveSafetyBudgets(bool reject)
        {
            List<BoardEntityState> entities = Entities();
            entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Enemy, "enemy", 2, 0));
            entities.Add(Entity(401, BoardLayer.Actor, EntityKind.Enemy, "enemy", 1, 1));
            entities.Add(Entity(402, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 0, 1, false));
            BoardGenerationConfig config = Config(threats: reject ? 1 : 2, safeMoves: reject ? 1 : 0);
            var blueprint = new BoardBlueprint(Address(config), config, entities, Array.Empty<BoardItemSpawn>());
            BoardValidationReport report = BoardValidator.Validate(blueprint, Catalog());
            Assert.That(report.Metrics.SafeStartMoves, Is.Zero);
            Assert.That(report.Metrics.MaximumConcurrentThreats, Is.EqualTo(2));
            Assert.That(report.Metrics.InitialThreatsByCell[new GridPosition(1, 0)], Is.EqualTo(2));
            if (reject)
                CollectionAssert.AreEquivalent(new[] { "ConcurrentThreatBudget", "SafeStartMovesBudget" }, Codes(report));
            else
                Assert.That(report.IsValid, Is.True);
        }

        /// <summary>Actor occupancy and illegal terrain are independent of actor-ignoring path measurement.</summary>
        [TestCase("blocked-start", "BlockedStart")]
        [TestCase("adjacent", "UnsafeEnemyStart")]
        [TestCase("enemy-terrain", "BlockedEnemySpawn")]
        [TestCase("enemy-rule", "UnsupportedEnemyRule")]
        [TestCase("actor", "UnsupportedActor")]
        [TestCase("perimeter", "UnbrokenPerimeter")]
        [TestCase("exit-layer", "NonExecutableExit")]
        public void IllegalOpeningsAndNonExecutableExitsAreRejected(string scenario, string expectedCode)
        {
            List<BoardEntityState> entities = Entities();
            switch (scenario)
            {
                case "blocked-start": entities.Add(Entity(400, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 0, 0, false)); break;
                case "adjacent": entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Enemy, "enemy", 1, 0)); break;
                case "enemy-terrain": entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Enemy, "enemy", 4, 4));
                    entities.Add(Entity(401, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 4, 4, false)); break;
                case "enemy-rule": entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Enemy, "unmodeled", 4, 4)); break;
                case "actor": entities.Add(Entity(400, BoardLayer.Actor, new EntityKind("npc"), "npc", 4, 4)); break;
                case "perimeter": entities.RemoveAll(entity => entity.Id.Equals(new EntityId(101)));
                    entities.Add(Entity(400, BoardLayer.Actor, EntityKind.Enemy, "enemy", 7, 0)); break;
                case "exit-layer": entities.RemoveAll(entity => entity.Definition.Traits.IsExit);
                    entities.Add(Entity(63, BoardLayer.Terrain, new EntityKind("ground"), "ground", 7, 7));
                    entities.Add(Entity(400, BoardLayer.Item, EntityKind.Item, "exit", 7, 7, exit: true)); break;
            }
            BoardValidationReport report = BoardValidator.Validate(Blueprint(entities), Catalog());
            Assert.That(Codes(report), Does.Contain(expectedCode));
            if (scenario == "enemy-rule" || scenario == "actor")
            {
                Assert.That(report.Metrics.SafeStartMoves, Is.Null);
                Assert.That(report.Metrics.MaximumConcurrentThreats, Is.Null);
            }
        }

        /// <summary>Topology uses the resolver's explicit terrain and obstacle rules, not empty-cell or item traits.</summary>
        [TestCase("missing-terrain", false)]
        [TestCase("water", false)]
        [TestCase("wall", false)]
        [TestCase("bridge", true)]
        public void CardinalReachabilityRequiresWalkableTerrainAndObstacles(string crossing, bool reachable)
        {
            List<BoardEntityState> entities = Entities();
            if (crossing == "missing-terrain" || crossing == "water")
                entities.RemoveAll(entity => entity.Definition.Layer == BoardLayer.Terrain && entity.Position.Y == 1);
            for (int horizontal = 0; horizontal < 8; horizontal++)
            {
                if (crossing == "water") entities.Add(Entity(200 + horizontal, BoardLayer.Terrain, new EntityKind("water"), "water", horizontal, 1, false));
                if (crossing == "wall" || crossing == "bridge")
                    entities.Add(Entity(200 + horizontal, BoardLayer.Obstacle, EntityKind.Obstacle, crossing, horizontal, 1, crossing == "bridge"));
            }
            BoardValidationReport report = BoardValidator.Validate(Blueprint(entities), Catalog());
            Assert.That(report.IsValid, Is.EqualTo(reachable));
            Assert.That(report.Metrics.ExitDistances[new GridPosition(7, 7)], Is.EqualTo(reachable ? 14 : -1));
        }

        /// <summary>Every logical exit is required; one reachable or sufficiently long path cannot mask another failure.</summary>
        [TestCase("valid")]
        [TestCase("unreachable")]
        [TestCase("too-short")]
        public void AllLogicalExitsAreValidatedIndividually(string scenario)
        {
            List<BoardEntityState> entities = Entities();
            entities[7] = Entity(7, BoardLayer.Terrain, new EntityKind("ground"), "exit", 0, 7, exit: true);
            if (scenario == "unreachable")
            {
                entities.Add(Entity(400, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 0, 6, false));
                entities.Add(Entity(401, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 1, 7, false));
            }
            BoardGenerationConfig config = Config(path: scenario == "too-short" ? new GenerationRange(8, 63) : (GenerationRange?)null);
            var blueprint = new BoardBlueprint(Address(config), config, entities, Array.Empty<BoardItemSpawn>());
            BoardValidationReport report = BoardValidator.Validate(blueprint, Catalog());
            Assert.That(report.Metrics.ExitDistances.Count, Is.EqualTo(2));
            Assert.That(report.Metrics.ExitDistances[new GridPosition(7, 7)], Is.EqualTo(14));
            Assert.That(report.Metrics.ExitDistances[new GridPosition(0, 7)], Is.EqualTo(scenario == "unreachable" ? -1 : 7));
            Assert.That(report.IsValid, Is.EqualTo(scenario == "valid"));
            if (scenario != "valid")
            {
                Assert.That(report.Errors.Count, Is.EqualTo(1));
                Assert.That(report.Errors[0].Code, Is.EqualTo(scenario == "unreachable" ? "UnreachableExit" : "PathLengthBudget"));
                Assert.That(report.Errors[0].Position, Is.EqualTo(new GridPosition(0, 7)));
            }
        }

        /// <summary>A transitional terrain/item exit pair remains one executable exit and never counts as resource supply.</summary>
        [Test]
        public void LayeredExitDoesNotRequireAnItemSpawnOrDoubleCountAnExit()
        {
            List<BoardEntityState> entities = Entities();
            entities.Add(Entity(400, BoardLayer.Item, EntityKind.Item, "exit", 7, 7, exit: true));
            BoardBlueprint blueprint = Blueprint(entities);
            BoardValidationReport report = BoardValidator.Validate(blueprint, Catalog());
            Assert.That(report.IsValid, Is.True);
            Assert.That(report.Metrics.ExitDistances.Count, Is.EqualTo(1));
            Assert.That(blueprint.InitialResources, Is.Empty);
        }

        /// <summary>Optional loot need not be reachable and actor occupancy does not become a permanent topology wall.</summary>
        [Test]
        public void OptionalLootAndActorsDoNotChangeStaticPathRules()
        {
            List<BoardEntityState> entities = Entities();
            entities.Add(Entity(300, BoardLayer.Item, EntityKind.Item, "axe", 2, 2));
            entities.Add(Entity(400, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 1, 2, false));
            entities.Add(Entity(401, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 2, 1, false));
            entities.Add(Entity(402, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 3, 2, false));
            entities.Add(Entity(403, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 2, 3, false));
            var items = new[] { new BoardItemSpawn("axe-a", new EntityId(300), "axe", "tools", initiallyObservable: false) };
            Assert.That(BoardValidator.Validate(Blueprint(entities, items), Catalog()).IsValid, Is.True);
            entities.Add(Entity(500, BoardLayer.Actor, EntityKind.Enemy, "enemy", 1, 0));
            BoardValidationReport report = BoardValidator.Validate(Blueprint(entities, items), Catalog());
            Assert.That(report.Metrics.ExitDistances[new GridPosition(7, 7)], Is.EqualTo(14));
            Assert.That(Codes(report), Does.Contain("UnsafeEnemyStart").And.Not.Contain("UnreachableExit"));
        }

        /// <summary>Raw and constructed candidates produce identical diagnostics, independent of enumeration order and culture.</summary>
        [Test]
        public void ReportsAreDeterministicDetachedAndEquivalentAcrossEntryPoints()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            try
            {
                List<BoardEntityState> entities = Entities();
                entities.Add(Entity(300, BoardLayer.Item, EntityKind.Item, "axe", 1, 0));
                entities.Add(Entity(301, BoardLayer.Item, EntityKind.Item, "axe", 2, 0));
                var items = new List<BoardItemSpawn>
                {
                    new BoardItemSpawn("a", new EntityId(300), "axe", "tools", "unknown-a"),
                    new BoardItemSpawn("b", new EntityId(301), "axe", "tools", "unknown-b")
                };
                BoardGenerationConfig config = Config(path: new GenerationRange(15, 63), branches: new GenerationRange(0, 57),
                    budgets: new[] { new ResourceGenerationBudget("tools", new GenerationRange(3, 3)) });
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                BoardValidationReport first = BoardValidator.Validate(new BoardBlueprint(Address(config), config, entities, items), Catalog());
                entities.Reverse();
                items.Reverse();
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                BoardValidationReport second = BoardValidator.Validate(Address(config), config, entities, items, Catalog());
                CollectionAssert.AreEqual(Messages(first), Messages(second));
                Assert.That(second.Errors.Count, Is.EqualTo(5));
                Assert.That(second.Metrics.ExitDistances[new GridPosition(7, 7)], Is.EqualTo(14));
                Assert.Throws<NotSupportedException>(() => ((IDictionary<GridPosition, int>)second.Metrics.ExitDistances).Clear());
                Assert.Throws<NotSupportedException>(() => ((IDictionary<GridPosition, int>)second.Metrics.InitialThreatsByCell).Clear());
                entities.Clear();
                items.Clear();
                Assert.That(second.Errors.Count, Is.EqualTo(5));
                Assert.That(second.Metrics.ReachableCells, Is.EqualTo(63));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        /// <summary>Invalid coordinates are reported before topology arithmetic, including both Int32 extremes.</summary>
        [TestCase(-1, 0)]
        [TestCase(8, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 8)]
        [TestCase(int.MinValue, int.MaxValue)]
        public void OutOfBoundsCoordinatesCannotOverflowValidation(int horizontal, int vertical)
        {
            List<BoardEntityState> entities = Entities();
            entities.Add(Entity(400, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", horizontal, vertical, false));
            BoardGenerationConfig config = Config();
            BoardValidationReport report = BoardValidator.Validate(Address(config), config, entities, Array.Empty<BoardItemSpawn>(), Catalog());
            CollectionAssert.AreEqual(new[] { "OutOfBounds" }, Codes(report));
            Assert.That(report.Errors[0].Position, Is.EqualTo(new GridPosition(horizontal, vertical)));
        }

        /// <summary>Missing input cannot silently pass, while null blueprint misuse retains an explicit API exception.</summary>
        [Test]
        public void NullCandidatesFailClosed()
        {
            Assert.Throws<ArgumentNullException>(() => BoardValidator.Validate(null, Catalog()));
            BoardValidationReport report = BoardValidator.Validate(null, null, null, null, null);
            CollectionAssert.AreEquivalent(new[] { "MissingAddress", "MissingConfiguration", "MissingEntities", "MissingItems", "MissingStart", "MissingExit", "MissingCatalog" }, Codes(report));
            foreach (BoardValidationError error in report.Errors) Assert.That(error.Message, Does.Contain("address=<missing>, config=<missing>"));
            Assert.That(report.Metrics, Is.Null);
        }

        /// <summary>Enemy registrations cannot attach an unknown, non-enemy, null or duplicated opening rule.</summary>
        [Test]
        public void InvalidEnemyCatalogRegistrationsAreRejected()
        {
            var families = new Dictionary<string, string>();
            var sources = new Dictionary<string, string>();
            foreach (string content in new[] { "unknown", "player" })
                Assert.Throws<ArgumentException>(() => new BoardValidationCatalog(Definitions(), families, sources,
                    new[] { new KeyValuePair<string, ShamblerDefinition>(content, new ShamblerDefinition(10)) }));
            Assert.Throws<ArgumentException>(() => new BoardValidationCatalog(Definitions(), families, sources,
                new[] { new KeyValuePair<string, ShamblerDefinition>("enemy", null) }));
            Assert.Throws<ArgumentException>(() => new BoardValidationCatalog(Definitions(), families, sources,
                new[] { new KeyValuePair<string, ShamblerDefinition>("enemy", new ShamblerDefinition(10)), new KeyValuePair<string, ShamblerDefinition>("enemy", new ShamblerDefinition(20)) }));
        }

        private static List<string> Messages(BoardValidationReport report)
        {
            var messages = new List<string>();
            foreach (BoardValidationError error in report.Errors) messages.Add(error.Message);
            return messages;
        }

        private static List<string> Codes(BoardValidationReport report)
        {
            var codes = new List<string>();
            foreach (BoardValidationError error in report.Errors) codes.Add(error.Code);
            return codes;
        }

        private static List<BoardEntityState> Entities()
        {
            var entities = new List<BoardEntityState>();
            for (int horizontal = 0; horizontal < 8; horizontal++)
                for (int vertical = 0; vertical < 8; vertical++)
                    entities.Add(Entity(horizontal * 8 + vertical, BoardLayer.Terrain, new EntityKind("ground"),
                        horizontal == 7 && vertical == 7 ? "exit" : "ground", horizontal, vertical,
                        exit: horizontal == 7 && vertical == 7));
            entities.Add(Entity(100, BoardLayer.Actor, EntityKind.Player, "player", 0, 0));
            entities.Add(Entity(101, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 7, 0, false));
            return entities;
        }

        private static BoardBlueprint Blueprint(IEnumerable<BoardEntityState> entities, IEnumerable<BoardItemSpawn> items = null)
        {
            BoardGenerationConfig config = Config();
            return new BoardBlueprint(Address(config), config, entities, items ?? Array.Empty<BoardItemSpawn>());
        }

        private static BoardGenerationConfig Config(GenerationRange? path = null, GenerationRange? branches = null,
            GenerationRange? obstacles = null, GenerationRange? enemies = null, int threats = 2, int safeMoves = 1,
            IEnumerable<string> allowed = null, IEnumerable<ResourceGenerationBudget> budgets = null)
        {
            return new BoardGenerationConfig("ordinary", 1, path ?? new GenerationRange(1, 63),
                branches ?? new GenerationRange(0, 64), obstacles ?? new GenerationRange(0, 64), enemies ?? new GenerationRange(0, 8),
                threats, safeMoves, allowed ?? new[] { "player", "ground", "exit", "wall", "axe", "enemy", "berries", "water", "bridge", "unmodeled", "npc" },
                budgets ?? new[] { new ResourceGenerationBudget("tools", new GenerationRange(0, 64)), new ResourceGenerationBudget("food", new GenerationRange(0, 64)) });
        }

        private static List<BoardEntityDefinition> Definitions()
        {
            return new List<BoardEntityDefinition>
            {
                Entity(0, BoardLayer.Terrain, new EntityKind("ground"), "ground", 0, 0).Definition,
                Entity(0, BoardLayer.Terrain, new EntityKind("ground"), "exit", 0, 0, exit: true).Definition,
                Entity(0, BoardLayer.Terrain, new EntityKind("water"), "water", 0, 0, false).Definition,
                Entity(0, BoardLayer.Obstacle, EntityKind.Obstacle, "wall", 0, 0, false).Definition,
                Entity(0, BoardLayer.Obstacle, EntityKind.Obstacle, "bridge", 0, 0).Definition,
                Entity(0, BoardLayer.Actor, EntityKind.Player, "player", 0, 0).Definition,
                Entity(0, BoardLayer.Actor, EntityKind.Enemy, "enemy", 0, 0).Definition,
                Entity(0, BoardLayer.Actor, EntityKind.Enemy, "unmodeled", 0, 0).Definition,
                Entity(0, BoardLayer.Actor, new EntityKind("npc"), "npc", 0, 0).Definition,
                Entity(0, BoardLayer.Item, EntityKind.Item, "axe", 0, 0).Definition,
                Entity(0, BoardLayer.Item, EntityKind.Item, "berries", 0, 0).Definition,
                Entity(0, BoardLayer.Item, EntityKind.Item, "exit", 0, 0, exit: true).Definition
            };
        }

        private static BoardValidationCatalog Catalog()
        {
            return new BoardValidationCatalog(Definitions(),
                new Dictionary<string, string> { { "axe", "tools" }, { "berries", "food" } },
                new Dictionary<string, string> { { "source-a", "axe" }, { "source-b", "axe" }, { "berry-source", "berries" } },
                new Dictionary<string, ShamblerDefinition> { { "enemy", new ShamblerDefinition(10) } });
        }

        private static BoardAddress Address(BoardGenerationConfig config)
        {
            return new BoardAddress("run", "leg", "road", "segment", 3, config.Id, config.Version, -123);
        }

        private static BoardEntityState Entity(int identifier, BoardLayer layer, EntityKind kind, string content,
            int horizontal, int vertical, bool walkable = true, bool exit = false)
        {
            return new BoardEntityState(new EntityId(identifier), new BoardEntityDefinition(layer, kind, content,
                new BoardEntityTraits(walkable, exit, false)), new GridPosition(horizontal, vertical));
        }
    }
}