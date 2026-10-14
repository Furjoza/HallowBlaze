using System;
using System.Collections.Generic;
using System.Globalization;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies initial layout ownership without a scene or physics state.</summary>
    public class BoardBlueprintTests
    {
        /// <summary>Export and construction retain all layers and isolate subsequent mutations.</summary>
        [Test]
        public void StateRoundTripPreservesLayersWithoutSharingMutableCollections()
        {
            var state = new BoardState();
            var position = new GridPosition(2, 2);
            foreach (BoardLayer layer in Enum.GetValues(typeof(BoardLayer)))
                Assert.That(state.TryAdd(Entity((int)layer, layer, position)), Is.True);

            foreach (BoardEntityState marker in Markers()) Assert.That(state.TryAdd(marker), Is.True);
            var items = new List<BoardItemSpawn> { new BoardItemSpawn("item-a", new EntityId(2), "content-2", "food") };
            BoardBlueprint blueprint = BoardBlueprint.FromState(Address(), Config(), state, items);
            BoardState restored = blueprint.CreateState();
            CollectionAssert.AreEqual(state.GetEntities(), restored.GetEntities());
            Assert.That(restored.GetEntitiesAt(position).Count, Is.EqualTo(4));
            items.Clear();
            Assert.That(state.TryRemove(new EntityId(0)), Is.True);
            Assert.That(restored.TryMove(new EntityId(1), new GridPosition(3, 2)), Is.True);
            Assert.That(blueprint.Entities.Count, Is.EqualTo(7));
            Assert.That(blueprint.Exits.Count, Is.EqualTo(2));
            Assert.That(blueprint.Items.Count, Is.EqualTo(1));
            Assert.That(blueprint.CreateState().TryGetEntity(new EntityId(1), out BoardEntityState original), Is.True);
            Assert.That(original.Position, Is.EqualTo(position));
            Assert.Throws<NotSupportedException>(() => ((IList<BoardEntityState>)blueprint.Entities).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<GridPosition>)blueprint.Exits).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<BoardItemSpawn>)blueprint.Items).Clear());
            Assert.That(BoardBlueprint.FromState(blueprint.Address, blueprint.Config, blueprint.CreateState(), blueprint.Items),
                Is.EqualTo(blueprint));
        }

        /// <summary>Duplicate player starts, a missing exit, and start/exit overlap are rejected.</summary>
        [Test]
        public void MarkersMustBeUniqueAndInBounds()
        {
            var valid = new List<BoardEntityState>(Markers());
            Assert.Throws<ArgumentException>(() => Blueprint(Array.Empty<BoardEntityState>()));
            Assert.Throws<ArgumentException>(() => Blueprint(new[] { valid[0] }));
            Assert.Throws<ArgumentException>(() => Blueprint(new[] { valid[1], valid[2] }));
            valid.Add(new BoardEntityState(new EntityId(200), valid[0].Definition, new GridPosition(1, 0)));
            Assert.Throws<ArgumentException>(() => Blueprint(valid));
            valid.RemoveAt(valid.Count - 1);
            valid.Add(new BoardEntityState(new EntityId(200), new BoardEntityDefinition(BoardLayer.Obstacle,
                EntityKind.Obstacle, "duplicate-exit", new BoardEntityTraits(true, true, false)), valid[1].Position));
            Assert.That(Blueprint(valid).Exits.Count, Is.EqualTo(2), "Layer markers at one position denote one logical exit.");
            Assert.Throws<ArgumentException>(() => Blueprint(new[] { valid[0],
                new BoardEntityState(new EntityId(201), valid[1].Definition, valid[0].Position) }));
            Assert.Throws<ArgumentException>(() => Blueprint(new[] { valid[0],
                new BoardEntityState(new EntityId(201), valid[1].Definition, new GridPosition(8, 7)) }));
        }

        /// <summary>Entity identity and same-layer occupancy cannot silently lose data during mapping.</summary>
        [Test]
        public void EntityConflictsAreRejectedBeforeStateConstruction()
        {
            var original = Entity(10, BoardLayer.Actor, new GridPosition(2, 2));
            foreach (BoardEntityState conflict in new[]
            {
                Entity(10, BoardLayer.Item, new GridPosition(3, 2)),
                Entity(11, BoardLayer.Actor, new GridPosition(2, 2)),
                Entity(12, BoardLayer.Item, new GridPosition(8, 2))
            })
            {
                var entities = new List<BoardEntityState>(Markers()) { original, conflict };
                Assert.Throws<ArgumentException>(() => Blueprint(entities));
            }
        }

        /// <summary>Generation is an engine-free domain assembly.</summary>
        [Test]
        public void GenerationAssemblyDoesNotReferenceUnity()
        {
            foreach (var reference in typeof(BoardBlueprint).Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
        }

        /// <summary>All unordered inputs normalize ordinally and no current culture enters comparison.</summary>
        [Test]
        public void EqualityAndHashAreIndependentOfInputOrderAndCulture()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                var entities = new List<BoardEntityState>(Markers())
                {
                    ItemEntity(10, "berries", new GridPosition(2, 2)),
                    ItemEntity(11, "axe", new GridPosition(3, 2))
                };
                var items = new List<BoardItemSpawn>
                {
                    new BoardItemSpawn("spawn-I", new EntityId(10), "berries", "food"),
                    new BoardItemSpawn("spawn-i", new EntityId(11), "axe", "tools", "source-oak")
                };
                var content = new List<string> { "player", "exit", "berries", "axe" };
                var budgets = new List<ResourceGenerationBudget>
                {
                    new ResourceGenerationBudget("tools", new GenerationRange(1, 1)),
                    new ResourceGenerationBudget("food", new GenerationRange(1, 4))
                };
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var first = new BoardBlueprint(Address(), Config(allowed: content, budgets: budgets), entities, items);
                content.Reverse();
                budgets.Reverse();
                entities.Reverse();
                items.Reverse();
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                var second = new BoardBlueprint(Address(), Config(allowed: content, budgets: budgets), entities, items);

                Assert.That(second, Is.EqualTo(first));
                Assert.That(second.GetHashCode(), Is.EqualTo(first.GetHashCode()));
                Assert.That(second.ContentHash, Is.EqualTo(first.ContentHash).And.Match("^[0-9a-f]{64}$"));
                Assert.That(first.Equals(null), Is.False);
                Assert.That(first.Equals(new object()), Is.False);
                Assert.That(first.GetHashCode(), Is.EqualTo(unchecked((int)uint.Parse(
                    first.ContentHash.Substring(6, 2) + first.ContentHash.Substring(4, 2) +
                    first.ContentHash.Substring(2, 2) + first.ContentHash.Substring(0, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture))));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }

        /// <summary>Equal seeds cannot alias different segment, traversal, or configuration identities.</summary>
        [TestCase("run")]
        [TestCase("leg")]
        [TestCase("edge")]
        [TestCase("segment")]
        [TestCase("day")]
        [TestCase("config")]
        [TestCase("version")]
        [TestCase("seed")]
        public void HashIncludesEveryAddressField(string field)
        {
            BoardBlueprint original = Blueprint(Markers());
            var address = new BoardAddress(field == "run" ? "other" : "run",
                field == "leg" ? "leg-2" : "leg-1", field == "edge" ? "road-2" : "road",
                field == "segment" ? "segment-b" : "segment-a", field == "day" ? 2 : 1,
                field == "config" ? "ordinary-2" : "ordinary", field == "version" ? 2 : 1,
                field == "seed" ? -123 : 123);
            var candidate = new BoardBlueprint(address,
                Config(id: address.GenerationConfigurationId, version: address.GenerationVersion),
                Markers(), Array.Empty<BoardItemSpawn>());
            AssertDifferent(original, candidate);
        }

        /// <summary>Topology, opening safety, resource pressure and allowed content are all comparison inputs.</summary>
        [TestCase("path-min")]
        [TestCase("path-max")]
        [TestCase("branches")]
        [TestCase("obstacles")]
        [TestCase("enemies")]
        [TestCase("threats")]
        [TestCase("safe-moves")]
        [TestCase("allowed")]
        [TestCase("resource-family")]
        [TestCase("resource-min")]
        [TestCase("resource-max")]
        public void HashIncludesEveryBudgetDimension(string field)
        {
            var changed = new BoardGenerationConfig("ordinary", 1,
                new GenerationRange(field == "path-min" ? 9 : 8, field == "path-max" ? 25 : 24),
                new GenerationRange(1, field == "branches" ? 13 : 12),
                new GenerationRange(0, field == "obstacles" ? 17 : 16),
                new GenerationRange(0, field == "enemies" ? 7 : 6),
                field == "threats" ? 3 : 2, field == "safe-moves" ? 2 : 1,
                field == "allowed" ? new[] { "player", "exit", "another-item" } : new[] { "player", "exit", "content-2" },
                new[] { new ResourceGenerationBudget(field == "resource-family" ? "tools" : "food",
                    new GenerationRange(field == "resource-min" ? 1 : 0, field == "resource-max" ? 5 : 4)) });
            AssertDifferent(Blueprint(Markers()), new BoardBlueprint(Address(), changed, Markers(), Array.Empty<BoardItemSpawn>()));
        }

        /// <summary>Every immutable entity value participates, including compatibility-only automatic food data.</summary>
        [TestCase("id")]
        [TestCase("x")]
        [TestCase("y")]
        [TestCase("layer")]
        [TestCase("kind")]
        [TestCase("content")]
        [TestCase("walkable")]
        [TestCase("interactable")]
        [TestCase("exit")]
        public void HashIncludesEveryEntityValue(string field)
        {
            var entities = new List<BoardEntityState>(Markers()) { Entity(10, BoardLayer.Obstacle, new GridPosition(2, 2)) };
            BoardBlueprint original = Blueprint(entities);
            entities[entities.Count - 1] = new BoardEntityState(new EntityId(field == "id" ? 11 : 10),
                new BoardEntityDefinition(field == "layer" ? BoardLayer.Terrain : BoardLayer.Obstacle,
                    new EntityKind(field == "kind" ? "boulder" : "obstacle"), field == "content" ? "other" : "content-10",
                    new BoardEntityTraits(field == "walkable", field == "exit", field == "interactable")),
                new GridPosition(field == "x" ? 3 : 2, field == "y" ? 3 : 2));
            AssertDifferent(original, Blueprint(entities));
        }

        /// <summary>Spawn identity, definition, category, source and visibility all participate in comparison.</summary>
        [TestCase("instance")]
        [TestCase("definition")]
        [TestCase("family")]
        [TestCase("source")]
        [TestCase("visibility")]
        [TestCase("reward")]
        public void HashIncludesItemMetadataAndLegacyReward(string field)
        {
            var entities = new List<BoardEntityState>(Markers()) { ItemEntity(10, "berries", new GridPosition(2, 2)) };
            var originalItems = new[] { new BoardItemSpawn("item-a", new EntityId(10), "berries", "food") };
            var original = new BoardBlueprint(Address(), Config(), entities, originalItems);
            string definition = field == "definition" ? "apple" : "berries";
            entities[entities.Count - 1] = ItemEntity(10, definition, new GridPosition(2, 2), field == "reward" ? 99 : 0);
            var changedItems = new[] { new BoardItemSpawn(field == "instance" ? "item-b" : "item-a", new EntityId(10),
                definition, field == "family" ? "medicine" : "food", field == "source" ? "orchard" : null,
                initiallyObservable: field != "visibility") };
            AssertDifferent(original, new BoardBlueprint(Address(), Config(), entities, changedItems));
        }

        /// <summary>Length-framed identity fields cannot collide merely by moving a textual boundary.</summary>
        [Test]
        public void CanonicalEncodingKeepsTextFieldBoundaries()
        {
            var first = new BoardAddress("a", "bc", "road", "segment-a", 1, "ordinary", 1, 123);
            var second = new BoardAddress("ab", "c", "road", "segment-a", 1, "ordinary", 1, 123);
            AssertDifferent(new BoardBlueprint(first, Config(), Markers(), Array.Empty<BoardItemSpawn>()),
                new BoardBlueprint(second, Config(), Markers(), Array.Empty<BoardItemSpawn>()));
        }

        /// <summary>Initial counts reflect item instances and visibility, not rewards, collection or configuration targets.</summary>
        [Test]
        public void ManifestCountsInstancesAndSeparatesInitialVisibility()
        {
            var entities = new List<BoardEntityState>(Markers())
            {
                ItemEntity(10, "berries", new GridPosition(2, 2), 99),
                ItemEntity(11, "berries", new GridPosition(3, 2), 1),
                ItemEntity(12, "axe", new GridPosition(4, 2))
            };
            var items = new List<BoardItemSpawn>
            {
                new BoardItemSpawn("a", new EntityId(10), "berries", "food"),
                new BoardItemSpawn("b", new EntityId(11), "berries", "food", initiallyObservable: false),
                new BoardItemSpawn("c", new EntityId(12), "axe", "tools", "old-oak")
            };
            var blueprint = new BoardBlueprint(Address(), Config(), entities, items);
            entities.Clear();
            items.Clear();
            BoardState state = blueprint.CreateState();
            Assert.That(state.TryRemove(new EntityId(10)), Is.True);
            Assert.That(blueprint.InitialResources.Count, Is.EqualTo(2));
            Assert.That(blueprint.InitialResources[0].ResourceFamilyId, Is.EqualTo("food"));
            Assert.That(blueprint.InitialResources[0].ItemCount, Is.EqualTo(2));
            Assert.That(blueprint.InitialResources[0].ObservableItemCount, Is.EqualTo(1));
            Assert.That(blueprint.InitialResources[1].ResourceFamilyId, Is.EqualTo("tools"));
            Assert.That(blueprint.InitialResources[1].ItemCount, Is.EqualTo(1));
            Assert.That(blueprint.InitialResources[1].ObservableItemCount, Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() => ((IList<InitialResourceManifestEntry>)blueprint.InitialResources).Clear());
            Assert.That(blueprint.Items[2].SourceId, Is.EqualTo("old-oak"));
        }

        /// <summary>Item metadata cannot create orphan, duplicate, ambiguous or mismatched instances.</summary>
        [TestCase("instance")]
        [TestCase("entity")]
        [TestCase("source")]
        [TestCase("orphan")]
        [TestCase("wrong-layer")]
        [TestCase("definition")]
        [TestCase("missing")]
        [TestCase("family")]
        [TestCase("null")]
        public void InvalidItemBindingsAreRejected(string field)
        {
            var entities = new List<BoardEntityState>(Markers())
            {
                ItemEntity(10, "berries", new GridPosition(2, 2)),
                ItemEntity(11, "berries", new GridPosition(3, 2))
            };
            var items = new List<BoardItemSpawn>
            {
                new BoardItemSpawn("a", new EntityId(10), "berries", "food", "source-a"),
                new BoardItemSpawn(field == "instance" ? "a" : "b",
                    new EntityId(field == "entity" ? 10 : field == "orphan" ? 999 : field == "wrong-layer" ? 100 : 11),
                    field == "definition" ? "apple" : "berries", field == "family" ? "tools" : "food",
                    field == "source" ? "source-a" : "source-b")
            };
            if (field == "missing") items.RemoveAt(1);
            if (field == "null") items[1] = null;
            Assert.Throws<ArgumentException>(() => new BoardBlueprint(Address(), Config(), entities, items));
        }

        /// <summary>Stable IDs reject whitespace rather than silently normalizing different content identities.</summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase(" padded")]
        [TestCase("padded ")]
        public void InvalidStableItemIdsAreRejected(string invalid)
        {
            Assert.Throws<ArgumentException>(() => new BoardItemSpawn(invalid, new EntityId(1), "berries", "food"));
            Assert.Throws<ArgumentException>(() => new BoardItemSpawn("a", new EntityId(1), invalid, "food"));
            Assert.Throws<ArgumentException>(() => new BoardItemSpawn("a", new EntityId(1), "berries", invalid));
            Assert.Throws<ArgumentException>(() => new ResourceGenerationBudget(invalid, new GenerationRange(0, 1)));
            if (invalid != null)
                Assert.Throws<ArgumentException>(() => new BoardItemSpawn("a", new EntityId(1), "berries", "food", invalid));
        }

        /// <summary>Invalid ranges and impossible single-dimension targets are rejected before generation.</summary>
        [Test]
        public void InvalidBudgetsAndConfigurationIdentitiesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationRange(-1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationRange(2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceGenerationBudget("food", new GenerationRange(0, 65)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Config(path: new GenerationRange(0, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Config(path: new GenerationRange(8, 64)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Config(safeMoves: 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => Config(threats: 7));
            Assert.Throws<ArgumentOutOfRangeException>(() => Config(version: 0));
            Assert.Throws<ArgumentException>(() => Config(id: " ordinary"));
            Assert.Throws<ArgumentException>(() => Config(allowed: Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => Config(allowed: new[] { "player", "player" }));
            Assert.Throws<ArgumentException>(() => Config(allowed: new[] { " player" }));
            Assert.Throws<ArgumentException>(() => Config(budgets: new[]
            {
                new ResourceGenerationBudget("food", new GenerationRange(0, 1)),
                new ResourceGenerationBudget("food", new GenerationRange(2, 3))
            }));
            Assert.Throws<ArgumentException>(() => Config(budgets: new ResourceGenerationBudget[] { null }));
            Assert.Throws<ArgumentException>(() => new BoardBlueprint(Address(), Config(id: "other"), Markers(), Array.Empty<BoardItemSpawn>()));
            Assert.Throws<ArgumentException>(() => new BoardBlueprint(Address(), Config(version: 2), Markers(), Array.Empty<BoardItemSpawn>()));
        }

        /// <summary>Config targets remain independent values and cannot be edited through caller-owned collections.</summary>
        [Test]
        public void ConfigurationIsDetachedAndExpressesPressureBeyondEnemyCount()
        {
            var allowed = new List<string> { "player", "exit" };
            var budgets = new List<ResourceGenerationBudget> { new ResourceGenerationBudget("food", new GenerationRange(1, 3)) };
            BoardGenerationConfig first = Config(allowed: allowed, budgets: budgets);
            BoardGenerationConfig second = Config(path: new GenerationRange(12, 30), safeMoves: 2,
                budgets: new[] { new ResourceGenerationBudget("food", new GenerationRange(0, 1)) });
            allowed.Clear();
            budgets.Clear();
            Assert.That(first.EnemyCount, Is.EqualTo(second.EnemyCount));
            Assert.That(first.PathLength, Is.Not.EqualTo(second.PathLength));
            Assert.That(first.MinimumSafeStartMoves, Is.Not.EqualTo(second.MinimumSafeStartMoves));
            Assert.That(first.ResourceBudgets[0].ItemCount, Is.Not.EqualTo(second.ResourceBudgets[0].ItemCount));
            Assert.That(first.AllowedContentIds.Count, Is.EqualTo(2));
            Assert.That(first.ResourceBudgets.Count, Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() => ((IList<string>)first.AllowedContentIds).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<ResourceGenerationBudget>)first.ResourceBudgets).Clear());
        }

        /// <summary>Construction checks structure without claiming reachability, content-catalog or budget validation.</summary>
        [Test]
        public void StructurallyValidCandidatesRemainInspectableBeforeGameplayValidation()
        {
            var entities = new List<BoardEntityState>(Markers());
            for (int column = 0; column < 8; column++)
                entities.Add(Entity(200 + column, BoardLayer.Obstacle, new GridPosition(column, 1)));
            BoardBlueprint candidate = Blueprint(entities);
            Assert.That(candidate.Entities.Count, Is.EqualTo(11));
            Assert.That(candidate.InitialResources, Is.Empty);
        }

        /// <summary>The current legacy board's floor/exit pair exports losslessly as one logical exit, not a resource.</summary>
        [Test]
        public void LegacyLayeredExitExportsWithoutBecomingAResourceItem()
        {
            var state = new BoardState();
            var start = new GridPosition(0, 0);
            var exit = new GridPosition(7, 7);
            Assert.That(state.TryAdd(new BoardEntityState(new EntityId(1), new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Player, "legacy.player"), start)), Is.True);
            Assert.That(state.TryAdd(new BoardEntityState(new EntityId(2), new BoardEntityDefinition(BoardLayer.Terrain,
                new EntityKind("terrain"), "legacy.floor", new BoardEntityTraits(true, true, false)), exit)), Is.True);
            Assert.That(state.TryAdd(new BoardEntityState(new EntityId(3), new BoardEntityDefinition(BoardLayer.Item,
                new EntityKind("exit"), "legacy.exit", new BoardEntityTraits(false, true, false)), exit)), Is.True);
            Assert.That(state.TryAdd(ItemEntity(4, "legacy.food", new GridPosition(2, 2), 10)), Is.True);
            var item = new BoardItemSpawn("legacy-food-4", new EntityId(4), "legacy.food", "food");
            BoardBlueprint blueprint = BoardBlueprint.FromState(Address(), Config(), state, new[] { item });
            CollectionAssert.AreEqual(state.GetEntities(), blueprint.CreateState().GetEntities());
            CollectionAssert.AreEqual(new[] { exit }, blueprint.Exits);
            Assert.That(blueprint.Start, Is.EqualTo(start));
            Assert.That(blueprint.InitialResources.Count, Is.EqualTo(1));
            Assert.That(blueprint.InitialResources[0].ItemCount, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => BoardBlueprint.FromState(Address(), Config(), state, new[]
            {
                item, new BoardItemSpawn("not-an-item", new EntityId(3), "legacy.exit", "food")
            }));
        }

        /// <summary>Required null inputs and unsupported legacy board dimensions cannot produce partial exports.</summary>
        [Test]
        public void NullInputsAndUnsupportedExportsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new BoardBlueprint(null, Config(), Markers(), Array.Empty<BoardItemSpawn>()));
            Assert.Throws<ArgumentNullException>(() => new BoardBlueprint(Address(), null, Markers(), Array.Empty<BoardItemSpawn>()));
            Assert.Throws<ArgumentNullException>(() => new BoardBlueprint(Address(), Config(), null, Array.Empty<BoardItemSpawn>()));
            Assert.Throws<ArgumentNullException>(() => new BoardBlueprint(Address(), Config(), Markers(), null));
            Assert.Throws<ArgumentNullException>(() => BoardBlueprint.FromState(Address(), Config(), null, Array.Empty<BoardItemSpawn>()));
            Assert.Throws<ArgumentException>(() => BoardBlueprint.FromState(Address(), Config(),
                new BoardState(new GridBounds(0, 0, 8, 8)), Array.Empty<BoardItemSpawn>()));
        }

        private static void AssertDifferent(BoardBlueprint original, BoardBlueprint candidate)
        {
            Assert.That(candidate, Is.Not.EqualTo(original));
            Assert.That(candidate.ContentHash, Is.Not.EqualTo(original.ContentHash));
        }

        private static BoardAddress Address() => new BoardAddress("run", "leg-1", "road", "segment-a", 1, "ordinary", 1, 123);

        private static BoardGenerationConfig Config(GenerationRange? path = null, int threats = 2, int safeMoves = 1,
            IEnumerable<string> allowed = null, IEnumerable<ResourceGenerationBudget> budgets = null,
            string id = "ordinary", int version = 1) => new BoardGenerationConfig(id, version,
            path ?? new GenerationRange(8, 24), new GenerationRange(1, 12), new GenerationRange(0, 16),
            new GenerationRange(0, 6), threats, safeMoves, allowed ?? new[] { "player", "exit", "content-2" },
            budgets ?? new[] { new ResourceGenerationBudget("food", new GenerationRange(0, 4)) });

        private static BoardBlueprint Blueprint(IEnumerable<BoardEntityState> entities) =>
            new BoardBlueprint(Address(), Config(), entities, Array.Empty<BoardItemSpawn>());

        private static BoardEntityState[] Markers() => new[]
        {
            new BoardEntityState(new EntityId(100), new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Player, "player"), new GridPosition(0, 0)),
            new BoardEntityState(new EntityId(101), new BoardEntityDefinition(BoardLayer.Terrain,
                new EntityKind("exit"), "exit", new BoardEntityTraits(true, true, false)), new GridPosition(7, 7)),
            new BoardEntityState(new EntityId(102), new BoardEntityDefinition(BoardLayer.Terrain,
                new EntityKind("exit"), "exit", new BoardEntityTraits(true, true, false)), new GridPosition(0, 7))
        };

        private static BoardEntityState Entity(long id, BoardLayer layer, GridPosition position) =>
            new BoardEntityState(new EntityId(id), new BoardEntityDefinition(layer,
                new EntityKind(layer.ToString().ToLowerInvariant()), "content-" + id), position);

        private static BoardEntityState ItemEntity(long id, string contentId, GridPosition position, int reward = 0) =>
            new BoardEntityState(new EntityId(id), new BoardEntityDefinition(BoardLayer.Item,
                EntityKind.Item, contentId, BoardEntityTraits.Default, reward), position);
    }
}