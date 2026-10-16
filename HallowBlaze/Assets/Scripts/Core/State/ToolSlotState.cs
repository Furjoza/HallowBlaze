using System;

namespace HallowBlaze.Core.State
{
    /// <summary>One generic equipment cell; default is empty and is separate from any future backpack.</summary>
    public readonly struct ToolSlotState
    {
        private readonly string toolId;
        private readonly int remainingUses;

        /// <summary>Builds a legacy definition/charges value; RunState assigns identity when equipping it.</summary>
        public ToolSlotState(string toolId, int remainingUses)
        {
            if (string.IsNullOrWhiteSpace(toolId) ||
                char.IsWhiteSpace(toolId[0]) ||
                char.IsWhiteSpace(toolId[toolId.Length - 1]))
                throw new ArgumentException("Tool ID cannot be empty.", nameof(toolId));
            if (remainingUses < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingUses));

            this.toolId = toolId;
            this.remainingUses = remainingUses;
            Instance = null;
        }

        /// <summary>Wraps an immutable instance without copying mutable state or taking ownership of a definition.</summary>
        public ToolSlotState(ToolInstanceState instance)
        {
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            toolId = instance.DefinitionId;
            remainingUses = instance.Charges;
        }

        /// <summary>Gets the empty generic cell.</summary>
        public static ToolSlotState Empty => default;

        /// <summary>Gets the definition ID compatibility projection, or empty text for an empty slot.</summary>
        public string ToolId => toolId ?? string.Empty;
        /// <summary>Gets remaining charges, including zero on an occupied depleted slot.</summary>
        public int RemainingUses => remainingUses;
        /// <summary>Gets the immutable identity-bearing instance, or null for an empty or unassigned legacy value.</summary>
        public ToolInstanceState Instance { get; }
        /// <summary>Gets whether this slot owns no tool; depletion does not empty a slot.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(toolId);
    }
}