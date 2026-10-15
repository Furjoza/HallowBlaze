using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies noise emission, propagation, and lifetime without scene or physics state.
    /// </summary>
    public class BoardNoiseTests
    {
        /// <summary>
        /// Strength zero limits audible cells to the source position.
        /// </summary>
        [Test]
        public void StrengthZeroLimitsToSourcePosition()
        {
            var board = new BoardState();
            var source = new GridPosition(4, 4);
            var noise = board.EmitNoise(new EntityId(1), source, 0, 2);

            Assert.That(noise.IsAudible(source), Is.True);
            Assert.That(noise.IsAudible(source.Move(Direction.North)), Is.False);
            Assert.That(noise.IsAudible(source.Move(Direction.East)), Is.False);
            Assert.That(noise.IsAudible(source.Move(Direction.South)), Is.False);
            Assert.That(noise.IsAudible(source.Move(Direction.West)), Is.False);
        }

        /// <summary>
        /// Noise propagates orthogonally through walkable terrain and stops at obstacles.
        /// </summary>
        [Test]
        public void NoisePropagatesOrthogonallyThroughWalkableTerrain()
        {
            var board = new BoardState();
            var source = new GridPosition(4, 4);

            // Add walkable terrain in all directions
            board.TryAdd(CreateEntity(10, BoardLayer.Terrain, source.Move(Direction.North), true, false, false));
            board.TryAdd(CreateEntity(11, BoardLayer.Terrain, source.Move(Direction.East), true, false, false));
            board.TryAdd(CreateEntity(12, BoardLayer.Terrain, source.Move(Direction.South), true, false, false));
            board.TryAdd(CreateEntity(13, BoardLayer.Terrain, source.Move(Direction.West), true, false, false));

            var noise = board.EmitNoise(new EntityId(1), source, 1, 2);

            Assert.That(noise.IsAudible(source), Is.True);
            Assert.That(noise.IsAudible(source.Move(Direction.North)), Is.True);
            Assert.That(noise.IsAudible(source.Move(Direction.East)), Is.True);
            Assert.That(noise.IsAudible(source.Move(Direction.South)), Is.True);
            Assert.That(noise.IsAudible(source.Move(Direction.West)), Is.True);
        }

        /// <summary>
        /// Obstacles block noise propagation while items and actors do not.
        /// </summary>
        [Test]
        public void ObstaclesBlockNoisePropagation()
        {
            var board = new BoardState();
            var source = new GridPosition(4, 4);

            // Add walkable terrain
            board.TryAdd(CreateEntity(10, BoardLayer.Terrain, source.Move(Direction.North), true, false, false));
            board.TryAdd(CreateEntity(11, BoardLayer.Terrain, source.Move(Direction.East), true, false, false));

            // Add obstacle to the north
            board.TryAdd(CreateEntity(12, BoardLayer.Terrain, source.Move(Direction.East).Move(Direction.East), true, false, false));
            board.TryAdd(CreateEntity(20, BoardLayer.Obstacle, source.Move(Direction.North)));

            // Add item and actor that should not block
            board.TryAdd(CreateEntity(30, BoardLayer.Item, source.Move(Direction.East)));
            board.TryAdd(CreateEntity(31, BoardLayer.Actor, source.Move(Direction.East)));

            var noise = board.EmitNoise(new EntityId(1), source, 2, 2);

            // North should be blocked by obstacle
            Assert.That(noise.IsAudible(source.Move(Direction.North)), Is.False);
            Assert.That(noise.IsAudible(source.Move(Direction.North).Move(Direction.North)), Is.False);

            // East should propagate through item and actor
            Assert.That(noise.IsAudible(source.Move(Direction.East)), Is.True);
            Assert.That(noise.IsAudible(source.Move(Direction.East).Move(Direction.East)), Is.True);
        }

        /// <summary>
        /// Noise footprint remains stable even when obstacles are removed after emission.
        /// </summary>
        [Test]
        public void NoiseFootprintStableAfterObstacleRemoval()
        {
            var board = new BoardState();
            var source = new GridPosition(4, 4);

            // Add walkable terrain
            board.TryAdd(CreateEntity(10, BoardLayer.Terrain, source.Move(Direction.North), true, false, false));
            board.TryAdd(CreateEntity(11, BoardLayer.Terrain, source.Move(Direction.North).Move(Direction.North), true, false, false));

            // Add obstacle
            board.TryAdd(CreateEntity(20, BoardLayer.Obstacle, source.Move(Direction.North)));

            var noise = board.EmitNoise(new EntityId(1), source, 2, 2);

            // Remove obstacle
            board.TryRemove(new EntityId(20));

            // Footprint should remain unchanged
            Assert.That(noise.IsAudible(source.Move(Direction.North)), Is.False);
            Assert.That(noise.IsAudible(source.Move(Direction.North).Move(Direction.North)), Is.False);
        }

        /// <summary>
        /// Noise lifetime decreases with each turn and expires after the specified duration.
        /// </summary>
        [Test]
        public void NoiseLifetimeDecreasesAndExpires()
        {
            var board = new BoardState();
            var source = new GridPosition(4, 4);
            var noise = board.EmitNoise(new EntityId(1), source, 1, 3);

            Assert.That(board.GetActiveNoises().Count, Is.EqualTo(1));
            Assert.That(noise.RemainingTurns, Is.EqualTo(3));

            board.AdvanceNoiseLifetime();
            Assert.That(board.GetActiveNoises().Count, Is.EqualTo(1));

            board.AdvanceNoiseLifetime();
            Assert.That(board.GetActiveNoises().Count, Is.EqualTo(1));

            board.AdvanceNoiseLifetime();
            Assert.That(board.GetActiveNoises().Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Multiple noise events can coexist and their footprints are independent.
        /// </summary>
        [Test]
        public void MultipleNoiseEventsCoexistIndependently()
        {
            var board = new BoardState();
            var source1 = new GridPosition(4, 4);
            var source2 = new GridPosition(2, 2);
            board.TryAdd(CreateEntity(10, BoardLayer.Terrain, source1.Move(Direction.North), true, false, false));
            board.TryAdd(CreateEntity(11, BoardLayer.Terrain, source2.Move(Direction.East), true, false, false));

            var noise1 = board.EmitNoise(new EntityId(1), source1, 1, 2);
            var noise2 = board.EmitNoise(new EntityId(2), source2, 1, 3);

            Assert.That(board.GetActiveNoises().Count, Is.EqualTo(2));
            Assert.That(noise1.IsAudible(source1), Is.True);
            Assert.That(noise1.IsAudible(source1.Move(Direction.North)), Is.True);
            Assert.That(noise2.IsAudible(source2), Is.True);
            Assert.That(noise2.IsAudible(source2.Move(Direction.East)), Is.True);
        }

        /// <summary>
        /// Invalid parameters leave the board state and next ID unchanged.
        /// </summary>
        [Test]
        public void InvalidParametersLeaveStateUnchanged()
        {
            var board = new BoardState();
            var initialCount = board.GetActiveNoises().Count;

            Assert.Throws<ArgumentOutOfRangeException>(() => board.EmitNoise(new EntityId(1), new GridPosition(4, 4), -1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.EmitNoise(new EntityId(1), new GridPosition(4, 4), 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.EmitNoise(new EntityId(1), new GridPosition(9, 9), 1, 2));

            Assert.That(board.GetActiveNoises().Count, Is.EqualTo(initialCount));
            Assert.That(board.EmitNoise(new EntityId(1), new GridPosition(4, 4), 1, 2).Id, Is.Zero);
        }

        /// <summary>
        /// Cloned board has independent noise collections and sequence ID.
        /// </summary>
        [Test]
        public void CloneHasIndependentNoiseCollections()
        {
            var original = new BoardState();
            var source = new GridPosition(4, 4);
            var noise = original.EmitNoise(new EntityId(1), source, 1, 2);

            var clone = original.Clone();

            Assert.That(original.GetActiveNoises().Count, Is.EqualTo(1));
            Assert.That(clone.GetActiveNoises().Count, Is.EqualTo(1));

            original.AdvanceNoiseLifetime();
            Assert.That(original.GetActiveNoises().Count, Is.EqualTo(1));
            Assert.That(clone.GetActiveNoises().Count, Is.EqualTo(1));

            var newNoise = original.EmitNoise(new EntityId(2), source, 1, 2);
            Assert.That(original.GetActiveNoises().Count, Is.EqualTo(2));
            Assert.That(clone.GetActiveNoises().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// New board starts with no active noises.
        /// </summary>
        [Test]
        public void NewBoardStartsWithNoActiveNoises()
        {
            var board = new BoardState();
            Assert.That(board.GetActiveNoises().Count, Is.EqualTo(0));
        }

        /// <summary>Open terrain produces the exact bounded Manhattan footprint without consuming strength.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void StrengthIsAnInclusivePathBudget(int strength)
        {
            BoardState board = CreateGroundBoard();
            var source = new GridPosition(3, 3);
            BoardNoise noise = board.EmitNoise(new EntityId(1), source, strength, 2);
            Assert.That(noise.Strength, Is.EqualTo(strength));
            foreach (BoardEntityState terrain in board.GetEntities())
                Assert.That(noise.IsAudible(terrain.Position), Is.EqualTo(source.ManhattanDistance(terrain.Position) <= strength));
        }

        /// <summary>A detour consumes its actual path length rather than passing through the wall.</summary>
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, true)]
        public void ObstacleDetoursConsumeRange(int strength, bool expected)
        {
            BoardState board = CreateGroundBoard();
            var wall = new GridPosition(4, 3);
            Assert.That(board.TryAdd(CreateEntity(500, BoardLayer.Obstacle, wall)), Is.True);
            BoardNoise noise = board.EmitNoise(new EntityId(1), new GridPosition(3, 3), strength, 1);
            Assert.That(noise.IsAudible(wall), Is.False);
            Assert.That(noise.IsAudible(new GridPosition(5, 3)), Is.EqualTo(expected));
        }

        /// <summary>Missing or impassable ground blocks sound while a walkable obstacle does not.</summary>
        [Test]
        public void GroundAndObstacleTraitsControlPropagation()
        {
            BoardState board = CreateGroundBoard();
            var source = new GridPosition(3, 3);
            var north = source.Move(Direction.North);
            var east = source.Move(Direction.East);
            Assert.That(board.TryGetEntity(BoardLayer.Terrain, north, out BoardEntityState oldNorth), Is.True);
            Assert.That(board.TryRemove(oldNorth.Id), Is.True);
            Assert.That(board.TryAdd(CreateEntity(600, BoardLayer.Terrain, north)), Is.True);
            Assert.That(board.TryGetEntity(BoardLayer.Terrain, east, out BoardEntityState oldEast), Is.True);
            Assert.That(board.TryRemove(oldEast.Id), Is.True);
            var south = source.Move(Direction.South);
            Assert.That(board.TryAdd(CreateEntity(601, BoardLayer.Obstacle, south, true, false, false)), Is.True);
            BoardNoise noise = board.EmitNoise(new EntityId(1), source, 2, 1);
            Assert.That(noise.IsAudible(north), Is.False);
            Assert.That(noise.IsAudible(east), Is.False);
            Assert.That(noise.IsAudible(south), Is.True);
        }

        /// <summary>Insertion order does not change the footprint, equality, or hash code.</summary>
        [Test]
        public void FootprintIsStableAcrossInsertionOrder()
        {
            var source = new GridPosition(3, 3);
            BoardNoise first = CreateGroundBoard().EmitNoise(new EntityId(1), source, 4, 2);
            BoardNoise reversed = CreateGroundBoard(true).EmitNoise(new EntityId(1), source, 4, 2);
            CollectionAssert.AreEqual(first.AudibleCells, reversed.AudibleCells);
            Assert.That(first, Is.EqualTo(reversed));
            Assert.That(first.GetHashCode(), Is.EqualTo(reversed.GetHashCode()));
            Assert.Throws<NotSupportedException>(() => ((IList<GridPosition>)first.AudibleCells).Clear());
        }

        /// <summary>Old snapshots retain values while clones advance independently with unique emission IDs.</summary>
        [Test]
        public void SnapshotsAndClonePreserveLifetimeAndSequence()
        {
            BoardState board = CreateGroundBoard();
            var source = new GridPosition(3, 3);
            BoardNoise emitted = board.EmitNoise(new EntityId(1), source, 2, 2);
            IReadOnlyList<BoardNoise> snapshot = board.GetActiveNoises();
            BoardState clone = board.Clone();
            board.AdvanceNoiseLifetime();
            Assert.That(board.GetActiveNoises()[0].RemainingTurns, Is.EqualTo(1));
            Assert.That(snapshot[0].RemainingTurns, Is.EqualTo(2));
            Assert.That(clone.GetActiveNoises()[0].RemainingTurns, Is.EqualTo(2));
            Assert.That(clone.EmitNoise(new EntityId(2), source, 1, 1).Id, Is.EqualTo(emitted.Id + 1));
            board.AdvanceNoiseLifetime();
            Assert.That(board.GetActiveNoises(), Is.Empty);
            Assert.That(snapshot.Count, Is.EqualTo(1));
            Assert.That(clone.GetActiveNoises().Count, Is.EqualTo(2));
            Assert.Throws<NotSupportedException>(() => ((IList<BoardNoise>)snapshot).Clear());
        }

        private static BoardState CreateGroundBoard(bool reverse = false)
        {
            var board = new BoardState();
            for (int cellIndex = 0; cellIndex < 64; cellIndex++)
            {
                int coordinate = reverse ? 63 - cellIndex : cellIndex;
                Assert.That(board.TryAdd(CreateEntity(100 + coordinate, BoardLayer.Terrain,
                    new GridPosition(coordinate / 8, coordinate % 8), true, false, false)), Is.True);
            }
            return board;
        }

        private static BoardEntityState CreateEntity(
            long id,
            BoardLayer layer,
            GridPosition position)
        {
            var kind = new EntityKind(layer.ToString().ToLowerInvariant());
            var definition = new BoardEntityDefinition(layer, kind, $"{kind.Id}-{id}");
            return new BoardEntityState(new EntityId(id), definition, position);
        }

        private static BoardEntityState CreateEntity(
            long id,
            BoardLayer layer,
            GridPosition position,
            bool isWalkable,
            bool isExit,
            bool isInteractable)
        {
            var kind = new EntityKind(layer.ToString().ToLowerInvariant());
            var traits = new BoardEntityTraits(isWalkable, isExit, isInteractable);
            var definition = new BoardEntityDefinition(layer, kind, $"{kind.Id}-{id}", traits);
            return new BoardEntityState(new EntityId(id), definition, position);
        }
    }
}
