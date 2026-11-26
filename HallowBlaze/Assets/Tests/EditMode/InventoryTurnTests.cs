using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Exercises bag proposals through the same player resolver and complete-turn controller as movement.</summary>
    public sealed class InventoryTurnTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);

        /// <summary>Bag effects happen before one retained hostile opportunity; they neither replan nor grant extra phases.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FoodAndMedicineResolveInPayloadOrderBeforeOneRealLockedAttack(bool reverse)
        {
            var board = NewBoard();
            AddFloors(board);
            var enemyId = new EntityId(2);
            Assert.That(board.TryAdd(new BoardEntityState(enemyId, new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Enemy, "enemy", BoardEntityTraits.Default), new GridPosition(2, 1))), Is.True);
            var enemy = new ShamblerState(enemyId, new ShamblerDefinition(10));
            int environment = 0;
            var phases = new EnemyBatchTurnPhases(new[] { enemy }, (state, current, actor, events) => environment++);
            phases.PlanInitialIntents(board, PlayerId);
            EnemyIntent locked = enemy.LockedIntent;
            Assert.That(locked.Kind, Is.EqualTo(EnemyIntentKind.Attack));
            RunState run = NewRun();
            run.RestoreInventoryState(run.InventoryContent, 0, new[]
            {
                new InventoryPlacement(new ItemInstanceState("food", "drink"), InventoryContainer.LeftPocket),
                new InventoryPlacement(new ItemInstanceState("medicine", "medicine"), InventoryContainer.MainGrid)
            });
            var actions = new[] { new InventoryItemAction(InventoryItemActionKind.Use, "food"),
                new InventoryItemAction(InventoryItemActionKind.Use, "medicine") };
            if (reverse) Array.Reverse(actions);
            var controller = new TurnController(board, run, PlayerId, phases: phases.Handlers);
            TurnResult result = controller.Resolve(new ManageInventoryCommand(0, actions, Array.Empty<InventoryLayoutEntry>()));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Events.Select(value => value.EventType), Is.EqualTo(new[]
                { "InventoryItemAction", "InventoryItemAction", "InventoryCommitted", "ActionCostApplied", "EnemyAttackResolved" }));
            Assert.That(result.Events.OfType<InventoryItemActionEvent>().Select(value => value.InstanceId),
                Is.EqualTo(actions.Select(value => value.InstanceId)));
            Assert.That(result.Events.OfType<InventoryItemActionEvent>().Single(value => value.ResourceId == "health").Applied, Is.EqualTo(5));
            Assert.That(run.Health, Is.EqualTo(90));
            Assert.That(run.Satiety, Is.EqualTo(149));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(environment, Is.EqualTo(1));
            Assert.That(result.Events.OfType<EnemyAttackResolvedEvent>().Single().TargetPosition, Is.EqualTo(locked.TargetPosition));
            Assert.That(run.Inventory.Placements, Is.Empty);
            Assert.That(controller.Resolve(new InventoryDraft(run).CreateCommand()).Accepted, Is.True);
            Assert.That(run.Satiety, Is.EqualTo(147), "Next turn must sample the new sated tier.");
            Assert.That(run.Health, Is.EqualTo(90), "The announced rest cannot become a second attack.");
            Assert.That(run.InventoryRevision, Is.EqualTo(2));
            Assert.That(environment, Is.EqualTo(2));
        }

        /// <summary>Immediate starvation stops hostile/environment/planning phases; one small use may prevent it before the cost.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void StarvationIsCheckedAfterAllBagEffectsAndTheSingleSelectedCost(bool useFood)
        {
            RunState run = NewRun(1);
            var draft = new InventoryDraft(run);
            if (useFood) Assert.That(draft.TryStage(InventoryItemActionKind.Use, "item-0"), Is.True);
            int later = 0;
            var phases = new TurnPhaseHandlers((board, current, actor, events) => later++,
                (board, current, actor, events) => later++, (board, current, actor, events) => later++);
            var controller = new TurnController(NewBoard(), run, PlayerId, phases: phases);
            Assert.That(controller.Resolve(draft.CreateCommand()).Accepted, Is.True);
            Assert.That(run.Satiety, Is.EqualTo(useFood ? 10 : 0));
            Assert.That(run.InventoryRevision, Is.EqualTo(1));
            Assert.That(later, Is.EqualTo(useFood ? 3 : 0));
            Assert.That(controller.IsTerminal, Is.EqualTo(!useFood));
            int satiety = run.Satiety;
            Assert.That(controller.Resolve(draft.CreateCommand()).Accepted, Is.False);
            Assert.That(run.Satiety, Is.EqualTo(satiety));
            Assert.That(later, Is.EqualTo(useFood ? 3 : 0));
        }

        /// <summary>Unknown source effects are rejected at the trusted boundary, and huge valid restorations report overflow-safe waste.</summary>
        [Test]
        public void RequiredEffectResolutionCannotBeBypassedAndExtremeRestorationStaysCapped()
        {
            RunState run = NewRun(199);
            InventoryContent missing = new InventoryContent(run.InventoryContent.Items,
                new ConsumableEffectCatalog(Array.Empty<ConsumableEffectDefinition>()));
            Assert.Throws<InventoryValidationException>(() => run.RestoreInventoryState(missing, 0, run.Inventory.Placements));
            run.RestoreInventoryState(run.InventoryContent, 0, new[]
                { new InventoryPlacement(new ItemInstanceState("huge", "huge"), InventoryContainer.MainGrid) });
            var draft = new InventoryDraft(run);
            Assert.That(draft.TryStage(InventoryItemActionKind.Use, "huge"), Is.True);
            TurnResult result = new TurnController(NewBoard(), run, PlayerId).Resolve(draft.CreateCommand());
            InventoryItemActionEvent used = result.Events.OfType<InventoryItemActionEvent>().Single();
            Assert.That(used.Requested, Is.EqualTo(int.MaxValue));
            Assert.That(used.Applied, Is.EqualTo(1));
            Assert.That(used.Wasted, Is.EqualTo(int.MaxValue - 1));
            Assert.That(run.Satiety, Is.EqualTo(198));
        }

        /// <summary>Noneligible/wide items and malformed pocket anchors cannot leave a valid first action partially applied.</summary>
        [TestCase("wide", 0)]
        [TestCase("berry", 1)]
        public void IncompatiblePocketsRejectTheWholeTransaction(string definitionId, int pocketColumn)
        {
            RunState run = NewRun();
            run.RestoreInventoryState(run.InventoryContent, 0, new[]
            {
                new InventoryPlacement(new ItemInstanceState("food", "berry"), InventoryContainer.LeftPocket),
                new InventoryPlacement(new ItemInstanceState("kept", definitionId), InventoryContainer.MainGrid)
            });
            string before = PersistenceJsonSerializer.SerializeRun(run);
            var command = new ManageInventoryCommand(0, new[] { new InventoryItemAction(InventoryItemActionKind.Use, "food") },
                new[] { new InventoryLayoutEntry("kept", InventoryLayoutEntry.RightPocket, pocketColumn) });
            Assert.That(new TurnController(NewBoard(), run, PlayerId).Resolve(command).Accepted, Is.False);
            Assert.That(PersistenceJsonSerializer.SerializeRun(run), Is.EqualTo(before));
        }

        /// <summary>Only committed state enters the existing v8 boundary, and backup recovery never mixes pre/post command fields.</summary>
        [Test]
        public void BagDraftAndAcceptedTurnUseExistingV8BoundaryWithoutPersistingTheDraft()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M104-" + Guid.NewGuid().ToString("N"));
            try
            {
                RunState run = NewRun();
                var store = new FileSystemSaveStore(root, inventoryContent: run.InventoryContent);
                var draft = new InventoryDraft(run);
                Assert.That(draft.TryStage(InventoryItemActionKind.Use, "item-0"), Is.True);
                Assert.That(draft.TryStage(InventoryItemActionKind.Discard, "item-1"), Is.True);
                Assert.That(store.SaveRun(run).IsSuccess, Is.True);
                string before = PersistenceJsonSerializer.SerializeRun(run);
                Assert.That(store.LoadRun().Data.Inventory.Placements, Has.Count.EqualTo(3));
                Assert.That(new TurnController(NewBoard(), run, PlayerId).Resolve(draft.CreateCommand()).Accepted, Is.True);
                Assert.That(store.SaveRun(run).IsSuccess, Is.True);
                RunState loaded = store.LoadRun().Data;
                Assert.That(loaded.InventoryRevision, Is.EqualTo(1));
                Assert.That(loaded.Inventory.Placements.Single().Item.InstanceId, Is.EqualTo("item-2"));
                Assert.That(loaded.Satiety, Is.EqualTo(109));
                string current = File.ReadAllText(Path.Combine(root, "run.json"));
                Assert.That(current, Does.Contain("\"schemaVersion\": 8").And.Not.Contain("expectedRevision").And.Not.Contain("finalLayout"));
                Assert.That(PersistenceJsonSerializer.SerializeRun(loaded), Is.EqualTo(PersistenceJsonSerializer.SerializeRun(run)));
                File.WriteAllText(Path.Combine(root, "run.json"), "{corrupt");
                SaveStoreResult<RunState> recovered = store.LoadRun();
                Assert.That(recovered.Type, Is.EqualTo(SaveStoreResultType.Recovered));
                Assert.That(PersistenceJsonSerializer.SerializeRun(recovered.Data), Is.EqualTo(before));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        /// <summary>Repacking all items into pockets/grid is included without action-budget spend or transient collision.</summary>
        [Test]
        public void RepackMovesEveryItemAndPocketSwapRemainsOneAtomicTurn()
        {
            RunState run = NewRun();
            var controller = new TurnController(NewBoard(), run, PlayerId);
            var first = new ManageInventoryCommand(0, Array.Empty<InventoryItemAction>(), new[]
            {
                new InventoryLayoutEntry("item-0", InventoryLayoutEntry.LeftPocket),
                new InventoryLayoutEntry("item-1", InventoryLayoutEntry.RightPocket),
                new InventoryLayoutEntry("item-2", InventoryLayoutEntry.MainGrid, 2, 2)
            });
            Assert.That(controller.Resolve(first).Accepted, Is.True);
            var swap = new ManageInventoryCommand(1, Array.Empty<InventoryItemAction>(), new[]
            {
                new InventoryLayoutEntry("item-1", InventoryLayoutEntry.LeftPocket),
                new InventoryLayoutEntry("item-0", InventoryLayoutEntry.RightPocket),
                new InventoryLayoutEntry("item-2", InventoryLayoutEntry.MainGrid, 0, 0)
            });
            Assert.That(controller.Resolve(swap).Accepted, Is.True);
            Assert.That(run.Inventory.GetPocketItem(InventoryContainer.LeftPocket).InstanceId, Is.EqualTo("item-1"));
            Assert.That(run.Inventory.GetPocketItem(InventoryContainer.RightPocket).InstanceId, Is.EqualTo("item-0"));
            Assert.That(run.Satiety, Is.EqualTo(98));
            Assert.That(run.InventoryRevision, Is.EqualTo(2));
        }

        [Test]
        public void DraftActionsAndRepackingNeverMutateTheRunAndPayloadCollectionsAreDetached()
        {
            RunState run = NewRun();
            string before = PersistenceJsonSerializer.SerializeRun(run);
            var draft = new InventoryDraft(run);
            Assert.That(draft.TryStage(InventoryItemActionKind.Use, "item-0"), Is.True);
            Assert.That(draft.TryStage(InventoryItemActionKind.Discard, "item-0"), Is.False);
            Assert.That(draft.TryStage(InventoryItemActionKind.Discard, "item-1"), Is.True);
            Assert.That(draft.TryStage(InventoryItemActionKind.Use, "item-2"), Is.False);
            ItemInstanceState remaining = draft.Layout.Placements.Single().Item;
            Assert.That(draft.TryRepack(new[] { new InventoryPlacement(remaining, InventoryContainer.LeftPocket) }), Is.True);
            ManageInventoryCommand captured = draft.CreateCommand();
            Assert.That(draft.TryRepack(new[] { new InventoryPlacement(remaining, InventoryContainer.RightPocket) }), Is.True);
            Assert.That(captured.FinalLayout.Single().ContainerId, Is.EqualTo(InventoryLayoutEntry.LeftPocket));
            Assert.That(captured.Actions.Select(action => action.Kind), Is.EqualTo(new[] { InventoryItemActionKind.Use, InventoryItemActionKind.Discard }));
            Assert.That(PersistenceJsonSerializer.SerializeRun(run), Is.EqualTo(before));
            var actions = new List<InventoryItemAction>();
            var entries = new List<InventoryLayoutEntry>();
            var command = new ManageInventoryCommand(0, actions, entries);
            actions.Add(new InventoryItemAction(InventoryItemActionKind.Use, "item-0"));
            entries.Add(new InventoryLayoutEntry("foreign", InventoryLayoutEntry.MainGrid));
            Assert.That(command.Actions, Is.Empty);
            Assert.That(command.FinalLayout, Is.Empty);
            Assert.Throws<NotSupportedException>(() => ((IList<InventoryItemAction>)captured.Actions).Clear());
            TurnResult result = new TurnController(NewBoard(), run, PlayerId).Resolve(captured);
            Assert.That(result.Accepted, Is.True);
            Assert.That(run.Inventory.GetPocketItem(InventoryContainer.LeftPocket).InstanceId, Is.EqualTo("item-2"));
            Assert.That(run.Satiety, Is.EqualTo(109));
            Assert.That(run.InventoryRevision, Is.EqualTo(1));
        }

        [TestCase("")]
        [TestCase("U")]
        [TestCase("D")]
        [TestCase("UU")]
        [TestCase("UD")]
        [TestCase("DU")]
        [TestCase("DD")]
        public void EveryActionMixReordersSurvivorsAndRunsExactlyOneSetOfLaterPhases(string mix)
        {
            var board = NewBoard();
            RunState run = NewRun();
            int enemies = 0, environment = 0, planning = 0;
            var phases = new TurnPhaseHandlers((state, current, actor, events) =>
            {
                enemies++;
                Assert.That(current.InventoryRevision, Is.EqualTo(1));
                Assert.That(current.Inventory.Placements, Has.Count.EqualTo(3 - mix.Length));
                Assert.That(events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
            }, (state, current, actor, events) => environment++, (state, current, actor, events) => planning++);
            var actions = mix.Select((kind, index) => new InventoryItemAction(kind == 'U'
                ? InventoryItemActionKind.Use : InventoryItemActionKind.Discard, "item-" + index)).ToArray();
            var layout = Enumerable.Range(mix.Length, 3 - mix.Length).Select(index =>
                new InventoryLayoutEntry("item-" + index, InventoryLayoutEntry.MainGrid, 2, 2 - index)).ToArray();
            TurnResult result = new TurnController(board, run, PlayerId, phases: phases).Resolve(new ManageInventoryCommand(0, actions, layout));
            Assert.That(result.Accepted && result.ConsumesTurn, Is.True);
            Assert.That(run.InventoryRevision, Is.EqualTo(1));
            Assert.That(enemies, Is.EqualTo(1));
            Assert.That(environment, Is.EqualTo(1));
            Assert.That(planning, Is.EqualTo(1));
            Assert.That(run.Health, Is.EqualTo(95));
            Assert.That(run.Satiety, Is.EqualTo(99 + mix.Count(kind => kind == 'U') * 10));
            Assert.That(result.Events.OfType<InventoryItemActionEvent>().Select(value => value.Kind), Is.EqualTo(actions.Select(value => value.Kind)));
            Assert.That(result.Events.OfType<InventoryItemActionEvent>().Select(value => value.InstanceId), Is.EqualTo(actions.Select(value => value.InstanceId)));
            Assert.That(result.Events.OfType<InventoryCommittedEvent>().Count(), Is.EqualTo(1));
            Assert.That(board.GetEntities().Count, Is.EqualTo(1), "Discard must not create a ground item.");
            foreach (InventoryLayoutEntry entry in layout)
                Assert.That(run.Inventory.GetMainGridItem(entry.Column, entry.Row).InstanceId, Is.EqualTo(entry.InstanceId));
        }

        [TestCase("third")]
        [TestCase("duplicate")]
        [TestCase("unknown-second")]
        [TestCase("unknown-kind")]
        [TestCase("null-action")]
        [TestCase("null-actions")]
        [TestCase("null-layout")]
        [TestCase("null-entry")]
        [TestCase("removed-in-layout")]
        [TestCase("missing-survivor")]
        [TestCase("duplicate-placement")]
        [TestCase("overlap")]
        [TestCase("bounds")]
        [TestCase("container")]
        [TestCase("foreign")]
        [TestCase("stale")]
        [TestCase("revision-exhausted")]
        public void MalformedSecondActionOrFinalLayoutCannotLeakTheFirstEffect(string fault)
        {
            var board = NewBoard();
            RunState run = NewRun();
            InventoryItemAction[] actions = { new InventoryItemAction(InventoryItemActionKind.Use, "item-0") };
            InventoryLayoutEntry[] layout = { new InventoryLayoutEntry("item-1", InventoryLayoutEntry.LeftPocket),
                new InventoryLayoutEntry("item-2", InventoryLayoutEntry.MainGrid, 2, 2) };
            int revision = 0;
            switch (fault)
            {
                case "third": actions = Enumerable.Range(0, 3).Select(index => new InventoryItemAction(InventoryItemActionKind.Use, "item-" + index)).ToArray(); layout = Array.Empty<InventoryLayoutEntry>(); break;
                case "duplicate": actions = new[] { actions[0], actions[0] }; break;
                case "unknown-second": actions = new[] { actions[0], new InventoryItemAction(InventoryItemActionKind.Use, "foreign") }; break;
                case "unknown-kind": actions = new[] { actions[0], new InventoryItemAction((InventoryItemActionKind)99, "item-1") }; break;
                case "null-action": actions = new[] { actions[0], null }; break;
                case "null-actions": actions = null; break;
                case "null-layout": layout = null; break;
                case "null-entry": layout[1] = null; break;
                case "removed-in-layout": layout[1] = new InventoryLayoutEntry("item-0", InventoryLayoutEntry.RightPocket); break;
                case "missing-survivor": layout = layout.Take(1).ToArray(); break;
                case "duplicate-placement": layout[1] = new InventoryLayoutEntry("item-1", InventoryLayoutEntry.RightPocket); break;
                case "overlap": layout = new[] { new InventoryLayoutEntry("item-1", InventoryLayoutEntry.MainGrid), new InventoryLayoutEntry("item-2", InventoryLayoutEntry.MainGrid) }; break;
                case "bounds": layout[1] = new InventoryLayoutEntry("item-2", InventoryLayoutEntry.MainGrid, int.MaxValue); break;
                case "container": layout[1] = new InventoryLayoutEntry("item-2", "tool-slot"); break;
                case "foreign": layout[1] = new InventoryLayoutEntry("foreign", InventoryLayoutEntry.MainGrid); break;
                case "stale": revision = 1; break;
                case "revision-exhausted": run.RestoreInventoryState(run.InventoryContent, int.MaxValue, run.Inventory.Placements); revision = int.MaxValue; break;
            }
            string before = PersistenceJsonSerializer.SerializeRun(run);
            BoardEntityState[] boardBefore = board.GetEntities().ToArray();
            int later = 0;
            var phases = new TurnPhaseHandlers((state, current, actor, events) => later++, (state, current, actor, events) => later++);
            TurnResult result = new TurnController(board, run, PlayerId, phases: phases).Resolve(new ManageInventoryCommand(revision, actions, layout));
            Assert.That(result.Accepted || result.ConsumesTurn, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(PersistenceJsonSerializer.SerializeRun(run), Is.EqualTo(before));
            Assert.That(board.GetEntities(), Is.EqualTo(boardBefore));
            Assert.That(later, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OrderedUsesReportCapsAndWasteBeforeThePreselectedCost(bool reverse)
        {
            RunState run = NewRun(199);
            InventoryItemAction[] actions = { new InventoryItemAction(InventoryItemActionKind.Use, "item-0"),
                new InventoryItemAction(InventoryItemActionKind.Use, "item-1") };
            if (reverse) Array.Reverse(actions);
            TurnResult result = new TurnController(NewBoard(), run, PlayerId).Resolve(new ManageInventoryCommand(0, actions,
                new[] { new InventoryLayoutEntry("item-2", InventoryLayoutEntry.RightPocket) }));
            Assert.That(result.Accepted, Is.True);
            InventoryItemActionEvent[] effects = result.Events.OfType<InventoryItemActionEvent>().ToArray();
            Assert.That(effects.Select(value => value.InstanceId), Is.EqualTo(actions.Select(value => value.InstanceId)));
            Assert.That(effects.Select(value => value.Applied), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(effects.Select(value => value.Wasted), Is.EqualTo(new[] { 9, 10 }));
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Single().CostAmount, Is.EqualTo(2));
            Assert.That(run.Satiety, Is.EqualTo(198));
            Assert.That(result.Events.Select(value => value.EventType), Is.EqualTo(new[]
                { "InventoryItemAction", "InventoryItemAction", "InventoryCommitted", "ActionCostApplied" }));
        }

        private static BoardState NewBoard()
        {
            var board = new BoardState();
            Assert.That(board.TryAdd(new BoardEntityState(PlayerId, new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Player, "player", BoardEntityTraits.Default), new GridPosition(1, 1))), Is.True);
            return board;
        }

        private static RunState NewRun(int satiety = 100)
        {
            var content = new InventoryContent(new ItemCatalog(new[]
            {
                new ItemDefinition("berry", 1, 1, "satiety", "consumable", true, "restore"),
                new ItemDefinition("medicine", 1, 1, "health", "consumable", true, "heal"),
                new ItemDefinition("drink", 1, 2, "satiety", "consumable", true, "drink"),
                new ItemDefinition("huge", 1, 1, "satiety", "consumable", true, "huge"),
                new ItemDefinition("wide", 2, 1, "satiety", "consumable", false, "restore")
            }), new ConsumableEffectCatalog(new[]
            {
                new ConsumableEffectDefinition("restore", ConsumableResource.Satiety, 10),
                new ConsumableEffectDefinition("heal", ConsumableResource.Health, 10),
                new ConsumableEffectDefinition("drink", ConsumableResource.Satiety, 50),
                new ConsumableEffectDefinition("huge", ConsumableResource.Satiety, int.MaxValue)
            }));
            var run = new RunState("bag", 104, new RunStateConfiguration(95, satiety, 0, "start", inventoryContent: content));
            run.RestoreInventoryState(content, 0, Enumerable.Range(0, 3).Select(index => new InventoryPlacement(
                new ItemInstanceState("item-" + index, "berry"), InventoryContainer.MainGrid, index, 0)));
            return run;
        }

        private static void AddFloors(BoardState board)
        {
            int identity = 10;
            for (int column = 0; column < 4; column++)
                for (int row = 0; row < 3; row++)
                    Assert.That(board.TryAdd(new BoardEntityState(new EntityId(identity++), new BoardEntityDefinition(
                        BoardLayer.Terrain, new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false)),
                        new GridPosition(column, row))), Is.True);
        }

        [Test]
        public void EmptyFinishPaysOnceAdvancesRevisionAndDuplicateSubmitIsFree()
        {
            var board = new BoardState();
            var playerId = new EntityId(1);
            Assert.That(board.TryAdd(new BoardEntityState(playerId, new BoardEntityDefinition(BoardLayer.Actor,
                EntityKind.Player, "player", BoardEntityTraits.Default), new GridPosition(1, 1))), Is.True);
            var run = new RunState("bag", 104, new RunStateConfiguration(100, 100, 0, "start"));
            var controller = new TurnController(board, run, playerId);
            var command = new ManageInventoryCommand(0, Array.Empty<InventoryItemAction>(), Array.Empty<InventoryLayoutEntry>());
            TurnResult first = controller.Resolve(command);
            Assert.That(first.Accepted && first.ConsumesTurn, Is.True);
            Assert.That(first.Events.Select(value => value.EventType), Is.EqualTo(new[] { "InventoryCommitted", "ActionCostApplied" }));
            Assert.That(run.InventoryRevision, Is.EqualTo(1));
            Assert.That(run.Satiety, Is.EqualTo(99));
            TurnResult duplicate = controller.Resolve(command);
            Assert.That(duplicate.Accepted || duplicate.ConsumesTurn, Is.False);
            Assert.That(duplicate.Events, Is.Empty);
            Assert.That(run.InventoryRevision, Is.EqualTo(1));
            Assert.That(run.Satiety, Is.EqualTo(99));
        }
    }
}