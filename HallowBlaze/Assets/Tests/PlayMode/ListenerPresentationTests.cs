using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Verifies real Listener composition, announced investigation and exact replay without extra domain resolution.</summary>
    public sealed class ListenerPresentationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        /// <summary>Destroys only test-owned transient views.</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject instance in objects)
                if (instance != null) Object.Destroy(instance);
            objects.Clear();
            yield return null;
        }

        /// <summary>Both animation modes publish fixed next/source targets before input and execute that same announcement.</summary>
        [UnityTest]
        public IEnumerator AnnouncedInvestigationExecutesExactly([Values(false, true)] bool animations)
        {
            RunState run = new RunState("listener-view", 48, new RunStateConfiguration(100, 10, 0, "forest.start"));
            using (BoardRuntime runtime = CreateRuntime(run))
            {
                var owner = new GameObject("Listener intent display");
                objects.Add(owner);
                EnemyIntentPresenter display = owner.AddComponent<EnemyIntentPresenter>();
                var sinks = new Sinks();
                using (var presenter = new BoardEventPresenter(runtime, sinks, sinks, sinks,
                    diagnostic => Assert.Fail(diagnostic.Code.ToString()), 0.05f, display))
                {
                    presenter.AnimationsEnabled = animations;
                    presenter.SynchronizeFromState();
                    presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
                    var listener = runtime.Listeners.Single();
                    Assert.That(runtime.Enemies, Is.Empty);
                    Assert.That(display.Projections.Single().Symbol, Is.EqualTo(EnemyIntentSymbol.Wait));
                    EntityId leverId = runtime.BoardState.GetEntities().Single(entity => entity.Definition.ContentId == "legacy.aid").Id;
                    var interaction = presenter.Coordinator.SubmitAsync(new InteractCommand(leverId));
                    while (!interaction.IsCompleted) yield return null;
                    Assert.That(interaction.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                    EnemyIntent announced = listener.LockedIntent;
                    EnemyIntentProjection projection = display.Projections.Single();
                    Assert.That(projection.Symbol, Is.EqualTo(EnemyIntentSymbol.Investigate));
                    Assert.That(projection.TargetPosition, Is.EqualTo(new GridPosition(2, 1)));
                    Assert.That(projection.InvestigationTarget, Is.EqualTo(new GridPosition(1, 1)));
                    Assert.That(projection.NoiseId, Is.EqualTo(interaction.Result.Result.Events.OfType<NoiseEvent>().Single().Noise.Id));
                    Assert.That(display.Views[listener.ActorId].transform.position.x, Is.EqualTo(2));
                    Assert.That(display.Views[listener.ActorId].GetComponentsInChildren<LineRenderer>()
                        .Any(line => line.name == "HeardNoiseTarget" && line.enabled), Is.True);
                    Assert.That(presenter.Coordinator.SubmitAsync(new InteractCommand(new EntityId(999))).Result.Status,
                        Is.EqualTo(CommandSubmissionStatus.Rejected));
                    Assert.That(listener.LockedIntent, Is.SameAs(announced));
                    Assert.That(display.Projections.Single(), Is.SameAs(projection));
                    var move = presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.North));
                    if (animations)
                    {
                        Assert.That(move.IsCompleted, Is.False);
                        Assert.That(display.Projections.Single(), Is.SameAs(projection));
                    }
                    while (!move.IsCompleted) yield return null;
                    Assert.That(move.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                    EntityMovedEvent outcome = move.Result.Result.Events.OfType<EntityMovedEvent>()
                        .Single(movement => movement.EntityId.Equals(listener.ActorId));
                    Assert.That(outcome.To, Is.EqualTo(announced.TargetPosition));
                    Assert.That(runtime.Views[listener.ActorId].transform.position.x, Is.EqualTo(2));
                    Assert.That(runtime.Views[listener.ActorId].transform.position.y, Is.EqualTo(1));
                    Assert.That(listener.LockedIntent.InvestigationTarget, Is.EqualTo(new GridPosition(1, 1)));
                    Assert.That(run.Food, Is.EqualTo(8));
                    Assert.That(run.Health, Is.EqualTo(100));
                    Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(3));
                    var wait = presenter.Coordinator.SubmitAsync(new WaitCommand());
                    while (!wait.IsCompleted) yield return null;
                    Assert.That(display.Projections.Single().Symbol, Is.EqualTo(EnemyIntentSymbol.Wait));
                    Assert.That(runtime.BoardState.TryGetListenerNoise(listener.ActorId, out _), Is.False);
                    Assert.That(wait.Result.Result.Events.OfType<EntityMovedEvent>(), Is.Empty);
                    runtime.Dispose();
                    presenter.SynchronizeFromState();
                    Assert.That(display.IsDisposed, Is.True);
                    Assert.That(display.Views, Is.Empty);
                }
            }
        }

        /// <summary>Listeners cannot accidentally become a Shambler damage variant through ambiguous authoring.</summary>
        [Test]
        public void ListenerMappingRejectsDamageVariant()
        {
            var run = new RunState("invalid-listener-view", 48, new RunStateConfiguration(100, 10, 0, "forest.start"));
            var request = Request(run);
            var descriptor = View(LegacyBoardContentKind.Listener, 3, 1, 10);
            var error = Assert.Throws<BoardRuntimeCompositionException>(() => LegacyBoardRuntimeComposer.Compose(
                request, run, new GridBounds(0, 0, 4, 4), new[] { descriptor }));
            Assert.That(error.Code, Is.EqualTo(BoardRuntimeDiagnosticCode.InvalidInput));
        }

        private BoardRuntime CreateRuntime(RunState run)
        {
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 5; horizontal++)
                for (int vertical = 0; vertical < 5; vertical++)
                    layout.Add(View(LegacyBoardContentKind.Floor, horizontal, vertical));
            layout.Add(View(LegacyBoardContentKind.Player, 1, 1));
            layout.Add(View(LegacyBoardContentKind.Listener, 3, 1));
            layout.Add(View(LegacyBoardContentKind.Exit, 4, 4));
            layout.Add(View(LegacyBoardContentKind.Aid, 1, 2));
            return LegacyBoardRuntimeComposer.Compose(Request(run), run, new GridBounds(0, 0, 4, 4), layout);
        }

        private static BoardRequest Request(RunState run) => new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId,
            run.CurrentDay, run.GetBoardSeed(), "forest", "temperate", 1);

        private LegacyBoardView View(LegacyBoardContentKind kind, int horizontal, int vertical, int? damage = null)
        {
            var instance = new GameObject(kind.ToString());
            instance.transform.position = new Vector3(horizontal, vertical, 0);
            objects.Add(instance);
            return new LegacyBoardView(instance, kind, new GridPosition(horizontal, vertical), damage);
        }

        private sealed class Sinks : IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public void Refresh(int health, int food) { }
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) { }
            public bool TryNotify(BoardRequest request, GameEvent outcome) => true;
        }
    }
}