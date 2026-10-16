using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Immutable binding between a tool definition and one deterministic domain effect.</summary>
    public sealed class ToolAction
    {
        /// <summary>Binds explicit behavior without adding tool effects to the placeholder content.</summary>
        /// <param name="definitionId">Stable tool content ID, not an instance or source ID.</param>
        /// <param name="behavior">Trusted domain hook; Evaluate is pure and Apply must complete on validated state.</param>
        /// <param name="effectLabel">Concise consequence shown before confirmation.</param>
        /// <param name="noiseStrength">Nonnegative path-budget strength; zero means silent.</param>
        /// <param name="noiseLifetimeTurns">Positive lifetime of emitted noise.</param>
        /// <param name="observedNoiseFactId">Optional knowledge recorded after presenting this actual noise, never an unlock condition.</param>
        public ToolAction(string definitionId, IInteractionAction behavior, string effectLabel,
            int noiseStrength, int noiseLifetimeTurns, string observedNoiseFactId = null)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || definitionId != definitionId.Trim())
                throw new ArgumentException("A tool definition ID is required.", nameof(definitionId));
            Behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
            if (string.IsNullOrWhiteSpace(behavior.Id) || behavior.Id != behavior.Id.Trim())
                throw new ArgumentException("A stable tool behavior ID is required.", nameof(behavior));
            if (string.IsNullOrWhiteSpace(effectLabel)) throw new ArgumentException("An effect label is required.", nameof(effectLabel));
            if (noiseStrength < 0) throw new ArgumentOutOfRangeException(nameof(noiseStrength));
            if (noiseLifetimeTurns <= 0) throw new ArgumentOutOfRangeException(nameof(noiseLifetimeTurns));
            if (observedNoiseFactId != null && (noiseStrength == 0 || string.IsNullOrWhiteSpace(observedNoiseFactId) ||
                observedNoiseFactId != observedNoiseFactId.Trim()))
                throw new ArgumentException("A noise fact requires an audible consequence and a stable ID.", nameof(observedNoiseFactId));
            DefinitionId = definitionId;
            EffectLabel = effectLabel;
            NoiseStrength = noiseStrength;
            NoiseLifetimeTurns = noiseLifetimeTurns;
            ObservedNoiseFactId = observedNoiseFactId;
        }

        /// <summary>Gets the immutable content identity used for dispatch.</summary>
        public string DefinitionId { get; }
        /// <summary>Gets the selected deterministic effect; it never applies costs, charges or noise itself.</summary>
        public IInteractionAction Behavior { get; }
        /// <summary>Gets the direct consequence label supplied by the authored behavior.</summary>
        public string EffectLabel { get; }
        /// <summary>Gets emitted noise strength, or zero for a silent action.</summary>
        public int NoiseStrength { get; }
        /// <summary>Gets the positive lifetime of newly emitted noise.</summary>
        public int NoiseLifetimeTurns { get; }
        /// <summary>Gets optional knowledge discovered only when the emitted consequence is presented.</summary>
        public string ObservedNoiseFactId { get; }
    }

    /// <summary>Detached consequence preview retaining the exact tool, charges, target, behavior and current cost.</summary>
    public sealed class ToolUseOption
    {
        private readonly UseToolCommand command;

        internal ToolUseOption(UseToolCommand command, ToolAction action, int foodCost, CommandRejectionCode rejectionCode)
        {
            this.command = command;
            DefinitionId = action.DefinitionId;
            EffectLabel = action.EffectLabel;
            FoodCost = foodCost;
            NoiseStrength = action.NoiseStrength;
            NoiseLifetimeTurns = action.NoiseLifetimeTurns;
            RejectionCode = rejectionCode;
        }

        /// <summary>Gets the generic equipment cell shown during selection.</summary>
        public int SlotIndex => command.SlotIndex;
        /// <summary>Gets the equipped identity captured by the preview.</summary>
        public string InstanceId => command.InstanceId;
        /// <summary>Gets the tool definition used for dispatch.</summary>
        public string DefinitionId { get; }
        /// <summary>Gets the charge precondition; an accepted decision cannot be submitted twice.</summary>
        public int ExpectedCharges => command.ExpectedCharges;
        /// <summary>Gets the exact selected ground entity.</summary>
        public EntityId TargetId => command.TargetId;
        /// <summary>Gets the selected position whose displacement invalidates this decision.</summary>
        public GridPosition TargetPosition => command.TargetPosition;
        /// <summary>Gets the selected behavior, not a preferred alternative.</summary>
        public string ActionId => command.ActionId;
        /// <summary>Gets the direct effect shown before confirmation.</summary>
        public string EffectLabel { get; }
        /// <summary>Gets the active policy's accepted-action food cost.</summary>
        public int FoodCost { get; }
        /// <summary>Gets the accepted-action charge cost; preview and rejection spend none.</summary>
        public int ChargeCost => 1;
        /// <summary>Gets the noise path budget that will be emitted on acceptance.</summary>
        public int NoiseStrength { get; }
        /// <summary>Gets how many accepted subsequent commands the new noise survives.</summary>
        public int NoiseLifetimeTurns { get; }
        /// <summary>Gets the current reason the option cannot commit, or None when available.</summary>
        public CommandRejectionCode RejectionCode { get; }
        /// <summary>Gets whether the current snapshot is available; commit always revalidates.</summary>
        public bool IsAvailable => RejectionCode == CommandRejectionCode.None;
        /// <summary>Returns the complete immutable decision; no ownership transfer or gameplay mutation occurs.</summary>
        public UseToolCommand CreateCommand() => command;
    }

    /// <summary>Shared tool dispatch and pure validation; an empty catalog deliberately gives placeholders no effects.</summary>
    public sealed class ToolUseQuery
    {
        private readonly Dictionary<string, SortedDictionary<string, ToolAction>> actions =
            new Dictionary<string, SortedDictionary<string, ToolAction>>(StringComparer.Ordinal);

        /// <summary>Freezes the explicit behavior catalog and current shared action-cost policy for a board lifetime.</summary>
        /// <param name="actions">Definition-specific effects; null means none, including no axe/shovel effects.</param>
        /// <param name="actionCosts">Current cost used by previews and the resolver for all accepted board commands.</param>
        public ToolUseQuery(IEnumerable<ToolAction> actions = null, ActionCostPolicy actionCosts = null)
        {
            ActionCosts = actionCosts ?? new ActionCostPolicy();
            foreach (ToolAction action in actions ?? Array.Empty<ToolAction>())
            {
                if (action == null) throw new ArgumentException("Tool actions cannot be null.", nameof(actions));
                if (!this.actions.TryGetValue(action.DefinitionId, out var behaviors))
                {
                    behaviors = new SortedDictionary<string, ToolAction>(StringComparer.Ordinal);
                    this.actions.Add(action.DefinitionId, behaviors);
                }
                behaviors.Add(action.Behavior.Id, action);
            }
        }

        /// <summary>Gets the immutable cost policy shared with the central resolver, not a tool-only surcharge.</summary>
        public ActionCostPolicy ActionCosts { get; }

        /// <summary>Reads adjacent tool choices in slot/entity/action order, including depleted or otherwise disabled choices.</summary>
        /// <returns>A detached read-only snapshot; empty slots and unknown definitions produce no invented actions.</returns>
        public IReadOnlyList<ToolUseOption> GetOptions(BoardState board, RunState run, EntityId playerId)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            var options = new List<ToolUseOption>();
            if (!CanAct(board, run, playerId, out BoardEntityState player)) return options.AsReadOnly();
            for (int slot = 0; slot < RunState.ToolSlotCount; slot++)
            {
                ToolInstanceState instance = run.ToolSlots[slot].Instance;
                if (instance == null || !actions.TryGetValue(instance.DefinitionId, out var behaviors)) continue;
                foreach (BoardEntityState target in board.GetEntities())
                {
                    if (target.Id.Equals(playerId) || player.Position.ManhattanDistance(target.Position) > 1) continue;
                    foreach (ToolAction action in behaviors.Values)
                    {
                        if (!action.Behavior.Supports(target)) continue;
                        var command = new UseToolCommand(slot, instance.InstanceId, instance.Charges,
                            target.Id, target.Position, action.Behavior.Id);
                        options.Add(new ToolUseOption(command, action, ActionCosts.FoodCost,
                            Validate(board, run, playerId, command, out _, out _)));
                    }
                }
            }
            return options.AsReadOnly();
        }

        /// <summary>Rechecks the complete preview without applying a tool effect, cost, noise or charge spend.</summary>
        public CommandRejectionCode GetRejection(BoardState board, RunState run, EntityId playerId, UseToolCommand command)
            => Validate(board, run, playerId, command, out _, out _);

        internal CommandRejectionCode Validate(BoardState board, RunState run, EntityId playerId, UseToolCommand command,
            out BoardEntityState target, out ToolAction action)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            target = null;
            action = null;
            if (!CanAct(board, run, playerId, out BoardEntityState player)) return CommandRejectionCode.InvalidState;
            if (command == null) return CommandRejectionCode.InvalidCommand;
            if (command.SlotIndex < 0 || command.SlotIndex >= RunState.ToolSlotCount) return CommandRejectionCode.InvalidToolSlot;
            ToolInstanceState instance = run.ToolSlots[command.SlotIndex].Instance;
            if (instance == null) return CommandRejectionCode.EmptyToolSlot;
            if (instance.InstanceId != command.InstanceId || instance.Charges != command.ExpectedCharges)
                return CommandRejectionCode.StaleTool;
            if (instance.Charges == 0) return CommandRejectionCode.ToolDepleted;
            if (!board.TryGetEntity(command.TargetId, out target) || target.Id.Equals(playerId) ||
                !target.Position.Equals(command.TargetPosition) || player.Position.ManhattanDistance(target.Position) > 1)
                return CommandRejectionCode.InvalidTarget;
            if (!actions.TryGetValue(instance.DefinitionId, out var behaviors) ||
                !behaviors.TryGetValue(command.ActionId, out action) || !action.Behavior.Supports(target))
                return CommandRejectionCode.UnsupportedTool;
            switch (action.Behavior.Evaluate(board, run, player, target))
            {
                case InteractionAvailability.Available: return CommandRejectionCode.None;
                case InteractionAvailability.InvalidState: return CommandRejectionCode.InvalidState;
                case InteractionAvailability.InvalidTarget:
                case InteractionAvailability.OutOfRange: return CommandRejectionCode.InvalidTarget;
                case InteractionAvailability.Blocked: return CommandRejectionCode.Blocked;
                default: return CommandRejectionCode.NoInteractionAvailable;
            }
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