using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Resolves one player command against authoritative board and run state.
    /// Rejected commands leave both states unchanged.
    /// </summary>
    public sealed class TurnResolver : IPlayerPhaseResolver
    {
        private const int ActionFoodCost = 1;
        private readonly ActionNoiseProfile noiseProfile;
        private readonly InteractionQuery interactions;

        /// <summary>Creates a resolver with an optional immutable noise profile; null preserves silent action behavior.</summary>
        /// <param name="noiseProfile">Action emission budgets, or null for no emission. Rejections never emit noise.</param>
        /// <param name="interactions">Shared read-only action catalog; null uses the default interaction hooks.</param>
        public TurnResolver(ActionNoiseProfile noiseProfile = null, InteractionQuery interactions = null)
        {
            this.noiseProfile = noiseProfile;
            this.interactions = interactions ?? new InteractionQuery();
        }

        /// <summary>Validates and resolves one player command.</summary>
        /// <param name="boardState">The authoritative mutable board state.</param>
        /// <param name="runState">The authoritative mutable run state.</param>
        /// <param name="playerId">The board-local player identifier.</param>
        /// <param name="command">The command to resolve.</param>
        /// <returns>An accepted result with ordered events, or a cost-free rejected result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="boardState" /> or <paramref name="runState" /> is null.</exception>
        public TurnResult Resolve(
            BoardState boardState,
            RunState runState,
            EntityId playerId,
            PlayerCommand command)
        {
            if (boardState == null)
                throw new ArgumentNullException(nameof(boardState));
            if (runState == null)
                throw new ArgumentNullException(nameof(runState));
            if (command == null)
                return new RejectedTurnResult(CommandRejectionCode.InvalidCommand);
            if (runState.Status != RunStatus.Active || runState.Health <= 0 || runState.Food <= 0)
                return new RejectedTurnResult(CommandRejectionCode.InvalidState);
            if (!boardState.TryGetEntity(playerId, out BoardEntityState player) ||
                player.Definition.Layer != BoardLayer.Actor ||
                !player.Definition.Kind.Equals(EntityKind.Player))
            {
                return new RejectedTurnResult(CommandRejectionCode.InvalidState);
            }

            TurnResult result;
            switch (command)
            {
                case MoveCommand moveCommand:
                    result = ResolveMove(boardState, runState, player, moveCommand);
                    break;
                case WaitCommand _:
                    result = ResolveWait(runState, playerId);
                    break;
                case InteractCommand interactCommand:
                    result = ResolveInteract(boardState, runState, player, interactCommand);
                    break;
                default:
                    return new RejectedTurnResult(CommandRejectionCode.InvalidCommand);
            }
            int strength = command is MoveCommand ? noiseProfile?.MovementStrength ?? 0 :
                command is InteractCommand ? noiseProfile?.InteractionStrength ?? 0 : 0;
            if (!result.Accepted || strength == 0)
                return result;
            boardState.TryGetEntity(playerId, out BoardEntityState currentPlayer);
            BoardNoise noise = boardState.EmitNoise(playerId, currentPlayer.Position, strength, noiseProfile.LifetimeTurns);
            var events = new List<GameEvent>(result.Events);
            int terminalIndex = events.FindIndex(gameEvent => gameEvent is ExitReachedEvent ||
                gameEvent is PlayerStarvedEvent || gameEvent is PlayerDiedEvent);
            events.Insert(terminalIndex < 0 ? events.Count : terminalIndex, new NoiseEvent(noise));
            return new AcceptedTurnResult(events);
        }

        private TurnResult ResolveMove(
            BoardState boardState,
            RunState runState,
            BoardEntityState player,
            MoveCommand command)
        {
            GridPosition destination;
            try
            {
                destination = player.Position.Move(command.Direction);
            }
            catch (OverflowException)
            {
                return new RejectedTurnResult(CommandRejectionCode.OutOfBounds);
            }

            if (!boardState.Bounds.Contains(destination))
                return new RejectedTurnResult(CommandRejectionCode.OutOfBounds);
            if (!boardState.TryGetEntity(BoardLayer.Terrain, destination, out BoardEntityState terrain) ||
                !terrain.Definition.Traits.IsWalkable)
            {
                return new RejectedTurnResult(CommandRejectionCode.Blocked);
            }
            if (boardState.TryGetEntity(BoardLayer.Obstacle, destination, out BoardEntityState obstacle) &&
                !obstacle.Definition.Traits.IsWalkable)
            {
                return new RejectedTurnResult(CommandRejectionCode.Blocked);
            }
            if (boardState.TryGetEntity(BoardLayer.Actor, destination, out _))
                return new RejectedTurnResult(CommandRejectionCode.Blocked);

            BoardEntityState automaticFood = null;
            if (boardState.TryGetEntity(BoardLayer.Item, destination, out BoardEntityState item) &&
                item.Definition.AutomaticFoodReward > 0)
            {
                if (runState.Food > int.MaxValue - item.Definition.AutomaticFoodReward)
                    return new RejectedTurnResult(CommandRejectionCode.InvalidState);
                automaticFood = item;
            }

            if (!boardState.TryMove(player.Id, destination))
                return new RejectedTurnResult(CommandRejectionCode.Blocked);

            var events = new List<GameEvent>
            {
                new EntityMovedEvent(player.Id, player.Position, destination)
            };
            if (automaticFood != null)
            {
                CollectAutomaticFood(boardState, runState, player.Id, automaticFood, events);
            }
            ApplyCost(runState, player.Id, events);
            if (runState.Food == 0)
            {
                events.Add(new PlayerStarvedEvent(player.Id));
                return new AcceptedTurnResult(events);
            }
            if (terrain.Definition.Traits.IsExit)
                events.Add(new ExitReachedEvent(player.Id, terrain.Id));
            return new AcceptedTurnResult(events);
        }

        private TurnResult ResolveWait(RunState runState, EntityId playerId)
        {
            var events = new List<GameEvent>
            {
                new EntityWaitedEvent(playerId)
            };
            ApplyCost(runState, playerId, events);
            if (runState.Food == 0)
                events.Add(new PlayerStarvedEvent(playerId));
            return new AcceptedTurnResult(events);
        }

        private TurnResult ResolveInteract(
            BoardState boardState,
            RunState runState,
            BoardEntityState player,
            InteractCommand command)
        {
            InteractionAvailability availability = interactions.Validate(boardState, runState, player.Id, command,
                out BoardEntityState target, out IInteractionAction action);
            if (availability != InteractionAvailability.Available)
            {
                CommandRejectionCode reason = availability == InteractionAvailability.InvalidTarget ||
                    availability == InteractionAvailability.OutOfRange ? CommandRejectionCode.InvalidTarget :
                    availability == InteractionAvailability.InvalidState ? CommandRejectionCode.InvalidState :
                    availability == InteractionAvailability.Blocked ? CommandRejectionCode.Blocked :
                    CommandRejectionCode.NoInteractionAvailable;
                return new RejectedTurnResult(reason);
            }

            var events = new List<GameEvent>
            {
                new InteractionPerformedEvent(player.Id, target.Id)
            };
            action.Apply(boardState, runState, player, target, events);
            ApplyCost(runState, player.Id, events);
            if (runState.Food == 0)
                events.Add(new PlayerStarvedEvent(player.Id));
            return new AcceptedTurnResult(events);
        }

        private static void CollectAutomaticFood(
            BoardState boardState,
            RunState runState,
            EntityId playerId,
            BoardEntityState item,
            ICollection<GameEvent> events)
        {
            if (!boardState.TryRemove(item.Id))
                throw new InvalidOperationException("Validated automatic food item could not be removed.");

            int reward = item.Definition.AutomaticFoodReward;
            runState.RestoreFood(reward);
            events.Add(new ItemCollectedEvent(playerId, item.Id));
            events.Add(new FoodRestoredEvent(playerId, item.Id, reward));
        }

        private void ApplyCost(
            RunState runState,
            EntityId playerId,
            ICollection<GameEvent> events)
        {
            runState.ConsumeFood(ActionFoodCost);
            events.Add(new ActionCostAppliedEvent(playerId, ActionFoodCost));
        }
    }
}
