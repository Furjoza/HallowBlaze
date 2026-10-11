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
    /// <summary>Verifies stable board-local enemy batching without production composition.</summary>
    public class EnemyBatchTests
    {
        /// <summary>
        /// Reciprocal locked moves resolve as two waits under both initiative assignments and input orders.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Swap_IsBlockedForBothInitiativeOrders(bool swapIdentities, bool reverseInput)
        {
            var playerId = new EntityId(-100);
            var leftPosition = new GridPosition(2, 3);
            var rightPosition = new GridPosition(3, 3);
            var leftId = new EntityId(swapIdentities ? 0 : -5);
            var rightId = new EntityId(swapIdentities ? -5 : 0);
            BoardState board = CreateBoard(playerId, new GridPosition(7, 7));
            Add(board, leftId.Value, BoardLayer.Actor, EntityKind.Enemy, leftPosition);
            Add(board, rightId.Value, BoardLayer.Actor, EntityKind.Enemy, rightPosition);
            RunState run = CreateRun();
            var left = new ShamblerState(leftId, new ShamblerDefinition(10));
            var right = new ShamblerState(rightId, new ShamblerDefinition(20));
            var leftIntent = new EnemyIntent(leftId, EnemyIntentKind.Move, rightPosition, playerId, true);
            var rightIntent = new EnemyIntent(rightId, EnemyIntentKind.Move, leftPosition, playerId, true);
            left.LockIntent(leftIntent);
            right.LockIntent(rightIntent);
            ShamblerState[] input = reverseInput ? new[] { right, left } : new[] { left, right };
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            new EnemyBatchExecutor().Execute(board, run, input, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(board.TryGetEntity(leftId, out BoardEntityState leftActor), Is.True);
            Assert.That(board.TryGetEntity(rightId, out BoardEntityState rightActor), Is.True);
            Assert.That(leftActor.Position, Is.EqualTo(leftPosition));
            Assert.That(rightActor.Position, Is.EqualTo(rightPosition));
            Assert.That(board.TryGetEntity(BoardLayer.Actor, leftPosition, out BoardEntityState leftOccupant), Is.True);
            Assert.That(board.TryGetEntity(BoardLayer.Actor, rightPosition, out BoardEntityState rightOccupant), Is.True);
            Assert.That(leftOccupant, Is.SameAs(leftActor));
            Assert.That(rightOccupant, Is.SameAs(rightActor));
            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events.All(gameEvent => gameEvent is EntityWaitedEvent), Is.True);
            Assert.That(events.Cast<EntityWaitedEvent>().Select(gameEvent => gameEvent.EntityId.Value),
                Is.EqualTo(new long[] { -5, 0 }));
            Assert.That(left.LockedIntent, Is.Null);
            Assert.That(right.LockedIntent, Is.Null);
            Assert.That(left.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(right.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(leftIntent.TargetPosition, Is.EqualTo(rightPosition));
            Assert.That(rightIntent.TargetPosition, Is.EqualTo(leftPosition));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.Food, Is.EqualTo(25));
            CollectionAssert.AreEqual(reverseInput ? new[] { right, left } : new[] { left, right }, input);
        }

        /// <summary>
        /// A initially empty target has one first legal winner for 2-4 contenders and an unrelated mover.
        /// An invalid earlier candidate cannot reserve it, and a later phase can contest it anew.
        /// </summary>
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        public void ContestedDestination_FirstLegalMoveWins(int contenderCount, bool firstInvalid)
        {
            var playerId = new EntityId(-100);
            var destination = new GridPosition(3, 3);
            long[] identities = { -5, 0, 7, 9 };
            GridPosition[] sources = { new GridPosition(2, 3), new GridPosition(4, 3),
                new GridPosition(3, 2), new GridPosition(3, 4) };
            if (firstInvalid)
                sources[0] = new GridPosition(0, 0);
            ShamblerState[] templates = identities.Take(contenderCount).Concat(new long[] { 42 }).Select(id =>
                new ShamblerState(new EntityId(id), new ShamblerDefinition(10))).ToArray();
            var executor = new EnemyBatchExecutor();
            int winnerIndex = firstInvalid ? 1 : 0;
            var winnerId = new EntityId(identities[winnerIndex]);

            foreach (ShamblerState[] permutation in Permutations(templates))
            {
                BoardState board = CreateBoard(playerId, new GridPosition(7, 7));
                RunState run = CreateRun();
                ShamblerState[] enemies = permutation.Select(enemy => new ShamblerState(enemy.ActorId, enemy.Definition)).ToArray();
                for (int index = 0; index < contenderCount; index++)
                    Add(board, identities[index], BoardLayer.Actor, EntityKind.Enemy, sources[index]);
                Add(board, 42, BoardLayer.Actor, EntityKind.Enemy, new GridPosition(6, 5));
                int count = board.Count;

                for (int round = 0; round < 2; round++)
                {
                    Assert.That(board.TryGetEntity(BoardLayer.Actor, destination, out _), Is.False);
                    foreach (ShamblerState enemy in enemies)
                        enemy.LockIntent(new EnemyIntent(enemy.ActorId, EnemyIntentKind.Move,
                            enemy.ActorId.Value == 42 ? new GridPosition(6, 6 + round) : destination, playerId, true));
                    var retained = enemies.ToDictionary(enemy => enemy.ActorId, enemy => enemy.LockedIntent);
                    var events = new List<GameEvent>();

                    executor.Execute(board, run, enemies, events);

                    Assert.That(board.Count, Is.EqualTo(count));
                    Assert.That(board.TryGetEntity(BoardLayer.Actor, destination, out BoardEntityState winner), Is.True);
                    Assert.That(winner.Id, Is.EqualTo(winnerId));
                    Assert.That(events.Count, Is.EqualTo(contenderCount + 1));
                    for (int index = 0; index < contenderCount; index++)
                    {
                        var actorId = new EntityId(identities[index]);
                        ShamblerState enemy = enemies.Single(state => state.ActorId.Equals(actorId));
                        Assert.That(board.TryGetEntity(actorId, out BoardEntityState actor), Is.True);
                        Assert.That(actor.Position, Is.EqualTo(index == winnerIndex ? destination : sources[index]));
                        Assert.That(board.TryGetEntity(BoardLayer.Actor, actor.Position, out BoardEntityState occupant), Is.True);
                        Assert.That(occupant, Is.SameAs(actor));
                        if (index == winnerIndex)
                        {
                            Assert.That(events[index], Is.TypeOf<EntityMovedEvent>());
                            var movement = (EntityMovedEvent)events[index];
                            Assert.That(movement.EntityId, Is.EqualTo(actorId));
                            Assert.That(movement.From, Is.EqualTo(sources[index]));
                            Assert.That(movement.To, Is.EqualTo(destination));
                            Assert.That(board.TryGetEntity(BoardLayer.Actor, sources[index], out _), Is.False);
                        }
                        else
                        {
                            Assert.That(events[index], Is.TypeOf<EntityWaitedEvent>());
                            Assert.That(((EntityWaitedEvent)events[index]).EntityId, Is.EqualTo(actorId));
                        }
                        Assert.That(enemy.LockedIntent, Is.Null);
                        Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
                        Assert.That(retained[actorId].Kind, Is.EqualTo(EnemyIntentKind.Move));
                        Assert.That(retained[actorId].TargetPosition, Is.EqualTo(destination));
                    }
                    Assert.That(events[contenderCount], Is.TypeOf<EntityMovedEvent>());
                    var unrelatedMove = (EntityMovedEvent)events[contenderCount];
                    Assert.That(unrelatedMove.EntityId, Is.EqualTo(new EntityId(42)));
                    Assert.That(unrelatedMove.From, Is.EqualTo(new GridPosition(6, 5 + round)));
                    Assert.That(unrelatedMove.To, Is.EqualTo(new GridPosition(6, 6 + round)));
                    Assert.That(enemies.Single(enemy => enemy.ActorId.Value == 42).Phase, Is.EqualTo(ShamblerPhase.Rest));
                    Assert.That(run.Food, Is.EqualTo(25));
                    Assert.That(run.Health, Is.EqualTo(100));

                    if (round == 0)
                    {
                        foreach (ShamblerState enemy in enemies)
                            enemy.LockIntent(new EnemyIntent(enemy.ActorId, EnemyIntentKind.Wait));
                        events.Clear();
                        executor.Execute(board, run, enemies, events);
                        Assert.That(events.Count, Is.EqualTo(enemies.Length));
                        Assert.That(events.All(gameEvent => gameEvent is EntityWaitedEvent), Is.True);
                        Assert.That(enemies.All(enemy => enemy.Phase == ShamblerPhase.Active && enemy.LockedIntent == null), Is.True);
                        Assert.That(board.TryMove(winnerId, sources[winnerIndex]), Is.True);
                    }
                }
            }
        }

        /// <summary>
        /// Disjoint movements and planned rests execute once in initiative order for all input permutations.
        /// Malformed retention is rejected before the first actor's position or cadence changes.
        /// </summary>
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        public void BatchExecution_NonconflictingIntentsExecuteOnce(int actorCount)
        {
            var playerId = new EntityId(-100);
            long[] identities = { -3, 0, 7, 9, 11 };
            GridPosition[] sources = { new GridPosition(1, 1), new GridPosition(3, 1), new GridPosition(5, 1),
                new GridPosition(1, 4), new GridPosition(3, 4) };
            ShamblerState[] templates = identities.Take(actorCount).Select(id =>
                new ShamblerState(new EntityId(id), new ShamblerDefinition(10))).ToArray();
            var executor = new EnemyBatchExecutor();
            foreach (ShamblerState[] permutation in Permutations(templates))
            {
                BoardState board = CreateBoard(playerId, new GridPosition(7, 7));
                RunState run = CreateRun();
                ShamblerState[] enemies = permutation.Select(enemy => new ShamblerState(enemy.ActorId, enemy.Definition)).ToArray();
                for (int index = 0; index < actorCount; index++)
                {
                    Add(board, identities[index], BoardLayer.Actor, EntityKind.Enemy, sources[index]);
                    ShamblerState enemy = enemies.Single(state => state.ActorId.Value == identities[index]);
                    if (index % 2 == 0)
                        enemy.LockIntent(new EnemyIntent(enemy.ActorId, EnemyIntentKind.Move,
                            sources[index].Move(Direction.North), playerId, true));
                    else
                    {
                        enemy.LockIntent(new EnemyIntent(enemy.ActorId, EnemyIntentKind.Wait));
                        enemy.ConsumeIntent();
                        enemy.LockIntent(new EnemyIntent(enemy.ActorId, EnemyIntentKind.Wait));
                    }
                }
                var prefix = new EntityWaitedEvent(playerId);
                var events = new List<GameEvent> { prefix };
                int count = board.Count;

                executor.Execute(board, run, enemies, events);

                Assert.That(board.Count, Is.EqualTo(count));
                Assert.That(events.Count, Is.EqualTo(actorCount + 1));
                Assert.That(events[0], Is.SameAs(prefix));
                for (int index = 0; index < actorCount; index++)
                {
                    var actorId = new EntityId(identities[index]);
                    ShamblerState enemy = enemies.Single(state => state.ActorId.Equals(actorId));
                    GridPosition destination = index % 2 == 0 ? sources[index].Move(Direction.North) : sources[index];
                    Assert.That(board.TryGetEntity(actorId, out BoardEntityState actor), Is.True);
                    Assert.That(actor.Position, Is.EqualTo(destination));
                    Assert.That(board.TryGetEntity(BoardLayer.Actor, destination, out BoardEntityState occupant), Is.True);
                    Assert.That(occupant, Is.SameAs(actor));
                    if (index % 2 == 0)
                    {
                        Assert.That(board.TryGetEntity(BoardLayer.Actor, sources[index], out _), Is.False);
                        Assert.That(events[index + 1], Is.TypeOf<EntityMovedEvent>());
                        var movement = (EntityMovedEvent)events[index + 1];
                        Assert.That(movement.EntityId, Is.EqualTo(actorId));
                        Assert.That(movement.From, Is.EqualTo(sources[index]));
                        Assert.That(movement.To, Is.EqualTo(destination));
                    }
                    else
                    {
                        Assert.That(events[index + 1], Is.TypeOf<EntityWaitedEvent>());
                        Assert.That(((EntityWaitedEvent)events[index + 1]).EntityId, Is.EqualTo(actorId));
                    }
                    Assert.That(enemy.LockedIntent, Is.Null);
                    Assert.That(enemy.Phase, Is.EqualTo(index % 2 == 0 ? ShamblerPhase.Rest : ShamblerPhase.Active));
                }
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(25));
                var after = board.GetEntities();
                Assert.Throws<InvalidOperationException>(() => executor.Execute(board, run, enemies, events));
                CollectionAssert.AreEqual(after, board.GetEntities());
                Assert.That(events.Count, Is.EqualTo(actorCount + 1));
            }

            BoardState invalidBoard = CreateBoard(playerId, new GridPosition(7, 7));
            Add(invalidBoard, -3, BoardLayer.Actor, EntityKind.Enemy, sources[0]);
            templates[0].LockIntent(new EnemyIntent(templates[0].ActorId, EnemyIntentKind.Move,
                sources[0].Move(Direction.North), playerId, true));
            EnemyIntent retained = templates[0].LockedIntent;
            var before = invalidBoard.GetEntities();
            var invalidEvents = new List<GameEvent>();
            Assert.Throws<InvalidOperationException>(() => executor.Execute(invalidBoard, CreateRun(), templates, invalidEvents));
            CollectionAssert.AreEqual(before, invalidBoard.GetEntities());
            Assert.That(templates[0].LockedIntent, Is.SameAs(retained));
            Assert.That(templates[0].Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(invalidEvents, Is.Empty);
        }

        /// <summary>
        /// A shared destination remains shared through all 24 input orders; rest and attack payloads
        /// also read the same board, while existing intent locks cannot be partially overwritten.
        /// </summary>
        [Test]
        public void BatchPlanning_UsesOneBoardState()
        {
            var playerId = new EntityId(-1);
            ShamblerState[] templates = new long[] { -5, 0, 42, 99 }.Select(id =>
                new ShamblerState(new EntityId(id), new ShamblerDefinition(10))).ToArray();
            var planner = new EnemyBatchPlanner();
            int permutations = 0;
            foreach (ShamblerState[] permutation in Permutations(templates))
            {
                BoardState board = CreateBoard(playerId, new GridPosition(3, 5));
                Add(board, -5, BoardLayer.Actor, EntityKind.Enemy, new GridPosition(2, 3));
                Add(board, 0, BoardLayer.Actor, EntityKind.Enemy, new GridPosition(4, 3));
                Add(board, 42, BoardLayer.Actor, EntityKind.Enemy, new GridPosition(7, 7));
                Add(board, 99, BoardLayer.Actor, EntityKind.Enemy, new GridPosition(3, 6));
                Add(board, 1000, BoardLayer.Obstacle, EntityKind.Obstacle, new GridPosition(2, 4));
                Add(board, 1001, BoardLayer.Obstacle, EntityKind.Obstacle, new GridPosition(4, 4));
                ShamblerState[] enemies = permutation.Select(enemy =>
                    new ShamblerState(enemy.ActorId, enemy.Definition)).ToArray();
                ShamblerState resting = enemies.Single(enemy => enemy.ActorId.Value == 42);
                resting.LockIntent(new EnemyIntent(resting.ActorId, EnemyIntentKind.Wait));
                resting.ConsumeIntent();
                var before = board.GetEntities();
                var beforePhases = enemies.Select(enemy => enemy.Phase).ToArray();
                RunState run = CreateRun();

                IReadOnlyList<EnemyIntent> plans = planner.Plan(board, enemies, playerId);

                Assert.That(plans.Select(intent => intent.ActorId.Value), Is.EqualTo(new long[] { -5, 0, 42, 99 }));
                for (int index = 0; index < 2; index++)
                {
                    Assert.That(plans[index].Kind, Is.EqualTo(EnemyIntentKind.Move));
                    Assert.That(plans[index].TargetPosition, Is.EqualTo(new GridPosition(3, 3)));
                    Assert.That(plans[index].TargetId, Is.EqualTo(playerId));
                    Assert.That(plans[index].AttackOnPlayerEntry, Is.True);
                }
                Assert.That(plans[2].Kind, Is.EqualTo(EnemyIntentKind.Wait));
                Assert.That(plans[2].TargetPosition, Is.Null);
                Assert.That(plans[2].TargetId, Is.Null);
                Assert.That(plans[3].Kind, Is.EqualTo(EnemyIntentKind.Attack));
                Assert.That(plans[3].TargetPosition, Is.EqualTo(new GridPosition(3, 5)));
                Assert.That(plans[3].TargetId, Is.EqualTo(playerId));
                Assert.That(plans[3].AttackOnPlayerEntry, Is.False);
                foreach (ShamblerState enemy in enemies)
                    Assert.That(enemy.LockedIntent, Is.SameAs(plans.Single(intent => intent.ActorId.Equals(enemy.ActorId))));
                CollectionAssert.AreEqual(before, board.GetEntities());
                Assert.That(enemies.Select(enemy => enemy.Phase), Is.EqualTo(beforePhases));
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(25));
                Assert.Throws<InvalidOperationException>(() => planner.Plan(board, enemies, playerId));
                foreach (ShamblerState enemy in enemies)
                    Assert.That(enemy.LockedIntent, Is.SameAs(plans.Single(intent => intent.ActorId.Equals(enemy.ActorId))));
                permutations++;
            }
            Assert.That(permutations, Is.EqualTo(24));

            var unlocked = new ShamblerState(new EntityId(-5), new ShamblerDefinition(10));
            var locked = new ShamblerState(new EntityId(0), new ShamblerDefinition(10));
            var retained = new EnemyIntent(locked.ActorId, EnemyIntentKind.Wait);
            locked.LockIntent(retained);
            Assert.Throws<InvalidOperationException>(() => planner.Plan(CreateBoard(playerId, new GridPosition(3, 5)),
                new[] { unlocked, locked }, playerId));
            Assert.That(unlocked.LockedIntent, Is.Null);
            Assert.That(locked.LockedIntent, Is.SameAs(retained));
            Assert.Throws<ArgumentNullException>(() => planner.Plan(null, templates, playerId));
        }

        /// <summary>
        /// All 120 input permutations preserve signed ordering, caller collections, and enemy state;
        /// malformed batches are rejected before any enemy operation and empty input remains valid.
        /// </summary>
        [Test]
        public void InitiativeOrder_IsStableAndDetached()
        {
            long[] expectedIds = { long.MinValue, -1, 0, 42, long.MaxValue };
            ShamblerState[] enemies = expectedIds.Select(id => new ShamblerState(new EntityId(id),
                new ShamblerDefinition(10))).ToArray();
            enemies[2].LockIntent(new EnemyIntent(enemies[2].ActorId, EnemyIntentKind.Wait));
            enemies[2].ConsumeIntent();
            foreach (ShamblerState enemy in enemies)
                enemy.LockIntent(new EnemyIntent(enemy.ActorId, EnemyIntentKind.Wait));
            var phases = enemies.Select(enemy => enemy.Phase).ToArray();
            var intents = enemies.Select(enemy => enemy.LockedIntent).ToArray();
            int permutations = 0;

            foreach (ShamblerState[] permutation in Permutations(enemies))
            {
                var input = permutation.ToList();
                IReadOnlyList<ShamblerState> ordered = EnemyInitiativeOrder.Create(input);

                Assert.That(ordered.Select(enemy => enemy.ActorId.Value), Is.EqualTo(expectedIds));
                CollectionAssert.AreEqual(permutation, input);
                CollectionAssert.AreEqual(enemies, ordered);
                input.Clear();
                Assert.That(ordered.Count, Is.EqualTo(5));
                Assert.Throws<NotSupportedException>(() => ((IList<ShamblerState>)ordered).Add(null));
                Assert.That(enemies.Select(enemy => enemy.Phase), Is.EqualTo(phases));
                CollectionAssert.AreEqual(intents, enemies.Select(enemy => enemy.LockedIntent));
                permutations++;
            }

            Assert.That(permutations, Is.EqualTo(120));
            Assert.That(EnemyInitiativeOrder.Create(Array.Empty<ShamblerState>()), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => EnemyInitiativeOrder.Create(null));
            Assert.Throws<ArgumentException>(() => EnemyInitiativeOrder.Create(new[] { enemies[0], null }));
            Assert.Throws<ArgumentException>(() => EnemyInitiativeOrder.Create(new[] { enemies[0], enemies[0] }));
            Assert.Throws<ArgumentException>(() => EnemyInitiativeOrder.Create(new[] { enemies[0],
                new ShamblerState(enemies[0].ActorId, new ShamblerDefinition(20)) }));
            Assert.That(enemies.Select(enemy => enemy.Phase), Is.EqualTo(phases));
            CollectionAssert.AreEqual(intents, enemies.Select(enemy => enemy.LockedIntent));
        }

        private static BoardState CreateBoard(EntityId playerId, GridPosition playerPosition)
        {
            var board = new BoardState();
            long terrainId = 100;
            for (int column = board.Bounds.MinX; column <= board.Bounds.MaxX; column++)
                for (int row = board.Bounds.MinY; row <= board.Bounds.MaxY; row++)
                    Add(board, terrainId++, BoardLayer.Terrain, new EntityKind("floor"),
                        new GridPosition(column, row), true);
            Add(board, playerId.Value, BoardLayer.Actor, EntityKind.Player, playerPosition);
            return board;
        }

        private static RunState CreateRun() =>
            new RunState("batch-test", 42, new RunStateConfiguration(100, 25, 1, "start"));

        private static void Add(BoardState board, long id, BoardLayer layer, EntityKind kind,
            GridPosition position, bool isWalkable = false)
        {
            var definition = new BoardEntityDefinition(layer, kind, $"{kind.Id}-{id}",
                new BoardEntityTraits(isWalkable, false, false));
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(id), definition, position)), Is.True);
        }

        private static IEnumerable<ShamblerState[]> Permutations(ShamblerState[] values)
        {
            if (values.Length == 0)
            {
                yield return Array.Empty<ShamblerState>();
                yield break;
            }
            for (int index = 0; index < values.Length; index++)
            {
                ShamblerState selected = values[index];
                ShamblerState[] remaining = values.Where((enemy, position) => position != index).ToArray();
                foreach (ShamblerState[] tail in Permutations(remaining))
                    yield return new[] { selected }.Concat(tail).ToArray();
            }
        }
    }
}