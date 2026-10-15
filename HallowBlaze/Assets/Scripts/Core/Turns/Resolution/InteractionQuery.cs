using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Explains availability without consuming a turn or changing an entity.</summary>
    public enum InteractionAvailability
    {
        /// <summary>The exact action can currently commit.</summary>
        Available,
        /// <summary>The run or player cannot act.</summary>
        InvalidState,
        /// <summary>The selected entity is missing or has moved since preview.</summary>
        InvalidTarget,
        /// <summary>The target is beyond the cardinal interaction range.</summary>
        OutOfRange,
        /// <summary>The action is unknown or does not apply to this entity.</summary>
        Unsupported,
        /// <summary>A required destination is occupied or impassable.</summary>
        Blocked,
        /// <summary>A future composite decision is required; this option cannot commit by itself.</summary>
        DecisionRequired
    }

    /// <summary>Immutable preview of one action on the exact ground entity; it never owns or removes that entity.</summary>
    public sealed class InteractionOption
    {
        internal InteractionOption(BoardEntityState target, string actionId, InteractionAvailability availability)
        {
            TargetId = target.Id;
            TargetPosition = target.Position;
            ActionId = actionId;
            Availability = availability;
        }

        /// <summary>Gets the exact board-local instance, not its content definition.</summary>
        public EntityId TargetId { get; }
        /// <summary>Gets the position whose displacement invalidates this selection.</summary>
        public GridPosition TargetPosition { get; }
        /// <summary>Gets the stable action identifier used by the resolver.</summary>
        public string ActionId { get; }
        /// <summary>Gets the reason this snapshot is available or disabled.</summary>
        public InteractionAvailability Availability { get; }
        /// <summary>Gets whether this snapshot may be submitted without a further decision.</summary>
        public bool IsAvailable => Availability == InteractionAvailability.Available;

        /// <summary>Builds a complete selection; the resolver must still validate current state.</summary>
        /// <returns>A command retaining the exact target ID, position and action.</returns>
        public InteractCommand CreateCommand() => new InteractCommand(TargetId, TargetPosition, ActionId);
    }

    /// <summary>Domain extension for one explicitly selected action; implementations must be deterministic.</summary>
    public interface IInteractionAction
    {
        /// <summary>Gets a unique nonempty stable action identifier.</summary>
        string Id { get; }
        /// <summary>Tests immutable applicability without changing state.</summary>
        /// <param name="target">The current ground entity.</param>
        /// <returns>Whether this action belongs in the target's option set.</returns>
        bool Supports(BoardEntityState target);
        /// <summary>Checks all effect preconditions without mutating board, run, or target.</summary>
        /// <returns>Available only when Apply can complete atomically on this state.</returns>
        InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target);
        /// <summary>
        /// Applies an already validated effect and appends its events. Called synchronously only after
        /// validation; never called by preview. Does not apply turn costs, noise or enemy phases.
        /// </summary>
        void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events);
    }

    /// <summary>Reads adjacent options in entity-ID/action-ID order without mutating authoritative state.</summary>
    public sealed class InteractionQuery
    {
        private readonly SortedDictionary<string, IInteractionAction> actions =
            new SortedDictionary<string, IInteractionAction>(StringComparer.Ordinal);

        /// <summary>Creates a fixed action catalog shared with the resolver for the board lifetime.</summary>
        /// <param name="actions">Explicit domain hooks, or null for contextual interaction and collect.</param>
        /// <exception cref="ArgumentException">An action is null, has an invalid ID, or duplicates an ID.</exception>
        public InteractionQuery(IEnumerable<IInteractionAction> actions = null)
        {
            foreach (IInteractionAction action in actions ?? new IInteractionAction[]
                { new ContextInteractionAction(), new CollectInteractionAction() })
            {
                if (action == null || string.IsNullOrWhiteSpace(action.Id) || action.Id != action.Id.Trim())
                    throw new ArgumentException("Actions require stable identifiers.", nameof(actions));
                this.actions.Add(action.Id, action);
            }
        }

        /// <summary>Reads all applicable adjacent actions, including disabled options with explicit reasons.</summary>
        /// <param name="board">The authoritative board, read only during this call.</param>
        /// <param name="run">The authoritative run, read only during this call.</param>
        /// <param name="playerId">The active board player.</param>
        /// <returns>A detached read-only snapshot; invalid player/run state produces an empty set.</returns>
        public IReadOnlyList<InteractionOption> GetOptions(BoardState board, RunState run, EntityId playerId)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            var options = new List<InteractionOption>();
            if (!CanAct(board, run, playerId, out BoardEntityState player))
                return options.AsReadOnly();
            foreach (BoardEntityState target in board.GetEntities())
            {
                if (target.Id.Equals(playerId) || player.Position.ManhattanDistance(target.Position) > 1)
                    continue;
                foreach (IInteractionAction action in actions.Values)
                    if (action.Supports(target))
                        options.Add(new InteractionOption(target, action.Id, action.Evaluate(board, run, player, target)));
            }
            return options.AsReadOnly();
        }

        /// <summary>Rechecks an explicit selection; an old preview never grants permission to execute.</summary>
        /// <returns>The current availability without effects, costs, or replacement of the selected entity.</returns>
        public InteractionAvailability GetAvailability(BoardState board, RunState run, EntityId playerId, InteractCommand command)
        {
            return Validate(board, run, playerId, command, out _, out _);
        }

        internal InteractionAvailability Validate(BoardState board, RunState run, EntityId playerId, InteractCommand command,
            out BoardEntityState target, out IInteractionAction action)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            target = null;
            action = null;
            if (!CanAct(board, run, playerId, out BoardEntityState player))
                return InteractionAvailability.InvalidState;
            if (command == null || !board.TryGetEntity(command.TargetId, out target) || target.Id.Equals(playerId) ||
                command.TargetPosition.HasValue && !command.TargetPosition.Value.Equals(target.Position))
                return InteractionAvailability.InvalidTarget;
            if (player.Position.ManhattanDistance(target.Position) > 1)
                return InteractionAvailability.OutOfRange;
            if (!actions.TryGetValue(command.ActionId, out action) || !action.Supports(target))
                return InteractionAvailability.Unsupported;
            return action.Evaluate(board, run, player, target);
        }

        private static bool CanAct(BoardState board, RunState run, EntityId playerId, out BoardEntityState player)
        {
            player = null;
            return run.Status == RunStatus.Active && run.Health > 0 && run.Food > 0 &&
                board.TryGetEntity(playerId, out player) && player.Definition.Layer == BoardLayer.Actor &&
                player.Definition.Kind.Equals(EntityKind.Player);
        }
    }
}