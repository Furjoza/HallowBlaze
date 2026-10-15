using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies fixed noise-only plans, deterministic paths and no-replan execution.</summary>
    public sealed class ListenerTests
    {
        private static readonly EntityId ListenerId = new EntityId(1);
        private readonly ListenerPlanner planner = new ListenerPlanner();

        /// <summary>A Listener waits without noise rather than knowing an adjacent player.</summary>
        [Test]
        public void NoNoiseMeansWaitWithoutPlayerPursuit()
        {
            BoardState board = CreateBoard();
            ListenerState listener = CreateListener(board);
            Assert.That(planner.Plan(board, listener).Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(listener.LockedIntent, Is.Null);
        }

        /// <summary>Equal paths choose North before East and preserve both destination and noise source.</summary>
        [Test]
        public void InvestigationAnnouncesStepAndHeardSource()
        {
            BoardState board = CreateBoard();
            ListenerState listener = CreateListener(board);
            BoardNoise noise = board.EmitNoise(new EntityId(8), new GridPosition(4, 4), 6, 5);
            EnemyIntent intent = planner.Plan(board, listener);
            Assert.That(intent.Kind, Is.EqualTo(EnemyIntentKind.Investigate));
            Assert.That(intent.TargetPosition, Is.EqualTo(new GridPosition(1, 2)));
            Assert.That(intent.InvestigationTarget, Is.EqualTo(noise.Position));
            Assert.That(intent.NoiseId, Is.EqualTo(noise.Id));
            Assert.That(intent.SourcePosition, Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(intent.TargetId, Is.Null);
            Assert.That(intent.AttackOnPlayerEntry, Is.False);
        }

        /// <summary>Arrived, expired, or inaccessible targets produce explicit target-free waits.</summary>
        [TestCase("arrived")]
        [TestCase("expired")]
        [TestCase("blocked")]
        public void UnavailableGoalWaits(string scenario)
        {
            BoardState board = CreateBoard();
            ListenerState listener = CreateListener(board);
            var target = scenario == "arrived" ? new GridPosition(1, 1) : new GridPosition(3, 1);
            board.EmitNoise(new EntityId(8), target, 5, 1);
            if (scenario == "expired") board.AdvanceNoiseLifetime();
            if (scenario == "blocked")
                Assert.That(board.TryAdd(Entity(900, BoardLayer.Obstacle, target)), Is.True);
            EnemyIntent intent = planner.Plan(board, listener);
            Assert.That(intent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(intent.TargetPosition, Is.Null);
        }

        /// <summary>Blocking or expiration consumes the announcement without finding another legal route.</summary>
        [TestCase("obstacle")]
        [TestCase("actor")]
        [TestCase("expired")]
        [TestCase("displaced")]
        public void InvalidatedInvestigationWaitsWithoutReplanning(string reason)
        {
            BoardState board = CreateBoard();
            ListenerState listener = CreateListener(board);
            board.EmitNoise(new EntityId(8), new GridPosition(4, 1), 4, 3);
            EnemyIntent intent = planner.Plan(board, listener);
            listener.LockIntent(intent);
            if (reason == "obstacle") Assert.That(board.TryAdd(Entity(900, BoardLayer.Obstacle, intent.TargetPosition.Value)), Is.True);
            if (reason == "actor") Assert.That(board.TryAdd(Entity(900, BoardLayer.Actor, intent.TargetPosition.Value)), Is.True);
            if (reason == "expired") for (int tick = 0; tick < 3; tick++) board.AdvanceNoiseLifetime();
            if (reason == "displaced") Assert.That(board.TryMove(ListenerId, new GridPosition(2, 2)), Is.True);
            var events = new List<GameEvent>();
            new ListenerIntentExecutor().Execute(board, listener, events);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.TypeOf<EntityWaitedEvent>());
            Assert.That(listener.LockedIntent, Is.Null);
            Assert.Throws<InvalidOperationException>(() => new ListenerIntentExecutor().Execute(board, listener, events));
        }

        /// <summary>New noise never retargets the current locked step, but controls the following plan.</summary>
        [Test]
        public void NewNoiseLuresOnlyAfterAnnouncedStepExecutes()
        {
            BoardState board = CreateBoard();
            ListenerState listener = CreateListener(board);
            board.EmitNoise(new EntityId(8), new GridPosition(4, 1), 6, 5);
            EnemyIntent announced = planner.Plan(board, listener);
            listener.LockIntent(announced);
            BoardNoise lure = board.EmitNoise(new EntityId(9), new GridPosition(1, 4), 6, 5);
            var events = new List<GameEvent>();
            new ListenerIntentExecutor().Execute(board, listener, events);
            Assert.That(((EntityMovedEvent)events[0]).To, Is.EqualTo(new GridPosition(2, 1)));
            EnemyIntent next = planner.Plan(board, listener);
            Assert.That(next.InvestigationTarget, Is.EqualTo(lure.Position));
            Assert.That(next.TargetPosition, Is.EqualTo(new GridPosition(2, 2)));
        }

        /// <summary>An occupied source is approached, not attacked or entered.</summary>
        [Test]
        public void OccupiedNoiseSourceDoesNotCreateHiddenAttack()
        {
            BoardState board = CreateBoard();
            ListenerState listener = CreateListener(board);
            Assert.That(board.TryAdd(Entity(900, BoardLayer.Actor, new GridPosition(3, 1))), Is.True);
            board.EmitNoise(new EntityId(900), new GridPosition(3, 1), 4, 5);
            listener.LockIntent(planner.Plan(board, listener));
            new ListenerIntentExecutor().Execute(board, listener, new List<GameEvent>());
            Assert.That(planner.Plan(board, listener).Kind, Is.EqualTo(EnemyIntentKind.Wait));
        }

        /// <summary>Malformed investigation fields are rejected rather than becoming hidden movement or attacks.</summary>
        [Test]
        public void InvestigationPayloadAndLockAreValidated()
        {
            Assert.Throws<ArgumentException>(() => new EnemyIntent(ListenerId, EnemyIntentKind.Investigate));
            Assert.Throws<ArgumentException>(() => new EnemyIntent(ListenerId, EnemyIntentKind.Wait, noiseId: 1));
            var listener = new ListenerState(ListenerId, new ListenerDefinition());
            Assert.Throws<ArgumentException>(() => listener.LockIntent(new EnemyIntent(new EntityId(9), EnemyIntentKind.Wait)));
            listener.LockIntent(new EnemyIntent(ListenerId, EnemyIntentKind.Wait));
            Assert.Throws<InvalidOperationException>(() => listener.LockIntent(new EnemyIntent(ListenerId, EnemyIntentKind.Wait)));
        }

        private static ListenerState CreateListener(BoardState board)
        {
            board.RegisterListener(ListenerId);
            return new ListenerState(ListenerId, new ListenerDefinition());
        }

        private static BoardState CreateBoard()
        {
            var board = new BoardState();
            for (int coordinate = 0; coordinate < 64; coordinate++)
                Assert.That(board.TryAdd(Entity(100 + coordinate, BoardLayer.Terrain,
                    new GridPosition(coordinate / 8, coordinate % 8), true)), Is.True);
            Assert.That(board.TryAdd(Entity(1, BoardLayer.Actor, new GridPosition(1, 1))), Is.True);
            return board;
        }

        private static BoardEntityState Entity(long id, BoardLayer layer, GridPosition position, bool walkable = false) =>
            new BoardEntityState(new EntityId(id), new BoardEntityDefinition(layer,
                layer == BoardLayer.Actor ? EntityKind.Enemy : new EntityKind(layer.ToString().ToLowerInvariant()),
                "listener-test", new BoardEntityTraits(walkable, false, false)), position);
    }
}