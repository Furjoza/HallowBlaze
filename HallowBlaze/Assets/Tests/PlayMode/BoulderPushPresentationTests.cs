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
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;
using Object = UnityEngine.Object;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Checks synchronized player/boulder replay through the production picker, resolver and command gate.</summary>
    public sealed class BoulderPushPresentationTests
    {
        private readonly List<Fixture> fixtures = new List<Fixture>();

        /// <summary>Releases fixture-owned runtimes and views without modifying the current scene or save data.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (Fixture fixture in fixtures) fixture.Dispose();
            fixtures.Clear();
        }

        /// <summary>One confirmed push commits both cells once, then replays them on the same interpolation clock without accepting rapid extra input.</summary>
        [UnityTest]
        public IEnumerator PickerCommitsOnePushAndPresentsBothParticipantsTogether()
        {
            foreach (bool animations in new[] { false, true })
            {
                Fixture fixture = CreateFixture(animations);
                var picker = fixture.Presenter.InteractionDisplay;
                Assert.That(picker.Open(), Is.True);
                Assert.That(picker.Options.Single().ActionId, Is.EqualTo(BoulderPushAction.ActionId));
                Assert.That(picker.GetOptionLabel(0), Does.Contain("advance one cell").And.Contain("Food -1").And.Contain("Noise 4 / 2"));
                Assert.That(picker.Select(0), Is.True);
                var operation = picker.ConfirmAsync();
                Assert.That(fixture.PlayerCell, Is.EqualTo(new GridPosition(2, 1)));
                Assert.That(fixture.BoulderCell, Is.EqualTo(new GridPosition(3, 1)));
                Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.NoCommand));
                if (!operation.IsCompleted)
                    Assert.That(fixture.Presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status, Is.EqualTo(CommandSubmissionStatus.Blocked));
                while (!operation.IsCompleted)
                {
                    Assert.That(fixture.Boulder.transform.position.x - fixture.Player.transform.position.x, Is.EqualTo(1).Within(0.001));
                    yield return null;
                }
                Assert.That(operation.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(operation.Result.Result.Events.OfType<BoulderPushedEvent>().Count(), Is.EqualTo(1));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
                Assert.That(fixture.Run.Food, Is.EqualTo(19));
                Assert.That(fixture.Player.transform.position.x, Is.EqualTo(2));
                Assert.That(fixture.Boulder.transform.position.x, Is.EqualTo(3));
                Assert.That(fixture.Feedback.OfType<BoulderPushedEvent>().Count(), Is.EqualTo(1));
                Assert.That(fixture.Presenter.NoiseDisplay.Projections.Single().Position, Is.EqualTo(fixture.PlayerCell));
                fixture.Dispose();
                yield return null;
            }
        }

        /// <summary>Cancel is free and a destination that changes after preview rejects without partial movement or releasing another modal.</summary>
        [UnityTest]
        public IEnumerator CancelAndStaleDestinationKeepBothViewsAndIndependentModalOwnership()
        {
            Fixture fixture = CreateFixture(false);
            var picker = fixture.Presenter.InteractionDisplay;
            Assert.That(picker.Open(), Is.True);
            picker.Cancel();
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
            Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.Zero);
            yield return null;
            Assert.That(picker.Open(), Is.True);
            picker.Select(0);
            fixture.Runtime.BoardState.TryAdd(new BoardEntityState(new EntityId(900), new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Enemy, "blocker"), new GridPosition(3, 1)));
            Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(fixture.PlayerCell, Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(fixture.BoulderCell, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(fixture.Player.transform.position.x, Is.EqualTo(1));
            Assert.That(fixture.Boulder.transform.position.x, Is.EqualTo(2));
            Assert.That(fixture.Run.Food, Is.EqualTo(20));
            Assert.That(fixture.Runtime.BoardState.GetActiveNoises(), Is.Empty);
            yield return null;
            Assert.That(picker.Open(), Is.True);
            fixture.Presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Modal, true);
            picker.Cancel();
            yield return null;
            Assert.That(fixture.Presenter.Coordinator.Gate.Blocks & PresentationInputBlock.Modal, Is.EqualTo(PresentationInputBlock.Modal));
        }

        /// <summary>Canceling in-flight replay recovers both committed positions without a refund, second resolution or duplicate feedback.</summary>
        [UnityTest]
        public IEnumerator CanceledReplayRecoversBothViewsWithoutRepeatingThePush()
        {
            Fixture fixture = CreateFixture(true, true, 1f);
            using (var cancellation = new CancellationTokenSource())
            {
                InteractCommand command = fixture.Runtime.Interactions.GetOptions(fixture.Runtime.BoardState, fixture.Run,
                    fixture.Runtime.PlayerId).Single().CreateCommand();
                var operation = fixture.Presenter.Coordinator.SubmitAsync(command, cancellation.Token);
                while (!operation.IsCompleted && fixture.Player.transform.position.x == 1f) yield return null;
                Assert.That(fixture.Player.transform.position.x, Is.GreaterThan(1f).And.LessThan(2f));
                Assert.That(fixture.Boulder.transform.position.x - fixture.Player.transform.position.x, Is.EqualTo(1).Within(0.001));
                cancellation.Cancel();
                while (!operation.IsCompleted) yield return null;
                Assert.That(operation.Result.Status, Is.EqualTo(CommandSubmissionStatus.PresentationFailed));
                Assert.That(operation.Result.Result.Accepted, Is.True);
                Assert.That(operation.Result.Diagnostic.Code, Is.EqualTo(PresentationDiagnosticCode.Canceled));
                Assert.That(fixture.Diagnostics.Count, Is.EqualTo(1));
                Assert.That(fixture.Presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
                Assert.That(fixture.Run.Food, Is.EqualTo(19));
                Assert.That(fixture.Player.transform.position.x, Is.EqualTo(fixture.PlayerCell.X));
                Assert.That(fixture.Boulder.transform.position.x, Is.EqualTo(fixture.BoulderCell.X));
                Assert.That(fixture.Feedback.OfType<BoulderPushedEvent>(), Is.Empty);
                Assert.That(fixture.Presenter.Coordinator.CanSubmit, Is.True);
            }
        }

        /// <summary>Explicit blueprint content retains the same boulder definition and production action without adding it to any generator.</summary>
        [Test]
        public void BlueprintComposerUsesTheSameExplicitPushCatalog()
        {
            var fixture = new Fixture { Run = new RunState("boulder-blueprint", 62, new RunStateConfiguration(100, 20, 0, "forest.start")) };
            fixtures.Add(fixture);
            fixture.Run.StartLeg("boulder.edge", fixture.Run.WorldNodeId, "boulder.destination",
                new[] { new RouteSegmentState("boulder.segment", "boulder.fixture", 1) });
            var request = new BoardRequest(BoardAddressFactory.CreateFromState(fixture.Run), fixture.Run.RunSeed,
                fixture.Run.WorldNodeId, "forest", "temperate", 1);
            var floor = new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "boulder.floor", new BoardEntityTraits(true, false, false));
            var boundary = new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "boulder.boundary");
            var exit = new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "boulder.exit", new BoardEntityTraits(true, true, false));
            var player = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "boulder.player");
            var definitions = new[] { floor, boundary, exit, player, BoulderPushAction.Definition };
            var entities = new List<BoardEntityState>();
            var views = new Dictionary<EntityId, GameObject>();
            for (int horizontal = 0; horizontal < 8; horizontal++)
            for (int vertical = 0; vertical < 8; vertical++)
            {
                BoardEntityDefinition definition = horizontal == 0 || horizontal == 7 || vertical == 0 || vertical == 7 ? boundary :
                    horizontal == 6 && vertical == 6 ? exit : floor;
                var entity = new BoardEntityState(new EntityId(100 + horizontal * 8 + vertical), definition, new GridPosition(horizontal, vertical));
                entities.Add(entity);
                views.Add(entity.Id, View(fixture, LegacyBoardContentKind.Floor, horizontal, vertical).View);
            }
            entities.Add(new BoardEntityState(new EntityId(1), player, new GridPosition(1, 1)));
            entities.Add(new BoardEntityState(new EntityId(2), BoulderPushAction.Definition, new GridPosition(2, 1)));
            views.Add(new EntityId(1), View(fixture, LegacyBoardContentKind.Player, 1, 1).View);
            views.Add(new EntityId(2), View(fixture, LegacyBoardContentKind.Boulder, 2, 1).View);
            var config = new BoardGenerationConfig(request.Address.GenerationConfigurationId, request.Address.GenerationVersion,
                new GenerationRange(1, 63), new GenerationRange(0, 64), new GenerationRange(1, 1), new GenerationRange(0, 0), 0, 0,
                definitions.Select(definition => definition.ContentId), Array.Empty<ResourceGenerationBudget>());
            var blueprint = new BoardBlueprint(request.Address, config, entities, Array.Empty<BoardItemSpawn>());
            fixture.Runtime = BlueprintBoardRuntimeComposer.Compose(request, fixture.Run, blueprint,
                new BoardValidationCatalog(definitions, new Dictionary<string, string>(), new Dictionary<string, string>()), views);
            InteractionOption option = fixture.Runtime.Interactions.GetOptions(fixture.Runtime.BoardState, fixture.Run, fixture.Runtime.PlayerId).Single();
            Assert.That(option.ActionId, Is.EqualTo(BoulderPushAction.ActionId));
            Assert.That(fixture.Runtime.Controller.Resolve(option.CreateCommand()).Accepted, Is.True);
            Assert.That(fixture.PlayerCell, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(fixture.Runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(new EntityId(2))).Position, Is.EqualTo(new GridPosition(3, 1)));
            Assert.That(blueprint.Entities.Single(entity => entity.Id.Equals(new EntityId(2))).Position, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(fixture.Run.Food, Is.EqualTo(19));
        }

        /// <summary>Real authored sprites remain framed and nonblank before/after a push in desktop and portrait captures.</summary>
        [UnityTest]
        public IEnumerator AuthoredBoulderAndPlayerRemainReadableInBothViewportShapes()
        {
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(320, 640) })
            {
                Fixture fixture = CreateFixture(false);
                Color32[] before = Capture(fixture, size, "before");
                var picker = fixture.Presenter.InteractionDisplay;
                Assert.That(picker.Open(), Is.True);
                Assert.That(picker.Select(0), Is.True);
                Assert.That(picker.ConfirmAsync().Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Color32[] after = Capture(fixture, size, "after");
                Assert.That(before.Zip(after, (first, second) => !first.Equals(second)).Count(changed => changed), Is.GreaterThan(100));
                fixture.Dispose();
                yield return null;
            }
        }

        private static Color32[] Capture(Fixture fixture, Vector2Int size, string stage)
        {
            var owner = new GameObject("Boulder capture camera");
            var target = new RenderTexture(size.x, size.y, 24);
            var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Camera camera = owner.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(2.1f, 3f * size.y / size.x);
                camera.transform.position = new Vector3(2, 1, -10);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(12, 28, 20, 255);
                camera.targetTexture = target;
                foreach (GameObject view in new[] { fixture.Player, fixture.Boulder })
                {
                    Bounds bounds = view.GetComponent<SpriteRenderer>().bounds;
                    foreach (Vector3 corner in new[] { bounds.min, bounds.max })
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
                Assert.That(pixels.Count(color => color.r > 70 && Math.Abs(color.r - color.g) < 24 && Math.Abs(color.g - color.b) < 24),
                    Is.GreaterThan(100), "The rock's gray facets must be visible rather than a blank camera.");
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M62-push-" + stage + "-" + size.x + "x" + size.y + ".png"), texture.EncodeToPNG());
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

        private Fixture CreateFixture(bool animations, bool allowDiagnostic = false, float duration = 0.08f)
        {
            var fixture = new Fixture();
            fixtures.Add(fixture);
            fixture.Run = new RunState("boulder-presentation", 62, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var request = new BoardRequest(fixture.Run.RunId, fixture.Run.RunSeed, fixture.Run.WorldNodeId, fixture.Run.CurrentDay,
                fixture.Run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 5; horizontal++)
            for (int vertical = 0; vertical < 3; vertical++)
                layout.Add(View(fixture, LegacyBoardContentKind.Floor, horizontal, vertical));
            fixture.Player = View(fixture, LegacyBoardContentKind.Player, 1, 1).View;
            fixture.Boulder = View(fixture, LegacyBoardContentKind.Boulder, 2, 1).View;
            layout.Add(new LegacyBoardView(fixture.Player, LegacyBoardContentKind.Player, new GridPosition(1, 1)));
            layout.Add(new LegacyBoardView(fixture.Boulder, LegacyBoardContentKind.Boulder, new GridPosition(2, 1)));
            layout.Add(View(fixture, LegacyBoardContentKind.Exit, 4, 2));
            fixture.Runtime = LegacyBoardRuntimeComposer.Compose(request, fixture.Run, new GridBounds(0, 0, 4, 2), layout);
            fixture.BoulderId = fixture.Runtime.BoardState.GetEntities().Single(entity => entity.Definition.ContentId == BoulderPushAction.ContentId).Id;
            fixture.Presenter = new BoardEventPresenter(fixture.Runtime, fixture, fixture, fixture,
                diagnostic =>
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
            var owner = new GameObject(kind.ToString());
            fixture.Objects.Add(owner);
            owner.transform.position = new Vector3(horizontal, vertical, 0);
            string path = kind == LegacyBoardContentKind.Floor ? "Assets/Prefabs/Tiles/Floor1.prefab" :
                kind == LegacyBoardContentKind.Boulder ? "Assets/Prefabs/Tiles/Boulder.prefab" :
                kind == LegacyBoardContentKind.Exit ? "Assets/Prefabs/Tiles/Exit.prefab" : "Assets/Prefabs/Player.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Sprite sprite = prefab.GetComponentInChildren<SpriteRenderer>(true).sprite;
            if (sprite == null)
            {
                Animator animator = prefab.GetComponentInChildren<Animator>(true);
                sprite = animator.runtimeAnimatorController.animationClips.SelectMany(clip =>
                    AnimationUtility.GetObjectReferenceCurveBindings(clip).Where(binding => binding.type == typeof(SpriteRenderer))
                        .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(clip, binding)))
                    .Select(frame => frame.value as Sprite).First(candidate => candidate != null);
            }
            SpriteRenderer renderer = owner.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = kind == LegacyBoardContentKind.Floor ? "Floor" : "Units";
            return new LegacyBoardView(owner, kind, new GridPosition(horizontal, vertical));
        }

        private sealed class Fixture : IDisposable, IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public readonly List<GameObject> Objects = new List<GameObject>();
            public readonly List<GameEvent> Feedback = new List<GameEvent>();
            public readonly List<PresentationDiagnostic> Diagnostics = new List<PresentationDiagnostic>();
            public RunState Run;
            public BoardRuntime Runtime;
            public BoardEventPresenter Presenter;
            public GameObject Player;
            public GameObject Boulder;
            public EntityId BoulderId;
            private bool disposed;
            public GridPosition PlayerCell => Runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(Runtime.PlayerId)).Position;
            public GridPosition BoulderCell => Runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(BoulderId)).Position;
            public void Refresh(int health, int food) { }
            public void ShowEvent(GameEvent gameEvent) => Feedback.Add(gameEvent);
            public void ShowRejection(CommandRejectionCode reason) { }
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