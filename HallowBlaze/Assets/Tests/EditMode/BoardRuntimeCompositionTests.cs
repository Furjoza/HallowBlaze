using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Tests.EditMode
{
    internal sealed class BoardRuntimeCompositionTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--)
            {
                if (createdObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[index]);
            }
            createdObjects.Clear();
        }

        [Test]
        public void Catalog_ClassifiesEveryCurrentLegacyContentKind()
        {
            LegacyBoardContentKind[] expectedKinds =
            {
                LegacyBoardContentKind.Player,
                LegacyBoardContentKind.Floor,
                LegacyBoardContentKind.OuterWall,
                LegacyBoardContentKind.Wall,
                LegacyBoardContentKind.Exit,
                LegacyBoardContentKind.Food,
                LegacyBoardContentKind.Soda,
                LegacyBoardContentKind.BushFood,
                LegacyBoardContentKind.BuriedFood,
                LegacyBoardContentKind.Aid,
                LegacyBoardContentKind.Enemy,
                LegacyBoardContentKind.Listener
            };

            CollectionAssert.AreEquivalent(
                expectedKinds,
                LegacyBoardContentCatalog.Definitions.Select(definition => definition.Kind));
            Assert.That(
                LegacyBoardContentCatalog.Definitions.Single(
                    definition => definition.Kind == LegacyBoardContentKind.OuterWall).Classification,
                Is.EqualTo(LegacyBoardContentClassification.PresentationOnly));
            Assert.That(
                LegacyBoardContentCatalog.Definitions.Where(
                    definition => definition.Kind != LegacyBoardContentKind.OuterWall),
                Has.All.Property(nameof(LegacyBoardContentDefinition.Classification))
                    .EqualTo(LegacyBoardContentClassification.MappedGameplayEntity));
            Assert.That(FoodReward(LegacyBoardContentKind.Food), Is.EqualTo(10));
            Assert.That(FoodReward(LegacyBoardContentKind.Soda), Is.EqualTo(20));
            Assert.That(FoodReward(LegacyBoardContentKind.BushFood), Is.EqualTo(10));
            LegacyBoardContentDefinition listener = LegacyBoardContentCatalog.Definitions.Single(
                definition => definition.Kind == LegacyBoardContentKind.Listener);
            Assert.That(listener.ContentId, Is.EqualTo("enemy.listener"));
            Assert.That(listener.Layer, Is.EqualTo(BoardLayer.Actor));
            Assert.That(listener.EntityKind, Is.EqualTo(EntityKind.Enemy));
            Assert.That(LegacyBoardContentCatalog.DeferredLegacyMutationPaths, Is.Empty);
        }

        /// <summary>Blueprint composition retains IDs, independent state and catalog enemy rules even after the caller clears its bindings.</summary>
        [Test]
        public void BlueprintCompositionRetainsTheModelWithoutDependingOnViewData()
        {
            RunState run = new RunState("ordinary-composition", 77, new RunStateConfiguration(100, 100, 3, "forest.start"));
            run.StartLeg("edge", "forest.start", "destination", new[]
            {
                new RouteSegmentState("segment", OrdinaryBoardGenerator.ConfigurationId, 1)
            });
            var request = new BoardRequest(BoardAddressFactory.CreateFromState(run), run.RunSeed,
                run.WorldNodeId, "forest", "temperate", 4);
            BoardValidationCatalog catalog = OrdinaryBoardContent.CreateCatalog();
            BoardBlueprint blueprint = OrdinaryBoardGenerator.Generate(request.Address,
                OrdinaryBoardContent.CreateConfiguration(request.Address), catalog, run.RunSeed);
            var views = blueprint.Entities.Reverse().ToDictionary(entity => entity.Id,
                entity => CreateView(entity.Definition.ContentId, entity.Position.X, entity.Position.Y));
            using (BoardRuntime runtime = BlueprintBoardRuntimeComposer.Compose(request, run, blueprint, catalog, views))
            {
                Assert.That(runtime.BoardState.GetEntities(), Is.EqualTo(blueprint.Entities));
                Assert.That(runtime.Views.Keys, Is.EquivalentTo(blueprint.Entities.Select(entity => entity.Id)));
                Assert.That(runtime.Enemies.Count, Is.EqualTo(2));
                foreach (ShamblerState enemy in runtime.Enemies)
                {
                    BoardEntityState entity = blueprint.Entities.Single(candidate => candidate.Id.Equals(enemy.ActorId));
                    Assert.That(enemy.Definition.Damage, Is.EqualTo(entity.Definition.ContentId == OrdinaryBoardContent.Enemy10Id ? 10 : 20));
                    Assert.That(enemy.LockedIntent, Is.Not.Null);
                }
                views.Clear();
                Assert.That(runtime.Views.Count, Is.EqualTo(blueprint.Entities.Count));
                runtime.BoardState.TryRemove(blueprint.Items[0].EntityId);
                Assert.That(blueprint.CreateState().Count, Is.EqualTo(blueprint.Entities.Count));
                Assert.That(run.Food, Is.EqualTo(100));
                Assert.That(run.Health, Is.EqualTo(100));
            }
        }

        /// <summary>Missing, foreign, repeated and misplaced view bindings cannot alter a blueprint or create a runtime.</summary>
        [TestCase("count", BoardRuntimeDiagnosticCode.InvalidInput)]
        [TestCase("missing", BoardRuntimeDiagnosticCode.DuplicateEntity)]
        [TestCase("duplicate", BoardRuntimeDiagnosticCode.DuplicateEntity)]
        [TestCase("position", BoardRuntimeDiagnosticCode.ViewPositionMismatch)]
        [TestCase("identity", BoardRuntimeDiagnosticCode.InvalidInput)]
        public void BlueprintCompositionRejectsChangedViewBindings(string scenario, BoardRuntimeDiagnosticCode expected)
        {
            RunState run = new RunState("ordinary-composition", 77, new RunStateConfiguration(100, 100, 0, "forest.start"));
            run.StartLeg("edge", "forest.start", "destination", new[]
            {
                new RouteSegmentState("segment", OrdinaryBoardGenerator.ConfigurationId, 1)
            });
            var request = new BoardRequest(BoardAddressFactory.CreateFromState(run), run.RunSeed,
                run.WorldNodeId, "forest", "temperate", 1);
            BoardValidationCatalog catalog = OrdinaryBoardContent.CreateCatalog();
            BoardBlueprint blueprint = OrdinaryBoardGenerator.Generate(request.Address,
                OrdinaryBoardContent.CreateConfiguration(request.Address), catalog, run.RunSeed);
            var views = blueprint.Entities.ToDictionary(entity => entity.Id,
                entity => CreateView(entity.Definition.ContentId, entity.Position.X, entity.Position.Y));
            EntityId first = blueprint.Entities[0].Id;
            if (scenario == "count") views.Remove(first);
            if (scenario == "missing") { GameObject view = views[first]; views.Remove(first); views.Add(new EntityId(99999), view); }
            if (scenario == "duplicate")
            {
                BoardEntityState player = blueprint.Entities.Single(entity => entity.Definition.Kind.Equals(EntityKind.Player));
                BoardEntityState terrain = blueprint.Entities.Single(entity => entity.Definition.Layer == BoardLayer.Terrain &&
                    entity.Position.Equals(player.Position));
                views[player.Id] = views[terrain.Id];
            }
            if (scenario == "position") views[first].transform.position += Vector3.right;
            if (scenario == "identity") request = new BoardRequest(new BoardAddress(request.RunId, request.Address.LegId,
                request.Address.EdgeId, "foreign", 1, OrdinaryBoardGenerator.ConfigurationId, 1, request.BoardSeed),
                request.RunSeed, run.WorldNodeId, "forest", "temperate", 1);
            string hash = blueprint.ContentHash;
            BoardRuntimeCompositionException failure = Assert.Throws<BoardRuntimeCompositionException>(() =>
                BlueprintBoardRuntimeComposer.Compose(request, run, blueprint, catalog, views));
            Assert.That(failure.Code, Is.EqualTo(expected));
            Assert.That(blueprint.ContentHash, Is.EqualTo(hash));
            Assert.That(run.Food, Is.EqualTo(100));
            Assert.That(run.Health, Is.EqualTo(100));
        }

        [Test]
        public void Compose_ExitDescriptorMarksWalkableTerrainAndResolvesOneExitAfterCost()
        {
            RunState run = CreateRun();
            using (BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(CreateRequest(run), run,
                new GridBounds(0, 0, 1, 1), CreateValidLayout()))
            {
                Assert.That(runtime.Controller.Resolve(new MoveCommand(Direction.North)).Accepted, Is.True);
                TurnResult result = runtime.Controller.Resolve(new MoveCommand(Direction.East));
                Assert.That(result.Accepted, Is.True);
                Assert.That(result.Events.Last(), Is.TypeOf<ExitReachedEvent>());
                Assert.That(result.Events.OfType<ExitReachedEvent>().Count(), Is.EqualTo(1));
                Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
                Assert.That(run.Food, Is.EqualTo(98));
                Assert.That(runtime.Controller.IsTerminal, Is.True);
                Assert.That(runtime.BoardState.GetEntities().Single(entity => entity.Definition.Layer == BoardLayer.Terrain &&
                    entity.Position.Equals(new GridPosition(1, 1))).Definition.Traits.IsExit, Is.True);
            }
        }

        [Test]
        public void Compose_AssignsStableIdsIndependentOfDescriptorOrder()
        {
            RunState run = CreateRun();
            BoardRequest request = CreateRequest(run);
            List<LegacyBoardView> layout = CreateValidLayout();

            BoardRuntime first = LegacyBoardRuntimeComposer.Compose(
                request,
                run,
                new GridBounds(0, 0, 1, 1),
                layout);
            Dictionary<GameObject, long> firstIds = first.Views.ToDictionary(
                pair => pair.Value,
                pair => pair.Key.Value);
            first.Dispose();

            layout.Reverse();
            BoardRuntime second = LegacyBoardRuntimeComposer.Compose(
                request,
                run,
                new GridBounds(0, 0, 1, 1),
                layout);
            Dictionary<GameObject, long> secondIds = second.Views.ToDictionary(
                pair => pair.Value,
                pair => pair.Key.Value);

            Assert.That(secondIds, Is.EquivalentTo(firstIds));
            second.Dispose();
        }

        [Test]
        public void Compose_EnemyBatchHasStableIdsInitialPlansAndOneController()
        {
            RunState run = CreateRun();
            var layout = new List<LegacyBoardView>();
            for (int column = 0; column < 3; column++)
                for (int row = 0; row < 3; row++)
                    layout.Add(Descriptor("Floor", LegacyBoardContentKind.Floor, column, row));
            layout.Add(Descriptor("Player", LegacyBoardContentKind.Player, 1, 1));
            layout.Add(Descriptor("Exit", LegacyBoardContentKind.Exit, 2, 2));
            layout.Add(new LegacyBoardView(CreateView("Enemy20", 0, 1), LegacyBoardContentKind.Enemy,
                new GridPosition(0, 1), 20));
            layout.Add(new LegacyBoardView(CreateView("Enemy10", 2, 1), LegacyBoardContentKind.Enemy,
                new GridPosition(2, 1), 10));

            Dictionary<GameObject, long> firstIds;
            using (BoardRuntime initial = LegacyBoardRuntimeComposer.Compose(CreateRequest(run), run,
                new GridBounds(0, 0, 2, 2), layout))
            {
                firstIds = initial.Views.ToDictionary(pair => pair.Value, pair => pair.Key.Value);
                Assert.That(initial.Enemies.Select(enemy => enemy.Definition.Damage), Is.EquivalentTo(new[] { 10, 20 }));
                Assert.That(initial.Enemies.Select(enemy => enemy.ActorId.Value), Is.Ordered.Ascending);
                Assert.That(initial.Enemies.All(enemy => enemy.Phase == ShamblerPhase.Active &&
                    enemy.LockedIntent.Kind == EnemyIntentKind.Attack), Is.True);
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(100));
            }

            layout.Reverse();
            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(CreateRequest(run), run,
                new GridBounds(0, 0, 2, 2), layout);
            try
            {
                Assert.That(runtime.Views.ToDictionary(pair => pair.Value, pair => pair.Key.Value), Is.EquivalentTo(firstIds));
                IReadOnlyList<ShamblerState> roster = runtime.Enemies;
                var structuralView = (IList<ShamblerState>)roster;
                Assert.Throws<NotSupportedException>(() => structuralView.Clear());
                EnemyIntent[] initialPlans = roster.Select(enemy => enemy.LockedIntent).ToArray();
                TurnController soleController = runtime.Controller;
                Assert.That(soleController.Resolve(new MoveCommand(Direction.West)).Accepted, Is.False);
                CollectionAssert.AreEqual(initialPlans, roster.Select(enemy => enemy.LockedIntent));
                Assert.That(run.Food, Is.EqualTo(100));
                Assert.That(run.Health, Is.EqualTo(100));

                TurnResult active = soleController.Resolve(new WaitCommand());
                Assert.That(active.Accepted, Is.True);
                Assert.That(active.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
                EnemyAttackResolvedEvent[] attacks = active.Events.OfType<EnemyAttackResolvedEvent>().ToArray();
                Assert.That(attacks, Has.Length.EqualTo(2));
                CollectionAssert.AreEqual(roster.Select(enemy => enemy.ActorId), attacks.Select(attack => attack.AttackerId));
                Assert.That(run.Health, Is.EqualTo(70));
                Assert.That(run.Food, Is.EqualTo(99));
                Assert.That(roster.All(enemy => enemy.Phase == ShamblerPhase.Rest &&
                    enemy.LockedIntent.Kind == EnemyIntentKind.Wait), Is.True);

                TurnResult rest = soleController.Resolve(new WaitCommand());
                Assert.That(rest.Events.OfType<EnemyAttackResolvedEvent>(), Is.Empty);
                Assert.That(run.Health, Is.EqualTo(70));
                Assert.That(run.Food, Is.EqualTo(98));
                Assert.That(roster.All(enemy => enemy.Phase == ShamblerPhase.Active &&
                    enemy.LockedIntent.Kind == EnemyIntentKind.Attack), Is.True);

                TurnResult escaped = soleController.Resolve(new MoveCommand(Direction.North));
                Assert.That(escaped.Events.OfType<EnemyAttackResolvedEvent>().Count(), Is.EqualTo(2));
                Assert.That(run.Health, Is.EqualTo(70));
                EnemyIntent[] terminalPlans = roster.Select(enemy => enemy.LockedIntent).ToArray();
                TurnResult exit = soleController.Resolve(new MoveCommand(Direction.East));
                Assert.That(exit.Events.Last(), Is.TypeOf<ExitReachedEvent>());
                Assert.That(soleController.IsTerminal, Is.True);
                CollectionAssert.AreEqual(terminalPlans, roster.Select(enemy => enemy.LockedIntent));
                Assert.That(runtime.Controller, Is.SameAs(soleController));
                Assert.That(soleController.Resolve(new WaitCommand()).Accepted, Is.False);
                Assert.That(run.Food, Is.EqualTo(96));
                runtime.Dispose();
                runtime.Dispose();
                Assert.That(roster, Is.Empty);
                Assert.That(layout.All(descriptor => descriptor.View != null), Is.True);
                Assert.Throws<ObjectDisposedException>(() => { object ignored = runtime.Enemies; });
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [TestCase(null)]
        [TestCase(0)]
        [TestCase(15)]
        public void Compose_MissingOrUnsupportedEnemyDamageHasStableDiagnostic(int? damage)
        {
            RunState run = CreateRun();
            List<LegacyBoardView> layout = CreateValidLayout();
            layout.Add(new LegacyBoardView(CreateView("Invalid Enemy", 0, 1), LegacyBoardContentKind.Enemy,
                new GridPosition(0, 1), damage));
            BoardRuntimeCompositionException diagnostic = Assert.Throws<BoardRuntimeCompositionException>(() =>
                LegacyBoardRuntimeComposer.Compose(CreateRequest(run), run, new GridBounds(0, 0, 1, 1), layout));
            Assert.That(diagnostic.Code, Is.EqualTo(BoardRuntimeDiagnosticCode.InvalidInput));
            Assert.That(diagnostic.Context, Does.StartWith("enemy-damage:"));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.Food, Is.EqualTo(100));
        }

        [Test]
        public void Compose_ProducesReadOnlyRegistryAndMatchingModelPositions()
        {
            RunState run = CreateRun();
            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(
                CreateRequest(run),
                run,
                new GridBounds(0, 0, 1, 1),
                CreateValidLayout());

            var mutableView = runtime.Views as IDictionary<EntityId, GameObject>;
            Assert.That(mutableView, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() =>
                mutableView.Add(new EntityId(999), CreateView("Injected", 0, 0)));

            foreach (BoardEntityState entity in runtime.BoardState.GetEntities())
            {
                Assert.That(runtime.Views.ContainsKey(entity.Id), Is.True, entity.Id.ToString());
                Vector3 viewPosition = runtime.Views[entity.Id].transform.position;
                Assert.That(viewPosition.x, Is.EqualTo(entity.Position.X));
                Assert.That(viewPosition.y, Is.EqualTo(entity.Position.Y));
            }

            int mappedCount = runtime.BoardState.Count;
            Assert.That(runtime.Views, Has.Count.EqualTo(mappedCount));
            runtime.Dispose();
            Assert.That(mutableView, Is.Empty);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                object ignored = runtime.Controller;
            });
        }

        [TestCase("unknown", BoardRuntimeDiagnosticCode.UnknownContent)]
        [TestCase("duplicate", BoardRuntimeDiagnosticCode.DuplicateEntity)]
        [TestCase("out-of-bounds", BoardRuntimeDiagnosticCode.OutOfBounds)]
        [TestCase("missing-player", BoardRuntimeDiagnosticCode.MissingPlayer)]
        [TestCase("missing-terrain", BoardRuntimeDiagnosticCode.MissingTerrain)]
        [TestCase("missing-exit", BoardRuntimeDiagnosticCode.MissingExit)]
        [TestCase("view-mismatch", BoardRuntimeDiagnosticCode.ViewPositionMismatch)]
        public void Compose_InvalidLayoutsFailWithStableDiagnostic(
            string scenario,
            BoardRuntimeDiagnosticCode expectedCode)
        {
            RunState run = CreateRun();
            List<LegacyBoardView> layout = CreateValidLayout();
            MakeInvalid(layout, scenario);

            BoardRuntimeCompositionException exception = Assert.Throws<BoardRuntimeCompositionException>(() =>
                LegacyBoardRuntimeComposer.Compose(
                    CreateRequest(run),
                    run,
                    new GridBounds(0, 0, 1, 1),
                    layout));

            Assert.That(exception.Code, Is.EqualTo(expectedCode));
            Assert.That(exception.Context, Is.Not.Empty);
        }

        [Test]
        public void Compose_DoesNotMutateRunStateAndKeepsUnityOutOfDomainTypes()
        {
            RunState run = CreateRun();
            string runId = run.RunId;
            int seed = run.RunSeed;
            int day = run.CurrentDay;
            string node = run.WorldNodeId;
            int health = run.Health;
            int food = run.Food;
            RunStatus status = run.Status;
            int routeCount = run.Route.Count;

            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(
                CreateRequest(run),
                run,
                new GridBounds(0, 0, 1, 1),
                CreateValidLayout());

            Assert.That(run.RunId, Is.EqualTo(runId));
            Assert.That(run.RunSeed, Is.EqualTo(seed));
            Assert.That(run.CurrentDay, Is.EqualTo(day));
            Assert.That(run.WorldNodeId, Is.EqualTo(node));
            Assert.That(run.Health, Is.EqualTo(health));
            Assert.That(run.Food, Is.EqualTo(food));
            Assert.That(run.Status, Is.EqualTo(status));
            Assert.That(run.Route, Has.Count.EqualTo(routeCount));

            AssertDomainTypeHasNoUnityObjectFields(typeof(BoardState));
            AssertDomainTypeHasNoUnityObjectFields(typeof(BoardEntityState));
            AssertDomainTypeHasNoUnityObjectFields(typeof(BoardEntityDefinition));
            runtime.Dispose();
        }

        private static int FoodReward(LegacyBoardContentKind kind)
        {
            return LegacyBoardContentCatalog.Definitions.Single(
                definition => definition.Kind == kind).AutomaticFoodReward;
        }

        private static void AssertDomainTypeHasNoUnityObjectFields(Type type)
        {
            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.That(
                    typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType),
                    Is.False,
                    type.FullName + "." + field.Name);
            }
        }

        private void MakeInvalid(List<LegacyBoardView> layout, string scenario)
        {
            switch (scenario)
            {
                case "unknown":
                    layout.Add(new LegacyBoardView(
                        CreateView("Unknown", 0, 1),
                        LegacyBoardContentKind.Unknown,
                        new GridPosition(0, 1)));
                    break;
                case "duplicate":
                    layout.Add(new LegacyBoardView(
                        CreateView("Wall A", 0, 1),
                        LegacyBoardContentKind.Wall,
                        new GridPosition(0, 1)));
                    layout.Add(new LegacyBoardView(
                        CreateView("Wall B", 0, 1),
                        LegacyBoardContentKind.Wall,
                        new GridPosition(0, 1)));
                    break;
                case "out-of-bounds":
                    layout.Add(new LegacyBoardView(
                        CreateView("Outside", 3, 3),
                        LegacyBoardContentKind.Wall,
                        new GridPosition(3, 3)));
                    break;
                case "missing-player":
                    layout.RemoveAll(view => view.Kind == LegacyBoardContentKind.Player);
                    break;
                case "missing-terrain":
                    layout.RemoveAll(view =>
                        view.Kind == LegacyBoardContentKind.Floor &&
                        view.Position.Equals(new GridPosition(1, 0)));
                    break;
                case "missing-exit":
                    layout.RemoveAll(view => view.Kind == LegacyBoardContentKind.Exit);
                    break;
                case "view-mismatch":
                    LegacyBoardView player = layout.Single(
                        view => view.Kind == LegacyBoardContentKind.Player);
                    player.View.transform.position = new Vector3(0.5f, 0f, 0f);
                    break;
                default:
                    Assert.Fail("Unknown invalid-layout scenario: " + scenario);
                    break;
            }
        }

        [Test]
        public void AddressedComposition_RejectsStaleIdentityWithoutPartialRuntime()
        {
            RunState run = RouteLegTests.NewRun();
            run.StartLeg("edge", "a", "b", RouteLegTests.Segments());
            BoardRequest request = BoardRequest.CreateFromRun(run, RouteLegTests.World());
            List<LegacyBoardView> layout = CreateValidLayout();
            using (BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 1, 1), layout))
                Assert.That(runtime.Request.Address, Is.SameAs(request.Address));
            foreach (BoardAddress foreign in RouteLegTests.ForeignAddresses(request.Address))
            {
                BoardRequest wrong = new BoardRequest(foreign, run.RunSeed, "a", "shelter", "forest", 1);
                BoardRuntimeCompositionException error = Assert.Throws<BoardRuntimeCompositionException>(() =>
                    LegacyBoardRuntimeComposer.Compose(wrong, run, new GridBounds(0, 0, 1, 1), layout));
                Assert.That(error.Code, Is.EqualTo(BoardRuntimeDiagnosticCode.InvalidInput));
            }
            BoardRequest legacy = new BoardRequest(run.RunId, run.RunSeed, "a", 1, request.BoardSeed, "shelter", "forest", 1);
            Assert.Throws<BoardRuntimeCompositionException>(() => LegacyBoardRuntimeComposer.Compose(legacy, run,
                new GridBounds(0, 0, 1, 1), layout));
            Assert.That(run.CompletedDayCount, Is.Zero);
            Assert.That(run.TravelHistory, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(100));
            Assert.That(layout.All(view => view.View != null && view.View.activeSelf), Is.True);
        }

        private List<LegacyBoardView> CreateValidLayout()
        {
            return new List<LegacyBoardView>
            {
                Descriptor("Floor 0,0", LegacyBoardContentKind.Floor, 0, 0),
                Descriptor("Floor 0,1", LegacyBoardContentKind.Floor, 0, 1),
                Descriptor("Floor 1,0", LegacyBoardContentKind.Floor, 1, 0),
                Descriptor("Floor 1,1", LegacyBoardContentKind.Floor, 1, 1),
                Descriptor("Player", LegacyBoardContentKind.Player, 0, 0),
                Descriptor("Exit", LegacyBoardContentKind.Exit, 1, 1),
                Descriptor("Food", LegacyBoardContentKind.Food, 1, 0),
                Descriptor("Outer", LegacyBoardContentKind.OuterWall, -1, 0)
            };
        }

        private LegacyBoardView Descriptor(
            string name,
            LegacyBoardContentKind kind,
            int x,
            int y)
        {
            return new LegacyBoardView(CreateView(name, x, y), kind, new GridPosition(x, y));
        }

        private GameObject CreateView(string name, float x, float y)
        {
            var view = new GameObject(name);
            view.transform.position = new Vector3(x, y, 0f);
            createdObjects.Add(view);
            return view;
        }

        private static RunState CreateRun()
        {
            return new RunState(
                "runtime-tests-run",
                12345,
                new RunStateConfiguration(100, 100, 0, "forest.start"));
        }

        private static BoardRequest CreateRequest(RunState run)
        {
            return new BoardRequest(
                run.RunId,
                run.RunSeed,
                run.WorldNodeId,
                run.CurrentDay,
                run.GetBoardSeed(),
                "forest",
                "temperate",
                1);
        }
    }
}
