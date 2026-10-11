using System;
using System.Reflection;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies pure Shambler configuration and board-local intent/cadence lifecycle.
    /// </summary>
    public class ShamblerModelTests
    {
        /// <summary>
        /// Both existing variants share the named rule, active start, alternating cadence, and attack range.
        /// </summary>
        [TestCase(10)]
        [TestCase(20)]
        public void DefinitionsExposeTheAcceptedRuleAndParameters(int damage)
        {
            var definition = new ShamblerDefinition(damage);

            Assert.That(definition.RuleName, Is.EqualTo("Shortest-path pursuit with alternating rest"));
            Assert.That(definition.InitialPhase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(definition.ActivePhaseTurns, Is.EqualTo(1));
            Assert.That(definition.RestPhaseTurns, Is.EqualTo(1));
            Assert.That(definition.AttackRange, Is.EqualTo(1));
            Assert.That(definition.OrthogonalAttacksOnly, Is.True);
            Assert.That(definition.Damage, Is.EqualTo(damage));
        }

        /// <summary>
        /// Explicitly supplied approved configuration has the same contract as the defaults.
        /// </summary>
        [TestCase(10)]
        [TestCase(20)]
        public void DefinitionsAcceptExplicitApprovedConfiguration(int damage)
        {
            var definition = new ShamblerDefinition(damage, activePhaseTurns: 1, restPhaseTurns: 1, attackRange: 1);
            var defaults = new ShamblerDefinition(damage);

            Assert.That(definition.RuleName, Is.EqualTo(defaults.RuleName));
            Assert.That(definition.InitialPhase, Is.EqualTo(defaults.InitialPhase));
            Assert.That(definition.ActivePhaseTurns, Is.EqualTo(defaults.ActivePhaseTurns));
            Assert.That(definition.RestPhaseTurns, Is.EqualTo(defaults.RestPhaseTurns));
            Assert.That(definition.AttackRange, Is.EqualTo(defaults.AttackRange));
            Assert.That(definition.OrthogonalAttacksOnly, Is.EqualTo(defaults.OrthogonalAttacksOnly));
            Assert.That(definition.Damage, Is.EqualTo(defaults.Damage));
        }

        /// <summary>
        /// Damage outside the two existing deterministic variants fails at creation, including boundaries.
        /// </summary>
        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(9)]
        [TestCase(11)]
        [TestCase(19)]
        [TestCase(21)]
        [TestCase(int.MaxValue)]
        public void DefinitionsRejectUnapprovedDamage(int damage)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => new ShamblerDefinition(damage));

            Assert.That(error.ParamName, Is.EqualTo("damage"));
            Assert.That(error.ActualValue, Is.EqualTo(damage));
        }

        /// <summary>
        /// The accepted cadence cannot skip its active phase or allow multiple consecutive active phases.
        /// </summary>
        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void DefinitionsRejectUnapprovedActivePhaseLength(int activePhaseTurns)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ShamblerDefinition(10, activePhaseTurns: activePhaseTurns));

            Assert.That(error.ParamName, Is.EqualTo("activePhaseTurns"));
            Assert.That(error.ActualValue, Is.EqualTo(activePhaseTurns));
        }

        /// <summary>
        /// The accepted cadence cannot skip its rest phase or introduce a different rest duration.
        /// </summary>
        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void DefinitionsRejectUnapprovedRestPhaseLength(int restPhaseTurns)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ShamblerDefinition(20, restPhaseTurns: restPhaseTurns));

            Assert.That(error.ParamName, Is.EqualTo("restPhaseTurns"));
            Assert.That(error.ActualValue, Is.EqualTo(restPhaseTurns));
        }

        /// <summary>
        /// The accepted attack range cannot be zero, negative, or extend beyond one orthogonal cell.
        /// </summary>
        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void DefinitionsRejectUnapprovedAttackRange(int attackRange)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ShamblerDefinition(10, attackRange: attackRange));

            Assert.That(error.ParamName, Is.EqualTo("attackRange"));
            Assert.That(error.ActualValue, Is.EqualTo(attackRange));
        }

        /// <summary>
        /// Definitions cannot be extended with mutable state or expose collections, setters, or domain operations.
        /// </summary>
        [Test]
        public void DefinitionExposesOnlyImmutableData()
        {
            var definitionType = typeof(ShamblerDefinition);

            Assert.That(definitionType.IsSealed, Is.True);
            Assert.That(definitionType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static), Is.Empty);
            foreach (var property in definitionType.GetProperties())
            {
                Assert.That(property.CanWrite, Is.False, property.Name);
                Assert.That(property.PropertyType.IsValueType || property.PropertyType == typeof(string), Is.True, property.Name);
            }
            foreach (var field in definitionType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Assert.That(field.IsInitOnly, Is.True, field.Name);
            }
            foreach (var method in definitionType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.That(method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal), Is.True, method.Name);
            }
            foreach (var constructor in definitionType.GetConstructors())
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    Assert.That(parameter.ParameterType.IsValueType, Is.True, parameter.Name);
                }
            }
        }

        /// <summary>
        /// Both variants start active without a previous-board intent and retain their definition.
        /// </summary>
        [TestCase(10)]
        [TestCase(20)]
        public void FreshStateStartsActiveWithoutAnIntent(int damage)
        {
            var actorId = new EntityId(1);
            var definition = new ShamblerDefinition(damage);
            var state = new ShamblerState(actorId, definition);

            Assert.That(state.ActorId, Is.EqualTo(actorId));
            Assert.That(state.Definition, Is.SameAs(definition));
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(state.LockedIntent, Is.Null);
        }

        /// <summary>
        /// State does not introduce an invalid-ID sentinel beyond the existing EntityId contract.
        /// </summary>
        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void StatePreservesPrimitiveActorIds(int value)
        {
            var actorId = new EntityId(value);
            var state = new ShamblerState(actorId, new ShamblerDefinition(10));
            var intent = new EnemyIntent(actorId, EnemyIntentKind.Wait);

            state.LockIntent(intent);

            Assert.That(state.ActorId, Is.EqualTo(actorId));
            Assert.That(state.ConsumeIntent(), Is.SameAs(intent));
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// State requires an existing validated definition rather than an implicit default rule.
        /// </summary>
        [Test]
        public void StateRejectsANullDefinition()
        {
            var error = Assert.Throws<ArgumentNullException>(() => new ShamblerState(new EntityId(1), null));

            Assert.That(error.ParamName, Is.EqualTo("definition"));
        }

        /// <summary>
        /// Locking and repeated reads preserve the exact plan and its payload without consuming a phase.
        /// </summary>
        [TestCase(EnemyIntentKind.Move)]
        [TestCase(EnemyIntentKind.Attack)]
        [TestCase(EnemyIntentKind.Wait)]
        public void LockingRetainsTheExactIntentWithoutAdvancingCadence(EnemyIntentKind kind)
        {
            var state = new ShamblerState(new EntityId(1), new ShamblerDefinition(10));
            var intent = CreateIntent(state.ActorId, kind);

            state.LockIntent(intent);
            for (var readIndex = 0; readIndex < 3; readIndex++)
            {
                Assert.That(state.LockedIntent, Is.SameAs(intent));
                Assert.That(state.LockedIntent.Kind, Is.EqualTo(kind));
                Assert.That(state.LockedIntent.TargetId, Is.EqualTo(intent.TargetId));
                Assert.That(state.LockedIntent.TargetPosition, Is.EqualTo(intent.TargetPosition));
                Assert.That(state.LockedIntent.AttackOnPlayerEntry, Is.EqualTo(intent.AttackOnPlayerEntry));
                Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Active));
            }
        }

        /// <summary>
        /// Null plans and plans belonging to another actor fail without changing either lifecycle phase.
        /// </summary>
        [TestCase(ShamblerPhase.Active)]
        [TestCase(ShamblerPhase.Rest)]
        public void InvalidLocksPreserveTheCurrentPhase(ShamblerPhase phase)
        {
            var state = CreateStateInPhase(phase);
            var foreignIntent = CreateIntent(new EntityId(2), EnemyIntentKind.Wait);

            var nullError = Assert.Throws<ArgumentNullException>(() => state.LockIntent(null));
            var actorError = Assert.Throws<ArgumentException>(() => state.LockIntent(foreignIntent));

            Assert.That(nullError.ParamName, Is.EqualTo("intent"));
            Assert.That(actorError.ParamName, Is.EqualTo("intent"));
            Assert.That(state.LockedIntent, Is.Null);
            Assert.That(state.Phase, Is.EqualTo(phase));
        }

        /// <summary>
        /// Neither a replacement plan nor a repeated lock can overwrite an active or resting plan.
        /// </summary>
        [TestCase(ShamblerPhase.Active, false)]
        [TestCase(ShamblerPhase.Active, true)]
        [TestCase(ShamblerPhase.Rest, false)]
        [TestCase(ShamblerPhase.Rest, true)]
        public void LockingRejectsOverwriteWithoutChangingTheIntentOrPhase(ShamblerPhase phase, bool sameInstance)
        {
            var state = CreateStateInPhase(phase);
            var intent = CreateIntent(state.ActorId, EnemyIntentKind.Wait);
            var replacement = sameInstance ? intent : CreateIntent(state.ActorId, EnemyIntentKind.Wait);
            state.LockIntent(intent);

            Assert.Throws<InvalidOperationException>(() => state.LockIntent(replacement));

            Assert.That(state.LockedIntent, Is.SameAs(intent));
            Assert.That(state.Phase, Is.EqualTo(phase));
        }

        /// <summary>
        /// Every consumed active action, including wait or an unsuccessful opportunity, is followed by rest.
        /// Consuming rest returns to active; duplicate consumption never advances either phase.
        /// </summary>
        [TestCase(EnemyIntentKind.Move)]
        [TestCase(EnemyIntentKind.Attack)]
        [TestCase(EnemyIntentKind.Wait)]
        public void ConsumptionAdvancesEachPhaseExactlyOnce(EnemyIntentKind activeKind)
        {
            var state = new ShamblerState(new EntityId(1), new ShamblerDefinition(20));
            var activeIntent = CreateIntent(state.ActorId, activeKind);
            state.LockIntent(activeIntent);

            Assert.That(state.ConsumeIntent(), Is.SameAs(activeIntent));
            Assert.That(state.LockedIntent, Is.Null);
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.Throws<InvalidOperationException>(() => state.ConsumeIntent());
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Rest));

            var restIntent = CreateIntent(state.ActorId, EnemyIntentKind.Wait);
            state.LockIntent(restIntent);
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(state.ConsumeIntent(), Is.SameAs(restIntent));
            Assert.That(state.LockedIntent, Is.Null);
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.Throws<InvalidOperationException>(() => state.ConsumeIntent());
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Active));
        }

        /// <summary>
        /// Missing planning is an explicit error, not an implicit wait or cadence advancement.
        /// </summary>
        [TestCase(ShamblerPhase.Active)]
        [TestCase(ShamblerPhase.Rest)]
        public void ConsumptionWithoutALockedIntentPreservesThePhase(ShamblerPhase phase)
        {
            var state = CreateStateInPhase(phase);

            Assert.Throws<InvalidOperationException>(() => state.ConsumeIntent());

            Assert.That(state.LockedIntent, Is.Null);
            Assert.That(state.Phase, Is.EqualTo(phase));
        }

        /// <summary>
        /// Rest cannot retain movement or attack even though those are valid active intent values.
        /// </summary>
        [TestCase(EnemyIntentKind.Move)]
        [TestCase(EnemyIntentKind.Attack)]
        public void RestRejectsNonWaitIntentsWithoutAdvancing(EnemyIntentKind kind)
        {
            var state = CreateStateInPhase(ShamblerPhase.Rest);

            var error = Assert.Throws<ArgumentException>(() => state.LockIntent(CreateIntent(state.ActorId, kind)));

            Assert.That(error.ParamName, Is.EqualTo("intent"));
            Assert.That(state.LockedIntent, Is.Null);
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// A board reset discards any old plan, restores active, and preserves identity/configuration.
        /// Reset is repeatable and leaves the state ready to lock and consume a fresh plan.
        /// </summary>
        [TestCase(ShamblerPhase.Active, false)]
        [TestCase(ShamblerPhase.Active, true)]
        [TestCase(ShamblerPhase.Rest, false)]
        [TestCase(ShamblerPhase.Rest, true)]
        public void ResetForBoardClearsIntentAndRestoresTheInitialPhase(ShamblerPhase phase, bool hasIntent)
        {
            var state = CreateStateInPhase(phase);
            var actorId = state.ActorId;
            var definition = state.Definition;
            if (hasIntent)
            {
                state.LockIntent(CreateIntent(actorId, phase == ShamblerPhase.Active ? EnemyIntentKind.Move : EnemyIntentKind.Wait));
            }

            state.ResetForBoard();
            state.ResetForBoard();

            Assert.That(state.ActorId, Is.EqualTo(actorId));
            Assert.That(state.Definition, Is.SameAs(definition));
            Assert.That(state.LockedIntent, Is.Null);
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.Throws<InvalidOperationException>(() => state.ConsumeIntent());
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Active));

            var firstPlan = CreateIntent(actorId, EnemyIntentKind.Move);
            state.LockIntent(firstPlan);
            Assert.That(state.ConsumeIntent(), Is.SameAs(firstPlan));
            Assert.That(state.Phase, Is.EqualTo(ShamblerPhase.Rest));
        }

        /// <summary>
        /// Creating state for a fresh board cannot inherit the previous board's phase or retained plan.
        /// </summary>
        [Test]
        public void FreshBoardStateDoesNotReuseThePreviousIntentOrPhase()
        {
            var previous = CreateStateInPhase(ShamblerPhase.Rest);
            var previousIntent = CreateIntent(previous.ActorId, EnemyIntentKind.Wait);
            previous.LockIntent(previousIntent);

            var fresh = new ShamblerState(previous.ActorId, previous.Definition);

            Assert.That(fresh.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(fresh.LockedIntent, Is.Null);
            Assert.That(previous.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(previous.LockedIntent, Is.SameAs(previousIntent));
        }

        /// <summary>
        /// State exposes no public mutation bypass or stored position, board, run, clock, or RNG authority.
        /// </summary>
        [Test]
        public void StateOwnsOnlyIdentityDefinitionCadenceAndIntent()
        {
            var stateType = typeof(ShamblerState);

            Assert.That(stateType.IsSealed, Is.True);
            Assert.That(stateType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static), Is.Empty);
            foreach (var property in stateType.GetProperties())
            {
                Assert.That(property.GetSetMethod(), Is.Null, property.Name);
            }
            foreach (var field in stateType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                Assert.That(new[] { typeof(EntityId), typeof(ShamblerDefinition), typeof(ShamblerPhase), typeof(EnemyIntent) },
                    Does.Contain(field.FieldType), field.Name);
            }
        }

        /// <summary>
        /// Definition, phase, and state remain in the existing pure Resolution assembly without Unity references.
        /// </summary>
        [Test]
        public void DefinitionAssemblyDoesNotReferenceUnity()
        {
            var assembly = typeof(ShamblerDefinition).Assembly;

            Assert.That(assembly, Is.SameAs(typeof(TurnPhaseHandlers).Assembly));
            Assert.That(typeof(ShamblerPhase).Assembly, Is.SameAs(assembly));
            Assert.That(typeof(ShamblerState).Assembly, Is.SameAs(assembly));
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
            }
        }

        private static EnemyIntent CreateIntent(EntityId actorId, EnemyIntentKind kind)
        {
            return kind == EnemyIntentKind.Wait
                ? new EnemyIntent(actorId, kind)
                : new EnemyIntent(actorId, kind, new GridPosition(0, 1), new EntityId(99),
                    attackOnPlayerEntry: kind == EnemyIntentKind.Move);
        }

        private static ShamblerState CreateStateInPhase(ShamblerPhase phase)
        {
            var state = new ShamblerState(new EntityId(1), new ShamblerDefinition(10));
            if (phase == ShamblerPhase.Rest)
            {
                state.LockIntent(CreateIntent(state.ActorId, EnemyIntentKind.Wait));
                state.ConsumeIntent();
            }
            return state;
        }
    }
}