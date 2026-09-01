using System;

namespace HallowBlaze.Core.State
{
    public sealed class RunStateConfiguration
    {
        public RunStateConfiguration(
            int initialHealth,
            int initialFood,
            int initialDay,
            string initialWorldNodeId)
        {
            if (initialHealth < 0)
                throw new ArgumentOutOfRangeException(nameof(initialHealth));
            if (initialFood < 0)
                throw new ArgumentOutOfRangeException(nameof(initialFood));
            if (initialDay < 0)
                throw new ArgumentOutOfRangeException(nameof(initialDay));
            if (string.IsNullOrWhiteSpace(initialWorldNodeId) ||
                char.IsWhiteSpace(initialWorldNodeId[0]) ||
                char.IsWhiteSpace(initialWorldNodeId[initialWorldNodeId.Length - 1]))
                throw new ArgumentException("World node ID cannot be empty.", nameof(initialWorldNodeId));

            InitialHealth = initialHealth;
            InitialFood = initialFood;
            InitialDay = initialDay;
            InitialWorldNodeId = initialWorldNodeId;
        }

        public int InitialHealth { get; }
        public int InitialFood { get; }
        public int InitialDay { get; }
        public string InitialWorldNodeId { get; }
    }
}