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
        public void CheckpointTravelState_HasOneHistoryOwnerAndSafeReset()
        {
            RunState run = RouteLegTests.NewRun();
            List<TravelHistoryEntry> source = new List<TravelHistoryEntry> { TravelHistoryEntry.LegacyCheckpoint("a") };
            run.RestoreTravelState(3, "a", 0, source, null);
            source.Clear();
            Assert.That(run.Route, Is.EqualTo(new[] { "a" }));
            Assert.That(run.CompletedDayCount, Is.EqualTo(3));
            Assert.That(run.ActiveLeg, Is.Null);
            Assert.Throws<NotSupportedException>(() => ((IList<TravelHistoryEntry>)run.TravelHistory).Clear());
            Assert.Throws<ArgumentException>(() => run.RestoreTravelState(-1, "b", 0, source, null));
            Assert.That(run.WorldNodeId, Is.EqualTo("a"));
            Assert.That(run.CompletedDayCount, Is.EqualTo(3));
            Assert.That(run.TravelHistory, Has.Count.EqualTo(1));
            run.StartLeg("edge", "a", "b", RouteLegTests.Segments());
            run.Reset("fresh", 7, new RunStateConfiguration(100, 100, 0, "a"));
            Assert.That(run.TravelHistory, Is.Empty);
            Assert.That(run.TraversalSequence, Is.Zero);
            Assert.That(run.CompletedDayCount, Is.Zero);
            Assert.That(run.ActiveLeg, Is.Null);
        }

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
        public void AdvanceToWorldNodeAtomicallyMutatesDayNodeAndRoute()
        {
            RunState run = CreateRun();
            run.RecordRouteNode("forest.start");

            run.AdvanceToWorldNode("forest.clearing");

            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.clearing"));
            Assert.That(run.Route, Is.EqualTo(new[] { "forest.start", "forest.clearing" }));
        }

        [Test]
        public void AdvanceToWorldNodeRequiresValidStableNodeId()
        {
            RunState run = CreateRun();

            Assert.Throws<ArgumentException>(() => run.AdvanceToWorldNode(" "));
            Assert.Throws<ArgumentException>(() => run.AdvanceToWorldNode(" forest.end"));
            Assert.Throws<ArgumentException>(() => run.AdvanceToWorldNode("forest.end "));

            Assert.That(run.CurrentDay, Is.EqualTo(1));
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(run.Route, Is.Empty);
        }

        [Test]
        public void AdvanceToWorldNodeRequiresActiveRun()
        {
            RunState run = CreateRun();
            run.MarkDead();

            Assert.Throws<InvalidOperationException>(() => run.AdvanceToWorldNode("forest.end"));

            Assert.That(run.CurrentDay, Is.EqualTo(1));
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(run.Route, Is.Empty);
        }

        [Test]
        public void AdvanceToWorldNodeUsesCheckedArithmetic()
        {
            RunState run = CreateRun(CreateConfiguration(initialDay: int.MaxValue));

            Assert.Throws<OverflowException>(() => run.AdvanceToWorldNode("forest.end"));

            Assert.That(run.CurrentDay, Is.EqualTo(int.MaxValue));
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(run.Route, Is.Empty);
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

        /// <summary>Definitions are reusable content; immutable equipped snapshots and identity allocation belong to each run.</summary>
        [Test]
        public void ToolDefinitionsAndInstancesRemainIndependentAcrossRunsAndSnapshots()
        {
            var definition = new ToolDefinition("axe.basic", "Axe", 3);
            RunState first = CreateRun();
            RunState second = CreateRun();
            ToolInstanceState firstTool = first.CreateToolInstance(definition);
            ToolInstanceState secondTool = second.CreateToolInstance(definition);
            first.EquipTool(0, firstTool);
            second.EquipTool(1, secondTool);
            Assert.That(first.TrySpendToolCharge(0, firstTool.InstanceId), Is.True);
            Assert.That(first.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            Assert.That(firstTool.Charges, Is.EqualTo(3));
            Assert.That(second.ToolSlots[1].Instance.Charges, Is.EqualTo(3));
            Assert.That(definition.MaximumCharges, Is.EqualTo(3));
            Assert.That(firstTool.DefinitionId, Is.EqualTo(definition.Id));
            Assert.That(firstTool.InstanceId, Is.Not.EqualTo(definition.Id));
            Assert.That(first.Health, Is.EqualTo(100));
            Assert.That(first.Food, Is.EqualTo(100));
            Assert.Throws<NotSupportedException>(() => ((IList<ToolSlotState>)first.ToolSlots).Clear());
        }

        /// <summary>Replacing and removing return exact old instances and never reuse IDs or allow duplicate equipment.</summary>
        [Test]
        public void ToolReplacementRemovalAndResetPreserveOwnershipAndAllocator()
        {
            RunState run = CreateRun();
            var definition = new ToolDefinition("tool.generic", "Tool", 1);
            ToolInstanceState first = run.CreateToolInstance(definition);
            ToolInstanceState second = run.CreateToolInstance(definition);
            run.EquipTool(0, first);
            Assert.Throws<InvalidOperationException>(() => run.EquipTool(0, second));
            Assert.Throws<ArgumentException>(() => run.EquipTool(1, first));
            Assert.That(run.ReplaceTool(0, second), Is.SameAs(first));
            Assert.That(run.RemoveTool(0), Is.SameAs(second));
            Assert.That(run.RemoveTool(0), Is.Null);
            ToolInstanceState third = run.CreateToolInstance(definition);
            Assert.That(third.InstanceId, Is.EqualTo("tool.3"));
            run.EquipTool(1, third);
            run.Reset("new-run", 42, CreateConfiguration());
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(run.ToolInstanceSequence, Is.Zero);
        }

        /// <summary>Stale/depleted/empty spending is effect-free and zero charges remain a nonempty slot.</summary>
        [Test]
        public void ToolChargesRejectInvalidValuesAndCannotBecomeNegative()
        {
            RunState run = CreateRun();
            var definition = new ToolDefinition("shovel.basic", "Shovel", 1);
            ToolInstanceState instance = run.CreateToolInstance(definition);
            run.EquipTool(0, instance);
            Assert.That(run.TrySpendToolCharge(0, "different"), Is.False);
            Assert.That(run.TrySpendToolCharge(1, instance.InstanceId), Is.False);
            Assert.That(run.TrySpendToolCharge(0, instance.InstanceId), Is.True);
            Assert.That(run.TrySpendToolCharge(0, instance.InstanceId), Is.False);
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.Zero);
            Assert.That(run.ToolSlots[0].IsEmpty, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ToolInstanceState("tool.1", definition.Id, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ToolDefinition("tool", "Tool", 0));
            Assert.Throws<ArgumentException>(() => new ToolInstanceState(" ", definition.Id, 0));
            Assert.Throws<ArgumentException>(() => run.ReplaceTool(0, new ToolInstanceState("source.atlas", definition.Id, 1)));
        }

        /// <summary>Restore validates all equipment before mutation and detaches the caller's array.</summary>
        [Test]
        public void ToolRestoreRejectsPartialStateAndCopiesEquipmentCells()
        {
            RunState run = CreateRun();
            ToolInstanceState tool = run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3));
            run.EquipTool(0, tool);
            Assert.Throws<ArgumentException>(() => run.RestoreToolState(1, new[] { tool, tool }));
            Assert.Throws<ArgumentException>(() => run.RestoreToolState(0, new[] { tool, (ToolInstanceState)null }));
            Assert.Throws<ArgumentException>(() => run.RestoreToolState(1, new[] { tool }));
            Assert.That(run.ToolSlots[0].Instance, Is.SameAs(tool));
            Assert.That(run.ToolSlots[1].IsEmpty, Is.True);
            Assert.That(run.ToolInstanceSequence, Is.EqualTo(1));
            var source = new[] { (ToolInstanceState)null, tool };
            run.RestoreToolState(3, source);
            source[1] = null;
            Assert.That(run.ToolSlots[1].Instance, Is.SameAs(tool));
            Assert.That(run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 2)).InstanceId, Is.EqualTo("tool.4"));
        }

        /// <summary>Allocator overflow and terminal-state operations cannot create or modify equipment.</summary>
        [Test]
        public void ToolAllocatorOverflowAndTerminalMutationsAreEffectFree()
        {
            RunState run = CreateRun();
            var definition = new ToolDefinition("axe.basic", "Axe", 3);
            run.RestoreToolState(int.MaxValue, new ToolInstanceState[RunState.ToolSlotCount]);
            Assert.Throws<OverflowException>(() => run.CreateToolInstance(definition));
            Assert.That(run.ToolInstanceSequence, Is.EqualTo(int.MaxValue));
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            run.MarkDead();
            Assert.Throws<InvalidOperationException>(() => run.CreateToolInstance(definition));
            Assert.Throws<InvalidOperationException>(() => run.RemoveTool(0));
            Assert.Throws<InvalidOperationException>(() => run.TrySpendToolCharge(0, "tool.1"));
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
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