#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;
using Object = UnityEngine.Object;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Exercises explicit composition, ordered gate replay, cancellation and real prefab pixels through the sole command path.</summary>
    public sealed class PlateGatePresentationTests
    {
        private readonly List<Fixture> fixtures = new List<Fixture>();

        /// <summary>Releases only fixture-owned views/runtimes; no scene or real save directory is changed.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (Fixture fixture in fixtures) fixture.Dispose();
            fixtures.Clear();
        }

        /// <summary>Animation changes replay time only; entry/exit change pressure once, and rapid input cannot add a cost or turn.</summary>
        [UnityTest]
        public IEnumerator PlayerEntryAndExitKeepTheSameModelAndViewInBothAnimationModes()
        {
            foreach (bool animations in new[] { false, true })
            {
                Fixture fixture = Create(animations);
                Assert.That(fixture.Gate.DisplayedActive, Is.False);
                var entering = fixture.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East));
                Assert.That(fixture.Runtime.BoardState.GetGates().Single().IsOpen, Is.True);
                if (!entering.IsCompleted)
                {
                    Assert.That(fixture.Gate.DisplayedActive, Is.False, "The gate event must follow movement replay, not jump to final state early.");
                    Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                }
                while (!entering.IsCompleted) yield return null;
                Assert.That(entering.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(fixture.Plate.DisplayedActive, Is.True);
                Assert.That(fixture.Gate.DisplayedActive, Is.True);
                Assert.That(fixture.Gate.LinkLabel, Does.Contain("A").And.Contain("ALL OPEN"));
                var leaving = fixture.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.West));
                while (!leaving.IsCompleted) yield return null;
                Assert.That(leaving.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(fixture.Runtime.BoardState.GetGates().Single().IsOpen, Is.False);
                Assert.That(fixture.Gate.DisplayedActive || fixture.Plate.DisplayedActive, Is.False);
                Assert.That(fixture.Run.Food, Is.EqualTo(18));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(2));
                Assert.That(fixture.GateFeedback, Is.EqualTo(new[] { true, false }));
                fixture.Dispose();
                yield return null;
            }
        }

        /// <summary>Canceling player animation recovers the committed plate/gate state without charging or resolving again.</summary>
        [UnityTest]
        public IEnumerator CancelDuringMovementRecoversBothDerivedViews()
        {
            Fixture fixture = Create(true, true, 0.5f);
            using (var cancellation = new CancellationTokenSource())
            {
                var operation = fixture.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East), cancellation.Token);
                cancellation.Cancel();
                while (!operation.IsCompleted) yield return null;
                Assert.That(operation.Result.Status, Is.EqualTo(CommandSubmissionStatus.PresentationFailed));
                Assert.That(operation.Result.Result.Accepted, Is.True);
                Assert.That(operation.Result.Diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.Canceled));
                Assert.That(fixture.Gate.DisplayedActive && fixture.Plate.DisplayedActive, Is.True);
                Assert.That(fixture.GateFeedback, Is.Empty);
                Assert.That(fixture.Runtime.BoardState.GetGates().Single().IsOpen, Is.True);
                Assert.That(fixture.Run.Food, Is.EqualTo(19));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
                Assert.That(fixture.Presenter.Coordinator.CanSubmit, Is.True);
                Assert.That(fixture.Diagnostics.Count, Is.EqualTo(1));
            }
        }

        /// <summary>Intermediate open/close events replay their own snapshots instead of rereading a later closed model.</summary>
        [Test]
        public void HistoricalGateEventsAreNotReplacedWithFinalState()
        {
            Fixture fixture = Create(false);
            var events = new List<GameEvent>();
            fixture.Runtime.BoardState.TryMove(fixture.Runtime.PlayerId, new GridPosition(2, 1));
            Record(fixture.Runtime.BoardState, events);
            fixture.Runtime.BoardState.TryMove(fixture.Runtime.PlayerId, new GridPosition(1, 1));
            Record(fixture.Runtime.BoardState, events);
            Assert.That(fixture.Runtime.BoardState.GetGates().Single().IsOpen, Is.False);
            Assert.That(fixture.Presenter.Dispatcher.ReplayAsync(events).Result, Is.Null);
            Assert.That(fixture.GateFeedback, Is.EqualTo(new[] { true, false }));
            Assert.That(fixture.Gate.DisplayedActive || fixture.Plate.DisplayedActive, Is.False);
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
        }

        /// <summary>Missing and orphan legacy links fail before runtime publication without consuming resources.</summary>
        [TestCase(null)]
        [TestCase("orphan")]
        public void InvalidLegacyLinksRejectBeforePublication(string link)
        {
            Fixture fixture = Create(false);
            var layout = fixture.Layout.Select(descriptor => descriptor.Kind == LegacyBoardContentKind.Gate ?
                new LegacyBoardView(descriptor.View, descriptor.Kind, descriptor.Position, linkId: link) : descriptor).ToArray();
            Assert.Throws<BoardRuntimeCompositionException>(() => LegacyBoardRuntimeComposer.Compose(fixture.Request, fixture.Run,
                new GridBounds(0, 0, 4, 2), layout));
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
        }

        /// <summary>Blueprint composition validates explicit links before planning and derives initial open state from an actor already on its plate.</summary>
        [Test]
        public void BlueprintCompositionRequiresBindingsAndDerivesInitialPressure()
        {
            var fixture = new Fixture { Run = new RunState("plate-blueprint", 63, new RunStateConfiguration(100, 20, 0, "forest.start")) };
            fixtures.Add(fixture);
            fixture.Run.StartLeg("edge", fixture.Run.WorldNodeId, "destination", new[] { new RouteSegmentState("segment", "plate.fixture", 1) });
            var request = new BoardRequest(BoardAddressFactory.CreateFromState(fixture.Run), fixture.Run.RunSeed,
                fixture.Run.WorldNodeId, "forest", "temperate", 1);
            var floor = new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false));
            var boundary = new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "boundary");
            var exit = new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "exit", new BoardEntityTraits(true, true, false));
            var player = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player");
            var definitions = new[] { floor, boundary, exit, player, PlateGateValidator.PlateContent, PlateGateValidator.GateContent };
            var entities = new List<BoardEntityState>();
            var views = new Dictionary<EntityId, GameObject>();
            EntityId plateId = default;
            EntityId gateId = default;
            for (int horizontal = 0; horizontal < 8; horizontal++)
            for (int vertical = 0; vertical < 8; vertical++)
            {
                BoardEntityDefinition definition = horizontal == 0 || horizontal == 7 || vertical == 0 || vertical == 7 ? boundary :
                    horizontal == 6 && vertical == 6 ? exit : horizontal == 2 && vertical == 1 ? PlateGateValidator.PlateContent :
                    horizontal == 4 && vertical == 1 ? PlateGateValidator.GateContent : floor;
                var id = new EntityId(100 + horizontal * 8 + vertical);
                entities.Add(new BoardEntityState(id, definition, new GridPosition(horizontal, vertical)));
                if (definition == PlateGateValidator.PlateContent) plateId = id;
                if (definition == PlateGateValidator.GateContent) gateId = id;
                views.Add(id, View(fixture, definition == PlateGateValidator.PlateContent ? LegacyBoardContentKind.PressurePlate :
                    definition == PlateGateValidator.GateContent ? LegacyBoardContentKind.Gate : LegacyBoardContentKind.Floor, horizontal, vertical).View);
            }
            entities.Add(new BoardEntityState(new EntityId(1), player, new GridPosition(2, 1)));
            views.Add(new EntityId(1), View(fixture, LegacyBoardContentKind.Player, 2, 1).View);
            var config = new BoardGenerationConfig(request.Address.GenerationConfigurationId, request.Address.GenerationVersion,
                new GenerationRange(1, 63), new GenerationRange(0, 64), new GenerationRange(0, 0), new GenerationRange(0, 0), 0, 0,
                definitions.Select(definition => definition.ContentId), Array.Empty<ResourceGenerationBudget>());
            var blueprint = new BoardBlueprint(request.Address, config, entities, Array.Empty<BoardItemSpawn>());
            var catalog = new BoardValidationCatalog(definitions, new Dictionary<string, string>(), new Dictionary<string, string>());
            Assert.Throws<BoardRuntimeCompositionException>(() => BlueprintBoardRuntimeComposer.Compose(request, fixture.Run, blueprint, catalog, views));
            fixture.Runtime = BlueprintBoardRuntimeComposer.Compose(request, fixture.Run, blueprint, catalog, views,
                plates: new[] { new PressurePlateDefinition(plateId, "A") }, gates: new[] { new GateDefinition(gateId, "A", GateRule.Any) });
            Assert.That(fixture.Runtime.BoardState.GetGates().Single().IsOpen, Is.True);
            Assert.That(fixture.Runtime.BoardState.TakePlateGateChanges(), Is.Empty);
            Assert.That(blueprint.Entities.Single(entity => entity.Id.Equals(gateId)).Definition.Traits.IsWalkable, Is.False);
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
        }

        /// <summary>Actual linked prefab art and labels remain nonblank and within their cells on desktop and portrait renders.</summary>
        [UnityTest]
        public IEnumerator LinkedViewsRenderBothStatesInDesktopAndPortrait()
        {
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(320, 640) })
            {
                Fixture fixture = Create(false);
                Color32[] closed = Capture(fixture, size, "closed");
                Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East)).Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Color32[] open = Capture(fixture, size, "open");
                Assert.That(closed.Zip(open, (first, second) => !first.Equals(second)).Count(changed => changed), Is.GreaterThan(100));
                fixture.Dispose();
                yield return null;
            }
        }

        private static Color32[] Capture(Fixture fixture, Vector2Int size, string state)
        {
            var owner = new GameObject("Plate gate camera");
            var target = new RenderTexture(size.x, size.y, 24);
            var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Camera camera = owner.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(2, 3f * size.y / size.x);
                camera.transform.position = new Vector3(2, 1, -10);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(12, 28, 20, 255);
                camera.targetTexture = target;
                foreach (PlateGateView view in new[] { fixture.Plate, fixture.Gate })
                {
                    TextMesh label = view.GetComponentInChildren<TextMesh>();
                    Bounds labelBounds = label.GetComponent<MeshRenderer>().bounds;
                    Assert.That(labelBounds.size.x, Is.LessThanOrEqualTo(0.93f));
                    Assert.That(labelBounds.size.y, Is.LessThan(0.35f));
                    foreach (Vector3 corner in new[] { labelBounds.min, labelBounds.max })
                    {
                        Vector3 viewport = camera.WorldToViewportPoint(corner);
                        Assert.That(viewport.x, Is.InRange(0.01f, 0.99f));
                        Assert.That(viewport.y, Is.InRange(0.01f, 0.99f));
                    }
                }
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                texture.Apply();
                Color32[] pixels = texture.GetPixels32();
                Assert.That(pixels.Count(color => color.r > 120 && color.g > 100), Is.GreaterThan(100));
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M63-" + state + "-" + size.x + "x" + size.y + ".png"), texture.EncodeToPNG());
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(texture);
            }
        }

        private static void Record(BoardState board, ICollection<GameEvent> events)
        {
            foreach (PlateGateChange change in board.TakePlateGateChanges())
                if (change.Plate != null) events.Add(new PressurePlateChangedEvent(change.Plate)); else events.Add(new GateChangedEvent(change.Gate));
        }

        private Fixture Create(bool animations, bool allowDiagnostic = false, float duration = 0.08f)
        {
            var fixture = new Fixture { Run = new RunState("plate-gate", 63, new RunStateConfiguration(100, 20, 0, "forest.start")) };
            fixtures.Add(fixture);
            fixture.Request = new BoardRequest(fixture.Run.RunId, fixture.Run.RunSeed, fixture.Run.WorldNodeId, fixture.Run.CurrentDay,
                fixture.Run.GetBoardSeed(), "forest", "temperate", 1);
            for (int horizontal = 0; horizontal < 5; horizontal++)
            for (int vertical = 0; vertical < 3; vertical++)
            {
                LegacyBoardContentKind kind = horizontal == 2 && vertical == 1 ? LegacyBoardContentKind.PressurePlate :
                    horizontal == 4 && vertical == 1 ? LegacyBoardContentKind.Gate : LegacyBoardContentKind.Floor;
                fixture.Layout.Add(View(fixture, kind, horizontal, vertical));
            }
            fixture.Layout.Add(View(fixture, LegacyBoardContentKind.Player, 1, 1));
            fixture.Layout.Add(View(fixture, LegacyBoardContentKind.Exit, 4, 2));
            fixture.Runtime = LegacyBoardRuntimeComposer.Compose(fixture.Request, fixture.Run, new GridBounds(0, 0, 4, 2), fixture.Layout);
            fixture.Plate = fixture.Layout.Single(view => view.Kind == LegacyBoardContentKind.PressurePlate).View.GetComponent<PlateGateView>();
            fixture.Gate = fixture.Layout.Single(view => view.Kind == LegacyBoardContentKind.Gate).View.GetComponent<PlateGateView>();
            fixture.Presenter = new BoardEventPresenter(fixture.Runtime, fixture, fixture, fixture, diagnostic =>
            {
                fixture.Diagnostics.Add(diagnostic);
                if (!allowDiagnostic) Assert.Fail(diagnostic.Code + ": " + diagnostic.Error);
            }, duration) { AnimationsEnabled = animations };
            fixture.Presenter.SynchronizeFromState();
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
            return fixture;
        }

        private static LegacyBoardView View(Fixture fixture, LegacyBoardContentKind kind, int horizontal, int vertical)
        {
            string path = kind == LegacyBoardContentKind.PressurePlate ? "Assets/Prefabs/Tiles/PressurePlate.prefab" :
                kind == LegacyBoardContentKind.Gate ? "Assets/Prefabs/Tiles/Gate.prefab" :
                kind == LegacyBoardContentKind.Player ? "Assets/Prefabs/Player.prefab" :
                kind == LegacyBoardContentKind.Exit ? "Assets/Prefabs/Tiles/Exit.prefab" : "Assets/Prefabs/Tiles/Floor1.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            bool linked = kind == LegacyBoardContentKind.Gate || kind == LegacyBoardContentKind.PressurePlate;
            GameObject owner;
            if (linked) owner = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            else
            {
                owner = new GameObject(kind.ToString());
                Sprite sprite = prefab.GetComponentInChildren<SpriteRenderer>(true).sprite;
                if (sprite == null)
                    sprite = prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController.animationClips.SelectMany(clip =>
                        AnimationUtility.GetObjectReferenceCurveBindings(clip).Where(binding => binding.type == typeof(SpriteRenderer))
                            .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(clip, binding)))
                        .Select(frame => frame.value as Sprite).First(candidate => candidate != null);
                SpriteRenderer renderer = owner.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingLayerName = kind == LegacyBoardContentKind.Floor ? "Floor" : "Units";
            }
            fixture.Objects.Add(owner);
            owner.transform.position = new Vector3(horizontal, vertical, 0);
            return new LegacyBoardView(owner, kind, new GridPosition(horizontal, vertical), linkId: linked ? "A" : null);
        }

        private sealed class Fixture : IDisposable, IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public readonly List<GameObject> Objects = new List<GameObject>();
            public readonly List<LegacyBoardView> Layout = new List<LegacyBoardView>();
            public readonly List<bool> GateFeedback = new List<bool>();
            public readonly List<PresentationDiagnostic> Diagnostics = new List<PresentationDiagnostic>();
            public RunState Run;
            public BoardRequest Request;
            public BoardRuntime Runtime;
            public BoardEventPresenter Presenter;
            public PlateGateView Plate;
            public PlateGateView Gate;
            private bool disposed;
            public void Refresh(int health, int food) { }
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) { if (gameEvent is GateChangedEvent) GateFeedback.Add(Gate.DisplayedActive); }
            public bool TryNotify(BoardRequest request, GameEvent outcome) => true;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Presenter?.Dispose();
                Runtime?.Dispose();
                foreach (GameObject owner in Objects) if (owner != null) Object.DestroyImmediate(owner);
            }
        }
    }
}
#endif