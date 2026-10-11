using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies one locked enemy opportunity without replanning or additional player costs.
    /// </summary>
    public class ShamblerIntentExecutorTests
    {
        private static readonly EntityId EnemyId = new EntityId(0);
        private static readonly EntityId PlayerId = new EntityId(-1);
        private static readonly GridPosition Source = new GridPosition(3, 3);
        private static readonly GridPosition Destination = new GridPosition(3, 4);
        private readonly ShamblerIntentExecutor executor = new ShamblerIntentExecutor();
        private RunState runState;

        /// <summary>Creates isolated in-memory run data for each execution scenario.</summary>
        [SetUp]
        public void InitializeRun()
        {
            runState = new RunState("executor-test", 42, new RunStateConfiguration(100, 25, 1, "start"));
        }

        /// <summary>
        /// Every cardinal movement changes both occupancy indexes once and preserves ordered events.
        /// </summary>
        [TestCase(3, 4)]
        [TestCase(4, 3)]
        [TestCase(2, 3)]
        [TestCase(3, 2)]
        public void LegalLockedMovementUpdatesOnlyTheRecordedCell(int targetX, int targetY)
        {
            BoardState board = CreateBoard();
            var target = new GridPosition(targetX, targetY);
            ShamblerState enemy = LockMove(target);
            var prefix = new EntityWaitedEvent(PlayerId);
            var events = new List<GameEvent> { prefix };
            int count = board.Count;

            executor.Execute(board, runState, enemy, events);

            Assert.That(board.Count, Is.EqualTo(count));
            Assert.That(board.TryGetEntity(BoardLayer.Actor, Source, out _), Is.False);
            Assert.That(board.TryGetEntity(BoardLayer.Actor, target, out BoardEntityState moved), Is.True);
            Assert.That(moved.Id, Is.EqualTo(EnemyId));
            Assert.That(board.TryGetEntity(EnemyId, out BoardEntityState byId), Is.True);
            Assert.That(byId, Is.SameAs(moved));
            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[0], Is.SameAs(prefix));
            Assert.That(events[1], Is.TypeOf<EntityMovedEvent>());
            var movement = (EntityMovedEvent)events[1];
            Assert.That(movement.EntityId, Is.EqualTo(EnemyId));
            Assert.That(movement.From, Is.EqualTo(Source));
            Assert.That(movement.To, Is.EqualTo(target));
            Assert.That(runState.Health, Is.EqualTo(100));
            Assert.That(runState.Food, Is.EqualTo(25));
            Assert.That(enemy.LockedIntent, Is.Null);
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// Terrain, obstacles, and all actor occupants invalidate only the recorded destination.
        /// An alternative path exists but must never replace the locked opportunity.
        /// </summary>
        [TestCase("missing-floor")]
        [TestCase("terrain")]
        [TestCase("obstacle")]
        [TestCase("enemy")]
        [TestCase("walkable-enemy")]
        public void DestinationInvalidatedAfterPlanningProducesWait(string blocker)
        {
            BoardState board = CreateBoard();
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));
            EnemyIntent intent = new ShamblerPlanner().Plan(board, enemy, PlayerId);
            Assert.That(intent.TargetPosition, Is.EqualTo(Destination));
            enemy.LockIntent(intent);

            if (blocker == "missing-floor" || blocker == "terrain")
            {
                Assert.That(board.TryGetEntity(BoardLayer.Terrain, Destination, out BoardEntityState floor), Is.True);
                Assert.That(board.TryRemove(floor.Id), Is.True);
                if (blocker == "terrain")
                    AddEntity(board, 10000, BoardLayer.Terrain, new EntityKind("wall"), Destination);
            }
            else if (blocker == "obstacle")
                AddEntity(board, 10000, BoardLayer.Obstacle, EntityKind.Obstacle, Destination);
            else
                AddEntity(board, 10000, BoardLayer.Actor, EntityKind.Enemy, Destination,
                    blocker == "walkable-enemy");
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.TypeOf<EntityWaitedEvent>());
            Assert.That(((EntityWaitedEvent)events[0]).EntityId, Is.EqualTo(EnemyId));
            Assert.That(intent.AttackOnPlayerEntry, Is.True);
            Assert.That(intent.TargetPosition, Is.EqualTo(Destination));
            Assert.That(enemy.LockedIntent, Is.Null);
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// Same-cell, diagonal, distant, and out-of-bounds destinations cannot produce a move.
        /// </summary>
        [TestCase(3, 3)]
        [TestCase(4, 4)]
        [TestCase(3, 5)]
        [TestCase(-1, 3)]
        [TestCase(int.MaxValue, int.MinValue)]
        public void IllegalLockedDestinationProducesWait(int targetX, int targetY)
        {
            BoardState board = CreateBoard();
            ShamblerState enemy = LockMove(new GridPosition(targetX, targetY));
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.TypeOf<EntityWaitedEvent>());
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// Walkable obstacle and item layers remain in place and are not collected by an enemy move.
        /// </summary>
        [Test]
        public void LegalNonActorLayersArePreserved()
        {
            BoardState board = CreateBoard();
            AddEntity(board, 10000, BoardLayer.Obstacle, EntityKind.Obstacle, Destination, true);
            AddEntity(board, 10001, BoardLayer.Item, EntityKind.Item, Destination);
            ShamblerState enemy = LockMove(Destination);
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            Assert.That(board.TryGetEntity(EnemyId, out BoardEntityState moved), Is.True);
            Assert.That(moved.Position, Is.EqualTo(Destination));
            Assert.That(board.TryGetEntity(new EntityId(10000), out _), Is.True);
            Assert.That(board.TryGetEntity(new EntityId(10001), out _), Is.True);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.TypeOf<EntityMovedEvent>());
        }

        /// <summary>
        /// Active and resting waits never move or attack even beside the player.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitWaitChangesOnlyCadence(bool resting)
        {
            BoardState board = CreateBoard();
            Assert.That(board.TryMove(PlayerId, Destination), Is.True);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(20));
            if (resting)
            {
                enemy.LockIntent(new EnemyIntent(EnemyId, EnemyIntentKind.Wait));
                enemy.ConsumeIntent();
            }
            enemy.LockIntent(new EnemyIntent(EnemyId, EnemyIntentKind.Wait));
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.TypeOf<EntityWaitedEvent>());
            Assert.That(runState.Health, Is.EqualTo(100));
            Assert.That(runState.Food, Is.EqualTo(25));
            Assert.That(enemy.LockedIntent, Is.Null);
            Assert.That(enemy.Phase, Is.EqualTo(resting ? ShamblerPhase.Active : ShamblerPhase.Rest));
        }

        /// <summary>
        /// A removed source or an ID reused for an invalid kind/layer has no board or event effect.
        /// </summary>
        [TestCase("missing", EnemyIntentKind.Move)]
        [TestCase("player", EnemyIntentKind.Move)]
        [TestCase("item", EnemyIntentKind.Move)]
        [TestCase("missing", EnemyIntentKind.Attack)]
        [TestCase("player", EnemyIntentKind.Attack)]
        [TestCase("item", EnemyIntentKind.Attack)]
        public void StaleSourceIsConsumedWithoutActing(string replacement, EnemyIntentKind kind)
        {
            BoardState board = CreateBoard();
            Assert.That(board.TryMove(PlayerId, Destination), Is.True);
            ShamblerState enemy = kind == EnemyIntentKind.Attack ? LockAttack(Destination, 20) : LockMove(Destination);
            Assert.That(board.TryRemove(EnemyId), Is.True);
            if (replacement != "missing")
                AddEntity(board, EnemyId.Value,
                    replacement == "item" ? BoardLayer.Item : BoardLayer.Actor,
                    replacement == "item" ? EntityKind.Enemy : EntityKind.Player, Source);
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(events, Is.Empty);
            Assert.That(runState.Health, Is.EqualTo(100));
            Assert.That(runState.Food, Is.EqualTo(25));
            Assert.That(enemy.LockedIntent, Is.Null);
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// Repeating execution without a newly locked intent cannot move or advance cadence twice.
        /// </summary>
        [Test]
        public void DuplicateExecutionIsRejectedWithoutEffects()
        {
            BoardState board = CreateBoard();
            ShamblerState enemy = LockMove(Destination);
            var events = new List<GameEvent>();
            executor.Execute(board, runState, enemy, events);
            var before = board.GetEntities();

            Assert.Throws<InvalidOperationException>(() => executor.Execute(board, runState, enemy, events));

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// Both variants hit once at each cardinal neighbor without movement or extra player cost.
        /// </summary>
        [TestCase(10, 3, 4)]
        [TestCase(10, 4, 3)]
        [TestCase(10, 2, 3)]
        [TestCase(10, 3, 2)]
        [TestCase(20, 3, 4)]
        [TestCase(20, 4, 3)]
        [TestCase(20, 2, 3)]
        [TestCase(20, 3, 2)]
        public void LockedAttackHitsOriginalPlayerOnce(int damage, int targetX, int targetY)
        {
            BoardState board = CreateBoard();
            var target = new GridPosition(targetX, targetY);
            Assert.That(board.TryMove(PlayerId, target), Is.True);
            ShamblerState enemy = LockAttack(target, damage);
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            AssertAttackEvent(events, target, true, -damage);
            Assert.That(runState.Health, Is.EqualTo(100 - damage));
            Assert.That(runState.Food, Is.EqualTo(25));
            Assert.That(runState.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(enemy.LockedIntent, Is.Null);
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.Throws<InvalidOperationException>(() => executor.Execute(board, runState, enemy, events));
            Assert.That(runState.Health, Is.EqualTo(100 - damage));
            Assert.That(events.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Lethal damage reports the clamped HP difference and leaves the death decision to the controller.
        /// </summary>
        [TestCase(10)]
        [TestCase(20)]
        public void AttackReportsClampedEffectWithoutDecidingDeath(int damage)
        {
            BoardState board = CreateBoard();
            Assert.That(board.TryMove(PlayerId, Destination), Is.True);
            runState.TakeDamage(97);
            var events = new List<GameEvent>();

            executor.Execute(board, runState, LockAttack(Destination, damage), events);

            AssertAttackEvent(events, Destination, true, -3);
            Assert.That(runState.Health, Is.Zero);
            Assert.That(runState.Status, Is.EqualTo(RunStatus.Active));
        }

        /// <summary>
        /// An escaped, missing, replaced, or wrong-kind target produces a fixed-cell miss, not pursuit.
        /// </summary>
        [TestCase("escaped")]
        [TestCase("missing")]
        [TestCase("replacement-player")]
        [TestCase("replacement-enemy")]
        [TestCase("same-id-enemy")]
        public void LockedAttackNeverFollowsOrReplacesTarget(string change)
        {
            BoardState board = CreateBoard();
            Assert.That(board.TryMove(PlayerId, Destination), Is.True);
            ShamblerState enemy = LockAttack(Destination, 20);
            if (change == "escaped")
                Assert.That(board.TryMove(PlayerId, new GridPosition(4, 3)), Is.True);
            else
            {
                Assert.That(board.TryRemove(PlayerId), Is.True);
                if (change != "missing")
                    AddEntity(board, change == "same-id-enemy" ? PlayerId.Value : 10000,
                        BoardLayer.Actor, change == "replacement-player" ? EntityKind.Player : EntityKind.Enemy,
                        Destination);
            }
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            AssertAttackEvent(events, Destination, false, 0);
            Assert.That(runState.Health, Is.EqualTo(100));
            Assert.That(runState.Food, Is.EqualTo(25));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// Illegal range or terrain blocks an attack without damage, movement, or retargeting.
        /// </summary>
        [TestCase(4, 4, "none")]
        [TestCase(3, 5, "none")]
        [TestCase(-1, 3, "none")]
        [TestCase(3, 4, "missing-floor")]
        [TestCase(3, 4, "terrain")]
        [TestCase(3, 4, "obstacle")]
        public void IllegalAttackProducesWait(int targetX, int targetY, string blocker)
        {
            BoardState board = CreateBoard();
            var target = new GridPosition(targetX, targetY);
            if (board.Bounds.Contains(target))
                Assert.That(board.TryMove(PlayerId, target), Is.True);
            if (blocker == "missing-floor" || blocker == "terrain")
            {
                Assert.That(board.TryGetEntity(BoardLayer.Terrain, target, out BoardEntityState floor), Is.True);
                Assert.That(board.TryRemove(floor.Id), Is.True);
                if (blocker == "terrain")
                    AddEntity(board, 10000, BoardLayer.Terrain, new EntityKind("wall"), target);
            }
            if (blocker == "obstacle")
                AddEntity(board, 10000, BoardLayer.Obstacle, EntityKind.Obstacle, target);
            ShamblerState enemy = LockAttack(target, 20);
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.TypeOf<EntityWaitedEvent>());
            Assert.That(runState.Health, Is.EqualTo(100));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// Null dependencies fail before intent consumption or gameplay mutation.
        /// </summary>
        [TestCase("boardState")]
        [TestCase("runState")]
        [TestCase("enemyState")]
        [TestCase("events")]
        public void NullDependenciesAreRejected(string parameter)
        {
            BoardState board = CreateBoard();
            ShamblerState enemy = LockMove(Destination);
            EnemyIntent intent = enemy.LockedIntent;
            var events = new List<GameEvent>();

            var error = Assert.Throws<ArgumentNullException>(() => executor.Execute(
                parameter == "boardState" ? null : board,
                parameter == "runState" ? null : runState,
                parameter == "enemyState" ? null : enemy,
                parameter == "events" ? null : events));

            Assert.That(error.ParamName, Is.EqualTo(parameter));
            Assert.That(events, Is.Empty);
            Assert.That(enemy.LockedIntent, Is.SameAs(intent));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
        }

        /// <summary>
        /// A terminal run cannot execute or consume a retained opportunity.
        /// </summary>
        [TestCase(RunStatus.Dead)]
        [TestCase(RunStatus.Won)]
        public void TerminalRunIsRejectedBeforeConsumption(RunStatus status)
        {
            BoardState board = CreateBoard();
            Assert.That(board.TryMove(PlayerId, Destination), Is.True);
            ShamblerState enemy = LockAttack(Destination, 20);
            EnemyIntent intent = enemy.LockedIntent;
            if (status == RunStatus.Dead)
                runState.MarkDead();
            else
                runState.MarkWon();
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            Assert.Throws<InvalidOperationException>(() => executor.Execute(board, runState, enemy, events));

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(events, Is.Empty);
            Assert.That(runState.Health, Is.EqualTo(100));
            Assert.That(enemy.LockedIntent, Is.SameAs(intent));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
        }

        /// <summary>
        /// Player entry activates only the declared same-cell attack, consumes once, and is followed by rest.
        /// </summary>
        [TestCase(10, 100)]
        [TestCase(20, 100)]
        [TestCase(10, 3)]
        [TestCase(20, 3)]
        public void PlayerOnLockedMovementDestinationIsHitWithoutEnemyMovement(int damage, int health)
        {
            BoardState board = CreateBoard();
            runState.TakeDamage(100 - health);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(damage));
            var planner = new ShamblerPlanner();
            EnemyIntent intent = planner.Plan(board, enemy, PlayerId);
            Assert.That(intent.Kind, Is.EqualTo(EnemyIntentKind.Move));
            Assert.That(intent.TargetPosition, Is.EqualTo(Destination));
            Assert.That(intent.AttackOnPlayerEntry, Is.True);
            enemy.LockIntent(intent);
            Assert.That(board.TryMove(PlayerId, Destination), Is.True);
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            int actualDamage = Math.Min(health, damage);
            AssertAttackEvent(events, Destination, true, -actualDamage);
            Assert.That(runState.Health, Is.EqualTo(health - actualDamage));
            Assert.That(runState.Food, Is.EqualTo(25));
            Assert.That(enemy.LockedIntent, Is.Null);
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(intent.Kind, Is.EqualTo(EnemyIntentKind.Move));
            Assert.Throws<InvalidOperationException>(() => executor.Execute(board, runState, enemy, events));
            Assert.That(runState.Health, Is.EqualTo(health - actualDamage));
            Assert.That(events.Count, Is.EqualTo(1));

            EnemyIntent rest = planner.Plan(board, enemy, PlayerId);
            Assert.That(rest.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
            if (runState.Health > 0)
            {
                enemy.LockIntent(rest);
                events.Clear();
                executor.Execute(board, runState, enemy, events);
                Assert.That(events.Count, Is.EqualTo(1));
                Assert.That(events[0], Is.TypeOf<EntityWaitedEvent>());
                Assert.That(runState.Health, Is.EqualTo(health - actualDamage));
                Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
                CollectionAssert.AreEqual(before, board.GetEntities());
            }
        }

        /// <summary>
        /// A player on another adjacent cell does not change a legal locked move into an attack.
        /// </summary>
        [Test]
        public void PlayerOnAnotherAdjacentCellDoesNotActivateTheCondition()
        {
            BoardState board = CreateBoard();
            ShamblerState enemy = LockMove(Destination);
            Assert.That(board.TryMove(PlayerId, new GridPosition(4, 3)), Is.True);
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.TypeOf<EntityMovedEvent>());
            Assert.That(((EntityMovedEvent)events[0]).To, Is.EqualTo(Destination));
            Assert.That(runState.Health, Is.EqualTo(100));
            Assert.That(runState.Food, Is.EqualTo(25));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// Illegal ground and replacement identities/kinds block conditional attacks without friendly fire.
        /// </summary>
        [TestCase("missing-floor")]
        [TestCase("terrain")]
        [TestCase("obstacle")]
        [TestCase("replacement-player")]
        [TestCase("same-id-enemy")]
        public void ConditionalAttackStillRequiresLegalGroundAndOriginalPlayer(string blocker)
        {
            BoardState board = CreateBoard();
            ShamblerState enemy = LockMove(Destination);
            Assert.That(board.TryMove(PlayerId, Destination), Is.True);
            if (blocker == "missing-floor" || blocker == "terrain")
            {
                Assert.That(board.TryGetEntity(BoardLayer.Terrain, Destination, out BoardEntityState floor), Is.True);
                Assert.That(board.TryRemove(floor.Id), Is.True);
                if (blocker == "terrain")
                    AddEntity(board, 10000, BoardLayer.Terrain, new EntityKind("wall"), Destination);
            }
            else if (blocker == "obstacle")
                AddEntity(board, 10000, BoardLayer.Obstacle, EntityKind.Obstacle, Destination);
            else
            {
                Assert.That(board.TryRemove(PlayerId), Is.True);
                AddEntity(board, blocker == "same-id-enemy" ? PlayerId.Value : 10000, BoardLayer.Actor,
                    blocker == "same-id-enemy" ? EntityKind.Enemy : EntityKind.Player, Destination);
            }
            var before = board.GetEntities();
            var events = new List<GameEvent>();

            executor.Execute(board, runState, enemy, events);

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.TypeOf<EntityWaitedEvent>());
            Assert.That(runState.Health, Is.EqualTo(100));
            Assert.That(runState.Food, Is.EqualTo(25));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        private static ShamblerState LockAttack(GridPosition target, int damage)
        {
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(damage));
            enemy.LockIntent(new EnemyIntent(EnemyId, EnemyIntentKind.Attack, target, PlayerId));
            return enemy;
        }

        private static void AssertAttackEvent(List<GameEvent> events, GridPosition target,
            bool hit, int healthChange)
        {
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.TypeOf<EnemyAttackResolvedEvent>());
            var attack = (EnemyAttackResolvedEvent)events[0];
            Assert.That(attack.AttackerId, Is.EqualTo(EnemyId));
            Assert.That(attack.TargetPosition, Is.EqualTo(target));
            Assert.That(attack.IsHit, Is.EqualTo(hit));
            Assert.That(attack.AffectedTargetId, Is.EqualTo(hit ? (EntityId?)PlayerId : null));
            Assert.That(attack.HealthChange, Is.EqualTo(healthChange));
        }

        private static ShamblerState LockMove(GridPosition target)
        {
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));
            enemy.LockIntent(new EnemyIntent(EnemyId, EnemyIntentKind.Move, target, PlayerId,
                attackOnPlayerEntry: true));
            return enemy;
        }

        private static BoardState CreateBoard()
        {
            var board = new BoardState();
            long terrainId = 100;
            for (int column = board.Bounds.MinX; column <= board.Bounds.MaxX; column++)
                for (int row = board.Bounds.MinY; row <= board.Bounds.MaxY; row++)
                    AddEntity(board, terrainId++, BoardLayer.Terrain, new EntityKind("floor"),
                        new GridPosition(column, row), true);
            AddEntity(board, EnemyId.Value, BoardLayer.Actor, EntityKind.Enemy, Source);
            AddEntity(board, PlayerId.Value, BoardLayer.Actor, EntityKind.Player, new GridPosition(3, 5));
            return board;
        }

        private static void AddEntity(
            BoardState board, long id, BoardLayer layer, EntityKind kind, GridPosition position,
            bool isWalkable = false)
        {
            var definition = new BoardEntityDefinition(layer, kind, $"{kind.Id}-{id}",
                new BoardEntityTraits(isWalkable, false, false));
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(id), definition, position)), Is.True);
        }
    }
}