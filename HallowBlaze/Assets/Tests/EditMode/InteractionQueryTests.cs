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
    /// <summary>Verifies pure option discovery, exact target selection and atomic interaction effects.</summary>
    public sealed class InteractionQueryTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);
        private BoardState board;
        private RunState run;

        [SetUp]
        public void SetUp()
        {
            board = new BoardState();
            run = new RunState("m51", 51, new RunStateConfiguration(10, 5, 0, "forest.start"));
            Add(1, BoardLayer.Actor, EntityKind.Player, "player", new GridPosition(1, 1));
        }

        [Test]
        public void EmptyQueryDoesNotMutateState()
        {
            Assert.That(new InteractionQuery().GetOptions(board, run, PlayerId), Is.Empty);
            Assert.That(board.Count, Is.EqualTo(1));
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(board.GetActiveNoises(), Is.Empty);
        }

        [Test]
        public void PreviewRetainsExactInstanceAndCapturesActionAndPosition()
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(1, 2), 4);
            InteractionOption option = new InteractionQuery().GetOptions(board, run, PlayerId).Single();
            InteractCommand command = option.CreateCommand();
            Assert.That(option.IsAvailable, Is.True);
            Assert.That(command.ActionId, Is.EqualTo("collect"));
            Assert.That(command.TargetId, Is.EqualTo(item.Id));
            Assert.That(command.TargetPosition, Is.EqualTo(item.Position));
            Assert.That(board.TryGetEntity(item.Id, out BoardEntityState retained), Is.True);
            Assert.That(retained, Is.SameAs(item));
            Assert.That(run.Food, Is.EqualTo(5));
        }

        [Test]
        public void MultipleTargetsAndActionsHaveStableOrderWithoutAutomaticSelection()
        {
            Add(9, BoardLayer.Item, EntityKind.Item, "lever", new GridPosition(0, 1));
            Add(3, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            var options = PushQuery().GetOptions(board, run, PlayerId);
            Assert.That(options.Select(option => $"{option.TargetId.Value}:{option.ActionId}"),
                Is.EqualTo(new[] { "3:interact", "3:push", "9:interact" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDisplacedTargetIsRejectedWithoutCostOrNoise(bool displaced)
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(1, 2), 4);
            var query = new InteractionQuery();
            InteractCommand selection = query.GetOptions(board, run, PlayerId).Single().CreateCommand();
            if (displaced) board.TryMove(item.Id, new GridPosition(2, 1));
            else
            {
                board.TryRemove(item.Id);
                Add(8, BoardLayer.Item, EntityKind.Item, "carrot", item.Position, 4);
            }
            TurnResult result = new TurnResolver(ActionNoiseProfile.Prototype, query).Resolve(board, run, PlayerId, selection);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Events, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(board.GetActiveNoises(), Is.Empty);
            Assert.That(board.GetEntities().Single(entity => entity.Definition.Layer == BoardLayer.Item).Id,
                Is.EqualTo(displaced ? item.Id : new EntityId(8)));
        }

        [Test]
        public void CollectCommitsExactItemThenRewardThenOneCost()
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(1, 2), 4);
            var query = new InteractionQuery();
            TurnResult result = new TurnResolver(interactions: query).Resolve(board, run, PlayerId,
                query.GetOptions(board, run, PlayerId).Single().CreateCommand());
            Assert.That(result.Accepted, Is.True);
            Assert.That(board.TryGetEntity(item.Id, out _), Is.False);
            Assert.That(run.Food, Is.EqualTo(8));
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "InteractionPerformed", "ItemCollected", "FoodRestored", "ActionCostApplied" }));
        }

        [Test]
        public void BlockedPushIsDisabledAndFreeWhileClearPushMovesOnlyTarget()
        {
            BoardEntityState crate = Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            var query = PushQuery();
            InteractionOption push = query.GetOptions(board, run, PlayerId).Single(option => option.ActionId == "push");
            Assert.That(push.Availability, Is.EqualTo(InteractionAvailability.Blocked));
            var resolver = new TurnResolver(interactions: query);
            Assert.That(resolver.Resolve(board, run, PlayerId, push.CreateCommand()).Accepted, Is.False);
            Assert.That(run.Food, Is.EqualTo(5));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            Assert.That(resolver.Resolve(board, run, PlayerId, push.CreateCommand()).Accepted, Is.True);
            Assert.That(board.TryGetEntity(crate.Id, out BoardEntityState moved), Is.True);
            Assert.That(moved.Position, Is.EqualTo(new GridPosition(3, 1)));
            Assert.That(board.TryGetEntity(PlayerId, out BoardEntityState player), Is.True);
            Assert.That(player.Position, Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(run.Food, Is.EqualTo(4));
        }

        [Test]
        public void DecisionRequiredPreviewCannotRemoveGroundInstance()
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "future.item", new GridPosition(1, 2));
            var query = new InteractionQuery(new[] { new DeferredPickup() });
            InteractionOption option = query.GetOptions(board, run, PlayerId).Single();
            Assert.That(option.Availability, Is.EqualTo(InteractionAvailability.DecisionRequired));
            Assert.That(new TurnResolver(interactions: query).Resolve(board, run, PlayerId, option.CreateCommand()).Accepted, Is.False);
            Assert.That(board.TryGetEntity(item.Id, out BoardEntityState retained), Is.True);
            Assert.That(retained, Is.SameAs(item));
            Assert.That(run.Food, Is.EqualTo(5));
        }

        [TestCase("unknown", 1, 2, InteractionAvailability.Unsupported)]
        [TestCase("collect", 3, 1, InteractionAvailability.OutOfRange)]
        public void UnavailableSelectionsUseTheSameQueryAndResolverValidation(string action, int horizontal, int vertical,
            InteractionAvailability expected)
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(horizontal, vertical), 4);
            var query = new InteractionQuery();
            var command = new InteractCommand(item.Id, item.Position, action);
            Assert.That(query.GetAvailability(board, run, PlayerId, command), Is.EqualTo(expected));
            Assert.That(new TurnResolver(interactions: query).Resolve(board, run, PlayerId, command).Accepted, Is.False);
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(board.TryGetEntity(item.Id, out BoardEntityState retained), Is.True);
            Assert.That(retained, Is.SameAs(item));
        }

        [Test]
        public void PushRevalidatesDestinationAfterPreviewAndDoesNotConsumeAnythingWhenBlocked()
        {
            BoardEntityState crate = Add(7, BoardLayer.Obstacle, EntityKind.Obstacle, "crate", new GridPosition(2, 1));
            Add(100, BoardLayer.Terrain, new EntityKind("terrain"), "floor", new GridPosition(3, 1));
            var query = PushQuery();
            InteractCommand command = query.GetOptions(board, run, PlayerId).Single(option => option.ActionId == "push").CreateCommand();
            Add(8, BoardLayer.Item, EntityKind.Item, "other", new GridPosition(3, 1));
            Assert.That(query.GetAvailability(board, run, PlayerId, command), Is.EqualTo(InteractionAvailability.Blocked));
            Assert.That(new TurnResolver(interactions: query).Resolve(board, run, PlayerId, command).Accepted, Is.False);
            Assert.That(board.TryGetEntity(crate.Id, out BoardEntityState retained), Is.True);
            Assert.That(retained, Is.SameAs(crate));
            Assert.That(run.Food, Is.EqualTo(5));
        }

        [Test]
        public void ControllerExecutesLaterPhasesOnlyForAcceptedInteraction()
        {
            BoardEntityState item = Add(7, BoardLayer.Item, EntityKind.Item, "carrot", new GridPosition(1, 2), 4);
            BoardNoise existingNoise = board.EmitNoise(PlayerId, new GridPosition(1, 1), 1, 3);
            var phases = new List<string>();
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(ActionNoiseProfile.Prototype),
                new TurnPhaseHandlers(
                    (state, activeRun, actor, events) => phases.Add("enemies"),
                    (state, activeRun, actor, events) => phases.Add("environment"),
                    (state, activeRun, actor, events) => phases.Add("plan")));
            var query = new InteractionQuery();
            InteractCommand command = query.GetOptions(board, run, PlayerId).Single().CreateCommand();
            Assert.That(controller.Resolve(command).Accepted, Is.True);
            Assert.That(phases, Is.EqualTo(new[] { "enemies", "environment", "plan" }));
            Assert.That(board.GetActiveNoises().Single(noise => noise.Id == existingNoise.Id).RemainingTurns, Is.EqualTo(2));
            int foodAfterAccepted = run.Food;
            phases.Clear();
            Assert.That(controller.Resolve(command).Accepted, Is.False);
            Assert.That(phases, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(foodAfterAccepted));
            Assert.That(board.GetActiveNoises().Single(noise => noise.Id == existingNoise.Id).RemainingTurns, Is.EqualTo(2));
            Assert.That(board.TryGetEntity(item.Id, out _), Is.False);
        }

        private static InteractionQuery PushQuery() => new InteractionQuery(new IInteractionAction[]
            { new ContextInteractionAction(), new PushInteractionAction("crate") });

        private BoardEntityState Add(long id, BoardLayer layer, EntityKind kind, string content, GridPosition position, int food = 0)
        {
            var entity = new BoardEntityState(new EntityId(id), new BoardEntityDefinition(layer, kind, content,
                new BoardEntityTraits(layer == BoardLayer.Terrain, false, layer != BoardLayer.Actor && layer != BoardLayer.Terrain), food), position);
            Assert.That(board.TryAdd(entity), Is.True);
            return entity;
        }

        private sealed class DeferredPickup : IInteractionAction
        {
            public string Id => "pickup";
            public bool Supports(BoardEntityState target) => target.Definition.Layer == BoardLayer.Item;
            public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
                => InteractionAvailability.DecisionRequired;
            public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
                => Assert.Fail("A deferred decision cannot commit.");
        }
    }
}