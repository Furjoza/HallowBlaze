using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Tests.PlayMode
{
    internal sealed class BoardRuntimeStartupTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();
        private readonly List<UnityEngine.Object> createdAssets = new List<UnityEngine.Object>();
        private Type boardManagerType;
        private Type gameManagerType;
        private object previousPersistenceRoot;
        private string persistenceRoot;
        private float previousTimeScale;
        private bool hadHighScore;
        private int previousHighScore;

        [SetUp]
        public void SetUp()
        {
            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(
                assembly => assembly.GetName().Name == "Assembly-CSharp");
            boardManagerType = gameAssembly.GetType("BoardManager", true);
            gameManagerType = gameAssembly.GetType("GameManager", true);
            previousPersistenceRoot = gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            previousTimeScale = Time.timeScale;
            hadHighScore = PlayerPrefs.HasKey("HighScore");
            previousHighScore = PlayerPrefs.GetInt("HighScore");
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--)
            {
                if (createdObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[index]);
            }
            createdObjects.Clear();
            foreach (UnityEngine.Object asset in createdAssets)
                if (asset != null)
                    UnityEngine.Object.DestroyImmediate(asset);
            createdAssets.Clear();
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousPersistenceRoot);
            if (persistenceRoot != null && Directory.Exists(persistenceRoot))
                Directory.Delete(persistenceRoot, true);
            persistenceRoot = null;
            Time.timeScale = previousTimeScale;
            if (hadHighScore)
                PlayerPrefs.SetInt("HighScore", previousHighScore);
            else
                PlayerPrefs.DeleteKey("HighScore");
        }

        [UnityTest]
        public IEnumerator SuccessfulSetupPublishesCompleteRuntimeWithModelViewParity()
        {
            Component manager = CreateBoardManager();
            GameObject player = CreateObject("Player", 0, 0);
            RunState run = CreateRun();
            BoardRequest request = CreateRequest(run);

            BoardRuntime runtime = InvokeSetup(manager, request, run, player);

            Assert.That(runtime, Is.SameAs(GetProperty<BoardRuntime>(manager, "ActiveRuntime")));
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.SameAs(request));
            Assert.That(runtime.RunState, Is.SameAs(run));
            Assert.That(runtime.Controller, Is.Not.Null);
            Assert.That(runtime.BoardState.Count, Is.EqualTo(6));
            Assert.That(runtime.Views, Has.Count.EqualTo(runtime.BoardState.Count));
            foreach (var entity in runtime.BoardState.GetEntities())
            {
                Vector3 viewPosition = runtime.Views[entity.Id].transform.position;
                Assert.That(viewPosition.x, Is.EqualTo(entity.Position.X));
                Assert.That(viewPosition.y, Is.EqualTo(entity.Position.Y));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator AddressedRngRecreatesFullLayoutAfterCosmeticDrawsAndSavedContinue()
        {
            UnityEngine.Random.State previousRandom = UnityEngine.Random.state;
            try
            {
                Component manager = CreateBoardManager();
                SetField(manager, "columns", 8);
                SetField(manager, "rows", 8);
                SetCount(manager, "wallCount", 5, 9);
                SetCount(manager, "foodCount", 1, 5);
                SetCount(manager, "buriedCount", 0, 1);
                SetCount(manager, "aidCount", 0, 1);
                SetField(manager, "floorTiles", new[] { CreateObject("Floor A", 0, 0), CreateObject("Floor B", 0, 0) });
                SetField(manager, "outerWallTiles", new[] { CreateObject("Outer A", 0, 0), CreateObject("Outer B", 0, 0) });
                RunState run = CreateRun();
                run.StartLeg("edge", run.WorldNodeId, "destination", new[]
                {
                    new RouteSegmentState("segment.1", "legacy.board.v1", 1),
                    new RouteSegmentState("segment.2", "legacy.board.v1", 1)
                });
                run.TryConsumeIntro(run.ActiveLeg.LegId);
                var request = new BoardRequest(BoardAddressFactory.CreateFromState(run), run.RunSeed,
                    run.WorldNodeId, "forest", "temperate", 1);
                GameObject player = CreateObject("RNG Player", 0, 0);
                UnityEngine.Random.InitState(101);
                UnityEngine.Random.State beforeSetup = UnityEngine.Random.state;
                BoardRuntime first = InvokeSetup(manager, request, run, player);
                Assert.That(UnityEngine.Random.state, Is.EqualTo(beforeSetup), "Generation must not consume global RNG.");
                string[] baseline = GeneratedLayout(manager);
                string[] authoritative = GameplayLayout(first);
                for (int draw = 0; draw < 200; draw++)
                    UnityEngine.Random.Range(0, 1000);
                BoardRuntime second = InvokeSetup(manager, request, run, player);
                Assert.That(GeneratedLayout(manager), Is.EqualTo(baseline));
                Assert.That(GameplayLayout(second), Is.EqualTo(authoritative));
                SetField(manager, "floorTiles", new[] { CreateObject("Additional Cosmetic Variant", 0, 0) });
                BoardRuntime alteredCosmetics = InvokeSetup(manager, request, run, player);
                Assert.That(GameplayLayout(alteredCosmetics), Is.EqualTo(authoritative),
                    "Changing floor choices must not consume the gameplay stream.");
                SetField(manager, "floorTiles", new[] { CreateObject("Floor A", 0, 0), CreateObject("Floor B", 0, 0) });
                RunState restored = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(run));
                var continued = new BoardRequest(BoardAddressFactory.CreateFromState(restored), restored.RunSeed,
                    restored.WorldNodeId, "forest", "temperate", 1);
                Assert.That(continued.Address, Is.EqualTo(request.Address));
                BoardRuntime resumed = InvokeSetup(manager, continued, restored, player);
                Assert.That(GeneratedLayout(manager), Is.EqualTo(baseline));
                Assert.That(GameplayLayout(resumed), Is.EqualTo(authoritative));
                yield return null;
            }
            finally
            {
                UnityEngine.Random.state = previousRandom;
            }
        }

        [UnityTest]
        public IEnumerator LegacyRngPreservesSeededPlacementAfterV3RoundTrip()
        {
            Component manager = CreateBoardManager();
            SetField(manager, "columns", 8);
            SetField(manager, "rows", 8);
            SetCount(manager, "foodCount", 3, 3);
            SetField(manager, "groundFoodTiles", new[] { CreateObject("Legacy Food", 0, 0) });
            RunState run = new RunState("legacy-layout", 54321,
                new RunStateConfiguration(100, 100, 0, "forest.start"),
                RunState.LegacyRandomAlgorithmId, RunState.LegacyRandomAlgorithmVersion);
            BoardRequest request = CreateRequest(run);
            var reference = new System.Random(request.BoardSeed);
            var available = new List<GridPosition>();
            for (int column = 1; column < 7; column++)
                for (int row = 1; row < 7; row++)
                    available.Add(new GridPosition(column, row));
            reference.Next(0, 1);
            reference.Next(3, 4);
            var expected = new List<GridPosition>();
            for (int index = 0; index < 3; index++)
            {
                int selected = reference.Next(0, available.Count);
                expected.Add(available[selected]);
                available.RemoveAt(selected);
                reference.Next(0, 1);
            }
            GameObject player = CreateObject("Legacy Player", 0, 0);
            foreach (RunState boundary in new[] { run,
                PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(run)) })
            {
                BoardRuntime runtime = InvokeSetup(manager, request, boundary, player);
                Assert.That(runtime.BoardState.GetEntities()
                    .Where(entity => runtime.Views[entity.Id].name.StartsWith("Legacy Food", StringComparison.Ordinal))
                    .Select(entity => entity.Position), Is.EquivalentTo(expected));
            }
            yield return null;
        }

        /// <summary>Ordinary setup retains blueprint identity, initial state, enemy rules and view parity across Continue and cosmetic changes.</summary>
        [UnityTest]
        public IEnumerator OrdinarySetupBuildsViewsFromValidatedBlueprintAndContinuesIdentically()
        {
            UnityEngine.Random.State previousRandom = UnityEngine.Random.state;
            try
            {
                Component manager = CreateOrdinaryBoardManager();
                RunState run = CreateRun();
                run.StartLeg("edge", run.WorldNodeId, "destination", new[]
                {
                    new RouteSegmentState("ordinary.segment", OrdinaryBoardGenerator.ConfigurationId, 1)
                });
                var request = new BoardRequest(BoardAddressFactory.CreateFromState(run), run.RunSeed,
                    run.WorldNodeId, "forest", "temperate", 1);
                GameObject player = CreateObject("Ordinary Player", 0, 0);
                UnityEngine.Random.InitState(987);
                UnityEngine.Random.State beforeSetup = UnityEngine.Random.state;
                BoardRuntime first = InvokeSetup(manager, request, run, player);
                BoardBlueprint blueprint = GetProperty<BoardBlueprint>(manager, "ActiveBlueprint");
                Assert.That(blueprint, Is.Not.Null);
                Assert.That(BoardValidator.Validate(blueprint, OrdinaryBoardContent.CreateCatalog()).IsValid, Is.True);
                Assert.That(UnityEngine.Random.state, Is.EqualTo(beforeSetup));
                Assert.That(first.BoardState.GetEntities(), Is.EqualTo(blueprint.Entities));
                Assert.That(first.Views.Count, Is.EqualTo(blueprint.Entities.Count));
                foreach (var entity in blueprint.Entities)
                {
                    Assert.That(first.Views[entity.Id].transform.position,
                        Is.EqualTo(new Vector3(entity.Position.X, entity.Position.Y, 0)));
                    if (entity.Definition.Kind.Equals(EntityKind.Enemy))
                        Assert.That(first.Enemies.Single(enemy => enemy.ActorId.Equals(entity.Id)).Definition.Damage,
                            Is.EqualTo(entity.Definition.ContentId == OrdinaryBoardContent.Enemy10Id ? 10 : 20));
                }
                string fingerprint = blueprint.ContentHash;
                UnityEngine.Random.Range(0, 9999);
                SetField(manager, "floorTiles", new[] { CreateObject("Cosmetic floor alternative", 0, 0) });
                RunState restored = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(run));
                BoardRuntime resumed = InvokeSetup(manager, request, restored, player);
                Assert.That(first.IsDisposed, Is.True);
                Assert.That(GetProperty<BoardBlueprint>(manager, "ActiveBlueprint").ContentHash, Is.EqualTo(fingerprint));
                Assert.That(resumed.BoardState.GetEntities(), Is.EqualTo(blueprint.Entities));
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(100));
                yield return null;
            }
            finally { UnityEngine.Random.state = previousRandom; }
        }

        /// <summary>Missing ordinary mappings restore the player and cannot publish a blueprint, runtime or partial board.</summary>
        [UnityTest]
        public IEnumerator OrdinaryMappingFailureDoesNotPublishOrMovePlayer()
        {
            Component manager = CreateOrdinaryBoardManager();
            RunState run = CreateRun();
            run.StartLeg("edge", run.WorldNodeId, "destination", new[]
            {
                new RouteSegmentState("ordinary.segment", OrdinaryBoardGenerator.ConfigurationId, 1)
            });
            var request = new BoardRequest(BoardAddressFactory.CreateFromState(run), run.RunSeed,
                run.WorldNodeId, "forest", "temperate", 1);
            GameObject player = CreateObject("Player on failed setup", 2, 2);
            SetField(manager, "wallTiles", Array.Empty<GameObject>());
            TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() => InvokeSetup(manager, request, run, player));
            Assert.That(failure.InnerException, Is.TypeOf<BoardRuntimeCompositionException>());
            Assert.That(GetProperty<BoardBlueprint>(manager, "ActiveBlueprint"), Is.Null);
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.Null);
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.Null);
            Assert.That(player.transform.position, Is.EqualTo(new Vector3(2, 2, 0)));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.Food, Is.EqualTo(100));
            yield return null;
            Assert.That(manager.transform.childCount, Is.Zero);
        }

        private Component CreateOrdinaryBoardManager()
        {
            Component manager = CreateBoardManager();
            SetField(manager, "columns", 8);
            SetField(manager, "rows", 8);
            GameObject food = CreateObject("Ordinary food prefab", 0, 0);
            GameObject soda = CreateObject("Ordinary soda prefab", 0, 0);
            soda.tag = "Soda";
            SetField(manager, "groundFoodTiles", new[] { food, soda });
            var enemies = new List<GameObject>();
            foreach (int damage in new[] { 10, 20 })
            {
                GameObject enemy = CreateObject("Ordinary enemy " + damage, 0, 0);
                SetField(enemy.AddComponent(boardManagerType.Assembly.GetType("Enemy", true)), "playerDamage", damage);
                enemies.Add(enemy);
            }
            SetField(manager, "enemyTiles", enemies.ToArray());
            return manager;
        }

        /// <summary>Version-2 runtime retains blueprint and recovery diagnostics across a serialized Continue boundary.</summary>
        [UnityTest]
        public IEnumerator OrdinaryPipelineVersionTwoContinuesWithIdenticalSelection()
        {
            Component manager = CreateOrdinaryBoardManager();
            var run = new RunState("pipeline-runtime", 77, new RunStateConfiguration(100, 100, 3, "forest.start"));
            run.StartLeg("edge", run.WorldNodeId, "destination", new[]
            {
                new RouteSegmentState("pipeline.segment", OrdinaryBoardGenerator.ConfigurationId, 2)
            });
            var request = new BoardRequest(BoardAddressFactory.CreateFromState(run), run.RunSeed, run.WorldNodeId, "forest", "temperate", 4);
            GameObject player = CreateObject("Pipeline player", 0, 0);
            BoardRuntime first = InvokeSetup(manager, request, run, player);
            BoardGenerationResult result = GetProperty<BoardGenerationResult>(manager, "ActiveGenerationResult");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Attempts.Count, Is.InRange(1, 10));
            Assert.That(first.BoardState.GetEntities(), Is.EqualTo(result.Blueprint.Entities));
            foreach (var entity in result.Blueprint.Entities)
                Assert.That(first.Views[entity.Id].transform.position, Is.EqualTo(new Vector3(entity.Position.X, entity.Position.Y, 0)));
            RunState restored = PersistenceJsonSerializer.DeserializeRun(PersistenceJsonSerializer.SerializeRun(run));
            BoardRuntime second = InvokeSetup(manager, request, restored, player);
            BoardGenerationResult continued = GetProperty<BoardGenerationResult>(manager, "ActiveGenerationResult");
            Assert.That(continued.Blueprint, Is.EqualTo(result.Blueprint));
            Assert.That(continued.ToString(), Is.EqualTo(result.ToString()));
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.Request.Address, Is.EqualTo(request.Address));
            Assert.That(run.Food, Is.EqualTo(100));
            Assert.That(run.Health, Is.EqualTo(100));
            yield return null;
        }

        /// <summary>A forced authored fallback composes the same independent runtime and view bindings as a generated result.</summary>
        [UnityTest]
        public IEnumerator ForcedFallbackBuildsRuntimeWithoutLosingModelData()
        {
            var run = new RunState("fallback-runtime", 77, new RunStateConfiguration(100, 100, 3, "forest.start"));
            run.StartLeg("edge", run.WorldNodeId, "destination", new[]
            {
                new RouteSegmentState("fallback.segment", OrdinaryBoardGenerator.ConfigurationId, 2)
            });
            var request = new BoardRequest(BoardAddressFactory.CreateFromState(run), run.RunSeed, run.WorldNodeId, "forest", "temperate", 4);
            var input = new BoardGenerationRequest(request.Address, OrdinaryBoardContent.CreateConfiguration(request.Address),
                OrdinaryBoardContent.CreateCatalog(), run.RunSeed);
            BoardGenerationResult result = BoardGenerationPipeline.Generate(input, (_, __) => null, OrdinaryBoardFallbacks.CreateLibrary());
            Assert.That(result.UsedFallback, Is.True, result.ToString());
            Assert.That(result.Attempts.Count, Is.EqualTo(9));
            var views = result.Blueprint.Entities.ToDictionary(entity => entity.Id,
                entity => CreateObject(entity.Definition.ContentId, entity.Position.X, entity.Position.Y));
            using (BoardRuntime runtime = BlueprintBoardRuntimeComposer.Compose(request, run, result.Blueprint, input.Catalog, views))
            {
                Assert.That(runtime.BoardState.GetEntities(), Is.EqualTo(result.Blueprint.Entities));
                Assert.That(runtime.Views.Count, Is.EqualTo(result.Blueprint.Entities.Count));
                Assert.That(runtime.Enemies.Count, Is.EqualTo(2));
                Assert.That(runtime.Enemies.All(enemy => enemy.LockedIntent != null), Is.True);
                Assert.That(runtime.Controller.Resolve(new WaitCommand()).Accepted, Is.True);
                Assert.That(run.Food, Is.EqualTo(99));
            }
            yield return null;
        }

        /// <summary>Ordinary enemy variants retain their initial intent and execute only through the one runtime controller.</summary>
        [UnityTest]
        public IEnumerator OrdinaryEnemiesUseBlueprintRulesAndOneTurnController()
        {
            Component manager = CreateOrdinaryBoardManager();
            GameObject player = CreateObject("Ordinary enemy-test player", 0, 0);
            foreach (int day in new[] { 2, 4, 10 })
            {
                var run = new RunState("ordinary-day-" + day, 77,
                    new RunStateConfiguration(100, 100, day - 1, "forest.start"));
                run.StartLeg("edge", run.WorldNodeId, "destination", new[]
                {
                    new RouteSegmentState("ordinary.segment", OrdinaryBoardGenerator.ConfigurationId, 1)
                });
                var request = new BoardRequest(BoardAddressFactory.CreateFromState(run), run.RunSeed,
                    run.WorldNodeId, "forest", "temperate", day);
                BoardRuntime runtime = InvokeSetup(manager, request, run, player);
                BoardBlueprint blueprint = GetProperty<BoardBlueprint>(manager, "ActiveBlueprint");
                string hash = blueprint.ContentHash;
                Assert.That(runtime.Enemies.Count, Is.EqualTo(OrdinaryBoardContent.CreateConfiguration(request.Address).EnemyCount.Minimum));
                foreach (ShamblerState enemy in runtime.Enemies)
                {
                    var entity = blueprint.Entities.Single(candidate => candidate.Id.Equals(enemy.ActorId));
                    Component artwork = runtime.Views[enemy.ActorId].GetComponent(boardManagerType.Assembly.GetType("Enemy", true));
                    Assert.That(artwork.GetType().GetField("playerDamage").GetValue(artwork), Is.EqualTo(enemy.Definition.Damage));
                    Assert.That(enemy.Definition.Damage, Is.EqualTo(entity.Definition.ContentId == OrdinaryBoardContent.Enemy10Id ? 10 : 20));
                    Assert.That(enemy.LockedIntent, Is.Not.Null);
                }
                TurnController controller = runtime.Controller;
                Assert.That(controller.Resolve(new WaitCommand()).Accepted, Is.True);
                Assert.That(run.Food, Is.EqualTo(99));
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(runtime.Enemies.All(enemy => enemy.Phase == ShamblerPhase.Rest), Is.True);
                Assert.That(runtime.Controller, Is.SameAs(controller));
                Assert.That(blueprint.ContentHash, Is.EqualTo(hash));
                yield return null;
            }
        }

        /// <summary>A future generation version is rejected without leaving the previous runtime or its blueprint published.</summary>
        [UnityTest]
        public IEnumerator OrdinaryUnsupportedVersionReleasesThePreviousRuntime()
        {
            Component manager = CreateOrdinaryBoardManager();
            RunState run = CreateRun();
            run.StartLeg("edge", run.WorldNodeId, "destination", new[]
            {
                new RouteSegmentState("ordinary.segment", OrdinaryBoardGenerator.ConfigurationId, 1)
            });
            var request = new BoardRequest(BoardAddressFactory.CreateFromState(run), run.RunSeed,
                run.WorldNodeId, "forest", "temperate", 1);
            GameObject player = CreateObject("Version-test player", 0, 0);
            BoardRuntime previous = InvokeSetup(manager, request, run, player);
            var address = new BoardAddress(request.Address.RunId, request.Address.LegId, request.Address.EdgeId,
                request.Address.SegmentId, request.Address.DayNumber, OrdinaryBoardGenerator.ConfigurationId, 3, request.BoardSeed);
            var unsupported = new BoardRequest(address, run.RunSeed, run.WorldNodeId, "forest", "temperate", 1);
            Assert.Throws<TargetInvocationException>(() => InvokeSetup(manager, unsupported, run, player));
            Assert.That(previous.IsDisposed, Is.True);
            Assert.That(GetProperty<BoardBlueprint>(manager, "ActiveBlueprint"), Is.Null);
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.Null);
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProductionEnemyBatch_InitializesBeforeInputAndUsesSoleController()
        {
            foreach (int damage in new[] { 10, 20 })
            {
                Component manager = CreateBoardManager();
                SetField(manager, "columns", 4);
                SetField(manager, "rows", 4);
                GameObject prefab = CreateObject("Enemy " + damage + " Prefab", 0, 0);
                Component artwork = prefab.AddComponent(boardManagerType.Assembly.GetType("Enemy", true));
                SetField(artwork, "playerDamage", damage);
                SetField(manager, "enemyTiles", new[] { prefab });
                GameObject player = CreateObject("Player " + damage, 0, 0);
                RunState run = CreateRun();
                BoardRequest request = CreateRequest(run, 2);
                BoardRuntime runtime = InvokeSetup(manager, request, run, player);
                TurnController controller = runtime.Controller;
                IReadOnlyList<ShamblerState> oldRoster = runtime.Enemies;
                Assert.That(oldRoster, Has.Count.EqualTo(1));
                ShamblerState enemy = oldRoster[0];
                Assert.That(enemy.Definition.Damage, Is.EqualTo(damage));
                Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
                Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Move));
                Assert.That(enemy.LockedIntent.ActorId, Is.EqualTo(enemy.ActorId));
                Assert.That(runtime.Views[enemy.ActorId].activeInHierarchy, Is.True);
                GridPosition source = runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(enemy.ActorId)).Position;
                GridPosition target = enemy.LockedIntent.TargetPosition.Value;
                EnemyIntent initialPlan = enemy.LockedIntent;
                yield return null;
                Assert.That(enemy.LockedIntent, Is.SameAs(initialPlan));
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(100));

                Assert.That(controller.Resolve(new MoveCommand(Direction.West)).Accepted, Is.False);
                Assert.That(enemy.LockedIntent, Is.SameAs(initialPlan));
                TurnResult turn = controller.Resolve(new WaitCommand());
                Assert.That(turn.Accepted, Is.True);
                Assert.That(turn.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
                Assert.That(runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(enemy.ActorId)).Position,
                    Is.EqualTo(target));
                Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
                Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
                Assert.That(run.Food, Is.EqualTo(99));
                Assert.That(runtime.Controller, Is.SameAs(controller));
                Assert.That(runtime.Views[enemy.ActorId].transform.position,
                    Is.EqualTo(new Vector3(source.X, source.Y, 0f)), "Legacy artwork must not execute its own movement.");

                BoardRuntime replacement = InvokeSetup(manager, request, run, player);
                Assert.That(runtime.IsDisposed, Is.True);
                Assert.That(oldRoster, Is.Empty);
                Assert.That(replacement.Controller, Is.Not.SameAs(controller));
                Assert.That(replacement.Enemies[0], Is.Not.SameAs(enemy));
                Assert.That(replacement.Enemies[0].ActorId, Is.EqualTo(enemy.ActorId));
                Assert.That(replacement.Enemies[0].Phase, Is.EqualTo(ShamblerPhase.Active));
                Assert.That(replacement.Enemies[0].LockedIntent, Is.Not.Null);
                Assert.That(player, Is.Not.Null);
                TestContext.WriteLine("Production enemy damage=" + damage + "; initial plan before input; one controller; fresh replacement.");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator InvalidEnemyVariant_DoesNotPublishPartialRuntime()
        {
            foreach (bool missing in new[] { true, false })
            {
                Component manager = CreateBoardManager();
                SetField(manager, "columns", 4);
                SetField(manager, "rows", 4);
                GameObject prefab = CreateObject("Invalid Enemy Prefab", 0, 0);
                if (!missing)
                    SetField(prefab.AddComponent(boardManagerType.Assembly.GetType("Enemy", true)), "playerDamage", 15);
                SetField(manager, "enemyTiles", new[] { prefab });
                RunState run = CreateRun();
                GameObject player = CreateObject("Player", 0, 0);
                TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(() =>
                    InvokeSetup(manager, CreateRequest(run, 2), run, player));
                var diagnostic = invocation.InnerException as BoardRuntimeCompositionException;
                Assert.That(diagnostic, Is.Not.Null);
                Assert.That(diagnostic.Code, Is.EqualTo(BoardRuntimeDiagnosticCode.InvalidInput));
                Assert.That(diagnostic.Context, Does.StartWith("prefab:Enemy:"));
                Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.Null);
                Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.Null);
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(100));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator FailedSetupPublishesNeitherRequestNorPartialRuntime()
        {
            Component manager = CreateBoardManager();
            RunState run = CreateRun();

            TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(() =>
                InvokeSetup(manager, CreateRequest(run), run, null));
            var diagnostic = invocation.InnerException as BoardRuntimeCompositionException;

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.Code, Is.EqualTo(BoardRuntimeDiagnosticCode.MissingPlayer));
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.Null);
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReloadDisposesOldRuntimeAndDoesNotLeakGeneratedViews()
        {
            Component manager = CreateBoardManager();
            GameObject player = CreateObject("Player", 0, 0);
            RunState run = CreateRun();
            BoardRequest request = CreateRequest(run);

            BoardRuntime first = InvokeSetup(manager, request, run, player);
            IReadOnlyDictionary<EntityId, GameObject> firstRegistry = first.Views;
            List<GameObject> firstGeneratedViews = firstRegistry.Values
                .Where(view => view != player)
                .ToList();
            object firstController = first.Controller;

            BoardRuntime second = InvokeSetup(manager, request, run, player);

            Assert.That(first.IsDisposed, Is.True);
            Assert.That(firstRegistry, Is.Empty);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Controller, Is.Not.SameAs(firstController));
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.SameAs(second));
            Assert.That(
                second.Views.Values.Where(view => view != player),
                Has.None.Matches<GameObject>(view => firstGeneratedViews.Contains(view)));

            yield return null;
            Assert.That(
                firstGeneratedViews,
                Has.All.Matches<GameObject>(view => view == null));
        }

        [Test]
        public void GameManagerExposesTheAtomicRuntimePublicationSeam()
        {
            Assembly gameAssembly = boardManagerType.Assembly;
            Type gameManagerType = gameAssembly.GetType("GameManager", true);
            PropertyInfo property = gameManagerType.GetProperty(
                "ActiveBoardRuntime",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(property, Is.Not.Null);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(BoardRuntime)));
            Assert.That(property.CanWrite, Is.False);
        }

        /// <summary>Production setup prepares hidden intents during the day transition, reveals them before input and preserves one command path.</summary>
        [UnityTest]
        public IEnumerator ProductionIntents_AppearBeforeInputAndRespectBoardLifetime()
        {
            persistenceRoot = Path.Combine(Path.GetTempPath(), "HB-M3912-" + Guid.NewGuid().ToString("N"));
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, persistenceRoot);
            Time.timeScale = 1;
            Component board = CreateBoardManager();
            SetField(board, "columns", 4);
            SetField(board, "rows", 4);
            GameObject enemyPrefab = CreateObject("Production Enemy Prefab", 0, 0);
            SetField(enemyPrefab.AddComponent(boardManagerType.Assembly.GetType("Enemy", true)), "playerDamage", 10);
            SetField(board, "enemyTiles", new[] { enemyPrefab });
            Component manager = board.gameObject.AddComponent(gameManagerType);
            var world = new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            createdAssets.Add(world);
            SetField(manager, "worldDefinitionJson", world);
            SetField(manager, "levelStartDelay", 100f);
            Assert.That((bool)InvokeManager(manager, "TryEnsureWorldMap"), Is.True);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            session.StartNewRun("production-intents", 54321, new RunStateConfiguration(100, 100, 2, "forest.start"));
            GameObject playerObject = CreateObject("Production Player", 0, 0);
            playerObject.tag = "Player";
            Component player = playerObject.AddComponent(boardManagerType.Assembly.GetType("PlayerScript", true));
            SetField(player, "animationsEnabled", false);
            GameObject transition = CreateObject("LevelImage", 0, 0);
            InvokeManager(manager, "InitGame");
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            BoardEventPresenter presenter = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter");
            EnemyIntentPresenter display = GetManagerField<EnemyIntentPresenter>(manager, "activeIntents");
            EnemyIntentLegend legend = GetManagerField<EnemyIntentLegend>(manager, "activeIntentLegend");
            Assert.That(display.Views.Count, Is.EqualTo(runtime.Enemies.Count).And.EqualTo(1));
            ShamblerState enemy = runtime.Enemies.Single();
            EnemyIntent initial = enemy.LockedIntent;
            EnemyIntentView marker = display.Views[enemy.ActorId];
            Assert.That(marker.gameObject.activeSelf, Is.True, "Retained markers must already be prepared during setup.");
            Assert.That(marker.gameObject.activeInHierarchy, Is.False, "Day transition must hide all intent markers.");
            Assert.That(legend.Root.activeInHierarchy, Is.False, "Day transition must hide the legend canvas.");
            Assert.That(legend.Control.gameObject.activeInHierarchy, Is.False, "Day transition must hide the legend control.");
            Assert.That(transition.activeSelf, Is.True);
            Assert.That(display.Projections.Single(), Is.EqualTo(EnemyIntentProjection.FromRetainedIntent(initial,
                runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(enemy.ActorId)).Position)));
            Assert.That(display.Projections.Single().AttackOnPlayerEntry, Is.True);
            Assert.That(presenter.Coordinator.Gate.Blocks.HasFlag(PresentationInputBlock.Setup), Is.True);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            yield return null;
            Assert.That(transition.activeSelf, Is.True);
            Assert.That(display.gameObject.activeInHierarchy, Is.False);
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            ((Behaviour)manager).enabled = false;
            ((Behaviour)manager).enabled = true;
            Assert.That(display.Views.Count, Is.EqualTo(1));
            Assert.That(display.gameObject.activeInHierarchy, Is.False, "Enable during setup must not reveal the UI.");
            Assert.That(legend.Root.activeInHierarchy, Is.False);
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(100));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.Zero);
            marker = display.Views[enemy.ActorId];
            InvokeManager(manager, "HideLevelImage");
            Assert.That(transition.activeSelf, Is.False);
            Assert.That(marker.gameObject.activeInHierarchy, Is.True);
            Assert.That(legend.Root.activeInHierarchy, Is.True);
            Assert.That(legend.Control.gameObject.activeInHierarchy, Is.True);
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            yield return null;
            Assert.That(TrySubmit(manager, new MoveCommand(Direction.West), out Task<CommandSubmission> rejected), Is.True);
            Assert.That(rejected.Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(display.Views[enemy.ActorId], Is.SameAs(marker));
            int count = presenter.Coordinator.ResolutionCount;
            legend.Control.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.True);
            legend.Control.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.False);
            InvokeManager(manager, "SetGameplayInputBlocked", true);
            Time.timeScale = 0;
            yield return null;
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(count));
            Time.timeScale = 1;
            InvokeManager(manager, "SetGameplayInputBlocked", false);
            yield return null;
            Assert.That(TrySubmit(manager, new WaitCommand(), out Task<CommandSubmission> accepted), Is.True);
            while (!accepted.IsCompleted)
                yield return null;
            Assert.That(accepted.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(99));
            Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(count + 1));
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter"), Is.SameAs(presenter));
            Assert.That(display.Projections.Single().Symbol, Is.EqualTo(EnemyIntentSymbol.Wait));
            EnemyIntent rest = enemy.LockedIntent;
            ((Behaviour)manager).enabled = false;
            Assert.That(display.Views, Is.Empty);
            Assert.That(legend.Root.activeInHierarchy, Is.False);
            ((Behaviour)manager).enabled = true;
            Assert.That(enemy.LockedIntent, Is.SameAs(rest));
            Assert.That(display.Views.Count, Is.EqualTo(1));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(99));

            EntityId oldId = enemy.ActorId;
            var oldControl = legend.Control;
            GameObject oldOwner = display.gameObject;
            InvokeManager(manager, "BlockBoardStartup");
            Assert.That(runtime.IsDisposed, Is.True);
            Assert.That(display.IsDisposed, Is.True);
            Assert.That(display.Views, Is.Empty);
            Assert.That(oldOwner.activeSelf, Is.False);
            oldControl.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.False);
            transition.SetActive(true);
            InvokeManager(manager, "InitGame");
            BoardRuntime replacement = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(replacement.Enemies.Single().ActorId, Is.EqualTo(oldId));
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter"), Is.Not.SameAs(presenter));
            EnemyIntentPresenter nextDisplay = GetManagerField<EnemyIntentPresenter>(manager, "activeIntents");
            EnemyIntentLegend nextLegend = GetManagerField<EnemyIntentLegend>(manager, "activeIntentLegend");
            Assert.That(nextDisplay.Views.Count, Is.EqualTo(1));
            Assert.That(nextDisplay, Is.Not.SameAs(display));
            Assert.That(transition.activeSelf, Is.True);
            Assert.That(nextDisplay.gameObject.activeInHierarchy, Is.False, "Replacement setup must hide the new board UI.");
            Assert.That(nextLegend.Root.activeInHierarchy, Is.False);
            InvokeManager(manager, "HideLevelImage");
            Assert.That(nextDisplay.Views.Values.All(view => view.gameObject.activeInHierarchy), Is.True);
            Assert.That(nextLegend.Control.gameObject.activeInHierarchy, Is.True);
            yield return null;
            Assert.That(oldOwner == null, Is.True);
            session.ConsumeFood(session.ActiveRun.Food - 1);
            var terminalControl = nextLegend.Control;
            Assert.That(TrySubmit(manager, new WaitCommand(), out Task<CommandSubmission> terminal), Is.True);
            while (!terminal.IsCompleted)
                yield return null;
            Assert.That(terminal.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(nextDisplay.IsDisposed, Is.True);
            Assert.That(nextDisplay.Views, Is.Empty);
            Assert.That(nextLegend.Root.activeSelf, Is.False);
            terminalControl.onClick.Invoke();
            Assert.That(nextLegend.IsVisible, Is.False);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            Assert.That(Directory.Exists(persistenceRoot), Is.True, "Terminal checkpoint must stay in the isolated root.");

            ((Behaviour)manager).enabled = true;
            session.StartNewRun("production-intent-failure", 54321, new RunStateConfiguration(100, 100, 2, "forest.start"));
            Camera camera = CreateObject("Intent overlap camera", 0, 0).AddComponent<Camera>();
            camera.gameObject.tag = "MainCamera";
            camera.transform.position = new Vector3(1.5f, 1.5f, -10);
            camera.orthographic = false;
            camera.orthographicSize = 1;
            camera.fieldOfView = 1;
            camera.pixelRect = new Rect(0, 0, Screen.width, Screen.height);
            LogAssert.Expect(LogType.Error, new Regex("Board startup failed: Legend must leave the supplied inspected board area visible"));
            InvokeManager(manager, "InitGame");
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Null);
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter"), Is.Null);
            Assert.That(GetProperty<BoardRuntime>(board, "ActiveRuntime"), Is.Null);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            InvokeManager(manager, "HideLevelImage");
            yield return null;
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            TestContext.WriteLine("P1 production roster; markers/legend hidden during day transition including re-enable/replacement; prepared plans revealed before input without AI or cost; one coordinator; terminal/failure cleanup; isolated save root.");
        }

        /// <summary>Day-one startup with the production camera framing leaves the transition and enables the sole command path.</summary>
        [UnityTest]
        public IEnumerator DayOneStartup_WithCenteredCameraReleasesTransition()
        {
            persistenceRoot = Path.Combine(Path.GetTempPath(), "HB-LegendStartup-" + Guid.NewGuid().ToString("N"));
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, persistenceRoot);
            Time.timeScale = 1;
            Component board = CreateOrdinaryBoardManager();
            SetField(board, "columns", 8);
            SetField(board, "rows", 8);
            Component manager = board.gameObject.AddComponent(gameManagerType);
            var world = new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            createdAssets.Add(world);
            SetField(manager, "worldDefinitionJson", world);
            SetField(manager, "levelStartDelay", 0.05f);
            Assert.That((bool)InvokeManager(manager, "TryEnsureWorldMap"), Is.True);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            session.StartNewRun("day-one-legend-regression", 54321, new RunStateConfiguration(100, 100, 0, "forest.start"));
            Assert.That((bool)InvokeManager(manager, "TryEnsureWorldMap"), Is.True);
            WorldMapService map = GetManagerField<WorldMapService>(manager, "worldMap");
            Assert.That(map.BeginRouteChoice().IsSuccess, Is.True);
            Assert.That(map.ChooseRoute("road.start-east-creek").IsSuccess, Is.True);
            Camera camera = CreateObject("Production camera framing", 0, 0).AddComponent<Camera>();
            camera.gameObject.tag = "MainCamera";
            camera.transform.position = new Vector3(3.5f, 3.5f, -10);
            camera.orthographic = true;
            camera.orthographicSize = 5;
            GameObject playerObject = CreateObject("Day-one player", 0, 0);
            playerObject.tag = "Player";
            Component player = playerObject.AddComponent(boardManagerType.Assembly.GetType("PlayerScript", true));
            SetField(player, "animationsEnabled", false);
            GameObject transition = CreateObject("LevelImage", 0, 0);
            GameObject textObject = CreateObject("LevelText", 0, 0);
            textObject.transform.SetParent(transition.transform, false);
            var dayText = textObject.AddComponent<UnityEngine.UI.Text>();

            Rect originalRect = camera.rect;
            float originalSize = camera.orthographicSize;
            TestContext.WriteLine("Day-one test viewport=" + Screen.width + "x" + Screen.height);
            InvokeManager(manager, "InitGame");
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            Assert.That(GetProperty<BoardBlueprint>(board, "ActiveBlueprint"), Is.Not.Null);
            Assert.That(runtime.Enemies, Is.Empty);
            Assert.That(dayText.text, Is.EqualTo("Day: 1"));
            Assert.That(new HallowBlaze.Core.Persistence.Storage.FileSystemSaveStore(persistenceRoot).LoadRun().Data.ActiveLeg.IntroState,
                Is.EqualTo(DayIntroState.Consumed), "Consumed must be durable before Day 1 is rendered.");
            Assert.That(transition.activeSelf, Is.True);
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.False);
            EnemyIntentLegend legend = GetManagerField<EnemyIntentLegend>(manager, "activeIntentLegend");
            Assert.That(legend, Is.Not.Null);
            Assert.That(legend.Root.activeInHierarchy, Is.False, "Day 1 must not show a legend over its loading overlay.");
            Assert.That(legend.Control.gameObject.activeInHierarchy, Is.False);
            float deadline = Time.realtimeSinceStartup + 2;
            while (transition.activeSelf && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(transition.activeSelf, Is.False, "The completed board must release the Day: 1 overlay.");
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputEnabled"), Is.True);
            Assert.That(legend.Root.activeInHierarchy, Is.True);
            Assert.That(legend.Control.gameObject.activeInHierarchy, Is.True);
            var inspected = (Rect)InvokeManager(manager, "InspectedBoardArea");
            Assert.That(legend.PanelBounds.Overlaps(inspected) || legend.ControlBounds.Overlaps(inspected), Is.False);
            legend.Control.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.True);
            legend.Control.onClick.Invoke();
            Assert.That(legend.IsVisible, Is.False);
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(TrySubmit(manager, new WaitCommand(), out Task<CommandSubmission> pending), Is.True);
            while (!pending.IsCompleted)
                yield return null;
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(99));
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").Coordinator.ResolutionCount, Is.EqualTo(1));
            InvokeManager(manager, "BlockBoardStartup");
            Assert.That(camera.rect, Is.EqualTo(originalRect));
            Assert.That(camera.orthographicSize, Is.EqualTo(originalSize));
            TestContext.WriteLine("Day: 1 released; production camera (3.5,3.5,-10), size5; viewport=" + Screen.width + "x" + Screen.height + "; nonoverlapping legend; one command/cost.");
        }

        private static object InvokeManager(Component manager, string name, params object[] arguments)
        {
            return manager.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(manager, arguments);
        }

        [UnityTest]
        public IEnumerator ForeignBoardAddress_CannotReachProductionOutcomeEffects()
        {
            persistenceRoot = Path.Combine(Path.GetTempPath(), "HB-M311-guard-" + Guid.NewGuid().ToString("N"));
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, persistenceRoot);
            Component board = CreateBoardManager();
            Component manager = board.gameObject.AddComponent(gameManagerType);
            var authored = new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            createdAssets.Add(authored);
            SetField(manager, "worldDefinitionJson", authored);
            SetField(manager, "levelStartDelay", 100f);
            Assert.That((bool)InvokeManager(manager, "TryEnsureWorldMap"), Is.True);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            session.StartNewRun("production-address-guard", 54321, new RunStateConfiguration(100, 100, 0, "forest.start"));
            RunState run = session.ActiveRun;
            run.StartLeg("road.start-east-creek", "forest.start", "forest.east-creek",
                new[] { "entry", "stretch", "clearing", "approach", "arrival" }.Select(name =>
                    new RouteSegmentState("segment.start-east-creek." + name, "legacy.board.v1", 1)));
            run.TryConsumeIntro(run.ActiveLeg.LegId);
            GameObject playerObject = CreateObject("Address guard player", 0, 0);
            playerObject.tag = "Player";
            playerObject.AddComponent(boardManagerType.Assembly.GetType("PlayerScript", true));
            CreateObject("LevelImage", 0, 0);
            InvokeManager(manager, "InitGame");
            BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
            Assert.That(runtime, Is.Not.Null);
            BoardRequest expected = runtime.Request;
            BoardAddress address = expected.Address;
            Assert.That(address, Is.Not.Null);
            var beforeFiles = Directory.Exists(persistenceRoot)
                ? Directory.GetFiles(persistenceRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => Convert.ToBase64String(File.ReadAllBytes(path)))
                : new Dictionary<string, string>();
            ProfileState profile = session.Profile;
            NodeDiscovery[] nodes = profile.NodeDiscoveries.ToArray();
            EdgeDiscovery[] edges = profile.EdgeDiscoveries.ToArray();
            for (int field = 0; field < 8; field++)
            {
                BoardAddress foreign = new BoardAddress(field == 0 ? "foreign" : address.RunId,
                    field == 1 ? "foreign" : address.LegId, field == 2 ? "foreign" : address.EdgeId,
                    field == 3 ? "foreign" : address.SegmentId, field == 4 ? address.DayNumber + 1 : address.DayNumber,
                    field == 5 ? "foreign" : address.GenerationConfigurationId,
                    field == 6 ? address.GenerationVersion + 1 : address.GenerationVersion,
                    field == 7 ? address.BoardSeed + 1 : address.BoardSeed);
                BoardRequest wrong = new BoardRequest(foreign, run.RunSeed, run.WorldNodeId, expected.PlaceKind,
                    expected.BiomeFamily, expected.LegacyDifficultyLevel);
                foreach (BoardOutcome outcome in new[] { BoardOutcome.ExitReached(wrong), BoardOutcome.PlayerDied(wrong, DeathReason.Starvation) })
                    Assert.That((bool)InvokeManager(manager, "HandleBoardOutcome", outcome), Is.False, foreign.ToString());
            }
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(run.Food, Is.EqualTo(100));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.CompletedDayCount, Is.Zero);
            Assert.That(run.ActiveLeg.SegmentIndex, Is.Zero);
            Assert.That(profile.NodeDiscoveries, Is.EqualTo(nodes));
            Assert.That(profile.EdgeDiscoveries, Is.EqualTo(edges));
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.SameAs(runtime));
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").Coordinator.ResolutionCount, Is.Zero);
            var afterFiles = Directory.Exists(persistenceRoot)
                ? Directory.GetFiles(persistenceRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => Convert.ToBase64String(File.ReadAllBytes(path)))
                : new Dictionary<string, string>();
            Assert.That(afterFiles, Is.EquivalentTo(beforeFiles));
            SetField(manager, "sceneLoader", new Action<string>(_ => { }));
            Assert.That((bool)InvokeManager(manager, "HandleBoardOutcome", BoardOutcome.ExitReached(expected)), Is.True);
            Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(1));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            yield return null;
        }

        private static T GetManagerField<T>(Component manager, string name)
        {
            return (T)manager.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(manager);
        }

        [UnityTest]
        public IEnumerator RouteTravel_TwoSegmentsHaveOneDayAndFinalDiscovery() => ExerciseProductionTravel(2);

        [UnityTest]
        public IEnumerator RouteTravel_FiveSegmentsHaveOneDayAndFinalDiscovery() => ExerciseProductionTravel(5);

        [UnityTest]
        public IEnumerator RouteTravel_StartAndIntroFailuresKeepInputBlockedUntilDurableRetry()
        {
            Component manager = CreateTravelManager(2, out GameSession session, out TravelFaultStore store,
                out UnityEngine.UI.Text dayText, out _);
            InvokeManager(manager, "BeginRouteChoice");
            store.FailRun = true;
            LogAssert.Expect(LogType.Error, "Route choice save failed for Run: IoError");
            Assert.That((bool)InvokeManager(manager, "ChooseRoute", "road.start-east-creek"), Is.False);
            Assert.That(session.ActiveRun.ActiveLeg, Is.Null);
            Assert.That(session.ActiveRun.TraversalSequence, Is.Zero);
            Assert.That(store.LoadRun().Data.ActiveLeg, Is.Null);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            store.FailRun = false;
            Assert.That((bool)InvokeManager(manager, "RetryTravelCheckpoint"), Is.True);
            Assert.That(session.ActiveRun.ActiveLeg.LegId, Is.EqualTo("leg.1"));
            store.FailRun = true;
            LogAssert.Expect(LogType.Error, "Day intro checkpoint failed: IoError");
            InvokeManager(manager, "InitGame");
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime"), Is.Null);
            Assert.That(session.ActiveRun.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Pending));
            Assert.That(store.LoadRun().Data.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Pending));
            Assert.That(GetManagerField<GameObject>(manager, "levelImage").activeSelf, Is.False);
            InvokeManager(manager, "HideLevelImage");
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            store.FailRun = false;
            Assert.That((bool)InvokeManager(manager, "RetryTravelCheckpoint"), Is.True);
            Assert.That(store.LoadRun().Data.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Consumed));
            Assert.That(dayText.text, Is.EqualTo("Day: 1"));
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            InvokeManager(manager, "HideLevelImage");
            yield return null;
            Assert.That(TrySubmit(manager, new WaitCommand(), out Task<CommandSubmission> command), Is.True);
            while (!command.IsCompleted)
                yield return null;
            Assert.That(command.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(99));
        }

        [UnityTest]
        public IEnumerator RouteTravel_ContinuePendingAndConsumedKeepsAddressAndSkipsRepeatedIntro()
        {
            Component manager = CreateTravelManager(5, out GameSession session, out TravelFaultStore store,
                out _, out _);
            InvokeManager(manager, "BeginRouteChoice");
            InvokeManager(manager, "ChooseRoute", "road.start-east-creek");
            WorldDefinition world = GetManagerField<WorldDefinition>(manager, "worldDefinition");
            BoardAddress address = BoardAddressFactory.Create(session.ActiveRun, world);
            RunLifecycleService lifecycle = GetManagerField<RunLifecycleService>(manager, "lifecycle");
            lifecycle.ExitToMenu();
            Assert.That(lifecycle.ContinueRun().IsSuccess, Is.True);
            InvokeManager(manager, "InitGame");
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveBoardRequest").Address, Is.EqualTo(address));
            Assert.That(GetManagerField<GameObject>(manager, "levelImage").activeSelf, Is.True);
            Assert.That(store.LoadRun().Data.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Consumed));
            lifecycle.ExitToMenu();
            Assert.That(lifecycle.ContinueRun().IsSuccess, Is.True);
            InvokeManager(manager, "InitGame");
            Assert.That(GetManagerField<GameObject>(manager, "levelImage").activeSelf, Is.False,
                "Crash after Consumed and before rendering must skip rather than repeat the intro.");
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveBoardRequest").Address, Is.EqualTo(address));
            yield return null;
            Assert.That(TrySubmit(manager, new WaitCommand(), out Task<CommandSubmission> command), Is.True);
            while (!command.IsCompleted)
                yield return null;
            Assert.That(session.ActiveRun.Food, Is.EqualTo(99));
            lifecycle.ExitToMenu();
            Assert.That(lifecycle.ContinueRun().IsSuccess, Is.True);
            InvokeManager(manager, "InitGame");
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100), "Unsaved mid-board action restarts the same boundary.");
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveBoardRequest").Address, Is.EqualTo(address));
            Assert.That(GetManagerField<GameObject>(manager, "levelImage").activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator RouteTravel_SegmentAndArrivalFailuresRetryWithoutAnotherTurnOrArrivalSave()
        {
            Component manager = CreateTravelManager(2, out GameSession session, out TravelFaultStore store,
                out _, out _);
            InvokeManager(manager, "BeginRouteChoice");
            InvokeManager(manager, "ChooseRoute", "road.start-east-creek");
            InvokeManager(manager, "InitGame");
            InvokeManager(manager, "HideLevelImage");
            yield return null;
            BoardRequest first = GetProperty<BoardRequest>(manager, "ActiveBoardRequest");
            store.FailRun = true;
            LogAssert.Expect(LogType.Error, "Segment checkpoint failed: IoError");
            yield return MoveToTravelExit(manager);
            Assert.That(session.ActiveRun.ActiveLeg.SegmentIndex, Is.Zero);
            Assert.That(store.LoadRun().Data.ActiveLeg.SegmentIndex, Is.Zero);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            int resolutions = GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").Coordinator.ResolutionCount;
            store.FailRun = false;
            Assert.That((bool)InvokeManager(manager, "RetryTravelCheckpoint"), Is.True);
            Assert.That(GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").Coordinator.ResolutionCount, Is.EqualTo(resolutions));
            Assert.That(session.ActiveRun.ActiveLeg.SegmentIndex, Is.EqualTo(1));
            Assert.That((bool)InvokeManager(manager, "HandleBoardOutcome", BoardOutcome.ExitReached(first)), Is.False);
            InvokeManager(manager, "BlockBoardStartup");
            InvokeManager(manager, "InitGame");
            yield return null;
            store.FailProfile = true;
            LogAssert.Expect(LogType.Error, "Arrival discovery checkpoint failed: Injected discovery checkpoint failure.");
            yield return MoveToTravelExit(manager);
            Assert.That(session.ActiveRun.ActiveLeg, Is.Null);
            Assert.That(store.LoadRun().Data.TravelHistory, Has.Count.EqualTo(1));
            Assert.That(store.LoadProfile().Data.GetEdgeDiscoveryState("road.start-east-creek"), Is.EqualTo(EdgeDiscoveryState.Sighted));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            int runSaves = store.RunSaves;
            store.FailProfile = false;
            Assert.That((bool)InvokeManager(manager, "RetryTravelCheckpoint"), Is.True);
            Assert.That(store.RunSaves, Is.EqualTo(runSaves), "Discovery retry must not checkpoint final arrival twice.");
            Assert.That(session.ActiveRun.TravelHistory, Has.Count.EqualTo(1));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.True);
            Assert.That(store.LoadProfile().Data.GetNodeDiscoveryState("forest.east-creek"), Is.EqualTo(NodeDiscoveryState.Visited));
        }

        [UnityTest]
        public IEnumerator RouteTravel_DeathOnIntermediateBoardCountsOnlyCompletedDays()
        {
            Component manager = CreateTravelManager(5, out GameSession session, out TravelFaultStore store,
                out UnityEngine.UI.Text dayText, out _);
            InvokeManager(manager, "BeginRouteChoice");
            InvokeManager(manager, "ChooseRoute", "road.start-east-creek");
            InvokeManager(manager, "InitGame");
            InvokeManager(manager, "HideLevelImage");
            yield return null;
            yield return MoveToTravelExit(manager);
            InvokeManager(manager, "BlockBoardStartup");
            InvokeManager(manager, "InitGame");
            yield return null;
            RunState run = session.ActiveRun;
            session.ConsumeFood(run.Food - 1);
            Assert.That(TrySubmit(manager, new WaitCommand(), out Task<CommandSubmission> death), Is.True);
            while (!death.IsCompleted)
                yield return null;
            Assert.That(run.ActiveLeg.SegmentIndex, Is.EqualTo(1));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(store.LoadRun().Type, Is.EqualTo(SaveStoreResultType.Missing));
            Assert.That(store.LoadProfile().Data.RunSummaries.Single().DaysSurvived, Is.Zero);
            Assert.That(dayText.text, Does.Contain("Day 1").And.Contain("Completed days: 0"));
            Assert.That(session.Profile.GetEdgeDiscoveryState("road.start-east-creek"), Is.EqualTo(EdgeDiscoveryState.Sighted));
            Assert.That(session.Profile.GetNodeDiscoveryState("forest.east-creek"), Is.EqualTo(NodeDiscoveryState.Sighted));
            Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.False);
        }

        private static IEnumerator MoveToTravelExit(Component manager)
        {
            GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").AnimationsEnabled = false;
            foreach (Direction direction in new[] { Direction.North, Direction.East })
            {
                Assert.That(TrySubmit(manager, new MoveCommand(direction), out Task<CommandSubmission> command), Is.True);
                while (!command.IsCompleted)
                    yield return null;
                Assert.That(command.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                yield return null;
            }
        }

        private IEnumerator ExerciseProductionTravel(int count)
        {
            Component manager = CreateTravelManager(count, out GameSession session, out TravelFaultStore store,
                out UnityEngine.UI.Text dayText, out Component player);
            Assert.That((bool)InvokeManager(manager, "BeginRouteChoice"), Is.True);
            Assert.That((bool)InvokeManager(manager, "ChooseRoute", "road.start-east-creek"), Is.True);
            Assert.That(store.LoadRun().Data.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Pending));
            Assert.That(session.ActiveRun.ReachedCheckpointNodeId, Is.EqualTo("forest.start"));
            Assert.That(session.Profile.GetEdgeDiscoveryState("road.start-east-creek"), Is.EqualTo(EdgeDiscoveryState.Sighted));
            InvokeManager(manager, "InitGame");
            Assert.That(dayText.text, Is.EqualTo("Day: 1"));
            Assert.That(store.LoadRun().Data.ActiveLeg.IntroState, Is.EqualTo(DayIntroState.Consumed));
            Assert.That(GetManagerField<GameObject>(manager, "levelImage").activeSelf, Is.True);
            Assert.That(GetManagerField<UnityEngine.UI.Text>(player, "foodText").gameObject.activeSelf, Is.False);
            Assert.That(GetManagerField<UnityEngine.UI.Text>(player, "healthText").gameObject.activeSelf, Is.False);
            Assert.That(TrySubmit(manager, new WaitCommand(), out _), Is.False);
            InvokeManager(manager, "HideLevelImage");
            yield return null;

            for (int index = 0; index < count; index++)
            {
                BoardRuntime runtime = GetProperty<BoardRuntime>(manager, "ActiveBoardRuntime");
                BoardRequest request = runtime.Request;
                GetProperty<BoardEventPresenter>(manager, "ActiveBoardPresenter").AnimationsEnabled = false;
                Assert.That(request.Address.DayNumber, Is.EqualTo(1));
                Assert.That(session.ActiveRun.ActiveLeg.SegmentIndex, Is.EqualTo(index));
                Assert.That(runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(runtime.PlayerId)).Position,
                    Is.EqualTo(new GridPosition(0, 0)), "Every stable segment boundary starts at its own spawn.");
                int before = store.RunSaves;
                foreach (Direction direction in new[] { Direction.North, Direction.East })
                {
                    Assert.That(TrySubmit(manager, new MoveCommand(direction), out Task<CommandSubmission> submission), Is.True);
                    while (!submission.IsCompleted)
                        yield return null;
                    Assert.That(submission.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented),
                        "Segment " + index + ", direction " + direction);
                    yield return null;
                }
                Assert.That(runtime.Controller.IsTerminal, Is.True);
                Assert.That(runtime.RunState.Food, Is.EqualTo(100 - (index + 1) * 2));
                Assert.That(store.RunSaves, Is.EqualTo(before + 1));
                Assert.That((bool)InvokeManager(manager, "HandleBoardOutcome", BoardOutcome.ExitReached(request)), Is.False);
                Assert.That(store.RunSaves, Is.EqualTo(before + 1), "Duplicate outcome must not save again.");
                bool final = index == count - 1;
                Assert.That(GetProperty<bool>(manager, "IsRouteChoiceActive"), Is.EqualTo(final));
                Assert.That(session.ActiveRun.CompletedDayCount, Is.EqualTo(final ? 1 : 0));
                Assert.That(session.ActiveRun.ReachedCheckpointNodeId, Is.EqualTo(final ? "forest.east-creek" : "forest.start"));
                Assert.That(session.Profile.GetNodeDiscoveryState("forest.east-creek"),
                    Is.EqualTo(final ? NodeDiscoveryState.Visited : NodeDiscoveryState.Sighted));
                Assert.That(session.Profile.GetEdgeDiscoveryState("road.start-east-creek"),
                    Is.EqualTo(final ? EdgeDiscoveryState.Traversed : EdgeDiscoveryState.Sighted));
                if (!final)
                {
                    InvokeManager(manager, "BlockBoardStartup");
                    InvokeManager(manager, "InitGame");
                    Assert.That(GetManagerField<GameObject>(manager, "levelImage").activeSelf, Is.False);
                    Assert.That(GetManagerField<UnityEngine.UI.Text>(player, "foodText").gameObject.activeSelf, Is.True);
                    yield return null;
                }
            }
            Assert.That(session.ActiveRun.TravelHistory.Single().CompletedSegmentCount, Is.EqualTo(count));
            Assert.That(store.LoadRun().Data.CompletedDayCount, Is.EqualTo(1));
            Assert.That((bool)InvokeManager(manager, "ChooseRoute", "road.east-creek-old-road"), Is.True);
            Assert.That(session.ActiveRun.CurrentDay, Is.EqualTo(2));
            Assert.That(session.ActiveRun.CompletedDayCount, Is.EqualTo(1));
            Assert.That(session.ActiveRun.ActiveLeg.LegId, Is.EqualTo("leg.2"));
        }

        private Component CreateTravelManager(int count, out GameSession session, out TravelFaultStore store,
            out UnityEngine.UI.Text dayText, out Component player)
        {
            persistenceRoot = Path.Combine(Path.GetTempPath(), "HB-M312-production-" + Guid.NewGuid().ToString("N"));
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, persistenceRoot);
            Component board = CreateBoardManager();
            Component manager = board.gameObject.AddComponent(gameManagerType);
            TextAsset world = new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            createdAssets.Add(world);
            SetField(manager, "worldDefinitionJson", world);
            SetField(manager, "levelStartDelay", 100f);
            SetField(manager, "sceneLoader", new Action<string>(_ => { }));
            Assert.That((bool)InvokeManager(manager, "TryEnsureWorldMap"), Is.True);
            session = GetProperty<GameSession>(manager, "Session");
            store = new TravelFaultStore(new FileSystemSaveStore(persistenceRoot));
            var lifecycle = new RunLifecycleService(session, store);
            SetField(manager, "saveStore", store);
            SetField(manager, "lifecycle", lifecycle);
            Assert.That(lifecycle.StartNewRun("production-travel", 54321,
                new RunStateConfiguration(100, 100, 0, "forest.start")).IsSuccess, Is.True);
            WorldDefinition original = WorldDefinitionJson.Deserialize(world.text);
            WorldDefinition fixture = new WorldDefinition(original.WorldDefinitionId, original.Version,
                original.StartNodeId, original.GoalNodeId, original.Nodes, original.Edges.Select(edge =>
                    new WorldEdgeDefinition(edge.EdgeId, edge.FromNodeId, edge.ToNodeId, edge.WorldDirection, edge.ClueKey,
                        (edge.EdgeId == "road.start-east-creek" ? edge.Segments.Take(count) : edge.Segments)
                            .Select(segment => new RouteSegmentDefinition(segment.SegmentId, "legacy.board.v1", 1)))));
            SetField(manager, "worldDefinition", fixture);
            SetField(manager, "worldMap", new WorldMapService(fixture, session, store));
            Assert.That((bool)InvokeManager(manager, "TryEnterCurrentWorldNode"), Is.True);
            GameObject playerObject = CreateObject("Travel Player", 0, 0);
            playerObject.tag = "Player";
            player = playerObject.AddComponent(boardManagerType.Assembly.GetType("PlayerScript", true));
            SetField(player, "animationsEnabled", false);
            SetField(player, "foodText", CreateObject("Travel food HUD", 0, 0).AddComponent<UnityEngine.UI.Text>());
            SetField(player, "healthText", CreateObject("Travel health HUD", 0, 0).AddComponent<UnityEngine.UI.Text>());
            GameObject transition = CreateObject("LevelImage", 0, 0);
            GameObject text = CreateObject("LevelText", 0, 0);
            text.transform.SetParent(transition.transform, false);
            dayText = text.AddComponent<UnityEngine.UI.Text>();
            return manager;
        }

        private sealed class TravelFaultStore : ISaveStore
        {
            private readonly ISaveStore inner;
            public TravelFaultStore(ISaveStore inner) { this.inner = inner; }
            public bool FailRun { get; set; }
            public bool FailProfile { get; set; }
            public int RunSaves { get; private set; }
            public SaveStoreResult SaveRun(RunState run)
            {
                RunSaves++;
                return FailRun ? SaveStoreResult.IoError("Injected travel checkpoint failure.") : inner.SaveRun(run);
            }
            public SaveStoreResult SaveProfile(ProfileState profile) => FailProfile
                ? SaveStoreResult.IoError("Injected discovery checkpoint failure.") : inner.SaveProfile(profile);
            public SaveStoreResult<RunState> LoadRun() => inner.LoadRun();
            public SaveStoreResult<ProfileState> LoadProfile() => inner.LoadProfile();
            public SaveStoreResult ResetGame(ProfileState profile, RunState run) => inner.ResetGame(profile, run);
            public SaveStoreResult DeleteRun() => inner.DeleteRun();
        }

        private static bool TrySubmit(Component manager, PlayerCommand command, out Task<CommandSubmission> submission)
        {
            object[] arguments = { command, null };
            bool admitted = (bool)InvokeManager(manager, "TrySubmitCommand", arguments);
            submission = (Task<CommandSubmission>)arguments[1];
            return admitted;
        }

        private Component CreateBoardManager()
        {
            GameObject owner = CreateObject("Runtime Board Manager", 0, 0);
            Component manager = owner.AddComponent(boardManagerType);
            SetField(manager, "columns", 2);
            SetField(manager, "rows", 2);
            SetCount(manager, "wallCount", 0, 0);
            SetCount(manager, "foodCount", 0, 0);
            SetCount(manager, "aidCount", 0, 0);
            SetCount(manager, "buriedCount", 0, 0);

            GameObject floor = CreateObject("Floor Prefab", 0, 0);
            GameObject outer = CreateObject("Outer Prefab", 0, 0);
            GameObject unused = CreateObject("Unused Prefab", 0, 0);
            GameObject exit = CreateObject("Exit Prefab", 0, 0);
            SetField(manager, "floorTiles", new[] { floor });
            SetField(manager, "outerWallTiles", new[] { outer });
            SetField(manager, "wallTiles", new[] { unused });
            SetField(manager, "groundFoodTiles", new[] { unused });
            SetField(manager, "bushFoodTiles", unused);
            SetField(manager, "buriedFoodTiles", new[] { unused });
            SetField(manager, "aidTiles", new[] { unused });
            SetField(manager, "enemyTiles", new[] { unused });
            SetField(manager, "exit", exit);
            return manager;
        }

        private string[] GeneratedLayout(Component manager)
        {
            Transform holder = (Transform)boardManagerType.GetField("boardHolder",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            return holder.Cast<Transform>().Select(view => view.name + ":" + (int)view.position.x + ":" + (int)view.position.y)
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private static string[] GameplayLayout(BoardRuntime runtime)
        {
            return runtime.BoardState.GetEntities().Select(entity => entity.Id + ":" + entity.Definition.Layer + ":" + entity.Position)
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private BoardRuntime InvokeSetup(
            Component manager,
            BoardRequest request,
            RunState run,
            GameObject player)
        {
            MethodInfo method = boardManagerType.GetMethod(
                "SetupScene",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(BoardRequest), typeof(RunState), typeof(GameObject) },
                null);
            Assert.That(method, Is.Not.Null);
            return (BoardRuntime)method.Invoke(manager, new object[] { request, run, player });
        }

        private void SetCount(Component manager, string fieldName, int minimum, int maximum)
        {
            Type countType = boardManagerType.GetNestedType("Count", BindingFlags.Public);
            object count = Activator.CreateInstance(countType, minimum, maximum);
            SetField(manager, fieldName, count);
        }

        private static void SetField(Component target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static T GetProperty<T>(Component target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, propertyName);
            return (T)property.GetValue(target, null);
        }

        private GameObject CreateObject(string name, int x, int y)
        {
            var instance = new GameObject(name);
            instance.transform.position = new Vector3(x, y, 0f);
            createdObjects.Add(instance);
            return instance;
        }

        private static RunState CreateRun()
        {
            return new RunState(
                "runtime-playmode-run",
                54321,
                new RunStateConfiguration(100, 100, 0, "forest.start"));
        }

        private static BoardRequest CreateRequest(RunState run, int difficulty = 1)
        {
            return new BoardRequest(
                run.RunId,
                run.RunSeed,
                run.WorldNodeId,
                run.CurrentDay,
                run.GetBoardSeed(),
                "forest",
                "temperate",
                difficulty);
        }
    }
}
