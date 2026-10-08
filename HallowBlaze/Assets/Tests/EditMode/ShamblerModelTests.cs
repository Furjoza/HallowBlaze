using System;
using System.Reflection;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies pure Shambler definition data and rejection of unapproved configuration.
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
        /// Definition and phase data remain in the existing pure Resolution assembly without Unity references.
        /// </summary>
        [Test]
        public void DefinitionAssemblyDoesNotReferenceUnity()
        {
            var assembly = typeof(ShamblerDefinition).Assembly;

            Assert.That(assembly, Is.SameAs(typeof(TurnPhaseHandlers).Assembly));
            Assert.That(typeof(ShamblerPhase).Assembly, Is.SameAs(assembly));
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
            }
        }
    }
}