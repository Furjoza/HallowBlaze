using System;

namespace HallowBlaze.Core.State
{
    public readonly struct ToolSlotState
    {
        private readonly string toolId;

        public ToolSlotState(string toolId, int remainingUses)
        {
            if (string.IsNullOrWhiteSpace(toolId) ||
                char.IsWhiteSpace(toolId[0]) ||
                char.IsWhiteSpace(toolId[toolId.Length - 1]))
                throw new ArgumentException("Tool ID cannot be empty.", nameof(toolId));
            if (remainingUses < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingUses));

            this.toolId = toolId;
            RemainingUses = remainingUses;
        }

        public static ToolSlotState Empty => default;

        public string ToolId => toolId ?? string.Empty;
        public int RemainingUses { get; }
        public bool IsEmpty => string.IsNullOrEmpty(toolId);
    }
}