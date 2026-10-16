using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies mixed initiative, no retries, and real accepted/rejected noise-command sequences.</summary>
    public sealed class ListenerTurnTests
    {
        /// <summary>Two Listener plans conflict in ascending initiative regardless of roster order.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SharedDestinationUsesStableInitiative(bool reverse)
        {
            BoardState board = CreateBoard();
            ListenerState first = AddListener(board, 2, new GridPosition(1, 2));
            ListenerState second = AddListener(board, 3, new GridPosition(3, 2));
            BoardNoise noise = board.EmitNoise(new EntityId(9), new GridPosition(2, 2), 3, 4);
            var batch = new MixedEnemyTurnPhases(Array.Empty<ShamblerState>(), reverse ? new[] { second, first } : new[] { first, second });
            batch.PlanInitialIntents(board, new EntityId(1));
            Assert.That(first.LockedIntent.NoiseId, Is.EqualTo(noise.Id));
            var events = new List<GameEvent>();
            batch.Handlers.ExecuteLockedIntents(board, CreateRun(), new EntityId(1), events);
            Assert.That(((EntityMovedEvent)events[0]).EntityId, Is.EqualTo(first.ActorId));
            Assert.That(((ListenerInvestigatedEvent)events[1]).ActorId, Is.EqualTo(first.ActorId));
            Assert.That(((ListenerInvestigatedEvent)events[1]).NoiseId, Is.EqualTo(noise.Id));
            Assert.That(((EntityWaitedEvent)events[2]).EntityId, Is.EqualTo(second.ActorId));
            Assert.That(events.Count, Is.EqualTo(3));
            Assert.That(board.TryGetEntity(second.ActorId, out BoardEntityState blocked), Is.True);
            Assert.That(blocked.Position, Is.EqualTo(new GridPosition(3, 2)));
        }

        /// <summary>Both archetypes share the same live-occupancy order rather than receiving separate batches.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CrossArchetypeConflictRespectsActorId(bool listenerFirst)
        {
            BoardState board = CreateBoard();
            long listenerId = listenerFirst ? 2 : 3;
            long shamblerId = listenerFirst ? 3 : 2;
            ListenerState listener = AddListener(board, listenerId, new GridPosition(1, 2));
            Assert.That(board.TryAdd(Actor(shamblerId, new GridPosition(3, 2))), Is.True);
            var shambler = new ShamblerState(new EntityId(shamblerId), new ShamblerDefinition(10));
            var target = new GridPosition(2, 2);
            BoardNoise noise = board.EmitNoise(new EntityId(9), target, 3, 4);
            listener.LockIntent(new EnemyIntent(listener.ActorId, EnemyIntentKind.Investigate, target,
                investigationTarget: target, noiseId: noise.Id, sourcePosition: new GridPosition(1, 2)));
            shambler.LockIntent(new EnemyIntent(shambler.ActorId, EnemyIntentKind.Move, target, new EntityId(1), true));
            var batch = new MixedEnemyTurnPhases(new[] { shambler }, new[] { listener });
            var events = new List<GameEvent>();
            batch.Handlers.ExecuteLockedIntents(board, CreateRun(), new EntityId(1), events);
            Assert.That(((EntityMovedEvent)events[0]).EntityId, Is.EqualTo(new EntityId(2)));
            Assert.That(((EntityWaitedEvent)events[listenerFirst ? 2 : 1]).EntityId, Is.EqualTo(new EntityId(3)));
            Assert.That(events.OfType<ListenerInvestigatedEvent>().Count(), Is.EqualTo(listenerFirst ? 1 : 0));
            if (listenerFirst) Assert.That(((ListenerInvestigatedEvent)events[1]).ActorId, Is.EqualTo(listener.ActorId));
        }

        /// <summary>A real interaction lures the Listener toward the old source after the player leaves, with no extra action cost.</summary>
        [Test]
        public void PlayerCanLureListenerWithoutChangingAnnouncedOpportunity()
        {
            BoardState board = CreateBoard();
            ListenerState listener = AddListener(board, 2, new GridPosition(3, 1));
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(8),
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "lever", new BoardEntityTraits(true, false, true)),
                new GridPosition(1, 2))), Is.True);
            var batch = new MixedEnemyTurnPhases(Array.Empty<ShamblerState>(), new[] { listener });
            batch.PlanInitialIntents(board, new EntityId(1));
            EnemyIntent initial = listener.LockedIntent;
            RunState run = CreateRun();
            var controller = new TurnController(board, run, new EntityId(1), new TurnResolver(ActionNoiseProfile.Prototype), batch.Handlers);
            Assert.That(controller.Resolve(new InteractCommand(new EntityId(8))).Accepted, Is.True);
            EnemyIntent announced = listener.LockedIntent;
            Assert.That(initial.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(announced.Kind, Is.EqualTo(EnemyIntentKind.Investigate));
            Assert.That(announced.TargetPosition, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(announced.InvestigationTarget, Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(controller.Resolve(new InteractCommand(new EntityId(999))).Accepted, Is.False);
            Assert.That(listener.LockedIntent, Is.SameAs(announced));
            TurnResult escaped = controller.Resolve(new MoveCommand(Direction.North));
            Assert.That(escaped.Events.OfType<EntityMovedEvent>().Last().To, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(listener.LockedIntent.InvestigationTarget, Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(run.Food, Is.EqualTo(8));
            Assert.That(run.Health, Is.EqualTo(100));
        }

        /// <summary>Terminal player outcomes neither execute nor replace a Listener's announcement.</summary>
        [Test]
        public void TerminalOutcomeDoesNotConsumeListenerIntent()
        {
            BoardState board = CreateBoard();
            ListenerState listener = AddListener(board, 2, new GridPosition(3, 1));
            board.EmitNoise(new EntityId(9), new GridPosition(4, 1), 3, 4);
            var batch = new MixedEnemyTurnPhases(Array.Empty<ShamblerState>(), new[] { listener });
            batch.PlanInitialIntents(board, new EntityId(1));
            EnemyIntent announced = listener.LockedIntent;
            RunState run = CreateRun();
            run.ConsumeFood(9);
            var controller = new TurnController(board, run, new EntityId(1), phases: batch.Handlers);
            controller.Resolve(new WaitCommand());
            Assert.That(controller.IsTerminal, Is.True);
            Assert.That(listener.LockedIntent, Is.SameAs(announced));
        }

        /// <summary>Duplicate archetype identities and partial batches fail before any intent changes.</summary>
        [Test]
        public void BatchValidationIsAtomic()
        {
            BoardState board = CreateBoard();
            ListenerState listener = AddListener(board, 2, new GridPosition(3, 1));
            var shambler = new ShamblerState(listener.ActorId, new ShamblerDefinition(10));
            Assert.Throws<ArgumentException>(() => new MixedEnemyTurnPhases(new[] { shambler }, new[] { listener }));
            listener.LockIntent(new EnemyIntent(listener.ActorId, EnemyIntentKind.Wait));
            var batch = new MixedEnemyTurnPhases(Array.Empty<ShamblerState>(), new[] { listener });
            Assert.Throws<InvalidOperationException>(() => batch.PlanInitialIntents(board, new EntityId(1)));
            Assert.That(listener.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
        }

        private static RunState CreateRun() => new RunState("listener-turns", 48, new RunStateConfiguration(100, 10, 0, "forest.start"));

        private static ListenerState AddListener(BoardState board, long id, GridPosition position)
        {
            Assert.That(board.TryAdd(Actor(id, position)), Is.True);
            board.RegisterListener(new EntityId(id));
            return new ListenerState(new EntityId(id), new ListenerDefinition());
        }

        private static BoardEntityState Actor(long id, GridPosition position) => new BoardEntityState(new EntityId(id),
            new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "listener-test"), position);

        private static BoardState CreateBoard()
        {
            var board = new BoardState();
            for (int cellIndex = 0; cellIndex < 64; cellIndex++)
                Assert.That(board.TryAdd(new BoardEntityState(new EntityId(100 + cellIndex),
                    new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "ground", new BoardEntityTraits(true, false, false)),
                    new GridPosition(cellIndex / 8, cellIndex % 8))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(1),
                new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"), new GridPosition(1, 1))), Is.True);
            return board;
        }
    }
}