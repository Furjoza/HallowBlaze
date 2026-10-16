using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Records the accepted exact tool/target decision before its direct effect events.</summary>
    public sealed class ToolUsedEvent : GameEvent
    {
        /// <summary>Copies the validated decision and content identity without storing mutable state.</summary>
        public ToolUsedEvent(EntityId actorId, UseToolCommand command, string definitionId)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (string.IsNullOrWhiteSpace(definitionId) || definitionId != definitionId.Trim())
                throw new ArgumentException("A tool definition ID is required.", nameof(definitionId));
            ActorId = actorId;
            SlotIndex = command.SlotIndex;
            InstanceId = command.InstanceId;
            DefinitionId = definitionId;
            TargetId = command.TargetId;
            ActionId = command.ActionId;
        }
        /// <summary>Gets the player performing the action.</summary>
        public EntityId ActorId { get; }
        /// <summary>Gets the generic equipment cell used.</summary>
        public int SlotIndex { get; }
        /// <summary>Gets the exact run-local tool instance.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the immutable content used for behavior dispatch.</summary>
        public string DefinitionId { get; }
        /// <summary>Gets the exact board target.</summary>
        public EntityId TargetId { get; }
        /// <summary>Gets the behavior explicitly chosen.</summary>
        public string ActionId { get; }
        /// <inheritdoc />
        public override string EventType => "ToolUsed";
    }

    /// <summary>Records one accepted charge spend; zero remains an occupied tool slot.</summary>
    public sealed class ToolChargeSpentEvent : GameEvent
    {
        /// <summary>Copies the validated charge transition without giving presentation any mutation authority.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The decision has no charge to spend.</exception>
        public ToolChargeSpentEvent(EntityId actorId, UseToolCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.ExpectedCharges <= 0) throw new ArgumentOutOfRangeException(nameof(command));
            ActorId = actorId;
            SlotIndex = command.SlotIndex;
            InstanceId = command.InstanceId;
            PreviousCharges = command.ExpectedCharges;
            RemainingCharges = command.ExpectedCharges - 1;
        }
        /// <summary>Gets the player whose equipment changed.</summary>
        public EntityId ActorId { get; }
        /// <summary>Gets the generic equipment cell.</summary>
        public int SlotIndex { get; }
        /// <summary>Gets the identity preserved by the transition.</summary>
        public string InstanceId { get; }
        /// <summary>Gets charges immediately before this accepted use.</summary>
        public int PreviousCharges { get; }
        /// <summary>Gets charges immediately after this accepted use.</summary>
        public int RemainingCharges { get; }
        /// <inheritdoc />
        public override string EventType => "ToolChargeSpent";
    }
}