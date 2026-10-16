using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Detached pickup draft; choosing a free slot or replacement never changes ground ownership or equipment.</summary>
    public sealed class ToolPickupOption
    {
        internal ToolPickupOption(GroundToolState tool, GridPosition position, IReadOnlyList<PickupToolCommand> choices)
        {
            Tool = tool;
            Position = position;
            Choices = choices;
        }
        /// <summary>Gets the concrete ground instance displayed in the comparison.</summary>
        public GroundToolState Tool { get; }
        /// <summary>Gets the exact target position used by every choice.</summary>
        public GridPosition Position { get; }
        /// <summary>Gets the first-free choice or both explicit occupied-slot choices; empty means an invalid instance.</summary>
        public IReadOnlyList<PickupToolCommand> Choices { get; }
        /// <summary>Gets whether confirmation must include a conscious occupied-slot comparison.</summary>
        public bool RequiresReplacement => Choices.Count > 0 && Choices[0].ExpectedSlotInstanceId != null;
    }

    /// <summary>Shared pure equipment pickup preview and validation; food/consumables never enter this decision flow.</summary>
    public sealed class ToolPickupQuery
    {
        /// <summary>Reads adjacent concrete tools in board entity order; no IDs, charges, resources or profile data change.</summary>
        public IReadOnlyList<ToolPickupOption> GetOptions(BoardState board, RunState run, EntityId playerId)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            var result = new List<ToolPickupOption>();
            if (!CanAct(board, run, playerId, out BoardEntityState player)) return result.AsReadOnly();
            foreach (BoardEntityState target in board.GetEntities())
            {
                if (player.Position.ManhattanDistance(target.Position) > 1 || !board.TryGetGroundTool(target.Id, out GroundToolState tool)) continue;
                int free = FirstFreeSlot(run);
                var choices = new List<PickupToolCommand>();
                for (int slot = 0; slot < RunState.ToolSlotCount; slot++)
                {
                    if (free >= 0 && slot != free) continue;
                    ToolInstanceState old = run.ToolSlots[slot].Instance;
                    var command = new PickupToolCommand(tool, target.Position, slot, old?.InstanceId, old?.DefinitionId, old?.Charges ?? 0);
                    if (Validate(board, run, playerId, command, out _) == CommandRejectionCode.None) choices.Add(command);
                }
                result.Add(new ToolPickupOption(tool, target.Position, choices.AsReadOnly()));
            }
            return result.AsReadOnly();
        }

        /// <summary>Revalidates the complete immutable decision without allocating, transferring or resolving a turn.</summary>
        public CommandRejectionCode GetRejection(BoardState board, RunState run, EntityId playerId, PickupToolCommand command)
            => Validate(board, run, playerId, command, out _);

        internal CommandRejectionCode Validate(BoardState board, RunState run, EntityId playerId, PickupToolCommand command,
            out GroundToolState ground)
        {
            ground = null;
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (!CanAct(board, run, playerId, out BoardEntityState player)) return CommandRejectionCode.InvalidState;
            if (command == null) return CommandRejectionCode.InvalidCommand;
            if (!board.TryGetEntity(command.Tool.EntityId, out BoardEntityState target) ||
                !target.Position.Equals(command.TargetPosition) || player.Position.ManhattanDistance(target.Position) > 1 ||
                !board.TryGetGroundTool(target.Id, out ground)) return CommandRejectionCode.InvalidTarget;
            GroundToolState expected = command.Tool;
            if (!ReferenceEquals(ground, expected) || ground.InstanceId != expected.InstanceId || ground.DefinitionId != expected.DefinitionId ||
                ground.Charges != expected.Charges || ground.SourceId != expected.SourceId) return CommandRejectionCode.StaleTool;
            if (!board.TryGetEntity(BoardLayer.Terrain, target.Position, out BoardEntityState terrain) ||
                !terrain.Definition.Traits.IsWalkable || board.TryGetEntity(BoardLayer.Obstacle, target.Position, out BoardEntityState obstacle) &&
                !obstacle.Definition.Traits.IsWalkable || board.TryGetEntity(BoardLayer.Actor, target.Position, out BoardEntityState actor) &&
                !actor.Id.Equals(playerId)) return CommandRejectionCode.Blocked;
            int free = FirstFreeSlot(run);
            if (command.SlotIndex < 0 || command.SlotIndex >= RunState.ToolSlotCount)
                return free < 0 ? CommandRejectionCode.ToolSlotsFull : CommandRejectionCode.InvalidToolSlot;
            ToolInstanceState old = run.ToolSlots[command.SlotIndex].Instance;
            if (old?.InstanceId != command.ExpectedSlotInstanceId || old?.DefinitionId != command.ExpectedSlotDefinitionId ||
                (old?.Charges ?? 0) != command.ExpectedSlotCharges) return CommandRejectionCode.StaleTool;
            if (free >= 0 && (command.SlotIndex != free || old != null)) return CommandRejectionCode.StaleTool;
            var incoming = new ToolInstanceState(ground.InstanceId, ground.DefinitionId, ground.Charges);
            if (!run.CanReceiveTool(command.SlotIndex, incoming)) return CommandRejectionCode.InvalidState;
            if (old != null)
                foreach (BoardEntityState entity in board.GetEntities())
                    if (board.TryGetGroundTool(entity.Id, out GroundToolState other) && other.InstanceId == old.InstanceId)
                        return CommandRejectionCode.InvalidState;
            return CommandRejectionCode.None;
        }

        private static int FirstFreeSlot(RunState run)
        {
            for (int slot = 0; slot < RunState.ToolSlotCount; slot++) if (run.ToolSlots[slot].IsEmpty) return slot;
            return -1;
        }

        private static bool CanAct(BoardState board, RunState run, EntityId playerId, out BoardEntityState player)
        {
            player = null;
            return run.Status == RunStatus.Active && run.Health > 0 && run.Food > 0 &&
                board.TryGetEntity(playerId, out player) && player.Definition.Layer == BoardLayer.Actor && player.Definition.Kind.Equals(EntityKind.Player);
        }
    }
}