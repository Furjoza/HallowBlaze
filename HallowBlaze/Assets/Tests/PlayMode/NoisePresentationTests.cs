using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using HallowBlaze.Presentation.Runtime.Noise;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Verifies noise geometry, production replay, lifecycle ownership and visible multi-strength evidence.</summary>
    public sealed class NoisePresentationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        /// <summary>Releases only test-owned transient geometry and cameras.</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject instance in objects)
                if (instance != null)
                    UnityEngine.Object.Destroy(instance);
            objects.Clear();
            yield return null;
        }

        /// <summary>The displayed cells equal hearing query cells and snapshots, with no mutation from rendering.</summary>
        [Test]
        public void ViewUsesExactDomainFootprintAndClosesOnReplacement()
        {
            BoardState board = CreateBoard();
            BoardNoise noise = board.EmitNoise(new EntityId(1), new GridPosition(3, 3), 3, 2);
            BoardState active = board;
            BoardNoisePresenter view = CreateView(board, () => active);
            view.Refresh();
            Assert.That(view.Projections.Single(), Is.SameAs(noise));
            Assert.That(view.GetComponentsInChildren<LineRenderer>().Length, Is.EqualTo(noise.AudibleCells.Count));
            foreach (LineRenderer marker in view.GetComponentsInChildren<LineRenderer>())
                Assert.That(noise.IsAudible(new GridPosition(Mathf.RoundToInt(marker.transform.position.x),
                    Mathf.RoundToInt(marker.transform.position.y))), Is.True);
            Assert.That(view.GetComponentInChildren<TextMesh>().text, Is.EqualTo("S3 T2"));
            Assert.That(board.GetActiveNoises().Single(), Is.SameAs(noise));
            active = new BoardState();
            view.Refresh();
            Assert.That(view.IsDisposed, Is.True);
            Assert.That(view.GetComponentsInChildren<LineRenderer>(), Is.Empty);
            Assert.That(board.GetActiveNoises().Single(), Is.SameAs(noise));
        }

        /// <summary>Real runtime command replay publishes noise once, silent Wait ages it and terminal/disposal clears it.</summary>
        [UnityTest]
        public IEnumerator RuntimeReplayPublishesAndExpiresWithoutExtraResolution()
        {
            var run = new RunState("m47-view", 47, new RunStateConfiguration(100, 10, 0, "forest.start"));
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay,
                run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 3; horizontal++)
                for (int vertical = 0; vertical < 3; vertical++)
                    layout.Add(View(LegacyBoardContentKind.Floor, horizontal, vertical));
            layout.Add(View(LegacyBoardContentKind.Player, 0, 0));
            layout.Add(View(LegacyBoardContentKind.Exit, 2, 2));
            using (BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 2), layout))
            {
                var sinks = new Sinks();
                using (var presenter = new BoardEventPresenter(runtime, sinks, sinks, sinks,
                    diagnostic => Assert.Fail(diagnostic.Code.ToString()), 0f))
                {
                    presenter.SynchronizeFromState();
                    presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
                    var moved = presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.East));
                    while (!moved.IsCompleted)
                        yield return null;
                    Assert.That(moved.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                    NoiseEvent noise = moved.Result.Result.Events.OfType<NoiseEvent>().Single();
                    Assert.That(presenter.NoiseDisplay.Projections.Single(), Is.SameAs(noise.Noise));
                    Assert.That(sinks.Noises, Is.EqualTo(1));
                    Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(1));
                    Assert.That(run.Food, Is.EqualTo(9));
                    presenter.Coordinator.SubmitAsync(new WaitCommand()).GetAwaiter().GetResult();
                    Assert.That(presenter.NoiseDisplay.Projections.Single().RemainingTurns, Is.EqualTo(1));
                    presenter.Coordinator.SubmitAsync(new WaitCommand()).GetAwaiter().GetResult();
                    Assert.That(presenter.NoiseDisplay.Projections, Is.Empty);
                    Assert.That(sinks.Noises, Is.EqualTo(1));
                    runtime.BoardState.EmitNoise(runtime.PlayerId, new GridPosition(1, 0), 2, 2);
                    presenter.SynchronizeFromState();
                    Assert.That(presenter.NoiseDisplay.Projections.Count, Is.EqualTo(1));
                    runtime.Dispose();
                    presenter.SynchronizeFromState();
                    Assert.That(presenter.NoiseDisplay.IsDisposed, Is.True);
                }
            }
        }

        /// <summary>Graphics capture proves visible distinct footprints at three strengths without any audio dependency.</summary>
        [Test]
        public void SeveralStrengthsRenderVisibleFootprints()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            int[] strengths = { 1, 3, 5 };
            for (int sample = 0; sample < strengths.Length; sample++)
            {
                BoardState board = CreateBoard();
                board.EmitNoise(new EntityId(1), new GridPosition(3, 3), strengths[sample], 2);
                BoardNoisePresenter view = CreateView(board, () => board);
                view.transform.position = new Vector3(sample * 10, 0, 0);
                view.Refresh();
            }
            var cameraOwner = new GameObject("Noise evidence camera");
            objects.Add(cameraOwner);
            Camera camera = cameraOwner.AddComponent<Camera>();
            camera.transform.position = new Vector3(13.5f, 3.5f, -20);
            camera.orthographic = true;
            camera.orthographicSize = 5.2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.06f, 0.06f, 1);
            var target = new RenderTexture(1500, 500, 24);
            var pixels = new Texture2D(1500, 500, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1500, 500), 0, 0);
                pixels.Apply();
                Color32[] colors = pixels.GetPixels32();
                for (int sample = 0; sample < 3; sample++)
                {
                    int marked = 0;
                    for (int vertical = 0; vertical < 500; vertical++)
                        for (int horizontal = sample * 500; horizontal < (sample + 1) * 500; horizontal++)
                        {
                            Color32 color = colors[vertical * 1500 + horizontal];
                            if (color.r > 100 || color.g > 100 || color.b > 100)
                                marked++;
                        }
                    Assert.That(marked, Is.GreaterThan(100), "Visible pixels for strength " + strengths[sample]);
                }
                string directory = Path.Combine(Path.GetTempPath(), "HB-M47-visual-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string image = Path.Combine(directory, "noise-strengths-1-3-5.png");
                File.WriteAllBytes(image, pixels.EncodeToPNG());
                TestContext.WriteLine("Noise visual evidence: " + image);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private BoardNoisePresenter CreateView(BoardState board, Func<BoardState> active)
        {
            var owner = new GameObject("Noise test display");
            objects.Add(owner);
            BoardNoisePresenter view = owner.AddComponent<BoardNoisePresenter>();
            view.Bind(board, active);
            return view;
        }

        private LegacyBoardView View(LegacyBoardContentKind kind, int horizontal, int vertical)
        {
            var instance = new GameObject(kind.ToString());
            instance.transform.position = new Vector3(horizontal, vertical, 0);
            objects.Add(instance);
            return new LegacyBoardView(instance, kind, new GridPosition(horizontal, vertical));
        }

        private static BoardState CreateBoard()
        {
            var board = new BoardState();
            for (int cellIndex = 0; cellIndex < 64; cellIndex++)
                Assert.That(board.TryAdd(new BoardEntityState(new EntityId(100 + cellIndex),
                    new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "ground", new BoardEntityTraits(true, false, false)),
                    new GridPosition(cellIndex / 8, cellIndex % 8))), Is.True);
            return board;
        }

        private sealed class Sinks : IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public int Noises;
            public void Refresh(int health, int food) { }
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) { if (gameEvent is NoiseEvent) Noises++; }
            public bool TryNotify(BoardRequest request, GameEvent outcome) => true;
        }
    }
}