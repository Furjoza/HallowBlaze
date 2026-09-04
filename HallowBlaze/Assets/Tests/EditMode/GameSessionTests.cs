using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class GameSessionTests
    {
        [Test]
        public void ConstructorRequiresProfile()
        {
            Assert.Throws<ArgumentNullException>(() => new GameSession(null));
        }

        [Test]
        public void StartNewRunCreatesConfiguredRunAndPreservesProfile()
        {
            ProfileState profile = CreateProfile();
            GameSession session = new GameSession(profile);

            RunState run = session.StartNewRun("run-001", 1729, CreateConfiguration());

            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(session.ActiveRun, Is.SameAs(run));
            Assert.That(session.GetCurrentRun(), Is.SameAs(run));
            Assert.That(session.IsRunActive(), Is.True);
            Assert.That(run.RunId, Is.EqualTo("run-001"));
            Assert.That(run.RunSeed, Is.EqualTo(1729));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.Food, Is.EqualTo(80));
            Assert.That(run.CurrentDay, Is.Zero);
        }

        [Test]
        public void ReplacingRunCreatesFreshInstanceAndRaisesLifecycleInOrder()
        {
            GameSession session = new GameSession(CreateProfile());
            List<string> lifecycle = new List<string>();
            session.OnRunStarted += run =>
            {
                Assert.That(session.ActiveRun, Is.SameAs(run));
                lifecycle.Add("started:" + run.RunId);
            };
            session.OnRunAbandoned += run =>
            {
                Assert.That(session.ActiveRun, Is.Null);
                lifecycle.Add("abandoned:" + run.RunId);
            };

            RunState first = session.StartNewRun("run-first", 1, CreateConfiguration());
            first.ConsumeFood(10);
            RunState second = session.StartNewRun("run-second", 2, CreateConfiguration());

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Food, Is.EqualTo(80));
            Assert.That(session.ActiveRun, Is.SameAs(second));
            Assert.That(lifecycle, Is.EqualTo(new[]
            {
                "started:run-first",
                "abandoned:run-first",
                "started:run-second"
            }));
        }

        [Test]
        public void InvalidReplacementLeavesCurrentRunActiveAndRaisesNoLifecycleEvent()
        {
            GameSession session = new GameSession(CreateProfile());
            RunState current = session.StartNewRun("run-current", 1, CreateConfiguration());
            int startedCount = 0;
            int abandonedCount = 0;
            session.OnRunStarted += _ => startedCount++;
            session.OnRunAbandoned += _ => abandonedCount++;

            Assert.Throws<ArgumentException>(
                () => session.StartNewRun(" ", 2, CreateConfiguration()));

            Assert.That(session.ActiveRun, Is.SameAs(current));
            Assert.That(startedCount, Is.Zero);
            Assert.That(abandonedCount, Is.Zero);
        }

        [Test]
        public void AbandonRunIsIdempotentAndClearsBeforeNotification()
        {
            GameSession session = new GameSession(CreateProfile());
            RunState run = session.StartNewRun("run-001", 1, CreateConfiguration());
            int abandonedCount = 0;
            RunState notifiedRun = null;
            session.OnRunAbandoned += abandoned =>
            {
                abandonedCount++;
                notifiedRun = abandoned;
                Assert.That(session.ActiveRun, Is.Null);
            };

            session.AbandonRun();
            session.AbandonRun();

            Assert.That(abandonedCount, Is.EqualTo(1));
            Assert.That(notifiedRun, Is.SameAs(run));
            Assert.That(session.IsRunActive(), Is.False);
            Assert.Throws<InvalidOperationException>(() => session.GetCurrentRun());
        }

        [Test]
        public void ResourceAndDayMutationsDelegateToRunAndNotifyOnceEach()
        {
            GameSession session = new GameSession(CreateProfile());
            RunState run = session.StartNewRun("run-001", 1, CreateConfiguration());
            List<RunState> notifications = new List<RunState>();
            session.OnRunChanged += changedRun => notifications.Add(changedRun);

            session.ConsumeFood(5);
            session.RestoreFood(2);
            session.TakeDamage(10);
            session.RestoreHealth(3);
            session.AdvanceDay();

            Assert.That(run.Food, Is.EqualTo(77));
            Assert.That(run.Health, Is.EqualTo(93));
            Assert.That(run.CurrentDay, Is.EqualTo(1));
            Assert.That(notifications, Has.Count.EqualTo(5));
            Assert.That(notifications.All(changedRun => ReferenceEquals(changedRun, run)), Is.True);
        }

        [Test]
        public void MutationsRequireActiveRun()
        {
            GameSession session = new GameSession(CreateProfile());

            Assert.Throws<InvalidOperationException>(() => session.ConsumeFood());
            Assert.Throws<InvalidOperationException>(() => session.RestoreFood(1));
            Assert.Throws<InvalidOperationException>(() => session.TakeDamage(1));
            Assert.Throws<InvalidOperationException>(() => session.RestoreHealth(1));
            Assert.Throws<InvalidOperationException>(() => session.AdvanceDay());
            Assert.Throws<InvalidOperationException>(() => session.MarkDead());
            Assert.Throws<InvalidOperationException>(() => session.MarkWon());
        }

        [Test]
        public void FailedMutationDoesNotRaiseRunChanged()
        {
            GameSession session = new GameSession(CreateProfile());
            RunState run = session.StartNewRun("run-001", 1, CreateConfiguration());
            int changedCount = 0;
            session.OnRunChanged += _ => changedCount++;

            session.MarkDead();
            Assert.Throws<InvalidOperationException>(() => session.RestoreFood(1));

            Assert.That(run.Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(changedCount, Is.EqualTo(1));
        }

        [Test]
        public void MarkWonUsesExplicitTerminalStatusAndNotifies()
        {
            GameSession session = new GameSession(CreateProfile());
            RunState run = session.StartNewRun("run-001", 1, CreateConfiguration());
            RunState notifiedRun = null;
            session.OnRunChanged += changedRun => notifiedRun = changedRun;

            session.MarkWon();

            Assert.That(run.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(notifiedRun, Is.SameAs(run));
        }

        [Test]
        public void SessionAssemblyDoesNotReferenceUnity()
        {
            string[] references = typeof(GameSession).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.That(references.Any(name => name.StartsWith("Unity", StringComparison.Ordinal)), Is.False);
            Assert.That(typeof(GameSession).BaseType, Is.EqualTo(typeof(object)));
        }

        private static ProfileState CreateProfile()
        {
            return new ProfileState("profile-001", "world-001", 1);
        }

        private static RunStateConfiguration CreateConfiguration()
        {
            return new RunStateConfiguration(100, 80, 0, "forest.start");
        }
    }
}