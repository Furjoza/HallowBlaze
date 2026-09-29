using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies deterministic player-command resolution without Unity runtime state.
    /// </summary>
    public sealed class MovementResolverTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);
        private static readonly GridPosition Start = new GridPosition(1, 1);

        [Test]
        public void MoveChangesOneCellAndAppliesOneCost()
        {
            BoardState board = CreateBoardWithPlayer();
            GridPosition destination = Start.Move(Direction.East);
            AddTerrain(board, new EntityId(101), destination, isWalkable: true);
            RunState run = CreateRun(food: 5);

            TurnResult result = new TurnResolver().Resolve(
                board,
                run,
                PlayerId,
                new MoveCommand(Direction.East));

            Assert.That(result, Is.TypeOf<AcceptedTurnResult>());
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(board.TryGetEntity(PlayerId, out BoardEntityState player), Is.True);
            Assert.That(player.Position, Is.EqualTo(destination));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
            {
                "EntityMoved",
                "ActionCostApplied"
            }));
            var moved = (EntityMovedEvent)result.Events[0];
            Assert.That(moved.From, Is.EqualTo(Start));
            Assert.That(moved.To, Is.EqualTo(destination));
            Assert.That(((ActionCostAppliedEvent)result.Events[1]).CostAmount, Is.EqualTo(1));
        }

        [Test]
        public void RejectedMovesDoNotMutateBoardOrRun()
        {
            AssertRejectedMove(
                new GridPosition(0, 0),
                Direction.West,
                CommandRejectionCode.OutOfBounds,
                configureDestination: null);
            AssertRejectedMove(
                Start,
                Direction.East,
                CommandRejectionCode.Blocked,
                (board, destination) => AddTerrain(board, new EntityId(101), destination, isWalkable: false));
            AssertRejectedMove(
                Start,
                Direction.East,
                CommandRejectionCode.Blocked,
                (board, destination) =>
                {
                    AddTerrain(board, new EntityId(101), destination, isWalkable: true);
                    AddEntity(
                        board,
                        new EntityId(201),
                        BoardLayer.Obstacle,
                        EntityKind.Obstacle,
                        "rock",
                        destination,
                        new BoardEntityTraits(false, false, false));
                });
            AssertRejectedMove(
                Start,
                Direction.East,
                CommandRejectionCode.Blocked,
                (board, destination) =>
                {
                    AddTerrain(board, new EntityId(101), destination, isWalkable: true);
                    AddEntity(
                        board,
                        new EntityId(301),
                        BoardLayer.Actor,
                        EntityKind.Enemy,
                        "zombie",
                        destination,
                        BoardEntityTraits.Default);
                });
        }

        [Test]
        public void WaitConsumesOneTurnWithoutMovingPlayer()
        {
            BoardState board = CreateBoardWithPlayer();
            RunState run = CreateRun(food: 3);

            TurnResult result = new TurnResolver().Resolve(board, run, PlayerId, new WaitCommand());

            Assert.That(result, Is.TypeOf<AcceptedTurnResult>());
            Assert.That(run.Food, Is.EqualTo(2));
            Assert.That(board.TryGetEntity(PlayerId, out BoardEntityState player), Is.True);
            Assert.That(player.Position, Is.EqualTo(Start));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
            {
                "EntityWaited",
                "ActionCostApplied"
            }));
        }

        [Test]
        public void InteractAcceptsExplicitAdjacentTargetAndAppliesOneCost()
        {
            BoardState board = CreateBoardWithPlayer();
            EntityId targetId = new EntityId(2);
            AddEntity(
                board,
                targetId,
                BoardLayer.Item,
                EntityKind.Item,
                "lever",
                Start.Move(Direction.North),
                new BoardEntityTraits(false, false, true));
            RunState run = CreateRun(food: 4);

            TurnResult result = new TurnResolver().Resolve(
                board,
                run,
                PlayerId,
                new InteractCommand(targetId));

            Assert.That(result, Is.TypeOf<AcceptedTurnResult>());
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
            {
                "InteractionPerformed",
                "ActionCostApplied"
            }));
            Assert.That(((InteractionPerformedEvent)result.Events[0]).TargetId, Is.EqualTo(targetId));
            Assert.That(board.TryGetEntity(targetId, out _), Is.True);
        }

        [Test]
        public void InvalidInteractionsAreCostFreeAndLeaveStateUnchanged()
        {
            AssertRejectedInteraction(
                targetId: new EntityId(99),
                targetPosition: null,
                isInteractable: false,
                expectedCode: CommandRejectionCode.InvalidTarget);
            AssertRejectedInteraction(
                targetId: new EntityId(2),
                targetPosition: new GridPosition(5, 5),
                isInteractable: true,
                expectedCode: CommandRejectionCode.InvalidTarget);
            AssertRejectedInteraction(
                targetId: new EntityId(2),
                targetPosition: Start.Move(Direction.North),
                isInteractable: false,
                expectedCode: CommandRejectionCode.NoInteractionAvailable);
        }

        [Test]
        public void StarvationAfterActionCostPreventsExitOutcome()
        {
            BoardState board = CreateBoardWithPlayer();
            GridPosition destination = Start.Move(Direction.East);
            EntityId exitId = new EntityId(101);
            AddTerrain(board, exitId, destination, isWalkable: true, isExit: true);
            RunState run = CreateRun(food: 1);

            TurnResult result = new TurnResolver().Resolve(
                board,
                run,
                PlayerId,
                new MoveCommand(Direction.East));

            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
            {
                "EntityMoved",
                "ActionCostApplied",
                "PlayerStarved"
            }));
            Assert.That(run.Food, Is.Zero);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active));
        }

        [Test]
        public void AutomaticFoodIsCollectedAndAppliedBeforeActionCost()
        {
            BoardState board = CreateBoardWithPlayer();
            GridPosition destination = Start.Move(Direction.East);
            EntityId itemId = new EntityId(2);
            AddTerrain(board, new EntityId(101), destination, isWalkable: true);
            AddEntity(
                board,
                itemId,
                BoardLayer.Item,
                EntityKind.Item,
                "food",
                destination,
                new BoardEntityTraits(true, false, false),
                automaticFoodReward: 3);

            RunState run = CreateRun(food: 1);

            TurnResult result = new TurnResolver().Resolve(
                board,
                run,
                PlayerId,
                new MoveCommand(Direction.East));

            Assert.That(result, Is.TypeOf<AcceptedTurnResult>());
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(board.TryGetEntity(itemId, out _), Is.False);
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
            {
                "EntityMoved",
                "ItemCollected",
                "FoodRestored",
                "ActionCostApplied"
            }));
            Assert.That(((FoodRestoredEvent)result.Events[2]).Amount, Is.EqualTo(3));
        }

        [Test]
        public void AutomaticFoodCanSavePlayerBeforeExitIsEmitted()
        {
            BoardState board = CreateBoardWithPlayer();
            GridPosition destination = Start.Move(Direction.East);
            EntityId exitId = new EntityId(101);
            EntityId itemId = new EntityId(2);
            AddTerrain(board, exitId, destination, isWalkable: true, isExit: true);
            AddEntity(
                board,
                itemId,
                BoardLayer.Item,
                EntityKind.Item,
                "food",
                destination,
                BoardEntityTraits.Default,
                automaticFoodReward: 2);
            RunState run = CreateRun(food: 1);

            TurnResult result = new TurnResolver().Resolve(
                board,
                run,
                PlayerId,
                new MoveCommand(Direction.East));

            Assert.That(run.Food, Is.EqualTo(2));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
            {
                "EntityMoved",
                "ItemCollected",
                "FoodRestored",
                "ActionCostApplied",
                "ExitReached"
            }));
            Assert.That(((ExitReachedEvent)result.Events[4]).ExitId, Is.EqualTo(exitId));
        }

        [Test]
        public void NonFoodItemRemainsForExplicitInteraction()
        {
            BoardState board = CreateBoardWithPlayer();
            GridPosition destination = Start.Move(Direction.East);
            EntityId toolId = new EntityId(2);
            AddTerrain(board, new EntityId(101), destination, isWalkable: true);
            AddEntity(
                board,
                toolId,
                BoardLayer.Item,
                EntityKind.Item,
                "tool.shovel",
                destination,
                new BoardEntityTraits(true, false, true));

            TurnResult result = new TurnResolver().Resolve(
                board,
                CreateRun(food: 5),
                PlayerId,
                new MoveCommand(Direction.East));

            Assert.That(result, Is.TypeOf<AcceptedTurnResult>());
            Assert.That(board.TryGetEntity(toolId, out _), Is.True);
            Assert.That(result.Events, Has.Count.EqualTo(2));
        }

        [Test]
        public void OverflowingAutomaticFoodRewardIsRejectedWithoutMutation()
        {
            BoardState board = CreateBoardWithPlayer();
            GridPosition destination = Start.Move(Direction.East);
            AddTerrain(board, new EntityId(101), destination, isWalkable: true);
            AddEntity(
                board,
                new EntityId(2),
                BoardLayer.Item,
                EntityKind.Item,
                "food",
                destination,
                BoardEntityTraits.Default,
                automaticFoodReward: 1);
            RunState run = CreateRun(food: int.MaxValue);
            string before = Snapshot(board, run);

            TurnResult result = new TurnResolver().Resolve(
                board,
                run,
                PlayerId,
                new MoveCommand(Direction.East));

            AssertRejected(result, CommandRejectionCode.InvalidState);
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
        }

        [Test]
        public void AutomaticFoodRewardIsValidatedAndPartOfDefinitionIdentity()
        {
            var withoutReward = new BoardEntityDefinition(
                BoardLayer.Item,
                EntityKind.Item,
                "food",
                BoardEntityTraits.Default,
                automaticFoodReward: 0);
            var withReward = new BoardEntityDefinition(
                BoardLayer.Item,
                EntityKind.Item,
                "food",
                BoardEntityTraits.Default,
                automaticFoodReward: 2);
            var equivalentReward = new BoardEntityDefinition(
                BoardLayer.Item,
                EntityKind.Item,
                "food",
                BoardEntityTraits.Default,
                automaticFoodReward: 2);

            Assert.That(withReward, Is.EqualTo(equivalentReward));
            Assert.That(withReward.GetHashCode(), Is.EqualTo(equivalentReward.GetHashCode()));
            Assert.That(withReward, Is.Not.EqualTo(withoutReward));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardEntityDefinition(
                BoardLayer.Item,
                EntityKind.Item,
                "food",
                BoardEntityTraits.Default,
                automaticFoodReward: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardEntityDefinition(
                BoardLayer.Terrain,
                new EntityKind("terrain"),
                "terrain.food",
                BoardEntityTraits.Default,
                automaticFoodReward: 1));
        }

        [Test]
        public void InvalidStateAndCommandAreRejectedWithoutMutation()
        {
            BoardState board = CreateBoardWithPlayer();
            RunState run = CreateRun(food: 5);
            string before = Snapshot(board, run);
            var resolver = new TurnResolver();

            AssertRejected(
                resolver.Resolve(board, run, PlayerId, null),
                CommandRejectionCode.InvalidCommand);
            AssertRejected(
                resolver.Resolve(board, run, PlayerId, new UnsupportedCommand()),
                CommandRejectionCode.InvalidCommand);
            AssertRejected(
                resolver.Resolve(board, run, new EntityId(999), new WaitCommand()),
                CommandRejectionCode.InvalidState);

            Assert.That(Snapshot(board, run), Is.EqualTo(before));
        }

        [Test]
        public void EquivalentInputsProduceEquivalentStateAndOrderedEvents()
        {
            BoardState firstBoard = CreateBoardWithPlayer();
            BoardState secondBoard = CreateBoardWithPlayer();
            GridPosition destination = Start.Move(Direction.North);
            AddTerrain(firstBoard, new EntityId(101), destination, isWalkable: true, isExit: true);
            AddTerrain(secondBoard, new EntityId(101), destination, isWalkable: true, isExit: true);
            RunState firstRun = CreateRun(food: 2);
            RunState secondRun = CreateRun(food: 2);
            var resolver = new TurnResolver();

            TurnResult first = resolver.Resolve(
                firstBoard,
                firstRun,
                PlayerId,
                new MoveCommand(Direction.North));
            TurnResult second = resolver.Resolve(
                secondBoard,
                secondRun,
                PlayerId,
                new MoveCommand(Direction.North));

            Assert.That(Snapshot(secondBoard, secondRun), Is.EqualTo(Snapshot(firstBoard, firstRun)));
            Assert.That(EventSignatures(second.Events), Is.EqualTo(EventSignatures(first.Events)));
        }

        [Test]
        public void ResolutionAssemblyDoesNotReferenceUnity()
        {
            foreach (System.Reflection.AssemblyName reference in typeof(TurnResolver).Assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
            }
        }

        private static void AssertRejectedMove(
            GridPosition start,
            Direction direction,
            CommandRejectionCode expectedCode,
            Action<BoardState, GridPosition> configureDestination)
        {
            BoardState board = CreateBoardWithPlayer(start);
            GridPosition destination = start.Move(direction);
            configureDestination?.Invoke(board, destination);
            RunState run = CreateRun(food: 5);
            string before = Snapshot(board, run);

            TurnResult result = new TurnResolver().Resolve(
                board,
                run,
                PlayerId,
                new MoveCommand(direction));

            AssertRejected(result, expectedCode);
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
        }

        private static void AssertRejectedInteraction(
            EntityId targetId,
            GridPosition? targetPosition,
            bool isInteractable,
            CommandRejectionCode expectedCode)
        {
            BoardState board = CreateBoardWithPlayer();
            if (targetPosition.HasValue)
            {
                AddEntity(
                    board,
                    targetId,
                    BoardLayer.Item,
                    EntityKind.Item,
                    "target",
                    targetPosition.Value,
                    new BoardEntityTraits(false, false, isInteractable));
            }

            RunState run = CreateRun(food: 5);
            string before = Snapshot(board, run);
            TurnResult result = new TurnResolver().Resolve(
                board,
                run,
                PlayerId,
                new InteractCommand(targetId));

            AssertRejected(result, expectedCode);
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
        }

        private static void AssertRejected(TurnResult result, CommandRejectionCode expectedCode)
        {
            Assert.That(result, Is.TypeOf<RejectedTurnResult>());
            Assert.That(((RejectedTurnResult)result).RejectionCode, Is.EqualTo(expectedCode));
            Assert.That(result.ConsumesTurn, Is.False);
            Assert.That(result.Events, Is.Empty);
        }

        private static BoardState CreateBoardWithPlayer()
        {
            return CreateBoardWithPlayer(Start);
        }

        private static BoardState CreateBoardWithPlayer(GridPosition position)
        {
            var board = new BoardState();
            AddTerrain(board, new EntityId(100), position, isWalkable: true);
            AddEntity(
                board,
                PlayerId,
                BoardLayer.Actor,
                EntityKind.Player,
                "player",
                position,
                BoardEntityTraits.Default);
            return board;
        }

        private static void AddTerrain(
            BoardState board,
            EntityId id,
            GridPosition position,
            bool isWalkable,
            bool isExit = false)
        {
            AddEntity(
                board,
                id,
                BoardLayer.Terrain,
                new EntityKind("terrain"),
                isExit ? "exit" : "ground",
                position,
                new BoardEntityTraits(isWalkable, isExit, false));
        }

        private static void AddEntity(
            BoardState board,
            EntityId id,
            BoardLayer layer,
            EntityKind kind,
            string contentId,
            GridPosition position,
            BoardEntityTraits traits,
            int automaticFoodReward = 0)
        {
            Assert.That(
                board.TryAdd(new BoardEntityState(
                    id,
                    new BoardEntityDefinition(layer, kind, contentId, traits, automaticFoodReward),
                    position)),
                Is.True);
        }

        private static RunState CreateRun(int food)
        {
            return new RunState(
                "m3.4-test-run",
                3400,
                new RunStateConfiguration(10, food, 0, "forest.start"));
        }

        private static string Snapshot(BoardState board, RunState run)
        {
            string entities = string.Join(
                "|",
                board.GetEntities().Select(entity =>
                    $"{entity.Id.Value}:{entity.Definition.Layer}:{entity.Position.X}:{entity.Position.Y}"));
            return $"{entities};health={run.Health};food={run.Food};status={run.Status}";
        }

        private static IReadOnlyList<string> EventSignatures(IReadOnlyList<GameEvent> events)
        {
            return events.Select(gameEvent =>
            {
                switch (gameEvent)
                {
                    case EntityMovedEvent moved:
                        return $"{moved.EventType}:{moved.EntityId.Value}:{moved.From}:{moved.To}";
                    case ActionCostAppliedEvent cost:
                        return $"{cost.EventType}:{cost.EntityId.Value}:{cost.CostAmount}";
                    case ItemCollectedEvent collected:
                        return $"{collected.EventType}:{collected.PlayerId.Value}:{collected.ItemId.Value}";
                    case FoodRestoredEvent restored:
                        return $"{restored.EventType}:{restored.PlayerId.Value}:{restored.SourceItemId.Value}:{restored.Amount}";
                    case ExitReachedEvent exit:
                        return $"{exit.EventType}:{exit.PlayerId.Value}:{exit.ExitId.Value}";
                    case PlayerStarvedEvent starved:
                        return $"{starved.EventType}:{starved.EntityId.Value}";
                    default:
                        return gameEvent.EventType;
                }
            }).ToArray();
        }

        private sealed class UnsupportedCommand : PlayerCommand
        {
            public override string CommandType => "Unsupported";
        }
    }
}