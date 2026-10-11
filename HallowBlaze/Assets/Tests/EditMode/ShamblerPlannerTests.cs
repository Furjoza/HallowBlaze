using System;
using System.Reflection;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies deterministic, read-only pursuit for one eligible Shambler opportunity.
    /// </summary>
    public class ShamblerPlannerTests
    {
        private static readonly EntityId EnemyId = new EntityId(0);
        private static readonly EntityId PlayerId = new EntityId(-1);
        private readonly ShamblerPlanner planner = new ShamblerPlanner();

        /// <summary>
        /// All cardinal directions attack at range one and move at range two without entering the player cell.
        /// </summary>
        [TestCase(3, 4, 3, 4, EnemyIntentKind.Attack)]
        [TestCase(4, 3, 4, 3, EnemyIntentKind.Attack)]
        [TestCase(2, 3, 2, 3, EnemyIntentKind.Attack)]
        [TestCase(3, 2, 3, 2, EnemyIntentKind.Attack)]
        [TestCase(3, 5, 3, 4, EnemyIntentKind.Move)]
        [TestCase(5, 3, 4, 3, EnemyIntentKind.Move)]
        [TestCase(1, 3, 2, 3, EnemyIntentKind.Move)]
        [TestCase(3, 1, 3, 2, EnemyIntentKind.Move)]
        public void CardinalTargetsRespectAttackRange(
            int playerX, int playerY, int targetX, int targetY, EnemyIntentKind kind)
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(playerX, playerY));
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            EnemyIntent intent = planner.Plan(board, enemy, PlayerId);

            AssertIntent(intent, kind, new GridPosition(targetX, targetY), PlayerId);
        }

        /// <summary>
        /// Diagonal targets move rather than attack and distinguish North over East/West and West/East over South.
        /// </summary>
        [TestCase(4, 4, 3, 4)]
        [TestCase(2, 4, 3, 4)]
        [TestCase(4, 2, 4, 3)]
        [TestCase(2, 2, 2, 3)]
        public void EqualShortestFirstStepsUseTheApprovedPriority(
            int playerX, int playerY, int targetX, int targetY)
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(playerX, playerY));
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(20));

            EnemyIntent intent = planner.Plan(board, enemy, PlayerId);

            AssertIntent(intent, EnemyIntentKind.Move, new GridPosition(targetX, targetY), PlayerId);
        }

        /// <summary>
        /// Symmetric detours complete the priority examples with North over South and East over West.
        /// </summary>
        [TestCase(5, 3, 4, 3, 3, 4)]
        [TestCase(3, 5, 3, 4, 4, 3)]
        public void EqualDetoursUseTheApprovedPriority(
            int playerX, int playerY, int obstacleX, int obstacleY, int targetX, int targetY)
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(playerX, playerY));
            BlockCell(board, new GridPosition(obstacleX, obstacleY), BoardLayer.Obstacle);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            EnemyIntent intent = planner.Plan(board, enemy, PlayerId);

            AssertIntent(intent, EnemyIntentKind.Move, new GridPosition(targetX, targetY), PlayerId);
        }

        /// <summary>
        /// A shorter southern route beats the northern priority even though both initially move away from the player.
        /// </summary>
        [Test]
        public void ShortestDetourTakesPrecedenceOverDirectionPriority()
        {
            BoardState board = CreateBoard(new GridPosition(1, 2), new GridPosition(5, 2),
                new GridBounds(0, 0, 6, 4));
            BlockCell(board, new GridPosition(2, 2), BoardLayer.Obstacle);
            BlockCell(board, new GridPosition(2, 3), BoardLayer.Obstacle, 10001);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            EnemyIntent intent = planner.Plan(board, enemy, PlayerId);

            AssertIntent(intent, EnemyIntentKind.Move, new GridPosition(1, 1), PlayerId);
        }

        /// <summary>
        /// Missing floor, impassable terrain, and impassable obstacles each disconnect a one-cell corridor.
        /// </summary>
        [TestCase(BoardLayer.Terrain, false)]
        [TestCase(BoardLayer.Terrain, true)]
        [TestCase(BoardLayer.Obstacle, false)]
        public void IllegalIntermediateCellsProduceWait(BoardLayer layer, bool missingTerrain)
        {
            BoardState board = CreateBoard(new GridPosition(0, 0), new GridPosition(2, 0),
                new GridBounds(0, 0, 2, 0));
            var blockedPosition = new GridPosition(1, 0);
            if (missingTerrain)
                RemoveTerrain(board, blockedPosition);
            else
                BlockCell(board, blockedPosition, layer);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Wait, null, null);
        }

        /// <summary>
        /// Legal obstacle and item layers do not block pursuit, and planning neither moves nor collects them.
        /// </summary>
        [Test]
        public void WalkableObstaclesAndItemsDoNotBlockThePath()
        {
            BoardState board = CreateBoard(new GridPosition(0, 0), new GridPosition(2, 0),
                new GridBounds(0, 0, 2, 0));
            var destination = new GridPosition(1, 0);
            AddEntity(board, 10000, BoardLayer.Obstacle, EntityKind.Obstacle, destination, true);
            AddEntity(board, 10001, BoardLayer.Item, EntityKind.Item, destination);
            var before = board.GetEntities();
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(20));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Move, destination, PlayerId);
            CollectionAssert.AreEqual(before, board.GetEntities());
        }

        /// <summary>
        /// A different actor blocks a corridor regardless of its walkable trait and is never attacked.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OtherActorsBlockThePath(bool isWalkable)
        {
            BoardState board = CreateBoard(new GridPosition(0, 0), new GridPosition(2, 0),
                new GridBounds(0, 0, 2, 0));
            AddEntity(board, 10000, BoardLayer.Actor, EntityKind.Enemy, new GridPosition(1, 0), isWalkable);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Wait, null, null);
        }

        /// <summary>
        /// A blocked actor cell is bypassed using a shortest legal route rather than targeted for attack.
        /// </summary>
        [Test]
        public void OtherActorsCanBeDetouredAround()
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(3, 5));
            AddEntity(board, 10000, BoardLayer.Actor, EntityKind.Enemy, new GridPosition(3, 4));
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Move,
                new GridPosition(4, 3), PlayerId);
        }

        /// <summary>
        /// Player occupancy permits only a legal terminal cell, not attack or pursuit through blocked terrain/obstacles.
        /// </summary>
        [TestCase(BoardLayer.Terrain, 1)]
        [TestCase(BoardLayer.Terrain, 2)]
        [TestCase(BoardLayer.Obstacle, 1)]
        [TestCase(BoardLayer.Obstacle, 2)]
        public void IllegalPlayerCellsPreventAttackAndPursuit(BoardLayer layer, int playerX)
        {
            var playerPosition = new GridPosition(playerX, 0);
            BoardState board = CreateBoard(new GridPosition(0, 0), playerPosition,
                new GridBounds(0, 0, 2, 0));
            BlockCell(board, playerPosition, layer);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Wait, null, null);
        }

        /// <summary>
        /// An adjacent player without a floor cannot be attacked.
        /// </summary>
        [Test]
        public void PlayerWithoutTerrainProducesWait()
        {
            var playerPosition = new GridPosition(3, 4);
            BoardState board = CreateBoard(new GridPosition(3, 3), playerPosition);
            RemoveTerrain(board, playerPosition);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Wait, null, null);
        }

        /// <summary>
        /// A disconnected region produces wait; the search cannot escape the board to bypass its barrier.
        /// </summary>
        [Test]
        public void ABoardSpanningBarrierMakesThePlayerUnreachable()
        {
            BoardState board = CreateBoard(new GridPosition(1, 1), new GridPosition(1, 3),
                new GridBounds(0, 0, 2, 4));
            for (int column = 0; column <= 2; column++)
                BlockCell(board, new GridPosition(column, 2), BoardLayer.Obstacle, 10000 + column);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Wait, null, null);
        }

        /// <summary>
        /// No detection radius or line of sight is needed to pursue a player across the whole default board.
        /// </summary>
        [Test]
        public void WholeBoardPursuitHasNoDetectionRadius()
        {
            BoardState board = CreateBoard(new GridPosition(0, 0), new GridPosition(7, 7));
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Move,
                new GridPosition(0, 1), PlayerId);
        }

        /// <summary>
        /// Inclusive, shifted, and one-cell-wide bounds still permit a legal shortest first step.
        /// </summary>
        [TestCase(0, 0, 0, 4, 0, 0, 0, 4, 0, 1)]
        [TestCase(0, 0, 4, 0, 0, 0, 4, 0, 1, 0)]
        [TestCase(-5, 10, -2, 13, -5, 11, -5, 13, -5, 12)]
        public void ExplicitBoundsAreRespected(
            int minX, int minY, int maxX, int maxY, int enemyX, int enemyY,
            int playerX, int playerY, int targetX, int targetY)
        {
            BoardState board = CreateBoard(new GridPosition(enemyX, enemyY), new GridPosition(playerX, playerY),
                new GridBounds(minX, minY, maxX, maxY));
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Move,
                new GridPosition(targetX, targetY), PlayerId);
        }

        /// <summary>
        /// Neighbor enumeration on integer-coordinate limits must not overflow or wrap into another cell.
        /// </summary>
        [TestCase(int.MinValue, int.MinValue + 2, false)]
        [TestCase(int.MinValue, int.MinValue + 2, true)]
        [TestCase(int.MaxValue - 2, int.MaxValue, false)]
        [TestCase(int.MaxValue - 2, int.MaxValue, true)]
        public void ExtremeCoordinateBoundsDoNotOverflow(int minimum, int maximum, bool startAtMaximum)
        {
            var enemyPosition = new GridPosition(startAtMaximum ? maximum : minimum,
                startAtMaximum ? maximum : minimum);
            var playerPosition = new GridPosition(startAtMaximum ? minimum : maximum,
                startAtMaximum ? minimum : maximum);
            BoardState board = CreateBoard(enemyPosition, playerPosition,
                new GridBounds(minimum, minimum, maximum, maximum));
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));
            GridPosition expected = enemyPosition.Move(startAtMaximum ? Direction.West : Direction.North);

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Move, expected, PlayerId);
        }

        /// <summary>
        /// Reversing every layer's insertion order cannot change the chosen route or its fixed target payload.
        /// </summary>
        [Test]
        public void InsertionOrderDoesNotChangeTheIntent()
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(3, 5));
            BlockCell(board, new GridPosition(3, 4), BoardLayer.Obstacle);
            var reversed = new BoardState(board.Bounds);
            var entities = board.GetEntities();
            for (int index = entities.Count - 1; index >= 0; index--)
                Assert.That(reversed.TryAdd(entities[index]), Is.True);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Move,
                new GridPosition(4, 3), PlayerId);
            AssertIntent(planner.Plan(reversed, enemy, PlayerId), EnemyIntentKind.Move,
                new GridPosition(4, 3), PlayerId);
        }

        /// <summary>
        /// Every planning outcome preserves the board indexes, configuration, cadence, and exact retained intent.
        /// </summary>
        [TestCase(3, 4, false, EnemyIntentKind.Attack)]
        [TestCase(3, 5, false, EnemyIntentKind.Move)]
        [TestCase(3, 5, true, EnemyIntentKind.Wait)]
        public void RepeatedPlanningLeavesBoardAndEnemyStateUnchanged(
            int playerX, int playerY, bool blocked, EnemyIntentKind kind)
        {
            var playerPosition = new GridPosition(playerX, playerY);
            BoardState board = CreateBoard(new GridPosition(3, 3), playerPosition);
            if (blocked)
                BlockCell(board, playerPosition, BoardLayer.Obstacle);
            var definition = new ShamblerDefinition(20);
            var enemy = new ShamblerState(EnemyId, definition);
            var retained = new EnemyIntent(EnemyId, EnemyIntentKind.Wait);
            enemy.LockIntent(retained);
            var before = board.GetEntities();
            GridBounds bounds = board.Bounds;
            GridPosition? target = kind == EnemyIntentKind.Wait ? (GridPosition?)null : new GridPosition(3, 4);
            EntityId? targetId = kind == EnemyIntentKind.Wait ? (EntityId?)null : PlayerId;

            for (int iteration = 0; iteration < 8; iteration++)
                AssertIntent(planner.Plan(board, enemy, PlayerId), kind, target, targetId);

            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(board.Bounds, Is.EqualTo(bounds));
            foreach (BoardEntityState entity in before)
            {
                Assert.That(board.TryGetEntity(entity.Id, out BoardEntityState byId), Is.True);
                Assert.That(byId, Is.SameAs(entity));
                Assert.That(board.TryGetEntity(entity.Definition.Layer, entity.Position, out BoardEntityState byCell), Is.True);
                Assert.That(byCell, Is.SameAs(entity));
            }
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(enemy.LockedIntent, Is.SameAs(retained));
            Assert.That(enemy.Definition, Is.SameAs(definition));
        }

        /// <summary>
        /// A new explicit planning call reads current authoritative positions without changing a previous intent.
        /// </summary>
        [Test]
        public void PlanningReadsCurrentBoardPositionsRatherThanACachedTarget()
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(3, 4));
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));
            EnemyIntent previous = planner.Plan(board, enemy, PlayerId);
            Assert.That(board.TryMove(PlayerId, new GridPosition(5, 3)), Is.True);

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Move,
                new GridPosition(4, 3), PlayerId);
            AssertIntent(previous, EnemyIntentKind.Attack, new GridPosition(3, 4), PlayerId);
        }

        /// <summary>
        /// A missing source or target is an explicit wait rather than movement or a fabricated attack.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MissingActorsProduceWait(bool removePlayer)
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(3, 4));
            Assert.That(board.TryRemove(removePlayer ? PlayerId : EnemyId), Is.True);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Wait, null, null);
        }

        /// <summary>
        /// Matching IDs in a non-actor layer or with the wrong actor kind cannot authorize a Shambler action.
        /// </summary>
        [TestCase(false, BoardLayer.Actor)]
        [TestCase(true, BoardLayer.Actor)]
        [TestCase(false, BoardLayer.Item)]
        [TestCase(true, BoardLayer.Item)]
        public void InvalidActorKindsOrLayersProduceWait(bool replacePlayer, BoardLayer layer)
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(3, 4));
            EntityId id = replacePlayer ? PlayerId : EnemyId;
            var position = replacePlayer ? new GridPosition(3, 4) : new GridPosition(3, 3);
            EntityKind kind = replacePlayer ? EntityKind.Player : EntityKind.Enemy;
            if (layer == BoardLayer.Actor)
                kind = replacePlayer ? EntityKind.Enemy : EntityKind.Player;
            Assert.That(board.TryRemove(id), Is.True);
            AddEntity(board, id.Value, layer, kind, position);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, PlayerId), EnemyIntentKind.Wait, null, null);
        }

        /// <summary>
        /// The enemy's identity cannot be used as the player target.
        /// </summary>
        [Test]
        public void SelfTargetProducesWait()
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(3, 4));
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            AssertIntent(planner.Plan(board, enemy, EnemyId), EnemyIntentKind.Wait, null, null);
        }

        /// <summary>
        /// Null dependencies fail explicitly without accessing or changing gameplay state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NullDependenciesAreRejected(bool nullBoard)
        {
            BoardState board = CreateBoard(new GridPosition(3, 3), new GridPosition(3, 4));
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));

            var error = Assert.Throws<ArgumentNullException>(() =>
                planner.Plan(nullBoard ? null : board, nullBoard ? enemy : null, PlayerId));

            Assert.That(error.ParamName, Is.EqualTo(nullBoard ? "boardState" : "enemyState"));
        }

        /// <summary>
        /// The planner has no per-instance state and resides in a domain assembly without Unity dependencies.
        /// </summary>
        [Test]
        public void PlannerHasNoInstanceStateOrUnityDependency()
        {
            Assert.That(typeof(ShamblerPlanner).GetFields(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic), Is.Empty);
            foreach (var reference in typeof(ShamblerPlanner).Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
        }

        private static void RemoveTerrain(BoardState board, GridPosition position)
        {
            Assert.That(board.TryGetEntity(BoardLayer.Terrain, position, out BoardEntityState terrain), Is.True);
            Assert.That(board.TryRemove(terrain.Id), Is.True);
        }

        private static void BlockCell(
            BoardState board, GridPosition position, BoardLayer layer, long id = 10000)
        {
            if (layer == BoardLayer.Terrain)
                RemoveTerrain(board, position);
            EntityKind kind = layer == BoardLayer.Terrain ? new EntityKind("wall") : EntityKind.Obstacle;
            AddEntity(board, id, layer, kind, position);
        }

        private static BoardState CreateBoard(
            GridPosition enemyPosition, GridPosition playerPosition, GridBounds? bounds = null)
        {
            var board = new BoardState(bounds ?? GridPosition.DefaultBoardBounds);
            long terrainId = 100;
            for (long column = board.Bounds.MinX; column <= board.Bounds.MaxX; column++)
            {
                for (long row = board.Bounds.MinY; row <= board.Bounds.MaxY; row++)
                {
                    AddEntity(board, terrainId++, BoardLayer.Terrain, new EntityKind("floor"),
                        new GridPosition((int)column, (int)row), true);
                }
            }
            AddEntity(board, EnemyId.Value, BoardLayer.Actor, EntityKind.Enemy, enemyPosition);
            AddEntity(board, PlayerId.Value, BoardLayer.Actor, EntityKind.Player, playerPosition);
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

        private static void AssertIntent(
            EnemyIntent intent, EnemyIntentKind kind, GridPosition? targetPosition, EntityId? targetId)
        {
            Assert.That(intent.ActorId, Is.EqualTo(EnemyId));
            Assert.That(intent.Kind, Is.EqualTo(kind));
            Assert.That(intent.TargetPosition, Is.EqualTo(targetPosition));
            Assert.That(intent.TargetId, Is.EqualTo(targetId));
            Assert.That(intent.AttackOnPlayerEntry, Is.EqualTo(kind == EnemyIntentKind.Move));
        }
    }
}