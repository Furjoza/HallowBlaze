using System;
using HallowBlaze.Core.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class ResourceRulesTests
    {
        [TestCase(0, 1)]
        [TestCase(99, 1)]
        [TestCase(100, 1)]
        [TestCase(101, 2)]
        [TestCase(200, 2)]
        public void AcceptedBalanceSamplesThePreCommandSatietyTier(int satiety, int cost)
        {
            Assert.That(ResourceRules.Default.MaximumHealth, Is.EqualTo(100));
            Assert.That(ResourceRules.Default.MaximumSatiety, Is.EqualTo(200));
            Assert.That(ResourceRules.Default.SelectActionCost(satiety), Is.EqualTo(cost));
        }

        [TestCase(95, 10, 100, 5, 5)]
        [TestCase(100, 50, 200, 50, 0)]
        [TestCase(199, int.MaxValue, 200, 1, int.MaxValue - 1)]
        [TestCase(100, int.MaxValue, 100, 0, int.MaxValue)]
        public void RestorationReportsAppliedAndWastedWithoutOverflow(int current, int requested, int cap,
            int applied, int wasted)
        {
            ResourceGain gain = new ResourceGain(current, requested, cap);
            Assert.That(gain.Requested, Is.EqualTo(requested));
            Assert.That(gain.Applied, Is.EqualTo(applied));
            Assert.That(gain.Wasted, Is.EqualTo(wasted));
        }

        [Test]
        public void ConfigurationIsDataDrivenAndRejectsInvalidRanges()
        {
            ResourceRules rules = new ResourceRules(80, 150, 60, 2, 3);
            Assert.That(rules.SelectActionCost(60), Is.EqualTo(2));
            Assert.That(rules.SelectActionCost(61), Is.EqualTo(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceRules(maximumHealth: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceRules(maximumSatiety: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceRules(normalSatietyThreshold: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceRules(normalSatietyThreshold: 201));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceRules(normalActionCost: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceRules(satedActionCost: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.SelectActionCost(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.SelectActionCost(151));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceGain(101, 1, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceGain(0, -1, 100));
        }

        [Test]
        public void RegisteredEffectsApplyOnlyTheirResourceAndReportExcessWithoutOwnershipOrCost()
        {
            var run = new RunState("effects", 42, new RunStateConfiguration(95, 100, 0, "start"));
            var effects = new ConsumableEffectCatalog(new[]
            {
                new ConsumableEffectDefinition("heal", ConsumableResource.Health, 10),
                new ConsumableEffectDefinition("eat", ConsumableResource.Satiety, 50)
            });
            Assert.That(effects.TryApply("heal", run, out ResourceGain healing), Is.EqualTo(ConsumableEffectRejection.None));
            Assert.That(healing.Applied, Is.EqualTo(5));
            Assert.That(healing.Wasted, Is.EqualTo(5));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.Satiety, Is.EqualTo(100));
            Assert.That(effects.TryApply("eat", run, out ResourceGain eating), Is.EqualTo(ConsumableEffectRejection.None));
            Assert.That(eating.Applied, Is.EqualTo(50));
            Assert.That(run.Satiety, Is.EqualTo(150));
            Assert.That(effects.TryApply("eat", run, out _), Is.EqualTo(ConsumableEffectRejection.None));
            Assert.That(effects.TryApply("eat", run, out ResourceGain wasted), Is.EqualTo(ConsumableEffectRejection.None));
            Assert.That(wasted.Applied, Is.Zero);
            Assert.That(wasted.Wasted, Is.EqualTo(50));
            Assert.That(run.Satiety, Is.EqualTo(200));
        }

        [Test]
        public void UnknownOrInvalidEffectsRejectBeforeMutation()
        {
            var effect = new ConsumableEffectDefinition("eat", ConsumableResource.Satiety, 50);
            var source = new System.Collections.Generic.List<ConsumableEffectDefinition> { effect };
            var catalog = new ConsumableEffectCatalog(source);
            source.Clear();
            var run = new RunState("effects", 42, new RunStateConfiguration(90, 100, 0, "start"));
            Assert.That(catalog.TryApply("EAT", run, out _), Is.EqualTo(ConsumableEffectRejection.UnknownEffect));
            Assert.That(catalog.TryApply(null, run, out _), Is.EqualTo(ConsumableEffectRejection.UnknownEffect));
            Assert.That(run.Health, Is.EqualTo(90));
            Assert.That(run.Satiety, Is.EqualTo(100));
            run.MarkDead();
            Assert.That(catalog.TryApply("eat", run, out _), Is.EqualTo(ConsumableEffectRejection.InvalidState));
            Assert.That(run.Satiety, Is.EqualTo(100));
            Assert.Throws<ArgumentException>(() => new ConsumableEffectCatalog(new[] { effect, effect }));
            Assert.Throws<ArgumentException>(() => new ConsumableEffectCatalog(new ConsumableEffectDefinition[] { null }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConsumableEffectDefinition("bad", ConsumableResource.Health, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConsumableEffectDefinition("bad", (ConsumableResource)99, 10));
        }
    }
}