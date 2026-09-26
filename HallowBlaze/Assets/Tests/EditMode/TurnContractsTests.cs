using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Turns.Contracts;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies player-command, turn-result, and ordered-event contracts without Unity runtime state.
    /// </summary>
    public class TurnContractsTests
    {
        /// <summary>
        /// Commands retain their discriminators and payloads while invalid movement intent is rejected.
        /// </summary>
        [Test]
        public void CommandsPreserveTheirDiscriminatorsAndPayloads()
        {
            var move = new MoveCommand(Direction.North);
            var wait = new WaitCommand();
            var interact = new InteractCommand(new EntityId(42));

            Assert.That(move.CommandType, Is.EqualTo("Move"));
            Assert.That(move.Direction, Is.EqualTo(Direction.North));
            Assert.That(wait.CommandType, Is.EqualTo("Wait"));
            Assert.That(interact.CommandType, Is.EqualTo("Interact"));
            Assert.That(interact.TargetId, Is.EqualTo(new EntityId(42)));
            Assert.Throws<ArgumentException>(() => new MoveCommand(default));
        }

        /// <summary>
        /// Events retain the board-local identifiers and positions supplied by the resolver.
        /// </summary>
        [Test]
        public void EventsPreserveBoardLocalIdsAndPositions()
        {
            var actorId = new EntityId(1);
            var targetId = new EntityId(2);
            var from = new GridPosition(0, 0);
            var to = new GridPosition(0, 1);
            var moved = new EntityMovedEvent(actorId, from, to);
            var waited = new EntityWaitedEvent(actorId);
            var interacted = new InteractionPerformedEvent(actorId, targetId);

            Assert.That(moved.EventType, Is.EqualTo("EntityMoved"));
            Assert.That(moved.EntityId, Is.EqualTo(actorId));
            Assert.That(moved.From, Is.EqualTo(from));
            Assert.That(moved.To, Is.EqualTo(to));
            Assert.That(waited.EventType, Is.EqualTo("EntityWaited"));
            Assert.That(waited.EntityId, Is.EqualTo(actorId));
            Assert.That(interacted.EventType, Is.EqualTo("InteractionPerformed"));
            Assert.That(interacted.ActorId, Is.EqualTo(actorId));
            Assert.That(interacted.TargetId, Is.EqualTo(targetId));
        }

        /// <summary>
        /// Accepted results consume one turn and retain event presentation order.
        /// </summary>
        [Test]
        public void AcceptedResultConsumesOneTurnAndPreservesEventOrder()
        {
            var events = new GameEvent[]
            {
                new EntityMovedEvent(new EntityId(1), new GridPosition(0, 0), new GridPosition(0, 1)),
                new EntityWaitedEvent(new EntityId(2)),
                new InteractionPerformedEvent(new EntityId(1), new EntityId(3))
            };

            var result = new AcceptedTurnResult(events);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ConsumesTurn, Is.True);
            Assert.That(result.Events, Is.EqualTo(events));
        }

        /// <summary>
        /// Accepted results do not share their event collection with callers and expose it read-only.
        /// </summary>
        [Test]
        public void AcceptedResultOwnsAnImmutableEventSnapshot()
        {
            var source = new List<GameEvent>
            {
                new EntityWaitedEvent(new EntityId(1))
            };
            var result = new AcceptedTurnResult(source);

            source.Add(new EntityWaitedEvent(new EntityId(2)));

            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<GameEvent>)result.Events).Add(new EntityWaitedEvent(new EntityId(3))));
        }

        /// <summary>
        /// Accepted results reject null collections and null event entries.
        /// </summary>
        [Test]
        public void AcceptedResultRejectsNullCollectionsAndEntries()
        {
            Assert.Throws<ArgumentNullException>(() => new AcceptedTurnResult(null));
            Assert.Throws<ArgumentException>(() => new AcceptedTurnResult(new GameEvent[]
            {
                new EntityWaitedEvent(new EntityId(1)),
                null
            }));
        }

        /// <summary>
        /// Every defined rejection reason produces a result with no turn cost or events.
        /// </summary>
        [Test]
        public void EveryDefinedReasonCreatesACostFreeRejectedResult()
        {
            foreach (CommandRejectionCode code in Enum.GetValues(typeof(CommandRejectionCode)))
            {
                if (code == CommandRejectionCode.None)
                {
                    continue;
                }

                var result = new RejectedTurnResult(code);
                Assert.That(result.Accepted, Is.False, code.ToString());
                Assert.That(result.ConsumesTurn, Is.False, code.ToString());
                Assert.That(result.RejectionCode, Is.EqualTo(code));
                Assert.That(result.Events, Is.Empty);
            }
        }

        /// <summary>
        /// Rejected results require a defined, non-default machine-readable reason.
        /// </summary>
        [Test]
        public void RejectedResultRequiresADefinedNonDefaultReason()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RejectedTurnResult(CommandRejectionCode.None));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RejectedTurnResult((CommandRejectionCode)999));
        }

        /// <summary>
        /// Rejection codes retain their explicitly assigned stable numeric values.
        /// </summary>
        [Test]
        public void RejectionCodeNumericValuesAreStable()
        {
            Assert.That((int)CommandRejectionCode.None, Is.EqualTo(0));
            Assert.That((int)CommandRejectionCode.InvalidCommand, Is.EqualTo(1));
            Assert.That((int)CommandRejectionCode.InvalidState, Is.EqualTo(2));
            Assert.That((int)CommandRejectionCode.InvalidTarget, Is.EqualTo(3));
            Assert.That((int)CommandRejectionCode.OutOfBounds, Is.EqualTo(4));
            Assert.That((int)CommandRejectionCode.Blocked, Is.EqualTo(5));
            Assert.That((int)CommandRejectionCode.NoInteractionAvailable, Is.EqualTo(6));
        }

        /// <summary>
        /// The turn-contract assembly remains independent of Unity runtime assemblies.
        /// </summary>
        [Test]
        public void ContractsAssemblyDoesNotReferenceUnity()
        {
            foreach (var reference in typeof(PlayerCommand).Assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
            }
        }
    }
}
