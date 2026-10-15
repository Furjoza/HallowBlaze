using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies board-owned hearing memory using the exact emitted footprint and lifetime.</summary>
    public sealed class ListenerNoiseMemoryTests
    {
        private static readonly EntityId ListenerId = new EntityId(1);

        /// <summary>The newest audible emission wins regardless of source identity or strength.</summary>
        [Test]
        public void LatestAudibleEmissionWinsRatherThanLoudest()
        {
            BoardState board = CreateBoard();
            board.RegisterListener(ListenerId);
            board.EmitNoise(new EntityId(8), new GridPosition(3, 1), 5, 3);
            BoardNoise latest = board.EmitNoise(new EntityId(9), new GridPosition(1, 2), 1, 3);
            board.EmitNoise(new EntityId(10), new GridPosition(7, 7), 1, 3);
            Assert.That(board.TryGetListenerNoise(ListenerId, out BoardNoise heard), Is.True);
            Assert.That(heard, Is.SameAs(latest));
            Assert.That(heard.IsAudible(new GridPosition(1, 1)), Is.True);
        }

        /// <summary>Hearing captures the actor's cell at emission, not a later position on the footprint.</summary>
        [Test]
        public void MovingIntoAnOldFootprintDoesNotHearRetroactively()
        {
            BoardState board = CreateBoard();
            board.RegisterListener(ListenerId);
            board.EmitNoise(new EntityId(8), new GridPosition(4, 1), 1, 3);
            Assert.That(board.TryMove(ListenerId, new GridPosition(3, 1)), Is.True);
            Assert.That(board.TryGetListenerNoise(ListenerId, out _), Is.False);
        }

        /// <summary>Moving away does not forget a heard signal, but expiration does.</summary>
        [Test]
        public void HeardTargetSurvivesMovementButExpiresWithNoise()
        {
            BoardState board = CreateBoard();
            board.RegisterListener(ListenerId);
            BoardNoise noise = board.EmitNoise(new EntityId(8), new GridPosition(1, 2), 1, 2);
            Assert.That(board.TryMove(ListenerId, new GridPosition(7, 7)), Is.True);
            board.AdvanceNoiseLifetime();
            Assert.That(board.TryGetListenerNoise(ListenerId, out BoardNoise aged), Is.True);
            Assert.That(aged.Id, Is.EqualTo(noise.Id));
            Assert.That(aged.RemainingTurns, Is.EqualTo(1));
            Assert.That(noise.RemainingTurns, Is.EqualTo(2));
            board.AdvanceNoiseLifetime();
            Assert.That(board.TryGetListenerNoise(ListenerId, out _), Is.False);
        }

        /// <summary>Forgetting a newer expired signal never revives an older louder one.</summary>
        [Test]
        public void ExpirationDoesNotFallBackToOlderNoise()
        {
            BoardState board = CreateBoard();
            board.RegisterListener(ListenerId);
            board.EmitNoise(new EntityId(8), new GridPosition(2, 1), 3, 5);
            board.EmitNoise(new EntityId(9), new GridPosition(1, 2), 1, 1);
            board.AdvanceNoiseLifetime();
            Assert.That(board.GetActiveNoises().Count, Is.EqualTo(1));
            Assert.That(board.TryGetListenerNoise(ListenerId, out _), Is.False);
        }

        /// <summary>Clones retain independent hearing registrations and current immutable signal values.</summary>
        [Test]
        public void CloneAndFreshBoardsHaveIndependentMemory()
        {
            BoardState board = CreateBoard();
            board.RegisterListener(ListenerId);
            BoardNoise first = board.EmitNoise(new EntityId(8), new GridPosition(2, 1), 2, 3);
            BoardState clone = board.Clone();
            board.EmitNoise(new EntityId(9), new GridPosition(1, 2), 2, 3);
            Assert.That(clone.TryGetListenerNoise(ListenerId, out BoardNoise heard), Is.True);
            Assert.That(heard, Is.SameAs(first));
            Assert.That(new BoardState().TryGetListenerNoise(ListenerId, out _), Is.False);
            Assert.That(clone.TryRemove(ListenerId), Is.True);
            Assert.That(clone.TryGetListenerNoise(ListenerId, out _), Is.False);
            Assert.That(board.TryGetListenerNoise(ListenerId, out _), Is.True);
        }

        /// <summary>Invalid/duplicate registration cannot change hearing or infer past emissions.</summary>
        [Test]
        public void RegistrationIsExplicitAndDoesNotHearThePast()
        {
            BoardState board = CreateBoard();
            board.EmitNoise(new EntityId(8), new GridPosition(2, 1), 2, 3);
            Assert.Throws<ArgumentException>(() => board.RegisterListener(new EntityId(999)));
            board.RegisterListener(ListenerId);
            Assert.Throws<ArgumentException>(() => board.RegisterListener(ListenerId));
            Assert.That(board.TryGetListenerNoise(ListenerId, out _), Is.False);
        }

        /// <summary>The domain/UI footprint, not direct Manhattan distance, decides hearing through obstacles.</summary>
        [Test]
        public void ImpassableObstacleBlocksHearing()
        {
            BoardState board = CreateBoard();
            board.RegisterListener(ListenerId);
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(900),
                new BoardEntityDefinition(BoardLayer.Obstacle, EntityKind.Obstacle, "wall"), new GridPosition(2, 1))), Is.True);
            BoardNoise noise = board.EmitNoise(new EntityId(8), new GridPosition(3, 1), 2, 3);
            Assert.That(noise.IsAudible(new GridPosition(1, 1)), Is.False);
            Assert.That(board.TryGetListenerNoise(ListenerId, out _), Is.False);
        }

        private static BoardState CreateBoard()
        {
            var board = new BoardState();
            for (int cellIndex = 0; cellIndex < 64; cellIndex++)
                Assert.That(board.TryAdd(new BoardEntityState(new EntityId(100 + cellIndex),
                    new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "ground", new BoardEntityTraits(true, false, false)),
                    new GridPosition(cellIndex / 8, cellIndex % 8))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(ListenerId,
                new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "listener"), new GridPosition(1, 1))), Is.True);
            return board;
        }
    }
}