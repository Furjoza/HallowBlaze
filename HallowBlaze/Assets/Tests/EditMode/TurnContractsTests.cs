using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Turns.Contracts;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies command, enemy-intent, turn-result, and ordered-event contracts without Unity runtime state.
    /// </summary>
    public class TurnContractsTests
    {
        /// <summary>
        /// Hits expose actual signed HP effects, including clamped or zero effects, without recalculation.
        /// </summary>
        [TestCase(-10)]
        [TestCase(-20)]
        [TestCase(-3)]
        [TestCase(0)]
        public void AttackHitPreservesActualEffect(int healthChange)
        {
            var attackerId = new EntityId(0);
            var playerId = new EntityId(-1);
            var cell = new GridPosition(0, 0);
            var resolved = new EnemyAttackResolvedEvent(attackerId, cell, true, playerId, healthChange);

            Assert.That(resolved.EventType, Is.EqualTo("EnemyAttackResolved"));
            Assert.That(resolved.AttackerId, Is.EqualTo(attackerId));
            Assert.That(resolved.TargetPosition, Is.EqualTo(cell));
            Assert.That(resolved.IsHit, Is.True);
            Assert.That(resolved.AffectedTargetId, Is.EqualTo(playerId));
            Assert.That(resolved.HealthChange, Is.EqualTo(healthChange));
        }

        /// <summary>
        /// A miss keeps the announced cell without inventing a target or health effect.
        /// </summary>
        [Test]
        public void AttackMissPreservesFixedCellAndZeroEffect()
        {
            var attacker = new EntityId(long.MinValue);
            var cell = new GridPosition(int.MinValue, int.MaxValue);
            var resolved = new EnemyAttackResolvedEvent(attacker, cell, false, null, 0);

            Assert.That(resolved.AttackerId, Is.EqualTo(attacker));
            Assert.That(resolved.TargetPosition, Is.EqualTo(cell));
            Assert.That(resolved.IsHit, Is.False);
            Assert.That(resolved.AffectedTargetId, Is.Null);
            Assert.That(resolved.HealthChange, Is.Zero);
        }

        /// <summary>
        /// Contradictory hit, target, healing, and miss-damage payloads cannot become event facts.
        /// </summary>
        [Test]
        public void AttackEventRejectsContradictoryPayloads()
        {
            var attacker = new EntityId(0);
            var player = new EntityId(-1);
            var cell = new GridPosition(0, 0);

            Assert.Throws<ArgumentException>(() => new EnemyAttackResolvedEvent(attacker, cell, true, null, -10));
            Assert.Throws<ArgumentException>(() => new EnemyAttackResolvedEvent(attacker, cell, true, attacker, -10));
            Assert.Throws<ArgumentException>(() => new EnemyAttackResolvedEvent(attacker, cell, false, player, 0));
            Assert.Throws<ArgumentException>(() => new EnemyAttackResolvedEvent(attacker, cell, false, null, -10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyAttackResolvedEvent(attacker, cell, true, player, 10));
        }

        /// <summary>
        /// Caller value changes cannot alter an event, whose payload is sealed and getter-only.
        /// </summary>
        [Test]
        public void AttackEventOwnsImmutablePayload()
        {
            var attacker = new EntityId(0);
            var player = new EntityId(-1);
            var cell = new GridPosition(0, 0);
            var resolved = new EnemyAttackResolvedEvent(attacker, cell, true, player, -20);
            attacker = new EntityId(10);
            player = new EntityId(11);
            cell = new GridPosition(5, 5);

            Assert.That(resolved.AttackerId, Is.EqualTo(new EntityId(0)));
            Assert.That(resolved.AffectedTargetId, Is.EqualTo(new EntityId(-1)));
            Assert.That(resolved.TargetPosition, Is.EqualTo(new GridPosition(0, 0)));
            Assert.That(resolved.HealthChange, Is.EqualTo(-20));
            Assert.That(typeof(EnemyAttackResolvedEvent).IsSealed, Is.True);
            foreach (var property in typeof(EnemyAttackResolvedEvent).GetProperties())
                Assert.That(property.CanWrite, Is.False, property.Name);
        }

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
            Assert.That(typeof(EnemyIntent).Assembly, Is.SameAs(typeof(PlayerCommand).Assembly));
            foreach (var reference in typeof(PlayerCommand).Assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
            }
        }

        /// <summary>
        /// Each intent retains its actor, action, fixed target, and declared player-entry policy.
        /// </summary>
        [Test]
        public void EnemyIntentsPreserveTheirActionSpecificPayloads()
        {
            var actorId = new EntityId(1);
            var playerId = new EntityId(2);
            var destination = new GridPosition(0, 1);
            var attackCell = new GridPosition(1, 0);
            var move = new EnemyIntent(actorId, EnemyIntentKind.Move, destination, playerId, attackOnPlayerEntry: true);
            var attack = new EnemyIntent(actorId, EnemyIntentKind.Attack, attackCell, playerId);
            var wait = new EnemyIntent(actorId, EnemyIntentKind.Wait);

            Assert.That(move.ActorId, Is.EqualTo(actorId));
            Assert.That(move.Kind, Is.EqualTo(EnemyIntentKind.Move));
            Assert.That(move.TargetPosition, Is.EqualTo(destination));
            Assert.That(move.TargetId, Is.EqualTo(playerId));
            Assert.That(move.AttackOnPlayerEntry, Is.True);
            Assert.That(attack.ActorId, Is.EqualTo(actorId));
            Assert.That(attack.Kind, Is.EqualTo(EnemyIntentKind.Attack));
            Assert.That(attack.TargetPosition, Is.EqualTo(attackCell));
            Assert.That(attack.TargetId, Is.EqualTo(playerId));
            Assert.That(attack.AttackOnPlayerEntry, Is.False);
            Assert.That(wait.ActorId, Is.EqualTo(actorId));
            Assert.That(wait.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(wait.TargetPosition, Is.Null);
            Assert.That(wait.TargetId, Is.Null);
            Assert.That(wait.AttackOnPlayerEntry, Is.False);
        }

        /// <summary>
        /// Zero, negative and extreme primitive values are retained without sentinel or board-bound checks.
        /// </summary>
        [TestCase(0L, -1L, 0, 0)]
        [TestCase(-42L, 0L, -10, -20)]
        [TestCase(long.MinValue, long.MaxValue, int.MinValue, int.MaxValue)]
        public void EnemyIntentsPreserveValidPrimitiveValues(long actor, long player, int x, int y)
        {
            var actorId = new EntityId(actor);
            var playerId = new EntityId(player);
            var targetCell = new GridPosition(x, y);
            var move = new EnemyIntent(actorId, EnemyIntentKind.Move, targetCell, playerId, attackOnPlayerEntry: true);
            var attack = new EnemyIntent(actorId, EnemyIntentKind.Attack, targetCell, playerId);
            var wait = new EnemyIntent(actorId, EnemyIntentKind.Wait);

            Assert.That(move.ActorId, Is.EqualTo(actorId));
            Assert.That(move.TargetPosition, Is.EqualTo(targetCell));
            Assert.That(move.TargetId, Is.EqualTo(playerId));
            Assert.That(attack.ActorId, Is.EqualTo(actorId));
            Assert.That(attack.TargetPosition, Is.EqualTo(targetCell));
            Assert.That(attack.TargetId, Is.EqualTo(playerId));
            Assert.That(wait.ActorId, Is.EqualTo(actorId));
        }

        /// <summary>
        /// Undefined action discriminators fail at creation rather than producing an ambiguous intent.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void EnemyIntentsRejectUndefinedKinds(int kind)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EnemyIntent(new EntityId(0), (EnemyIntentKind)kind));

            Assert.That(error.ParamName, Is.EqualTo("kind"));
        }

        /// <summary>
        /// Targeted actions require both a recorded cell and a distinct original player identity.
        /// </summary>
        [TestCase(EnemyIntentKind.Move)]
        [TestCase(EnemyIntentKind.Attack)]
        public void TargetedEnemyIntentsRejectMissingAndSelfTargets(EnemyIntentKind kind)
        {
            var actorId = new EntityId(0);
            var playerId = new EntityId(-1);
            var targetCell = new GridPosition(0, 0);
            var conditionalAttack = kind == EnemyIntentKind.Move;
            var missingCell = Assert.Throws<ArgumentException>(() =>
                new EnemyIntent(actorId, kind, targetId: playerId, attackOnPlayerEntry: conditionalAttack));
            var missingIdentity = Assert.Throws<ArgumentException>(() =>
                new EnemyIntent(actorId, kind, targetCell, attackOnPlayerEntry: conditionalAttack));
            var selfTarget = Assert.Throws<ArgumentException>(() =>
                new EnemyIntent(actorId, kind, targetCell, actorId, conditionalAttack));

            Assert.That(missingCell.ParamName, Is.EqualTo("targetPosition"));
            Assert.That(missingIdentity.ParamName, Is.EqualTo("targetId"));
            Assert.That(selfTarget.ParamName, Is.EqualTo("targetId"));
        }

        /// <summary>
        /// A move cannot omit its declared same-cell attack condition or attach it to a planned attack.
        /// </summary>
        [Test]
        public void EnemyIntentsRejectContradictoryAttackConditions()
        {
            var actorId = new EntityId(0);
            var playerId = new EntityId(-1);
            var targetCell = new GridPosition(0, 0);
            var missingCondition = Assert.Throws<ArgumentException>(() =>
                new EnemyIntent(actorId, EnemyIntentKind.Move, targetCell, playerId));
            var attackCondition = Assert.Throws<ArgumentException>(() =>
                new EnemyIntent(actorId, EnemyIntentKind.Attack, targetCell, playerId, attackOnPlayerEntry: true));

            Assert.That(missingCondition.ParamName, Is.EqualTo("attackOnPlayerEntry"));
            Assert.That(attackCondition.ParamName, Is.EqualTo("attackOnPlayerEntry"));
        }

        /// <summary>
        /// Wait rejects target fields and conditional attacks, including otherwise valid zero payloads.
        /// </summary>
        [Test]
        public void WaitIntentRejectsTargetsAndAttackConditions()
        {
            var actorId = new EntityId(-1);
            var cell = Assert.Throws<ArgumentException>(() =>
                new EnemyIntent(actorId, EnemyIntentKind.Wait, targetPosition: new GridPosition(0, 0)));
            var identity = Assert.Throws<ArgumentException>(() =>
                new EnemyIntent(actorId, EnemyIntentKind.Wait, targetId: new EntityId(0)));
            var condition = Assert.Throws<ArgumentException>(() =>
                new EnemyIntent(actorId, EnemyIntentKind.Wait, attackOnPlayerEntry: true));
            Assert.Throws<ArgumentException>(() =>
                new EnemyIntent(actorId, EnemyIntentKind.Wait, new GridPosition(0, 0), new EntityId(0), true));

            Assert.That(cell.ParamName, Is.EqualTo("targetPosition"));
            Assert.That(identity.ParamName, Is.EqualTo("targetId"));
            Assert.That(condition.ParamName, Is.EqualTo("attackOnPlayerEntry"));
        }

        /// <summary>
        /// Replacing caller-owned identities and cells cannot change an already recorded action or target.
        /// </summary>
        [Test]
        public void EnemyIntentsOwnImmutableTargetSnapshots()
        {
            var actorId = new EntityId(0);
            var playerIds = new[] { new EntityId(-1) };
            var cells = new[] { new GridPosition(0, 0), new GridPosition(-3, 4) };
            var move = new EnemyIntent(actorId, EnemyIntentKind.Move, cells[0], playerIds[0], attackOnPlayerEntry: true);
            var attack = new EnemyIntent(actorId, EnemyIntentKind.Attack, cells[1], playerIds[0]);
            var wait = new EnemyIntent(actorId, EnemyIntentKind.Wait);

            actorId = new EntityId(99);
            playerIds[0] = new EntityId(42);
            cells[0] = new GridPosition(9, 9);
            cells[1] = new GridPosition(8, 8);

            Assert.That(move.ActorId, Is.EqualTo(new EntityId(0)));
            Assert.That(move.TargetId, Is.EqualTo(new EntityId(-1)));
            Assert.That(move.TargetPosition, Is.EqualTo(new GridPosition(0, 0)));
            Assert.That(move.Kind, Is.EqualTo(EnemyIntentKind.Move));
            Assert.That(move.AttackOnPlayerEntry, Is.True);
            Assert.That(attack.ActorId, Is.EqualTo(new EntityId(0)));
            Assert.That(attack.TargetId, Is.EqualTo(new EntityId(-1)));
            Assert.That(attack.TargetPosition, Is.EqualTo(new GridPosition(-3, 4)));
            Assert.That(attack.Kind, Is.EqualTo(EnemyIntentKind.Attack));
            Assert.That(attack.AttackOnPlayerEntry, Is.False);
            Assert.That(wait.ActorId, Is.EqualTo(new EntityId(0)));
            Assert.That(wait.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(wait.TargetId, Is.Null);
            Assert.That(wait.TargetPosition, Is.Null);
            Assert.That(wait.AttackOnPlayerEntry, Is.False);
        }

        /// <summary>
        /// The intent cannot be extended with mutable state and exposes only getter-only value payloads.
        /// </summary>
        [Test]
        public void EnemyIntentHasNoMutablePublicPayload()
        {
            Assert.That(typeof(EnemyIntent).IsSealed, Is.True);
            foreach (var property in typeof(EnemyIntent).GetProperties())
            {
                Assert.That(property.CanWrite, Is.False, property.Name);
                Assert.That(property.PropertyType.IsValueType, Is.True, property.Name);
            }
        }
    }
}
