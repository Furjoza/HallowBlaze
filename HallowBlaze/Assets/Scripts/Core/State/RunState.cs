using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace HallowBlaze.Core.State
{
    public enum RunStatus
    {
        Active,
        Dead,
        Won
    }

    public sealed class RunState
    {
        public const int ToolSlotCount = 2;

        private const string BoardSeedDomain = "hallowblaze.board-layout.v1";
        private const uint FnvOffsetBasis = 2166136261;
        private const uint FnvPrime = 16777619;

        private readonly List<string> route = new List<string>();
        private readonly ReadOnlyCollection<string> routeView;
        private readonly ToolSlotState[] toolSlots = new ToolSlotState[ToolSlotCount];
        private readonly ReadOnlyCollection<ToolSlotState> toolSlotsView;

        public RunState(string runId, int runSeed, RunStateConfiguration configuration)
        {
            routeView = route.AsReadOnly();
            toolSlotsView = Array.AsReadOnly(toolSlots);
            Reset(runId, runSeed, configuration);
        }

        public string RunId { get; private set; }
        public int RunSeed { get; private set; }
        public int CurrentDay { get; private set; }
        public string WorldNodeId { get; private set; }
        public int Health { get; private set; }
        public int Food { get; private set; }
        public RunStatus Status { get; private set; }
        public IReadOnlyList<ToolSlotState> ToolSlots => toolSlotsView;
        public IReadOnlyList<string> Route => routeView;

        public void Reset(string runId, int runSeed, RunStateConfiguration configuration)
        {
            ValidateStableId(runId, nameof(runId));
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            RunId = runId;
            RunSeed = runSeed;
            CurrentDay = configuration.InitialDay;
            WorldNodeId = configuration.InitialWorldNodeId;
            Health = configuration.InitialHealth;
            Food = configuration.InitialFood;
            Status = RunStatus.Active;
            route.Clear();
            Array.Clear(toolSlots, 0, toolSlots.Length);
        }

        public void TakeDamage(int amount)
        {
            EnsureActive();
            ValidateNonNegative(amount, nameof(amount));
            Health = Math.Max(0, Health - amount);
        }

        public void RestoreHealth(int amount)
        {
            EnsureActive();
            ValidateNonNegative(amount, nameof(amount));
            Health = checked(Health + amount);
        }

        public void ConsumeFood(int amount)
        {
            EnsureActive();
            ValidateNonNegative(amount, nameof(amount));
            Food = Math.Max(0, Food - amount);
        }

        public void RestoreFood(int amount)
        {
            EnsureActive();
            ValidateNonNegative(amount, nameof(amount));
            Food = checked(Food + amount);
        }

        public void AdvanceDay()
        {
            EnsureActive();
            CurrentDay = checked(CurrentDay + 1);
        }

        public void SetCurrentWorldNode(string worldNodeId)
        {
            EnsureActive();
            ValidateStableId(worldNodeId, nameof(worldNodeId));
            WorldNodeId = worldNodeId;
        }

        public int GetBoardSeed()
        {
            uint hash = FnvOffsetBasis;
            AppendString(ref hash, BoardSeedDomain);
            AppendString(ref hash, RunId);
            AppendInt32(ref hash, RunSeed);
            AppendInt32(ref hash, CurrentDay);
            AppendString(ref hash, WorldNodeId);
            return unchecked((int)hash);
        }

        public void RecordRouteNode(string worldNodeId)
        {
            EnsureActive();
            ValidateStableId(worldNodeId, nameof(worldNodeId));
            route.Add(worldNodeId);
        }

        public void EquipTool(int slotIndex, ToolSlotState tool)
        {
            EnsureActive();
            ValidateSlotIndex(slotIndex);
            if (tool.IsEmpty)
                throw new ArgumentException("Use ClearTool to empty a slot.", nameof(tool));

            toolSlots[slotIndex] = tool;
        }

        public void ClearTool(int slotIndex)
        {
            EnsureActive();
            ValidateSlotIndex(slotIndex);
            toolSlots[slotIndex] = ToolSlotState.Empty;
        }

        public void MarkDead()
        {
            EnsureActive();
            Status = RunStatus.Dead;
        }

        public void MarkWon()
        {
            EnsureActive();
            Status = RunStatus.Won;
        }

        private void EnsureActive()
        {
            if (Status != RunStatus.Active)
                throw new InvalidOperationException("A completed run cannot be changed.");
        }

        private static void ValidateStableId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                char.IsWhiteSpace(value[0]) ||
                char.IsWhiteSpace(value[value.Length - 1]))
                throw new ArgumentException("Stable ID cannot be empty.", parameterName);
        }

        private static void ValidateNonNegative(int value, string parameterName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateSlotIndex(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= ToolSlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        private static void AppendString(ref uint hash, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            AppendInt32(ref hash, bytes.Length);
            for (int index = 0; index < bytes.Length; index++)
                AppendByte(ref hash, bytes[index]);
        }

        private static void AppendInt32(ref uint hash, int value)
        {
            AppendByte(ref hash, unchecked((byte)value));
            AppendByte(ref hash, unchecked((byte)(value >> 8)));
            AppendByte(ref hash, unchecked((byte)(value >> 16)));
            AppendByte(ref hash, unchecked((byte)(value >> 24)));
        }

        private static void AppendByte(ref uint hash, byte value)
        {
            hash = unchecked((hash ^ value) * FnvPrime);
        }
    }
}