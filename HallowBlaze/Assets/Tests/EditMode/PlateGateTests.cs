using System;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies explicit fixed terrain links and immediate derived gate state without Unity views.</summary>
    public sealed class PlateGateTests
    {
        /// <summary>Actor entry opens gate terrain immediately and departure closes it; old snapshots and clones remain independent.</summary>
        [Test]
        public void ActorPressureUpdatesWalkabilityInsideTheMutation()
        {
            BoardState board = CreateBoard();
            BoardState clone = board.Clone();
            Assert.That(board.GetGates().Single().IsOpen, Is.False);
            Assert.That(board.TryMove(new EntityId(1), new GridPosition(2, 1)), Is.True);
            Assert.That(board.GetPressurePlates().Single().IsPressed, Is.True);
            Assert.That(board.GetGates().Single().IsOpen, Is.True);
            Assert.That(board.TryGetEntity(new EntityId(20), out BoardEntityState gate), Is.True);
            Assert.That(gate.Definition.Traits.IsWalkable, Is.True);
            Assert.That(board.TakePlateGateChanges().Select(change => change.Plate != null ? "plate" : "gate"), Is.EqualTo(new[] { "plate", "gate" }));
            Assert.That(board.TryMove(new EntityId(1), new GridPosition(1, 1)), Is.True);
            Assert.That(board.GetGates().Single().IsOpen, Is.False);
            Assert.That(gate.Definition.Traits.IsWalkable, Is.True);
            Assert.That(clone.GetGates().Single().IsOpen, Is.False);
            Assert.That(clone.GetPressurePlates().Single().IsPressed, Is.False);
            Assert.That(clone.TakePlateGateChanges(), Is.Empty);
        }

        /// <summary>Invalid bindings fail atomically and fixed infrastructure cannot be moved or removed after publication.</summary>
        [Test]
        public void PublishedLayoutIsFixedAndRejectedMutationsDoNotEmitChanges()
        {
            BoardState board = CreateBoard();
            Assert.That(board.TryMove(new EntityId(10), new GridPosition(2, 2)), Is.False);
            Assert.That(board.TryRemove(new EntityId(20)), Is.False);
            Assert.That(board.TryMove(new EntityId(1), new GridPosition(9, 9)), Is.False);
            Assert.That(board.TakePlateGateChanges(), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => board.ConfigurePlateGates(Array.Empty<PressurePlateDefinition>(), Array.Empty<GateDefinition>()));
        }

        /// <summary>Player departure closes the gate before a previously announced enemy step without another cost, attack or replan.</summary>
        [Test]
        public void PlayerDepartureClosesGateBeforeLockedEnemyExecution()
        {
            BoardState board = CreateBoard();
            board.TryMove(new EntityId(1), new GridPosition(2, 1));
            board.TakePlateGateChanges();
            var enemy = new ShamblerState(new EntityId(2), new ShamblerDefinition(10));
            board.TryAdd(new BoardEntityState(enemy.ActorId, new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy"), new GridPosition(4, 2)));
            enemy.LockIntent(new EnemyIntent(enemy.ActorId, EnemyIntentKind.Move, new GridPosition(4, 1), new EntityId(1), true));
            var run = new RunState("gate", 63, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var controller = new TurnController(board, run, new EntityId(1), phases: new EnemyBatchTurnPhases(new[] { enemy }).Handlers);
            TurnResult result = controller.Resolve(new MoveCommand(Direction.West));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "EntityMoved", "PressurePlateChanged", "GateChanged", "ActionCostApplied", "EntityWaited" }));
            Assert.That(result.Events.OfType<GateChangedEvent>().Single().Gate.IsOpen, Is.False);
            Assert.That(run.Food, Is.EqualTo(19));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(enemy.ActorId)).Position, Is.EqualTo(new GridPosition(4, 2)));
        }

        /// <summary>An earlier enemy's departure recomputes and records gate closure before the next enemy uses live occupancy.</summary>
        [Test]
        public void EnemyDepartureClosesGateBetweenSequentialOpportunities()
        {
            BoardState board = CreateBoard();
            var first = new ShamblerState(new EntityId(2), new ShamblerDefinition(10));
            var second = new ShamblerState(new EntityId(3), new ShamblerDefinition(10));
            var definition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy");
            board.TryAdd(new BoardEntityState(first.ActorId, definition, new GridPosition(2, 1)));
            board.TryAdd(new BoardEntityState(second.ActorId, definition, new GridPosition(4, 2)));
            board.TakePlateGateChanges();
            first.LockIntent(new EnemyIntent(first.ActorId, EnemyIntentKind.Move, new GridPosition(2, 2), new EntityId(1), true));
            second.LockIntent(new EnemyIntent(second.ActorId, EnemyIntentKind.Move, new GridPosition(4, 1), new EntityId(1), true));
            var run = new RunState("gate", 63, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var controller = new TurnController(board, run, new EntityId(1), phases: new MixedEnemyTurnPhases(
                new[] { second, first }, Array.Empty<ListenerState>()).Handlers);
            TurnResult result = controller.Resolve(new WaitCommand());
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "EntityWaited", "ActionCostApplied", "EntityMoved", "PressurePlateChanged", "GateChanged", "EntityWaited" }));
            Assert.That(result.Events.OfType<EntityWaitedEvent>().Last().EntityId, Is.EqualTo(second.ActorId));
            Assert.That(board.GetGates().Single().IsOpen, Is.False);
            Assert.That(board.TakePlateGateChanges(), Is.Empty);
            Assert.That(run.Food, Is.EqualTo(19));
        }

        /// <summary>Atomic boulder/player handover keeps a shared pressure plate pressed without a false close/open pair.</summary>
        [Test]
        public void BoulderHandoverHasNoIntermediatePressureFlicker()
        {
            BoardState board = CreateBoard();
            board.TryAdd(new BoardEntityState(new EntityId(2), BoulderPushAction.Definition, new GridPosition(2, 1)));
            board.TakePlateGateChanges();
            var run = new RunState("gate", 63, new RunStateConfiguration(100, 20, 0, "forest.start"));
            TurnResult result = new TurnResolver().Resolve(board, run, new EntityId(1),
                new InteractCommand(new EntityId(2), new GridPosition(2, 1), BoulderPushAction.ActionId));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Events.OfType<BoulderPushedEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<PressurePlateChangedEvent>(), Is.Empty);
            Assert.That(result.Events.OfType<GateChangedEvent>(), Is.Empty);
            Assert.That(board.GetGates().Single().IsOpen, Is.True);
        }

        /// <summary>Only declared actor/boulder categories press plates; arbitrary obstacles and items never impersonate pressure sources.</summary>
        [TestCase(0, "player", true)]
        [TestCase(0, "enemy", true)]
        [TestCase(0, "boulder", false)]
        [TestCase(1, "player", false)]
        [TestCase(1, "enemy", false)]
        [TestCase(1, "boulder", true)]
        [TestCase(2, "player", true)]
        [TestCase(2, "enemy", true)]
        [TestCase(2, "boulder", true)]
        [TestCase(2, "crate", false)]
        [TestCase(2, "item", false)]
        [TestCase(2, "walkable-boulder", false)]
        public void PressureCategoriesAreExplicit(int category, string occupant, bool pressed)
        {
            BoardState board = CreateBoard((PlateOccupants)category);
            BoardEntityDefinition definition = occupant == "boulder" ? BoulderPushAction.Definition :
                occupant == "walkable-boulder" ? new BoardEntityDefinition(BoardLayer.Obstacle, EntityKind.Obstacle, BoulderPushAction.ContentId, new BoardEntityTraits(true, false, true)) :
                occupant == "item" ? new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "item") :
                occupant == "crate" ? new BoardEntityDefinition(BoardLayer.Obstacle, EntityKind.Obstacle, "crate") :
                new BoardEntityDefinition(BoardLayer.Actor, occupant == "player" ? EntityKind.Player : EntityKind.Enemy, occupant);
            board.TryAdd(new BoardEntityState(new EntityId(2), definition, new GridPosition(2, 1)));
            Assert.That(board.GetPressurePlates().Single().IsPressed, Is.EqualTo(pressed));
            Assert.That(board.GetGates().Single().IsOpen, Is.EqualTo(pressed));
            board.TryRemove(new EntityId(2));
            Assert.That(board.GetGates().Single().IsOpen, Is.False);
        }

        /// <summary>All and Any are explicit per-gate rules, not dependent on registration order or the last plate updated.</summary>
        [TestCase(0)]
        [TestCase(1)]
        public void MultiplePlatesFollowTheConfiguredCombination(int ruleValue)
        {
            GateRule rule = (GateRule)ruleValue;
            BoardState board = CreateBoard(rule: rule, secondPlate: true);
            board.TryMove(new EntityId(1), new GridPosition(2, 1));
            Assert.That(board.GetGates().Single().IsOpen, Is.EqualTo(rule == GateRule.Any));
            board.TryAdd(new BoardEntityState(new EntityId(2), new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy"), new GridPosition(2, 3)));
            Assert.That(board.GetGates().Single().IsOpen, Is.True);
            board.TryMove(new EntityId(1), new GridPosition(1, 1));
            Assert.That(board.GetGates().Single().IsOpen, Is.EqualTo(rule == GateRule.Any));
            board.TryRemove(new EntityId(2));
            Assert.That(board.GetGates().Single().IsOpen, Is.False);
            Assert.That(board.GetPressurePlates().All(plate => !plate.IsPressed), Is.True);
        }

        /// <summary>Boulder entry and exit update boulder-only plates even when the advancing player replaces it on the same cell.</summary>
        [Test]
        public void BoulderOnlyPlateReleasesWhenThePlayerReplacesItsWeight()
        {
            BoardState board = CreateBoard(PlateOccupants.Boulders);
            board.TryMove(new EntityId(1), new GridPosition(0, 1));
            board.TryAdd(new BoardEntityState(new EntityId(2), BoulderPushAction.Definition, new GridPosition(1, 1)));
            var run = new RunState("gate", 63, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var controller = new TurnController(board, run, new EntityId(1));
            TurnResult entered = controller.Resolve(new InteractCommand(new EntityId(2), new GridPosition(1, 1), BoulderPushAction.ActionId));
            Assert.That(entered.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "InteractionPerformed", "BoulderPushed", "PressurePlateChanged", "GateChanged", "ActionCostApplied", "Noise" }));
            Assert.That(board.GetGates().Single().IsOpen, Is.True);
            TurnResult left = controller.Resolve(new InteractCommand(new EntityId(2), new GridPosition(2, 1), BoulderPushAction.ActionId));
            Assert.That(left.Accepted, Is.True);
            Assert.That(left.Events.OfType<GateChangedEvent>().Single().Gate.IsOpen, Is.False);
            Assert.That(board.GetGates().Single().IsOpen, Is.False);
            Assert.That(run.Food, Is.EqualTo(18));
        }

        /// <summary>Closing does not crush a current occupant; leaving is legal, subsequent entry into the closed terrain is free rejection.</summary>
        [Test]
        public void ClosingGateNeverDisplacesOrDamagesItsExistingOccupant()
        {
            BoardState board = CreateBoard();
            board.TryMove(new EntityId(1), new GridPosition(2, 1));
            board.TryAdd(new BoardEntityState(new EntityId(2), new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy"), new GridPosition(4, 1)));
            board.TryMove(new EntityId(1), new GridPosition(1, 1));
            Assert.That(board.GetGates().Single().IsOpen, Is.False);
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(new EntityId(2))).Position, Is.EqualTo(new GridPosition(4, 1)));
            board.TryMove(new EntityId(1), new GridPosition(3, 1));
            board.TryRemove(new EntityId(2));
            board.TakePlateGateChanges();
            var run = new RunState("gate", 63, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var controller = new TurnController(board, run, new EntityId(1));
            BoardNoise previousNoise = board.EmitNoise(new EntityId(1), new GridPosition(3, 1), 4, 3);
            board.TryGetEntity(BoardLayer.Terrain, new GridPosition(6, 6), out BoardEntityState pitTerrain);
            Assert.That(board.TryCreatePit(pitTerrain.Id, 3), Is.True);
            TurnResult blocked = controller.Resolve(new MoveCommand(Direction.East));
            Assert.That(blocked.Accepted, Is.False);
            Assert.That(blocked.Events, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(20));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(board.GetActiveNoises().Single(), Is.SameAs(previousNoise));
            Assert.That(board.GetPits().Single().RemainingTurns, Is.EqualTo(3));
            Assert.That(board.TakePlateGateChanges(), Is.Empty);
        }

        /// <summary>A retained Listener investigation into a newly closed gate waits without retargeting or consuming another action.</summary>
        [Test]
        public void ClosedGateInvalidatesListenerInvestigationWithoutReplanning()
        {
            BoardState board = CreateBoard();
            board.TryMove(new EntityId(1), new GridPosition(2, 1));
            var listener = new ListenerState(new EntityId(2), new ListenerDefinition());
            board.TryAdd(new BoardEntityState(listener.ActorId, new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy.listener"), new GridPosition(4, 2)));
            board.RegisterListener(listener.ActorId);
            BoardNoise signal = board.EmitNoise(new EntityId(1), new GridPosition(3, 1), 4, 5);
            listener.LockIntent(new EnemyIntent(listener.ActorId, EnemyIntentKind.Investigate, new GridPosition(4, 1),
                investigationTarget: signal.Position, noiseId: signal.Id, sourcePosition: new GridPosition(4, 2)));
            board.TakePlateGateChanges();
            var run = new RunState("gate", 63, new RunStateConfiguration(100, 20, 0, "forest.start"));
            var controller = new TurnController(board, run, new EntityId(1), phases: new MixedEnemyTurnPhases(
                Array.Empty<ShamblerState>(), new[] { listener }).Handlers);
            TurnResult result = controller.Resolve(new MoveCommand(Direction.West));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "EntityMoved", "PressurePlateChanged", "GateChanged", "ActionCostApplied", "EntityWaited" }));
            Assert.That(result.Events.OfType<ListenerInvestigatedEvent>(), Is.Empty);
            Assert.That(board.GetEntities().Single(entity => entity.Id.Equals(listener.ActorId)).Position, Is.EqualTo(new GridPosition(4, 2)));
            Assert.That(run.Food, Is.EqualTo(19));
        }

        /// <summary>Path and hearing queries use the derived terrain immediately; old emitted hearing footprints remain immutable.</summary>
        [Test]
        public void PathAndNoiseUseNewGateStateWithoutInvalidatingOldSnapshots()
        {
            BoardState board = CreateBoard();
            board.TryMove(new EntityId(1), new GridPosition(3, 1));
            var enemy = new ShamblerState(new EntityId(2), new ShamblerDefinition(10));
            board.TryAdd(new BoardEntityState(enemy.ActorId, new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "enemy"), new GridPosition(5, 1)));
            var planner = new ShamblerPlanner();
            Assert.That(planner.Plan(board, enemy, new EntityId(1)).TargetPosition, Is.EqualTo(new GridPosition(5, 2)));
            Assert.That(board.EmitNoise(new EntityId(1), new GridPosition(3, 1), 2, 5).IsAudible(new GridPosition(4, 1)), Is.False);
            board.TryAdd(new BoardEntityState(new EntityId(3), new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "weight"), new GridPosition(2, 1)));
            Assert.That(planner.Plan(board, enemy, new EntityId(1)).TargetPosition, Is.EqualTo(new GridPosition(4, 1)));
            BoardNoise opened = board.EmitNoise(new EntityId(1), new GridPosition(3, 1), 2, 5);
            Assert.That(opened.IsAudible(new GridPosition(4, 1)), Is.True);
            board.TryRemove(new EntityId(3));
            Assert.That(opened.IsAudible(new GridPosition(4, 1)), Is.True);
            Assert.That(board.EmitNoise(new EntityId(1), new GridPosition(3, 1), 2, 5).IsAudible(new GridPosition(4, 1)), Is.False);
        }

        /// <summary>Starvation retains entry pressure/gate events before the terminal event and skips every later phase.</summary>
        [Test]
        public void StarvationDoesNotUndoPressureOrExecuteLaterPhases()
        {
            BoardState board = CreateBoard();
            var run = new RunState("gate", 63, new RunStateConfiguration(100, 1, 0, "forest.start"));
            TurnPhaseHandler forbidden = (state, activeRun, player, events) => Assert.Fail("Terminal turns cannot run later phases.");
            var controller = new TurnController(board, run, new EntityId(1), new TurnResolver(ActionNoiseProfile.Prototype),
                new TurnPhaseHandlers(forbidden, forbidden, forbidden));
            TurnResult result = controller.Resolve(new MoveCommand(Direction.East));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "EntityMoved", "PressurePlateChanged", "GateChanged", "ActionCostApplied", "Noise", "PlayerStarved" }));
            Assert.That(board.GetGates().Single().IsOpen, Is.True);
            Assert.That(controller.IsTerminal, Is.True);
            Assert.That(run.Food, Is.Zero);
        }

        /// <summary>Two gates may share one visible link but declare different All/Any rules; clones preserve and isolate that fixed layout.</summary>
        [Test]
        public void SharedLinkAllowsExplicitIndependentGateRulesAndCloneIsolation()
        {
            var board = new BoardState();
            board.TryAdd(new BoardEntityState(new EntityId(10), PlateGateValidator.PlateContent, new GridPosition(1, 1)));
            board.TryAdd(new BoardEntityState(new EntityId(11), PlateGateValidator.PlateContent, new GridPosition(2, 1)));
            board.TryAdd(new BoardEntityState(new EntityId(20), PlateGateValidator.GateContent, new GridPosition(3, 1)));
            board.TryAdd(new BoardEntityState(new EntityId(21), PlateGateValidator.GateContent, new GridPosition(4, 1)));
            board.ConfigurePlateGates(new[] { new PressurePlateDefinition(new EntityId(11), "A"), new PressurePlateDefinition(new EntityId(10), "A") },
                new[] { new GateDefinition(new EntityId(21), "A", GateRule.Any), new GateDefinition(new EntityId(20), "A", GateRule.All) });
            var actorDefinition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "weight");
            board.TryAdd(new BoardEntityState(new EntityId(1), actorDefinition, new GridPosition(1, 1)));
            Assert.That(board.GetGates().Select(gate => gate.IsOpen), Is.EqualTo(new[] { false, true }));
            board.TryAdd(new BoardEntityState(new EntityId(2), actorDefinition, new GridPosition(2, 1)));
            Assert.That(board.GetGates().All(gate => gate.IsOpen), Is.True);
            BoardState clone = board.Clone();
            board.TakePlateGateChanges();
            Assert.That(clone.TakePlateGateChanges().Count, Is.EqualTo(4));
            clone.TryRemove(new EntityId(1));
            Assert.That(clone.GetGates().Select(gate => gate.IsOpen), Is.EqualTo(new[] { false, true }));
            Assert.That(board.GetGates().All(gate => gate.IsOpen), Is.True);
            Assert.That(board.TakePlateGateChanges(), Is.Empty);
        }

        /// <summary>Environment mutations publish their gate changes before the next planner and terminal checks, without an extra player turn.</summary>
        [Test]
        public void EnvironmentPressureIsRecordedBeforeNextPlanning()
        {
            BoardState board = CreateBoard();
            var run = new RunState("gate", 63, new RunStateConfiguration(100, 20, 0, "forest.start"));
            bool planned = false;
            var phases = new TurnPhaseHandlers(applyEnvironment: (state, activeRun, player, events) =>
                state.TryMove(player, new GridPosition(2, 1)), planNextIntents: (state, activeRun, player, events) =>
                {
                    Assert.That(state.GetGates().Single().IsOpen, Is.True);
                    Assert.That(events.Last(), Is.TypeOf<GateChangedEvent>());
                    planned = true;
                });
            TurnResult result = new TurnController(board, run, new EntityId(1), phases: phases).Resolve(new WaitCommand());
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "EntityWaited", "ActionCostApplied", "PressurePlateChanged", "GateChanged" }));
            Assert.That(planned, Is.True);
            Assert.That(run.Food, Is.EqualTo(19));
        }

        /// <summary>Link validation is pure and rejects orphan, duplicate and unbound authored cells.</summary>
        [Test]
        public void LinkValidatorRejectsIncompleteBindingsWithoutMutation()
        {
            var board = new BoardState();
            var plateId = new EntityId(1);
            var gateId = new EntityId(2);
            board.TryAdd(new BoardEntityState(plateId, PlateGateValidator.PlateContent, new GridPosition(1, 1)));
            board.TryAdd(new BoardEntityState(gateId, PlateGateValidator.GateContent, new GridPosition(3, 1)));
            var plates = new[] { new PressurePlateDefinition(plateId, "link.bridge") };
            var gates = new[] { new GateDefinition(gateId, "link.bridge") };
            var before = board.GetEntities();
            Assert.That(PlateGateValidator.Validate(board, plates, gates), Is.Empty);
            Assert.That(PlateGateValidator.Validate(board, plates, new[] { new GateDefinition(gateId, "other") }), Is.Not.Empty);
            Assert.That(PlateGateValidator.Validate(board, new[] { plates[0], plates[0] }, gates), Is.Not.Empty);
            Assert.That(PlateGateValidator.Validate(board, Array.Empty<PressurePlateDefinition>(), gates), Is.Not.Empty);
            Assert.That(board.GetEntities(), Is.EqualTo(before));
            Assert.Throws<ArgumentException>(() => board.ConfigurePlateGates(plates, new[] { new GateDefinition(gateId, "other") }));
            Assert.That(board.GetGates(), Is.Empty);
            Assert.That(board.GetEntities(), Is.EqualTo(before));
            board.ConfigurePlateGates(plates, gates);
            Assert.That(board.GetGates().Single().IsOpen, Is.False);
        }

        private static BoardState CreateBoard(PlateOccupants occupants = PlateOccupants.ActorsOrBoulders, GateRule rule = GateRule.All, bool secondPlate = false)
        {
            var board = new BoardState();
            board.TryAdd(new BoardEntityState(new EntityId(10), PlateGateValidator.PlateContent, new GridPosition(2, 1)));
            board.TryAdd(new BoardEntityState(new EntityId(20), PlateGateValidator.GateContent, new GridPosition(4, 1)));
            if (secondPlate) board.TryAdd(new BoardEntityState(new EntityId(11), PlateGateValidator.PlateContent, new GridPosition(2, 3)));
            for (int horizontal = 0; horizontal < 8; horizontal++)
            for (int vertical = 0; vertical < 8; vertical++)
                if (!board.TryGetEntity(BoardLayer.Terrain, new GridPosition(horizontal, vertical), out _))
                    board.TryAdd(new BoardEntityState(new EntityId(100 + horizontal * 8 + vertical),
                        new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false)),
                        new GridPosition(horizontal, vertical)));
            board.TryAdd(new BoardEntityState(new EntityId(1), new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"), new GridPosition(1, 1)));
            var plates = new System.Collections.Generic.List<PressurePlateDefinition> { new PressurePlateDefinition(new EntityId(10), "link.bridge", occupants) };
            if (secondPlate) plates.Add(new PressurePlateDefinition(new EntityId(11), "link.bridge", occupants));
            board.ConfigurePlateGates(plates, new[] { new GateDefinition(new EntityId(20), "link.bridge", rule) });
            return board;
        }
    }
}