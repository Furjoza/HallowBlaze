using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>One consciously confirmed ground-to-equipment transfer, including the exact destination replacement decision.</summary>
    public sealed class PickupToolCommand : PlayerCommand
    {
        /// <summary>Captures immutable preview preconditions; constructing a decision never allocates, transfers or spends a turn.</summary>
        /// <param name="tool">Concrete ground entity/instance/definition/charge/source snapshot.</param>
        /// <param name="targetPosition">The exact ground position shown before confirmation.</param>
        /// <param name="slotIndex">First free slot, or the explicitly chosen occupied slot when full.</param>
        /// <param name="expectedSlotInstanceId">Null only for an empty destination.</param>
        /// <param name="expectedSlotDefinitionId">Definition compared by the replacement modal, or null for empty.</param>
        /// <param name="expectedSlotCharges">Charges compared by the replacement modal; zero for empty.</param>
        public PickupToolCommand(GroundToolState tool, GridPosition targetPosition, int slotIndex,
            string expectedSlotInstanceId = null, string expectedSlotDefinitionId = null, int expectedSlotCharges = 0)
        {
            Tool = tool ?? throw new ArgumentNullException(nameof(tool));
            if (expectedSlotCharges < 0) throw new ArgumentOutOfRangeException(nameof(expectedSlotCharges));
            TargetPosition = targetPosition;
            SlotIndex = slotIndex;
            ExpectedSlotInstanceId = expectedSlotInstanceId;
            ExpectedSlotDefinitionId = expectedSlotDefinitionId;
            ExpectedSlotCharges = expectedSlotCharges;
        }
        /// <summary>Gets the exact ground snapshot whose ownership must still match.</summary>
        public GroundToolState Tool { get; }
        /// <summary>Gets the selected cell; movement of the target invalidates the decision.</summary>
        public GridPosition TargetPosition { get; }
        /// <summary>Gets the single generic equipment cell affected by acceptance.</summary>
        public int SlotIndex { get; }
        /// <summary>Gets the expected destination instance; null means the destination was empty.</summary>
        public string ExpectedSlotInstanceId { get; }
        /// <summary>Gets the expected destination content, never a source ID.</summary>
        public string ExpectedSlotDefinitionId { get; }
        /// <summary>Gets the destination charge precondition; changed charges require a new decision.</summary>
        public int ExpectedSlotCharges { get; }
        /// <inheritdoc />
        public override string CommandType => "PickupTool";
    }

    /// <summary>Records the exact accepted transfer and any displaced tool left at the original ground binding.</summary>
    public sealed class ToolPickedUpEvent : GameEvent
    {
        /// <summary>Captures already-committed ownership; presentation must not perform another transfer.</summary>
        public ToolPickedUpEvent(EntityId playerId, PickupToolCommand command, GroundToolState displacedTool)
        {
            PlayerId = playerId;
            Command = command ?? throw new ArgumentNullException(nameof(command));
            DisplacedTool = displacedTool;
        }
        /// <summary>Gets the acting player.</summary>
        public EntityId PlayerId { get; }
        /// <summary>Gets the complete confirmed decision, including original source identity.</summary>
        public PickupToolCommand Command { get; }
        /// <summary>Gets the preserved old tool now on the ground, or null for an initially empty slot.</summary>
        public GroundToolState DisplacedTool { get; }
        /// <inheritdoc />
        public override string EventType => "ToolPickedUp";
    }
}