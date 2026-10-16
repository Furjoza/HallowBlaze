using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Captures one explicit tool/target decision; the resolver rechecks every snapshot before committing.</summary>
    public sealed class UseToolCommand : PlayerCommand
    {
        /// <summary>Captures a preview without spending a charge, turn or resource.</summary>
        /// <param name="slotIndex">Generic equipment cell; invalid indices are rejected by the resolver.</param>
        /// <param name="instanceId">Run-local instance shown in that cell.</param>
        /// <param name="expectedCharges">Charges shown during preview; spending invalidates repeat submissions.</param>
        /// <param name="targetId">Exact board-local target instance.</param>
        /// <param name="targetPosition">Position shown during preview; displacement invalidates this decision.</param>
        /// <param name="actionId">Explicit registered behavior, not an automatically selected alternative.</param>
        public UseToolCommand(int slotIndex, string instanceId, int expectedCharges,
            EntityId targetId, GridPosition targetPosition, string actionId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || instanceId != instanceId.Trim())
                throw new ArgumentException("A tool instance ID is required.", nameof(instanceId));
            if (string.IsNullOrWhiteSpace(actionId) || actionId != actionId.Trim())
                throw new ArgumentException("A tool action ID is required.", nameof(actionId));
            if (expectedCharges < 0) throw new ArgumentOutOfRangeException(nameof(expectedCharges));
            SlotIndex = slotIndex;
            InstanceId = instanceId;
            ExpectedCharges = expectedCharges;
            TargetId = targetId;
            TargetPosition = targetPosition;
            ActionId = actionId;
        }

        /// <summary>Gets the generic equipment cell selected by the player.</summary>
        public int SlotIndex { get; }
        /// <summary>Gets the expected equipped instance, never a definition or atlas source ID.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the optimistic charge precondition that prevents replay of an accepted decision.</summary>
        public int ExpectedCharges { get; }
        /// <summary>Gets the exact selected board entity.</summary>
        public EntityId TargetId { get; }
        /// <summary>Gets the selected position whose displacement rejects the command.</summary>
        public GridPosition TargetPosition { get; }
        /// <summary>Gets the selected domain behavior.</summary>
        public string ActionId { get; }
        /// <inheritdoc />
        public override string CommandType => "UseTool";
    }
}