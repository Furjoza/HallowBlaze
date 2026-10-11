using System;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies detached intent display values without gameplay prediction or mutation.</summary>
    public sealed class EnemyIntentProjectionTests
    {
        /// <summary>
        /// Accepted actions copy exact retained fields; examples do not expand domain actions,
        /// and invalid inputs fail explicitly without changing already projected values.
        /// </summary>
        [TestCase("conditional-move")]
        [TestCase("attack")]
        [TestCase("wait")]
        [TestCase("move-example")]
        [TestCase("investigate-example")]
        [TestCase("invalid")]
        public void RetainedIntent_ProjectsWithoutMutation(string action)
        {
            var actorId = new EntityId(0);
            var playerId = new EntityId(-1);
            var source = new GridPosition(0, 0);
            var target = new GridPosition(0, 1);
            if (action == "invalid")
            {
                Assert.Throws<ArgumentNullException>(() => EnemyIntentProjection.FromRetainedIntent(null, source));
                Assert.Throws<ArgumentOutOfRangeException>(() => EnemyIntentProjection.CreateExample(actorId, source,
                    (EnemyIntentSymbol)0));
                Assert.Throws<ArgumentOutOfRangeException>(() => EnemyIntentProjection.CreateExample(actorId, source,
                    (EnemyIntentSymbol)99));
                Assert.Throws<ArgumentException>(() => EnemyIntentProjection.CreateExample(actorId, source,
                    EnemyIntentSymbol.Wait, target));
                Assert.Throws<ArgumentException>(() => EnemyIntentProjection.CreateExample(actorId, source,
                    EnemyIntentSymbol.Wait, targetId: playerId));
                foreach (EnemyIntentSymbol symbol in new[] { EnemyIntentSymbol.Move, EnemyIntentSymbol.ConditionalMove,
                    EnemyIntentSymbol.Attack, EnemyIntentSymbol.Investigate })
                    Assert.Throws<ArgumentException>(() => EnemyIntentProjection.CreateExample(actorId, source, symbol));
                foreach (EnemyIntentSymbol symbol in new[] { EnemyIntentSymbol.ConditionalMove, EnemyIntentSymbol.Attack })
                {
                    Assert.Throws<ArgumentException>(() => EnemyIntentProjection.CreateExample(actorId, source, symbol, target));
                    Assert.Throws<ArgumentException>(() => EnemyIntentProjection.CreateExample(actorId, source, symbol, target, actorId));
                }
                foreach (EnemyIntentSymbol symbol in new[] { EnemyIntentSymbol.Move, EnemyIntentSymbol.Investigate })
                    Assert.Throws<ArgumentException>(() => EnemyIntentProjection.CreateExample(actorId, source, symbol, target, playerId));
                Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyIntent(actorId, (EnemyIntentKind)99));
                return;
            }
            EnemyIntentSymbol expectedSymbol;
            EnemyIntent intent;
            switch (action)
            {
                case "conditional-move":
                    expectedSymbol = EnemyIntentSymbol.ConditionalMove;
                    intent = new EnemyIntent(actorId, EnemyIntentKind.Move, target, playerId, true);
                    break;
                case "attack":
                    expectedSymbol = EnemyIntentSymbol.Attack;
                    intent = new EnemyIntent(actorId, EnemyIntentKind.Attack, target, playerId);
                    break;
                case "wait":
                    expectedSymbol = EnemyIntentSymbol.Wait;
                    intent = new EnemyIntent(actorId, EnemyIntentKind.Wait);
                    break;
                case "move-example":
                    expectedSymbol = EnemyIntentSymbol.Move;
                    intent = null;
                    break;
                case "investigate-example":
                    expectedSymbol = EnemyIntentSymbol.Investigate;
                    intent = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
            var state = new ShamblerState(actorId, new ShamblerDefinition(10));
            state.LockIntent(intent ?? new EnemyIntent(actorId, EnemyIntentKind.Wait));
            EnemyIntent locked = state.LockedIntent;
            var board = new BoardState();
            var definition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy,
                "projection.enemy", BoardEntityTraits.Default);
            Assert.That(board.TryAdd(new BoardEntityState(actorId, definition, source)), Is.True);
            var before = board.GetEntities();
            GridPosition? expectedTarget = expectedSymbol == EnemyIntentSymbol.Wait ? (GridPosition?)null : target;
            EntityId? expectedTargetId = intent?.TargetId;

            EnemyIntentProjection projection = intent != null
                ? EnemyIntentProjection.FromRetainedIntent(intent, source)
                : EnemyIntentProjection.CreateExample(actorId, source, expectedSymbol, expectedTarget);
            EnemyIntentProjection repeated = intent != null
                ? EnemyIntentProjection.FromRetainedIntent(intent, source)
                : EnemyIntentProjection.CreateExample(actorId, source, expectedSymbol, expectedTarget);

            Assert.That(projection.SourceId, Is.EqualTo(actorId));
            Assert.That(projection.SourcePosition, Is.EqualTo(source));
            Assert.That(projection.Symbol, Is.EqualTo(expectedSymbol));
            Assert.That(projection.TargetPosition, Is.EqualTo(expectedTarget));
            Assert.That(projection.TargetId, Is.EqualTo(expectedTargetId));
            Assert.That(projection.AttackOnPlayerEntry, Is.EqualTo(expectedSymbol == EnemyIntentSymbol.ConditionalMove));
            Assert.That(projection, Is.EqualTo(repeated));
            Assert.That(projection.GetHashCode(), Is.EqualTo(repeated.GetHashCode()));
            Assert.That(projection.Equals(null), Is.False);
            Assert.That(projection.Equals(new object()), Is.False);
            Assert.That(projection, Is.Not.EqualTo(EnemyIntentProjection.CreateExample(new EntityId(5), source,
                expectedSymbol, expectedTarget, expectedTargetId)));
            Assert.That(projection, Is.Not.EqualTo(EnemyIntentProjection.CreateExample(actorId, new GridPosition(1, 0),
                expectedSymbol, expectedTarget, expectedTargetId)));
            Assert.That(typeof(EnemyIntentProjection).GetProperties().All(property => property.SetMethod == null), Is.True);
            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(state.LockedIntent, Is.SameAs(locked));

            state.ConsumeIntent();
            state.LockIntent(new EnemyIntent(actorId, EnemyIntentKind.Wait));
            Assert.That(board.TryMove(actorId, new GridPosition(1, 0)), Is.True);
            source = new GridPosition(1, 0);

            Assert.That(projection.SourcePosition, Is.EqualTo(new GridPosition(0, 0)));
            Assert.That(projection.TargetPosition, Is.EqualTo(expectedTarget));
            Assert.That(projection.TargetId, Is.EqualTo(expectedTargetId));
            Assert.That(projection.Symbol, Is.EqualTo(expectedSymbol));
            Assert.That(projection, Is.EqualTo(repeated));
            if (intent != null)
            {
                Assert.That(intent.TargetPosition, Is.EqualTo(expectedTarget));
                Assert.That(intent.TargetId, Is.EqualTo(expectedTargetId));
            }
        }
    }
}