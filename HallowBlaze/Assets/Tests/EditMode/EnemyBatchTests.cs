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
        /// All permutations of four explicit 2-5 actor fixtures preserve authoritative state and
        /// ordered typed event payloads for contested cells, swaps, live chains, and removed sources.
        /// </summary>
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void InputPermutations_PreserveOutcomesAndOrderedEvents(int actorCount)
        {
            var playerId = new EntityId(-100);
            var playerPosition = new GridPosition(5, 5);
            EntityId[] identities = { new EntityId(-5), new EntityId(0), new EntityId(7),
                new EntityId(9), new EntityId(11) };
            GridPosition[] sources;
            GridPosition?[] targets;
            GridPosition?[] expectedPositions;
            EnemyIntentKind[] kinds;
            ShamblerPhase[] expectedPhases;
            GameEvent[] expectedEvents;
            int expectedHealth = 100;
            int expectedPermutations;
            switch (actorCount)
            {
                case 2:
                    sources = new[] { new GridPosition(2, 3), new GridPosition(4, 3) };
                    targets = new GridPosition?[] { new GridPosition(3, 3), new GridPosition(3, 3) };
                    expectedPositions = new GridPosition?[] { new GridPosition(3, 3), new GridPosition(4, 3) };
                    kinds = new[] { EnemyIntentKind.Move, EnemyIntentKind.Move };
                    expectedPhases = new[] { ShamblerPhase.Rest, ShamblerPhase.Rest };
                    expectedEvents = new GameEvent[] {
                        new EntityMovedEvent(identities[0], new GridPosition(2, 3), new GridPosition(3, 3)),
                        new EntityWaitedEvent(identities[1]) };
                    expectedPermutations = 2;
                    break;
                case 3:
                    sources = new[] { new GridPosition(2, 3), new GridPosition(3, 3), new GridPosition(6, 5) };
                    targets = new GridPosition?[] { new GridPosition(3, 3), new GridPosition(2, 3), new GridPosition(6, 6) };
                    expectedPositions = new GridPosition?[] { new GridPosition(2, 3), new GridPosition(3, 3), new GridPosition(6, 6) };
                    kinds = new[] { EnemyIntentKind.Move, EnemyIntentKind.Move, EnemyIntentKind.Move };
                    expectedPhases = new[] { ShamblerPhase.Rest, ShamblerPhase.Rest, ShamblerPhase.Rest };
                    expectedEvents = new GameEvent[] { new EntityWaitedEvent(identities[0]),
                        new EntityWaitedEvent(identities[1]),
                        new EntityMovedEvent(identities[2], new GridPosition(6, 5), new GridPosition(6, 6)) };
                    expectedPermutations = 6;
                    break;
                case 4:
                    sources = new[] { new GridPosition(3, 1), new GridPosition(2, 1),
                        new GridPosition(1, 1), new GridPosition(6, 5) };
                    targets = new GridPosition?[] { new GridPosition(4, 1), new GridPosition(3, 1),
                        new GridPosition(2, 1), null };
                    expectedPositions = new GridPosition?[] { new GridPosition(4, 1), new GridPosition(3, 1),
                        new GridPosition(2, 1), new GridPosition(6, 5) };
                    kinds = new[] { EnemyIntentKind.Move, EnemyIntentKind.Move, EnemyIntentKind.Move, EnemyIntentKind.Wait };
                    expectedPhases = new[] { ShamblerPhase.Rest, ShamblerPhase.Rest, ShamblerPhase.Rest, ShamblerPhase.Active };
                    expectedEvents = new GameEvent[] {
                        new EntityMovedEvent(identities[0], new GridPosition(3, 1), new GridPosition(4, 1)),
                        new EntityMovedEvent(identities[1], new GridPosition(2, 1), new GridPosition(3, 1)),
                        new EntityMovedEvent(identities[2], new GridPosition(1, 1), new GridPosition(2, 1)),
                        new EntityWaitedEvent(identities[3]) };
                    expectedPermutations = 24;
                    break;
                case 5:
                    sources = new[] { new GridPosition(2, 3), new GridPosition(4, 3), new GridPosition(3, 2),
                        new GridPosition(5, 6), new GridPosition(6, 5) };
                    targets = new GridPosition?[] { new GridPosition(3, 3), new GridPosition(3, 3),
                        new GridPosition(3, 3), playerPosition, null };
                    expectedPositions = new GridPosition?[] { null, new GridPosition(3, 3), new GridPosition(3, 2),
                        new GridPosition(5, 6), new GridPosition(6, 5) };
                    kinds = new[] { EnemyIntentKind.Move, EnemyIntentKind.Move, EnemyIntentKind.Move,
                        EnemyIntentKind.Attack, EnemyIntentKind.Wait };
                    expectedPhases = new[] { ShamblerPhase.Rest, ShamblerPhase.Rest, ShamblerPhase.Rest,
                        ShamblerPhase.Rest, ShamblerPhase.Active };
                    expectedEvents = new GameEvent[] {
                        new EntityMovedEvent(identities[1], new GridPosition(4, 3), new GridPosition(3, 3)),
                        new EntityWaitedEvent(identities[2]),
                        new EnemyAttackResolvedEvent(identities[3], playerPosition, true, playerId, -20),
                        new EntityWaitedEvent(identities[4]) };
                    expectedHealth = 80;
                    expectedPermutations = 120;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(actorCount));
            }
            ShamblerState[] templates = identities.Take(actorCount).Select(id =>
                new ShamblerState(id, new ShamblerDefinition(20))).ToArray();
            var executor = new EnemyBatchExecutor();
            int permutationCount = 0;

            foreach (ShamblerState[] permutation in Permutations(templates))
            {
                string context = $"Actors={actorCount}; IDs=[{string.Join(",", permutation.Select(enemy => enemy.ActorId.Value))}]";
                TestContext.WriteLine(context);
                BoardState board = CreateBoard(playerId, playerPosition);
                RunState run = CreateRun();
                ShamblerState[] enemies = permutation.Select(enemy =>
                    new ShamblerState(enemy.ActorId, enemy.Definition)).ToArray();
                ShamblerState[] inputBefore = enemies.ToArray();
                for (int index = 0; index < actorCount; index++)
                {
                    EntityId actorId = identities[index];
                    Add(board, actorId.Value, BoardLayer.Actor, EntityKind.Enemy, sources[index]);
                    ShamblerState enemy = enemies.Single(state => state.ActorId.Equals(actorId));
                    if (actorCount >= 4 && index == actorCount - 1)
                    {
                        enemy.LockIntent(new EnemyIntent(actorId, EnemyIntentKind.Wait));
                        enemy.ConsumeIntent();
                    }
                    enemy.LockIntent(kinds[index] == EnemyIntentKind.Wait
                        ? new EnemyIntent(actorId, EnemyIntentKind.Wait)
                        : new EnemyIntent(actorId, kinds[index], targets[index].Value, playerId,
                            kinds[index] == EnemyIntentKind.Move));
                }
                var retained = enemies.ToDictionary(enemy => enemy.ActorId, enemy => enemy.LockedIntent);
                if (actorCount == 5)
                    Assert.That(board.TryRemove(identities[0]), Is.True, context);
                var unchanged = board.GetEntities().Where(entity =>
                    entity.Definition.Layer != BoardLayer.Actor || entity.Id.Equals(playerId)).ToArray();
                int count = board.Count;
                var events = new List<GameEvent>();

                Assert.DoesNotThrow(() => executor.Execute(board, run, enemies, events), context);

                Assert.That(board.Count, Is.EqualTo(count), context);
                CollectionAssert.AreEqual(unchanged, board.GetEntities().Where(entity =>
                    entity.Definition.Layer != BoardLayer.Actor || entity.Id.Equals(playerId)).ToArray(), context);
                for (int index = 0; index < actorCount; index++)
                {
                    EntityId actorId = identities[index];
                    Assert.That(board.TryGetEntity(actorId, out BoardEntityState actor),
                        Is.EqualTo(expectedPositions[index].HasValue), context);
                    if (expectedPositions[index].HasValue)
                    {
                        Assert.That(actor.Position, Is.EqualTo(expectedPositions[index].Value), context);
                        Assert.That(board.TryGetEntity(BoardLayer.Actor, actor.Position,
                            out BoardEntityState occupant), Is.True, context);
                        Assert.That(occupant, Is.SameAs(actor), context);
                    }
                    ShamblerState enemy = enemies.Single(state => state.ActorId.Equals(actorId));
                    Assert.That(enemy.LockedIntent, Is.Null, context);
                    Assert.That(enemy.Phase, Is.EqualTo(expectedPhases[index]), context);
                    Assert.That(retained[actorId].Kind, Is.EqualTo(kinds[index]), context);
                    Assert.That(retained[actorId].TargetPosition, Is.EqualTo(targets[index]), context);
                }
                foreach (GridPosition cell in sources.Concat(targets.Where(target => target.HasValue)
                    .Select(target => target.Value)).Distinct())
                {
                    int index = Array.IndexOf(expectedPositions, (GridPosition?)cell);
                    bool occupied = board.TryGetEntity(BoardLayer.Actor, cell, out BoardEntityState occupant);
                    Assert.That(occupied, Is.EqualTo(index >= 0 || cell.Equals(playerPosition)), context);
                    if (occupied)
                        Assert.That(occupant.Id, Is.EqualTo(cell.Equals(playerPosition) ? playerId : identities[index]), context);
                }
                Assert.That(events.Count, Is.EqualTo(expectedEvents.Length), context);
                for (int index = 0; index < expectedEvents.Length; index++)
                {
                    GameEvent expected = expectedEvents[index];
                    Assert.That(events[index].GetType(), Is.EqualTo(expected.GetType()), context);
                    if (expected is EntityMovedEvent expectedMove)
                    {
                        var actual = (EntityMovedEvent)events[index];
                        Assert.That(actual.EntityId, Is.EqualTo(expectedMove.EntityId), context);
                        Assert.That(actual.From, Is.EqualTo(expectedMove.From), context);
                        Assert.That(actual.To, Is.EqualTo(expectedMove.To), context);
                    }
                    else if (expected is EntityWaitedEvent expectedWait)
                        Assert.That(((EntityWaitedEvent)events[index]).EntityId, Is.EqualTo(expectedWait.EntityId), context);
                    else
                    {
                        var expectedAttack = (EnemyAttackResolvedEvent)expected;
                        var actual = (EnemyAttackResolvedEvent)events[index];
                        Assert.That(actual.AttackerId, Is.EqualTo(expectedAttack.AttackerId), context);
                        Assert.That(actual.TargetPosition, Is.EqualTo(expectedAttack.TargetPosition), context);
                        Assert.That(actual.IsHit, Is.EqualTo(expectedAttack.IsHit), context);
                        Assert.That(actual.AffectedTargetId, Is.EqualTo(expectedAttack.AffectedTargetId), context);
                        Assert.That(actual.HealthChange, Is.EqualTo(expectedAttack.HealthChange), context);
                    }
                }
                Assert.That(run.Health, Is.EqualTo(expectedHealth), context);
                Assert.That(run.Food, Is.EqualTo(25), context);
                Assert.That(run.Status, Is.EqualTo(RunStatus.Active), context);
                CollectionAssert.AreEqual(inputBefore, enemies, context);
                permutationCount++;
            }
            Assert.That(permutationCount, Is.EqualTo(expectedPermutations), $"Actors={actorCount}");
        }

        /// <summary>
        /// Batch dispatch preserves fixed-cell hits, misses, conditional hits, enemy blockers,
        /// and rest for both damage variants without reserving attack cells or adding costs.
        /// </summary>
        [TestCase(10, "hit")]
        [TestCase(20, "hit")]
        [TestCase(10, "miss")]
        [TestCase(20, "miss")]
        [TestCase(10, "conditional-hit")]
        [TestCase(20, "conditional-hit")]
        [TestCase(10, "enemy-blocked")]
        [TestCase(20, "enemy-blocked")]
        [TestCase(10, "rest")]
        [TestCase(20, "rest")]
        public void BatchAttacks_PreserveLockedOutcomes(int damage, string outcome)
        {
            var playerId = new EntityId(-100);
            var target = new GridPosition(3, 3);
            var escapedPosition = new GridPosition(4, 3);
            long[] identities = { -5, 0, 7 };
            GridPosition[] attackSources = { new GridPosition(2, 3), new GridPosition(3, 2) };
            bool isMiss = outcome == "miss";
            bool isHit = outcome == "hit" || outcome == "conditional-hit";
            bool isRest = outcome == "rest";
            bool isConditional = outcome == "conditional-hit" || outcome == "enemy-blocked";
            GridPosition moverSource = isMiss ? new GridPosition(3, 4) : new GridPosition(6, 5);
            GridPosition moverTarget = isMiss ? target : new GridPosition(6, 6);
            ShamblerState[] templates = identities.Select(id =>
                new ShamblerState(new EntityId(id), new ShamblerDefinition(damage))).ToArray();
            var executor = new EnemyBatchExecutor();

            foreach (ShamblerState[] permutation in Permutations(templates))
            {
                BoardState board = CreateBoard(playerId, isConditional ? escapedPosition : target);
                RunState run = CreateRun();
                ShamblerState[] enemies = permutation.Select(enemy =>
                    new ShamblerState(enemy.ActorId, enemy.Definition)).ToArray();
                ShamblerState[] inputBefore = enemies.ToArray();
                for (int index = 0; index < attackSources.Length; index++)
                {
                    var actorId = new EntityId(identities[index]);
                    Add(board, actorId.Value, BoardLayer.Actor, EntityKind.Enemy, attackSources[index]);
                    ShamblerState enemy = enemies.Single(state => state.ActorId.Equals(actorId));
                    if (isRest)
                    {
                        enemy.LockIntent(new EnemyIntent(actorId, EnemyIntentKind.Wait));
                        enemy.ConsumeIntent();
                        enemy.LockIntent(new EnemyIntent(actorId, EnemyIntentKind.Wait));
                    }
                    else
                        enemy.LockIntent(new EnemyIntent(actorId,
                            isConditional ? EnemyIntentKind.Move : EnemyIntentKind.Attack,
                            target, playerId, isConditional));
                }
                var moverId = new EntityId(7);
                Add(board, moverId.Value, BoardLayer.Actor, EntityKind.Enemy, moverSource);
                enemies.Single(enemy => enemy.ActorId.Equals(moverId)).LockIntent(
                    new EnemyIntent(moverId, EnemyIntentKind.Move, moverTarget, playerId, true));
                if (isMiss)
                    Assert.That(board.TryMove(playerId, escapedPosition), Is.True);
                if (outcome == "conditional-hit")
                    Assert.That(board.TryMove(playerId, target), Is.True);
                if (outcome == "enemy-blocked")
                    Add(board, 42, BoardLayer.Actor, EntityKind.Enemy, target);
                var retained = enemies.ToDictionary(enemy => enemy.ActorId, enemy => enemy.LockedIntent);
                var unchanged = board.GetEntities().Where(entity => !entity.Id.Equals(moverId)).ToArray();
                int count = board.Count;
                var events = new List<GameEvent>();

                executor.Execute(board, run, enemies, events);

                Assert.That(board.Count, Is.EqualTo(count));
                CollectionAssert.AreEqual(unchanged,
                    board.GetEntities().Where(entity => !entity.Id.Equals(moverId)).ToArray());
                Assert.That(events.Count, Is.EqualTo(3));
                for (int index = 0; index < attackSources.Length; index++)
                {
                    var actorId = new EntityId(identities[index]);
                    Assert.That(board.TryGetEntity(actorId, out BoardEntityState actor), Is.True);
                    Assert.That(actor.Position, Is.EqualTo(attackSources[index]));
                    Assert.That(board.TryGetEntity(BoardLayer.Actor, actor.Position,
                        out BoardEntityState occupant), Is.True);
                    Assert.That(occupant, Is.SameAs(actor));
                    if (isHit || isMiss)
                    {
                        Assert.That(events[index], Is.TypeOf<EnemyAttackResolvedEvent>());
                        var attack = (EnemyAttackResolvedEvent)events[index];
                        Assert.That(attack.AttackerId, Is.EqualTo(actorId));
                        Assert.That(attack.TargetPosition, Is.EqualTo(target));
                        Assert.That(attack.IsHit, Is.EqualTo(isHit));
                        Assert.That(attack.AffectedTargetId, Is.EqualTo(isHit ? (EntityId?)playerId : null));
                        Assert.That(attack.HealthChange, Is.EqualTo(isHit ? -damage : 0));
                    }
                    else
                    {
                        Assert.That(events[index], Is.TypeOf<EntityWaitedEvent>());
                        Assert.That(((EntityWaitedEvent)events[index]).EntityId, Is.EqualTo(actorId));
                    }
                    ShamblerState enemy = enemies.Single(state => state.ActorId.Equals(actorId));
                    Assert.That(enemy.LockedIntent, Is.Null);
                    Assert.That(enemy.Phase, Is.EqualTo(isRest ? ShamblerPhase.Active : ShamblerPhase.Rest));
                    Assert.That(retained[actorId].Kind,
                        Is.EqualTo(isRest ? EnemyIntentKind.Wait : isConditional ? EnemyIntentKind.Move : EnemyIntentKind.Attack));
                    Assert.That(retained[actorId].TargetPosition, Is.EqualTo(isRest ? (GridPosition?)null : target));
                    Assert.That(retained[actorId].AttackOnPlayerEntry, Is.EqualTo(isConditional));
                }
                Assert.That(events[2], Is.TypeOf<EntityMovedEvent>());
                var movement = (EntityMovedEvent)events[2];
                Assert.That(movement.EntityId, Is.EqualTo(moverId));
                Assert.That(movement.From, Is.EqualTo(moverSource));
                Assert.That(movement.To, Is.EqualTo(moverTarget));
                Assert.That(board.TryGetEntity(moverId, out BoardEntityState mover), Is.True);
                Assert.That(mover.Position, Is.EqualTo(moverTarget));
                Assert.That(board.TryGetEntity(BoardLayer.Actor, moverTarget, out BoardEntityState destinationOccupant), Is.True);
                Assert.That(destinationOccupant, Is.SameAs(mover));
                Assert.That(board.TryGetEntity(BoardLayer.Actor, moverSource, out _), Is.False);
                Assert.That(enemies.Single(enemy => enemy.ActorId.Equals(moverId)).Phase, Is.EqualTo(ShamblerPhase.Rest));
                Assert.That(enemies.All(enemy => enemy.LockedIntent == null), Is.True);
                Assert.That(run.Health, Is.EqualTo(isHit ? 100 - 2 * damage : 100));
                Assert.That(run.Food, Is.EqualTo(25));
                Assert.That(run.Status, Is.EqualTo(RunStatus.Active));
                CollectionAssert.AreEqual(inputBefore, enemies);
            }
        }

        /// <summary>
        /// A source removed after retaining a move or attack has no effects or reservations,
        /// while live actors execute once in initiative order for every input permutation.
        /// </summary>
        [TestCase(EnemyIntentKind.Move)]
        [TestCase(EnemyIntentKind.Attack)]
        public void RemovedActor_DoesNotActOrReserve(EnemyIntentKind removedAction)
        {
            var playerId = new EntityId(-100);
            var removedId = new EntityId(-5);
            var contenderId = new EntityId(0);
            var unrelatedId = new EntityId(7);
            var destination = new GridPosition(3, 3);
            var playerPosition = new GridPosition(3, 4);
            GridPosition removedSource = removedAction == EnemyIntentKind.Move
                ? new GridPosition(2, 3) : new GridPosition(2, 4);
            GridPosition removedTarget = removedAction == EnemyIntentKind.Move ? destination : playerPosition;
            var contenderSource = new GridPosition(4, 3);
            var unrelatedSource = new GridPosition(6, 5);
            var unrelatedTarget = new GridPosition(6, 6);
            ShamblerState[] templates = { new ShamblerState(removedId, new ShamblerDefinition(20)),
                new ShamblerState(contenderId, new ShamblerDefinition(10)),
                new ShamblerState(unrelatedId, new ShamblerDefinition(10)) };
            var executor = new EnemyBatchExecutor();

            foreach (ShamblerState[] permutation in Permutations(templates))
            {
                BoardState board = CreateBoard(playerId, playerPosition);
                RunState run = CreateRun();
                ShamblerState[] enemies = permutation.Select(enemy =>
                    new ShamblerState(enemy.ActorId, enemy.Definition)).ToArray();
                ShamblerState[] inputBefore = enemies.ToArray();
                Add(board, removedId.Value, BoardLayer.Actor, EntityKind.Enemy, removedSource);
                Add(board, contenderId.Value, BoardLayer.Actor, EntityKind.Enemy, contenderSource);
                Add(board, unrelatedId.Value, BoardLayer.Actor, EntityKind.Enemy, unrelatedSource);
                ShamblerState removed = enemies.Single(enemy => enemy.ActorId.Equals(removedId));
                var removedIntent = new EnemyIntent(removedId, removedAction, removedTarget,
                    playerId, removedAction == EnemyIntentKind.Move);
                removed.LockIntent(removedIntent);
                enemies.Single(enemy => enemy.ActorId.Equals(contenderId)).LockIntent(
                    new EnemyIntent(contenderId, EnemyIntentKind.Move, destination, playerId, true));
                enemies.Single(enemy => enemy.ActorId.Equals(unrelatedId)).LockIntent(
                    new EnemyIntent(unrelatedId, EnemyIntentKind.Move, unrelatedTarget, playerId, true));
                Assert.That(board.TryRemove(removedId), Is.True);
                Assert.That(board.TryGetEntity(BoardLayer.Actor, destination, out _), Is.False);
                var unchanged = board.GetEntities().Where(entity =>
                    !entity.Id.Equals(contenderId) && !entity.Id.Equals(unrelatedId)).ToArray();
                int count = board.Count;
                var events = new List<GameEvent>();

                executor.Execute(board, run, enemies, events);

                Assert.That(board.Count, Is.EqualTo(count));
                CollectionAssert.AreEqual(unchanged, board.GetEntities().Where(entity =>
                    !entity.Id.Equals(contenderId) && !entity.Id.Equals(unrelatedId)).ToArray());
                Assert.That(board.TryGetEntity(removedId, out _), Is.False);
                Assert.That(board.TryGetEntity(BoardLayer.Actor, removedSource, out _), Is.False);
                Assert.That(events.Count, Is.EqualTo(2));
                EntityId[] expectedIds = { contenderId, unrelatedId };
                GridPosition[] expectedSources = { contenderSource, unrelatedSource };
                GridPosition[] expectedTargets = { destination, unrelatedTarget };
                for (int index = 0; index < expectedIds.Length; index++)
                {
                    Assert.That(events[index], Is.TypeOf<EntityMovedEvent>());
                    var movement = (EntityMovedEvent)events[index];
                    Assert.That(movement.EntityId, Is.EqualTo(expectedIds[index]));
                    Assert.That(movement.From, Is.EqualTo(expectedSources[index]));
                    Assert.That(movement.To, Is.EqualTo(expectedTargets[index]));
                    Assert.That(board.TryGetEntity(expectedIds[index], out BoardEntityState actor), Is.True);
                    Assert.That(actor.Position, Is.EqualTo(expectedTargets[index]));
                    Assert.That(board.TryGetEntity(BoardLayer.Actor, actor.Position,
                        out BoardEntityState occupant), Is.True);
                    Assert.That(occupant, Is.SameAs(actor));
                    Assert.That(board.TryGetEntity(BoardLayer.Actor, expectedSources[index], out _), Is.False);
                }
                Assert.That(enemies.All(enemy => enemy.LockedIntent == null && enemy.Phase == ShamblerPhase.Rest), Is.True);
                Assert.That(removedIntent.Kind, Is.EqualTo(removedAction));
                Assert.That(removedIntent.TargetPosition, Is.EqualTo(removedTarget));
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(25));
                CollectionAssert.AreEqual(inputBefore, enemies);
                var after = board.GetEntities();
                Assert.Throws<InvalidOperationException>(() => executor.Execute(board, run, enemies, events));
                CollectionAssert.AreEqual(after, board.GetEntities());
                Assert.That(events.Count, Is.EqualTo(2));
                Assert.That(enemies.All(enemy => enemy.Phase == ShamblerPhase.Rest), Is.True);
            }
        }

        /// <summary>
        /// Explicit 2-3 actor chains use live sequential occupancy for both initiative orders and all inputs.
        /// A blocked leader prevents following, and an earlier blocked follower is never retried.
        /// </summary>
        [TestCase(2, false, false)]
        [TestCase(2, true, false)]
        [TestCase(2, false, true)]
        [TestCase(2, true, true)]
        [TestCase(3, false, false)]
        [TestCase(3, true, false)]
        [TestCase(3, false, true)]
        [TestCase(3, true, true)]
        public void MovementChain_UsesApprovedOccupancyPolicy(int actorCount, bool frontFirst, bool leadingBlocked)
        {
            var playerId = new EntityId(-100);
            long[] initiativeIds = { -5, 0, 7 };
            long[] chainIds = initiativeIds.Take(actorCount).ToArray();
            if (frontFirst)
                Array.Reverse(chainIds);
            GridPosition[] sources = Enumerable.Range(0, actorCount)
                .Select(index => new GridPosition(index + 1, 1)).ToArray();
            GridPosition[] destinations = sources.Select(source => source.Move(Direction.East)).ToArray();
            bool[] expectedMoves = Enumerable.Range(0, actorCount)
                .Select(index => !leadingBlocked && (frontFirst || index == actorCount - 1)).ToArray();
            GridPosition[] expectedPositions = Enumerable.Range(0, actorCount)
                .Select(index => expectedMoves[index] ? destinations[index] : sources[index]).ToArray();
            ShamblerState[] templates = chainIds.Select(id =>
                new ShamblerState(new EntityId(id), new ShamblerDefinition(10))).ToArray();
            var executor = new EnemyBatchExecutor();

            foreach (ShamblerState[] permutation in Permutations(templates))
            {
                BoardState board = CreateBoard(playerId, new GridPosition(7, 7));
                RunState run = CreateRun();
                ShamblerState[] enemies = permutation.Select(enemy =>
                    new ShamblerState(enemy.ActorId, enemy.Definition)).ToArray();
                ShamblerState[] inputBefore = enemies.ToArray();
                for (int index = 0; index < actorCount; index++)
                {
                    var actorId = new EntityId(chainIds[index]);
                    Add(board, actorId.Value, BoardLayer.Actor, EntityKind.Enemy, sources[index]);
                    enemies.Single(enemy => enemy.ActorId.Equals(actorId)).LockIntent(
                        new EnemyIntent(actorId, EnemyIntentKind.Move, destinations[index], playerId, true));
                }
                var blockerId = new EntityId(42);
                GridPosition leadingDestination = destinations[actorCount - 1];
                if (leadingBlocked)
                    Add(board, blockerId.Value, BoardLayer.Actor, EntityKind.Enemy, leadingDestination);
                var retained = enemies.ToDictionary(enemy => enemy.ActorId, enemy => enemy.LockedIntent);
                var nonActors = board.GetEntities().Where(entity => entity.Definition.Layer != BoardLayer.Actor).ToArray();
                int count = board.Count;
                var events = new List<GameEvent>();

                executor.Execute(board, run, enemies, events);

                Assert.That(board.Count, Is.EqualTo(count));
                CollectionAssert.AreEqual(nonActors,
                    board.GetEntities().Where(entity => entity.Definition.Layer != BoardLayer.Actor).ToArray());
                Assert.That(events.Count, Is.EqualTo(actorCount));
                for (int eventIndex = 0; eventIndex < actorCount; eventIndex++)
                {
                    var actorId = new EntityId(initiativeIds[eventIndex]);
                    int chainIndex = Array.IndexOf(chainIds, actorId.Value);
                    Assert.That(board.TryGetEntity(actorId, out BoardEntityState actor), Is.True);
                    Assert.That(actor.Position, Is.EqualTo(expectedPositions[chainIndex]));
                    Assert.That(board.TryGetEntity(BoardLayer.Actor, actor.Position,
                        out BoardEntityState occupant), Is.True);
                    Assert.That(occupant, Is.SameAs(actor));
                    if (expectedMoves[chainIndex])
                    {
                        Assert.That(events[eventIndex], Is.TypeOf<EntityMovedEvent>());
                        var movement = (EntityMovedEvent)events[eventIndex];
                        Assert.That(movement.EntityId, Is.EqualTo(actorId));
                        Assert.That(movement.From, Is.EqualTo(sources[chainIndex]));
                        Assert.That(movement.To, Is.EqualTo(destinations[chainIndex]));
                    }
                    else
                    {
                        Assert.That(events[eventIndex], Is.TypeOf<EntityWaitedEvent>());
                        Assert.That(((EntityWaitedEvent)events[eventIndex]).EntityId, Is.EqualTo(actorId));
                    }
                    ShamblerState enemy = enemies.Single(state => state.ActorId.Equals(actorId));
                    Assert.That(enemy.LockedIntent, Is.Null);
                    Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
                    Assert.That(retained[actorId].Kind, Is.EqualTo(EnemyIntentKind.Move));
                    Assert.That(retained[actorId].TargetPosition, Is.EqualTo(destinations[chainIndex]));
                }
                for (int cellIndex = 0; cellIndex <= actorCount; cellIndex++)
                {
                    var position = new GridPosition(cellIndex + 1, 1);
                    int occupantIndex = Array.IndexOf(expectedPositions, position);
                    if (leadingBlocked && position.Equals(leadingDestination))
                    {
                        Assert.That(board.TryGetEntity(BoardLayer.Actor, position, out BoardEntityState blocker), Is.True);
                        Assert.That(blocker.Id, Is.EqualTo(blockerId));
                        Assert.That(board.TryGetEntity(blockerId, out BoardEntityState indexedBlocker), Is.True);
                        Assert.That(indexedBlocker, Is.SameAs(blocker));
                    }
                    else if (occupantIndex >= 0)
                    {
                        Assert.That(board.TryGetEntity(BoardLayer.Actor, position, out BoardEntityState occupant), Is.True);
                        Assert.That(occupant.Id, Is.EqualTo(new EntityId(chainIds[occupantIndex])));
                    }
                    else
                        Assert.That(board.TryGetEntity(BoardLayer.Actor, position, out _), Is.False);
                }
                Assert.That(board.TryGetEntity(playerId, out BoardEntityState player), Is.True);
                Assert.That(player.Position, Is.EqualTo(new GridPosition(7, 7)));
                Assert.That(run.Health, Is.EqualTo(100));
                Assert.That(run.Food, Is.EqualTo(25));
                CollectionAssert.AreEqual(inputBefore, enemies);
            }
        }

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