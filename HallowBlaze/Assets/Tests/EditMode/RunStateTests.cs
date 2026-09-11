using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class RunStateTests
    {
        [Test]
        public void NewRunUsesConfiguredValuesAndCallerProvidedIdentity()
        {
            RunStateConfiguration configuration = CreateConfiguration(
                initialHealth: 80,
                initialFood: 65,
                initialDay: 3,
                initialWorldNodeId: "forest.start");

            RunState run = new RunState("run-001", 1729, configuration);

            Assert.That(run.RunId, Is.EqualTo("run-001"));
            Assert.That(run.RunSeed, Is.EqualTo(1729));
            Assert.That(run.CurrentDay, Is.EqualTo(3));
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(run.Health, Is.EqualTo(80));
            Assert.That(run.Food, Is.EqualTo(65));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(run.ToolSlots, Has.Count.EqualTo(2));
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(run.Route, Is.Empty);
        }

        [Test]
        public void BoardSeedIsStableForTheSamePersistedIdentity()
        {
            RunStateConfiguration configuration = CreateConfiguration(initialDay: 0);
            RunState first = new RunState("run-001", 1729, configuration);
            RunState second = new RunState("run-001", 1729, configuration);

            Assert.That(second.GetBoardSeed(), Is.EqualTo(first.GetBoardSeed()));
            Assert.That(first.GetBoardSeed(), Is.EqualTo(-1283061345));
        }

        [Test]
        public void BoardSeedChangesWithRunOrCommittedStageIdentity()
        {
            RunState baseline = new RunState("run-001", 1729, CreateConfiguration());
            RunState otherRun = new RunState("run-002", 1729, CreateConfiguration());
            RunState otherSeed = new RunState("run-001", 1730, CreateConfiguration());
            RunState otherDay = new RunState(
                "run-001",
                1729,
                CreateConfiguration(initialDay: 2));
            RunState otherNode = new RunState(
                "run-001",
                1729,
                CreateConfiguration(initialWorldNodeId: "forest.clearing"));

            int boardSeed = baseline.GetBoardSeed();
            Assert.That(otherRun.GetBoardSeed(), Is.Not.EqualTo(boardSeed));
            Assert.That(otherSeed.GetBoardSeed(), Is.Not.EqualTo(boardSeed));
            Assert.That(otherDay.GetBoardSeed(), Is.Not.EqualTo(boardSeed));
            Assert.That(otherNode.GetBoardSeed(), Is.Not.EqualTo(boardSeed));
        }

        [Test]
        public void ResetRestoresBoardSeedForTheSameIdentity()
        {
            RunState run = new RunState("run-001", 1729, CreateConfiguration());
            int originalSeed = run.GetBoardSeed();
            run.AdvanceDay();
            run.SetCurrentWorldNode("forest.clearing");

            run.Reset("run-001", 1729, CreateConfiguration());

            Assert.That(run.GetBoardSeed(), Is.EqualTo(originalSeed));
        }

        [Test]
        public void ConfigurationRejectsInvalidInitialValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateConfiguration(initialHealth: -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateConfiguration(initialFood: -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateConfiguration(initialDay: -1));
            Assert.Throws<ArgumentException>(
                () => CreateConfiguration(initialWorldNodeId: " "));
            Assert.Throws<ArgumentException>(
                () => CreateConfiguration(initialWorldNodeId: " forest.start"));
            Assert.Throws<ArgumentException>(
                () => CreateConfiguration(initialWorldNodeId: "forest.start "));
        }

        [Test]
        public void RunIdentityMustBeStableText()
        {
            RunStateConfiguration configuration = CreateConfiguration();

            Assert.Throws<ArgumentException>(() => new RunState(" ", 1, configuration));
            Assert.Throws<ArgumentException>(() => new RunState(" run-001", 1, configuration));
            Assert.Throws<ArgumentException>(() => new RunState("run-001 ", 1, configuration));
            Assert.Throws<ArgumentNullException>(() => new RunState("run-001", 1, null));

            RunState run = new RunState("run-001", 1, configuration);
            Assert.Throws<ArgumentException>(() => run.Reset(" run-002 ", 2, configuration));
            Assert.That(run.RunId, Is.EqualTo("run-001"));
        }

        [Test]
        public void ResourceMutationsRespectBoundsWithoutChoosingOutcome()
        {
            RunState run = CreateRun();

            run.TakeDamage(150);
            run.ConsumeFood(150);

            Assert.That(run.Health, Is.Zero);
            Assert.That(run.Food, Is.Zero);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active));

            run.RestoreHealth(25);
            run.RestoreFood(30);

            Assert.That(run.Health, Is.EqualTo(25));
            Assert.That(run.Food, Is.EqualTo(30));
        }

        [Test]
        public void ResourceMutationsRejectNegativeAmountsWithoutChangingState()
        {
            RunState run = CreateRun();

            Assert.Throws<ArgumentOutOfRangeException>(() => run.TakeDamage(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.RestoreHealth(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.ConsumeFood(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.RestoreFood(-1));

            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.Food, Is.EqualTo(100));
        }

        [Test]
        public void IncreasingValuesUsesCheckedArithmetic()
        {
            RunState run = CreateRun(CreateConfiguration(
                initialHealth: int.MaxValue,
                initialFood: int.MaxValue,
                initialDay: int.MaxValue));

            Assert.Throws<OverflowException>(() => run.RestoreHealth(1));
            Assert.Throws<OverflowException>(() => run.RestoreFood(1));
            Assert.Throws<OverflowException>(() => run.AdvanceDay());
        }

        [Test]
        public void WorldProgressRequiresStableNodeIds()
        {
            RunState run = CreateRun();

            run.AdvanceDay();
            run.SetCurrentWorldNode("forest.clearing");
            run.RecordRouteNode("forest.start");
            run.RecordRouteNode("forest.clearing");

            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.clearing"));
            Assert.That(run.Route, Is.EqualTo(new[] { "forest.start", "forest.clearing" }));
            Assert.Throws<ArgumentException>(() => run.SetCurrentWorldNode(" "));
            Assert.Throws<ArgumentException>(() => run.SetCurrentWorldNode(" forest.end"));
            Assert.Throws<ArgumentException>(() => run.RecordRouteNode(string.Empty));
            Assert.Throws<ArgumentException>(() => run.RecordRouteNode("forest.end "));
        }

        [Test]
        public void RouteCannotBeModifiedOutsideRunState()
        {
            RunState run = CreateRun();
            run.RecordRouteNode("forest.start");

            IList<string> exposedRoute = (IList<string>)run.Route;

            Assert.That(exposedRoute.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => exposedRoute.Add("injected.node"));
            Assert.That(run.Route, Is.EqualTo(new[] { "forest.start" }));
        }

        [Test]
        public void ToolSlotsAreExactlyTwoAndHoldImmutableInstanceState()
        {
            RunState run = CreateRun();

            run.EquipTool(0, new ToolSlotState("axe.basic", 3));
            run.EquipTool(1, new ToolSlotState("shovel.basic", 2));

            Assert.That(run.ToolSlots, Has.Count.EqualTo(RunState.ToolSlotCount));
            Assert.That(RunState.ToolSlotCount, Is.EqualTo(2));
            Assert.That(run.ToolSlots[0].ToolId, Is.EqualTo("axe.basic"));
            Assert.That(run.ToolSlots[0].RemainingUses, Is.EqualTo(3));
            Assert.That(run.ToolSlots[1].ToolId, Is.EqualTo("shovel.basic"));
            Assert.That(run.ToolSlots[1].RemainingUses, Is.EqualTo(2));

            run.ClearTool(0);

            Assert.That(run.ToolSlots[0].IsEmpty, Is.True);
        }

        [Test]
        public void ToolStateAndSlotIndexRejectInvalidValues()
        {
            RunState run = CreateRun();

            Assert.Throws<ArgumentException>(() => new ToolSlotState(" ", 1));
            Assert.Throws<ArgumentException>(() => new ToolSlotState(" axe.basic", 1));
            Assert.Throws<ArgumentException>(() => new ToolSlotState("axe.basic ", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ToolSlotState("axe.basic", -1));
            Assert.Throws<ArgumentException>(() => run.EquipTool(0, ToolSlotState.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => run.EquipTool(-1, new ToolSlotState("axe.basic", 1)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => run.ClearTool(RunState.ToolSlotCount));
        }

        [Test]
        public void ToolSlotsCannotBeModifiedOutsideRunState()
        {
            RunState run = CreateRun();
            IList<ToolSlotState> exposedSlots = (IList<ToolSlotState>)run.ToolSlots;

            Assert.That(exposedSlots.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(
                () => exposedSlots[0] = new ToolSlotState("injected.tool", 1));
            Assert.That(run.ToolSlots[0].IsEmpty, Is.True);
        }

        [Test]
        public void SeparateRunsDoNotShareCollections()
        {
            RunState first = CreateRun(runId: "run-first", runSeed: 1);
            RunState second = CreateRun(runId: "run-second", runSeed: 2);

            first.RecordRouteNode("forest.start");
            first.EquipTool(0, new ToolSlotState("axe.basic", 3));

            Assert.That(second.Route, Is.Empty);
            Assert.That(second.ToolSlots.All(slot => slot.IsEmpty), Is.True);
        }

        [Test]
        public void ResetStartsFreshRunAndClearsMutableState()
        {
            RunState run = CreateRun();
            run.TakeDamage(40);
            run.ConsumeFood(30);
            run.AdvanceDay();
            run.SetCurrentWorldNode("forest.clearing");
            run.RecordRouteNode("forest.start");
            run.EquipTool(0, new ToolSlotState("axe.basic", 3));
            run.MarkDead();

            RunStateConfiguration nextConfiguration = CreateConfiguration(
                initialHealth: 70,
                initialFood: 55,
                initialDay: 4,
                initialWorldNodeId: "snow.border");
            run.Reset("run-002", -77, nextConfiguration);

            Assert.That(run.RunId, Is.EqualTo("run-002"));
            Assert.That(run.RunSeed, Is.EqualTo(-77));
            Assert.That(run.CurrentDay, Is.EqualTo(4));
            Assert.That(run.WorldNodeId, Is.EqualTo("snow.border"));
            Assert.That(run.Health, Is.EqualTo(70));
            Assert.That(run.Food, Is.EqualTo(55));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(run.Route, Is.Empty);
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
        }

        [TestCase(RunStatus.Dead)]
        [TestCase(RunStatus.Won)]
        public void TerminalOutcomeIsExplicitAndPreventsFurtherMutation(RunStatus outcome)
        {
            RunState run = CreateRun();

            if (outcome == RunStatus.Dead)
                run.MarkDead();
            else
                run.MarkWon();

            Assert.That(run.Status, Is.EqualTo(outcome));
            Assert.Throws<InvalidOperationException>(() => run.TakeDamage(1));
            Assert.Throws<InvalidOperationException>(() => run.RestoreFood(1));
            Assert.Throws<InvalidOperationException>(() => run.AdvanceDay());
            Assert.Throws<InvalidOperationException>(() => run.SetCurrentWorldNode("forest.end"));
            Assert.Throws<InvalidOperationException>(() => run.RecordRouteNode("forest.end"));
            Assert.Throws<InvalidOperationException>(
                () => run.EquipTool(0, new ToolSlotState("axe.basic", 1)));
            Assert.Throws<InvalidOperationException>(() => run.ClearTool(0));
            Assert.Throws<InvalidOperationException>(() => run.MarkDead());
            Assert.Throws<InvalidOperationException>(() => run.MarkWon());
        }

        [Test]
        public void DomainAssemblyDoesNotReferenceUnityEngine()
        {
            string[] references = typeof(RunState).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.That(typeof(RunState).BaseType, Is.EqualTo(typeof(object)));
            Assert.That(references.Any(name => name.StartsWith("UnityEngine", StringComparison.Ordinal)), Is.False);
        }

        private static RunState CreateRun(
            RunStateConfiguration configuration = null,
            string runId = "run-001",
            int runSeed = 12345)
        {
            return new RunState(runId, runSeed, configuration ?? CreateConfiguration());
        }

        private static RunStateConfiguration CreateConfiguration(
            int initialHealth = 100,
            int initialFood = 100,
            int initialDay = 1,
            string initialWorldNodeId = "forest.start")
        {
            return new RunStateConfiguration(
                initialHealth,
                initialFood,
                initialDay,
                initialWorldNodeId);
        }
    }
}