using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies layered board ownership and mutation invariants without scene or physics state.
    /// </summary>
    public class BoardStateTests
    {
        /// <summary>
        /// The default board accepts its full 8x8 perimeter and independent layers coexist at one position.
        /// </summary>
        [Test]
        public void DefaultBoardSupportsFullBoundsAndLayerCoexistence()
        {
            var board = new BoardState();
            var position = new GridPosition(7, 7);

            Assert.That(board.Bounds, Is.EqualTo(GridPosition.DefaultBoardBounds));
            Assert.That(board.TryAdd(CreateEntity(4, BoardLayer.Actor, position)), Is.True);
            Assert.That(board.TryAdd(CreateEntity(2, BoardLayer.Obstacle, position)), Is.True);
            Assert.That(board.TryAdd(CreateEntity(1, BoardLayer.Terrain, position)), Is.True);
            Assert.That(board.TryAdd(CreateEntity(3, BoardLayer.Item, position)), Is.True);

            IReadOnlyList<BoardEntityState> entities = board.GetEntitiesAt(position);
            Assert.That(entities.Count, Is.EqualTo(4));
            Assert.That(entities[0].Definition.Layer, Is.EqualTo(BoardLayer.Terrain));
            Assert.That(entities[1].Definition.Layer, Is.EqualTo(BoardLayer.Obstacle));
            Assert.That(entities[2].Definition.Layer, Is.EqualTo(BoardLayer.Item));
            Assert.That(entities[3].Definition.Layer, Is.EqualTo(BoardLayer.Actor));
        }

        /// <summary>
        /// Rejected additions preserve the ID and occupancy indexes exactly.
        /// </summary>
        [Test]
        public void RejectedAddsAreAtomic()
        {
            var board = new BoardState();
            var origin = new GridPosition(0, 0);
            var original = CreateEntity(0, BoardLayer.Actor, origin);

            Assert.That(board.TryAdd(original), Is.True, "EntityId zero is valid.");
            Assert.That(board.TryAdd(CreateEntity(1, BoardLayer.Actor, origin)), Is.False);
            Assert.That(board.TryAdd(CreateEntity(0, BoardLayer.Item, new GridPosition(1, 0))), Is.False);
            Assert.That(board.TryAdd(CreateEntity(2, BoardLayer.Item, new GridPosition(-1, 0))), Is.False);

            Assert.That(board.Count, Is.EqualTo(1));
            Assert.That(board.TryGetEntity(new EntityId(0), out BoardEntityState byId), Is.True);
            Assert.That(byId, Is.EqualTo(original));
            Assert.That(board.TryGetEntity(BoardLayer.Actor, origin, out BoardEntityState byPosition), Is.True);
            Assert.That(byPosition.Id, Is.EqualTo(new EntityId(0)));
            Assert.That(board.TryGetEntity(new EntityId(1), out _), Is.False);
            Assert.That(board.TryGetEntity(new EntityId(2), out _), Is.False);
        }

        /// <summary>
        /// Moves and removals keep both indexes consistent while rejected moves leave state unchanged.
        /// </summary>
        [Test]
        public void MoveAndRemoveKeepIndexesConsistent()
        {
            var board = new BoardState();
            var start = new GridPosition(0, 0);
            var destination = new GridPosition(0, 1);
            var blockerPosition = new GridPosition(1, 0);
            var actor = CreateEntity(10, BoardLayer.Actor, start);

            Assert.That(board.TryAdd(actor), Is.True);
            Assert.That(board.TryAdd(CreateEntity(11, BoardLayer.Actor, blockerPosition)), Is.True);
            Assert.That(board.TryAdd(CreateEntity(12, BoardLayer.Terrain, destination)), Is.True);
            Assert.That(board.TryMove(actor.Id, blockerPosition), Is.False);
            Assert.That(board.TryMove(actor.Id, new GridPosition(8, 0)), Is.False);
            Assert.That(board.TryGetEntity(actor.Id, out BoardEntityState unchanged), Is.True);
            Assert.That(unchanged.Position, Is.EqualTo(start));

            Assert.That(board.TryMove(actor.Id, destination), Is.True);
            Assert.That(board.TryMove(actor.Id, destination), Is.True, "Moving to the current cell is a successful no-op.");
            Assert.That(board.TryGetEntity(BoardLayer.Actor, start, out _), Is.False);
            Assert.That(board.TryGetEntity(BoardLayer.Actor, destination, out BoardEntityState moved), Is.True);
            Assert.That(moved.Id, Is.EqualTo(actor.Id));
            Assert.That(board.TryGetEntity(BoardLayer.Terrain, destination, out _), Is.True);

            Assert.That(board.TryRemove(actor.Id), Is.True);
            Assert.That(board.TryRemove(actor.Id), Is.False);
            Assert.That(board.TryGetEntity(actor.Id, out _), Is.False);
            Assert.That(board.TryGetEntity(BoardLayer.Actor, destination, out _), Is.False);
        }

        /// <summary>
        /// Clones own separate collections and query snapshots cannot mutate board storage.
        /// </summary>
        [Test]
        public void CloneAndQuerySnapshotsDoNotShareMutableCollections()
        {
            var original = new BoardState();
            var item = CreateEntity(20, BoardLayer.Item, new GridPosition(2, 2));
            Assert.That(original.TryAdd(item), Is.True);

            BoardState clone = original.Clone();
            Assert.That(original.TryMove(item.Id, new GridPosition(3, 2)), Is.True);
            Assert.That(clone.TryRemove(item.Id), Is.True);
            Assert.That(clone.TryAdd(CreateEntity(21, BoardLayer.Item, new GridPosition(4, 4))), Is.True);

            Assert.That(original.TryGetEntity(item.Id, out BoardEntityState originalItem), Is.True);
            Assert.That(originalItem.Position, Is.EqualTo(new GridPosition(3, 2)));
            Assert.That(original.TryGetEntity(new EntityId(21), out _), Is.False);
            Assert.That(clone.TryGetEntity(item.Id, out _), Is.False);
            Assert.That(clone.TryGetEntity(new EntityId(21), out _), Is.True);

            var snapshot = (IList<BoardEntityState>)original.GetEntities();
            Assert.Throws<NotSupportedException>(() => snapshot.Add(CreateEntity(30, BoardLayer.Item, new GridPosition(5, 5))));
            Assert.That(original.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Entity enumeration is deterministic and invalid definitions or query positions are rejected explicitly.
        /// </summary>
        [Test]
        public void OrderingAndBoundariesAreExplicit()
        {
            var board = new BoardState();
            Assert.That(board.TryAdd(CreateEntity(9, BoardLayer.Item, new GridPosition(1, 1))), Is.True);
            Assert.That(board.TryAdd(CreateEntity(-1, BoardLayer.Terrain, new GridPosition(2, 1))), Is.True);
            Assert.That(board.TryAdd(CreateEntity(0, BoardLayer.Actor, new GridPosition(3, 1))), Is.True);

            IReadOnlyList<BoardEntityState> entities = board.GetEntities();
            Assert.That(entities[0].Id, Is.EqualTo(new EntityId(-1)));
            Assert.That(entities[1].Id, Is.EqualTo(new EntityId(0)));
            Assert.That(entities[2].Id, Is.EqualTo(new EntityId(9)));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.GetEntitiesAt(new GridPosition(8, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BoardEntityDefinition((BoardLayer)99, EntityKind.Item, "food"));
            Assert.Throws<ArgumentException>(() =>
                new BoardEntityDefinition(BoardLayer.Item, default(EntityKind), "food"));
            Assert.Throws<ArgumentException>(() =>
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, " food "));
        }

        /// <summary>
        /// The board-state domain assembly has no Unity runtime dependency.
        /// </summary>
        [Test]
        public void DomainAssemblyDoesNotReferenceUnity()
        {
            foreach (var reference in typeof(BoardState).Assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
            }
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
    }
}