using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
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
                LegacyBoardContentKind.Enemy
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
            Assert.That(LegacyBoardContentCatalog.DeferredLegacyMutationPaths, Has.Count.EqualTo(6));
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
