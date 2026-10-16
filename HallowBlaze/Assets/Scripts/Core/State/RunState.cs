using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using HallowBlaze.Core.Random;

namespace HallowBlaze.Core.State
{
    public enum RunStatus
    {
        Active,
        Dead,
        Won
    }

    /// <summary>Owns one run's resources, travel and persisted RNG identity, independently of profile knowledge.</summary>
    public sealed class RunState
    {
        /// <summary>Exactly two generic equipment slots, independent of backpack or pocket capacity.</summary>
        public const int ToolSlotCount = 2;

        /// <summary>Identifies the seeded System.Random layout used by schema v1/v2 saves.</summary>
        public const string LegacyRandomAlgorithmId = "hallowblaze.legacy-system-random";
        /// <summary>Gets the legacy adapter version; migration never silently upgrades an existing layout.</summary>
        public const int LegacyRandomAlgorithmVersion = 1;

        private const string BoardSeedDomain = "hallowblaze.board-layout.v1";
        private const uint FnvOffsetBasis = 2166136261;
        private const uint FnvPrime = 16777619;

        private readonly List<TravelHistoryEntry> travelHistory = new List<TravelHistoryEntry>();
        private readonly ReadOnlyCollection<TravelHistoryEntry> historyView;
        private readonly ToolSlotState[] toolSlots = new ToolSlotState[ToolSlotCount];
        private readonly ReadOnlyCollection<ToolSlotState> toolSlotsView;

        /// <summary>Creates a run; new runs use PCG32 while old saves may explicitly retain legacy generation.</summary>
        /// <param name="runId">The stable run identity.</param>
        /// <param name="runSeed">The persisted root seed.</param>
        /// <param name="configuration">Initial resources and reached checkpoint.</param>
        /// <param name="randomAlgorithmId">The supported layout RNG identifier, never inferred from a board seed.</param>
        /// <param name="randomAlgorithmVersion">The supported generator/derivation version.</param>
        /// <exception cref="ArgumentException">The algorithm identity or run ID is unsupported.</exception>
        public RunState(string runId, int runSeed, RunStateConfiguration configuration,
            string randomAlgorithmId = RandomStreamProvider.AlgorithmId,
            int randomAlgorithmVersion = RandomStreamProvider.AlgorithmVersion)
        {
            if (!RandomStreamProvider.IsSupported(randomAlgorithmId, randomAlgorithmVersion) &&
                !(randomAlgorithmId == LegacyRandomAlgorithmId && randomAlgorithmVersion == LegacyRandomAlgorithmVersion))
                throw new ArgumentException("Unsupported run random algorithm identity.", nameof(randomAlgorithmId));
            historyView = travelHistory.AsReadOnly();
            toolSlotsView = Array.AsReadOnly(toolSlots);
            Reset(runId, runSeed, configuration);
            RandomAlgorithmId = randomAlgorithmId;
            RandomAlgorithmVersion = randomAlgorithmVersion;
        }

        public string RunId { get; private set; }
        public int RunSeed { get; private set; }
        /// <summary>Gets the persisted layout algorithm; legacy saves retain their original adapter.</summary>
        public string RandomAlgorithmId { get; private set; }
        /// <summary>Gets the persisted derivation and generator version needed to recreate a board boundary.</summary>
        public int RandomAlgorithmVersion { get; private set; }
        /// <summary>Gets the legacy checkpoint day, or the active leg's one-based displayed day.</summary>
        public int CurrentDay => ActiveLeg?.DayNumber ?? CompletedDayCount;
        /// <summary>Gets completed days; intermediate segments never increment it.</summary>
        public int CompletedDayCount { get; private set; }
        /// <summary>Gets the last reached checkpoint, not an active destination.</summary>
        public string WorldNodeId { get; private set; }
        /// <summary>Gets the explicit reached-checkpoint identity.</summary>
        public string ReachedCheckpointNodeId => WorldNodeId;
        /// <summary>Gets the optional immutable active travel snapshot.</summary>
        public ActiveRouteLegState ActiveLeg { get; private set; }
        /// <summary>Gets the durable monotonic traversal allocator.</summary>
        public int TraversalSequence { get; private set; }
        /// <summary>Gets the sole ordered travel-history authority.</summary>
        public IReadOnlyList<TravelHistoryEntry> TravelHistory => historyView;
        public int Health { get; private set; }
        public int Food { get; private set; }
        public RunStatus Status { get; private set; }
        /// <summary>Gets read-only generic equipment; instances are immutable and charges are owned by this run.</summary>
        public IReadOnlyList<ToolSlotState> ToolSlots => toolSlotsView;
        /// <summary>Gets the durable instance allocator; clearing or replacing tools never reuses an ID.</summary>
        public int ToolInstanceSequence { get; private set; }
        /// <summary>Gets a detached node-only compatibility projection of structured history.</summary>
        public IReadOnlyList<string> Route => travelHistory.ConvertAll(entry => entry.ToNodeId).AsReadOnly();

        /// <summary>Starts a fresh run with current RNG, resources, tools and travel without touching profile knowledge.</summary>
        public void Reset(string runId, int runSeed, RunStateConfiguration configuration)
        {
            ValidateStableId(runId, nameof(runId));
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            RunId = runId;
            RunSeed = runSeed;
            RandomAlgorithmId = RandomStreamProvider.AlgorithmId;
            RandomAlgorithmVersion = RandomStreamProvider.AlgorithmVersion;
            CompletedDayCount = configuration.InitialDay;
            ActiveLeg = null;
            TraversalSequence = 0;
            ToolInstanceSequence = 0;
            WorldNodeId = configuration.InitialWorldNodeId;
            Health = configuration.InitialHealth;
            Food = configuration.InitialFood;
            Status = RunStatus.Active;
            travelHistory.Clear();
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

        /// <summary>Advances a transitional checkpoint-only day, never an active leg.</summary>
        public void AdvanceDay()
        {
            EnsureCheckpoint();
            CompletedDayCount = checked(CompletedDayCount + 1);
        }

        /// <summary>Sets a transitional reached checkpoint; addressed travel cannot use this adapter.</summary>
        public void SetCurrentWorldNode(string worldNodeId)
        {
            EnsureCheckpoint();
            ValidateStableId(worldNodeId, nameof(worldNodeId));
            WorldNodeId = worldNodeId;
        }

        /// <summary>
        /// Atomically advances the run to the next world node, increasing the current day by one and recording the node on the route.
        /// </summary>
        /// <param name="worldNodeId">The stable ID of the world node to advance to.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="worldNodeId"/> is not a valid stable ID.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the run is not active.</exception>
        /// <exception cref="OverflowException">Thrown when the current day cannot be increased.</exception>
        public void AdvanceToWorldNode(string worldNodeId)
        {
            EnsureCheckpoint();
            ValidateStableId(worldNodeId, nameof(worldNodeId));
            int nextDay = checked(CurrentDay + 1);

            CompletedDayCount = nextDay;
            WorldNodeId = worldNodeId;
            travelHistory.Add(TravelHistoryEntry.LegacyCheckpoint(worldNodeId));
        }

        /// <summary>Gets the unchanged v1 checkpoint seed; active legs require the Session address factory.</summary>
        public int GetBoardSeed()
        {
            if (ActiveLeg != null)
                throw new InvalidOperationException("An active leg requires a segment board address.");
            uint hash = FnvOffsetBasis;
            AppendString(ref hash, BoardSeedDomain);
            AppendString(ref hash, RunId);
            AppendInt32(ref hash, RunSeed);
            AppendInt32(ref hash, CurrentDay);
            AppendString(ref hash, WorldNodeId);
            return unchecked((int)hash);
        }

        /// <summary>Imports node-only legacy evidence without inventing a road, day or traversal.</summary>
        public void RecordRouteNode(string worldNodeId)
        {
            EnsureCheckpoint();
            ValidateStableId(worldNodeId, nameof(worldNodeId));
            travelHistory.Add(TravelHistoryEntry.LegacyCheckpoint(worldNodeId));
        }

        /// <summary>Starts one leg without arrival, completed-day advancement or history effects.</summary>
        /// <returns>The Pending snapshot; Session must checkpoint it before travel.</returns>
        /// <exception cref="InvalidOperationException">Run is terminal, busy, or source is not reached.</exception>
        /// <exception cref="OverflowException">Day or traversal allocator is exhausted.</exception>
        public ActiveRouteLegState StartLeg(string edgeId, string fromNodeId, string toNodeId,
            IEnumerable<RouteSegmentState> segments)
        {
            EnsureCheckpoint();
            if (fromNodeId != WorldNodeId)
                throw new InvalidOperationException("The leg must start at the reached checkpoint.");
            int sequence = checked(TraversalSequence + 1);
            ActiveRouteLegState leg = new ActiveRouteLegState(LegIdFor(sequence), edgeId, fromNodeId,
                toNodeId, segments, checked(CompletedDayCount + 1));
            TraversalSequence = sequence;
            ActiveLeg = leg;
            return leg;
        }

        /// <summary>Consumes a matching intro once; Session must save Consumed before rendering.</summary>
        /// <returns>True only for Pending -> Consumed; stale, repeated or terminal input is effect-free.</returns>
        public bool TryConsumeIntro(string legId)
        {
            if (!MatchesLeg(legId) || ActiveLeg.IntroState == DayIntroState.Consumed)
                return false;
            ActiveLeg = ActiveLeg.WithProgress(ActiveLeg.SegmentIndex, DayIntroState.Consumed);
            return true;
        }

        /// <summary>Completes one non-final segment without changing checkpoint, day, history or resources.</summary>
        /// <returns>False without effects for stale, repeated, final, Pending or terminal input.</returns>
        public bool TryAdvanceSegment(string legId, string segmentId)
        {
            if (!MatchesSegment(legId, segmentId) || ActiveLeg.IsFinalSegment)
                return false;
            ActiveLeg = ActiveLeg.WithProgress(ActiveLeg.SegmentIndex + 1, DayIntroState.Consumed);
            return true;
        }

        /// <summary>Records final arrival once; Session still owns discovery and persistence.</summary>
        /// <returns>False unless the matching consumed final segment belongs to an active run.</returns>
        public bool TryCompleteLeg(string legId, string segmentId)
        {
            if (!MatchesSegment(legId, segmentId) || !ActiveLeg.IsFinalSegment)
                return false;
            TravelHistoryEntry entry = TravelHistoryEntry.Completed(ActiveLeg.LegId, ActiveLeg.EdgeId,
                ActiveLeg.FromNodeId, ActiveLeg.ToNodeId, ActiveLeg.DayNumber, ActiveLeg.Segments.Count);
            travelHistory.Add(entry);
            WorldNodeId = ActiveLeg.ToNodeId;
            CompletedDayCount = ActiveLeg.DayNumber;
            ActiveLeg = null;
            return true;
        }

        /// <summary>Restores a detached stable boundary after validating all cross-field invariants.</summary>
        /// <exception cref="ArgumentException">History, sequence, checkpoint or active progress is inconsistent.</exception>
        /// <remarks>All validation precedes mutation. No mid-board snapshot is restored.</remarks>
        public void RestoreTravelState(int completedDayCount, string checkpointNodeId, int traversalSequence,
            IEnumerable<TravelHistoryEntry> history, ActiveRouteLegState activeLeg)
        {
            ValidateStableId(checkpointNodeId, nameof(checkpointNodeId));
            if (history == null)
                throw new ArgumentNullException(nameof(history));
            List<TravelHistoryEntry> copy = new List<TravelHistoryEntry>(history);
            if (completedDayCount < 0 || traversalSequence < 0)
                throw new ArgumentException("Negative day or sequence.");
            int sequence = 0;
            int lastDay = 0;
            TravelHistoryEntry previous = null;
            foreach (TravelHistoryEntry entry in copy)
            {
                if (entry == null)
                    throw new ArgumentException("History contains null.");
                if (entry.Kind == TravelHistoryKind.CompletedTraversal)
                {
                    sequence = checked(sequence + 1);
                    if (entry.LegId != LegIdFor(sequence) || entry.DayNumber <= lastDay ||
                        entry.DayNumber > completedDayCount || (previous != null && previous.ToNodeId != entry.FromNodeId))
                        throw new ArgumentException("Inconsistent ordered traversal history.");
                    lastDay = entry.DayNumber.Value;
                }
                previous = entry;
            }
            if (previous?.Kind == TravelHistoryKind.CompletedTraversal &&
                (previous.ToNodeId != checkpointNodeId || previous.DayNumber != completedDayCount))
                throw new ArgumentException("Completed history does not match checkpoint.");
            if (activeLeg != null && (completedDayCount == int.MaxValue || activeLeg.FromNodeId != checkpointNodeId ||
                activeLeg.DayNumber != completedDayCount + 1 || sequence == int.MaxValue || activeLeg.LegId != LegIdFor(sequence + 1)))
                throw new ArgumentException("Active leg does not match checkpoint, day or sequence.");
            if (traversalSequence != sequence + (activeLeg == null ? 0 : 1))
                throw new ArgumentException("Sequence would reuse or skip a traversal identity.");
            CompletedDayCount = completedDayCount;
            WorldNodeId = checkpointNodeId;
            TraversalSequence = traversalSequence;
            travelHistory.Clear();
            travelHistory.AddRange(copy);
            ActiveLeg = activeLeg;
        }

        private static string LegIdFor(int sequence) => "leg." + sequence.ToString(CultureInfo.InvariantCulture);
        private bool MatchesLeg(string legId) => Status == RunStatus.Active && ActiveLeg != null && ActiveLeg.LegId == legId;
        private bool MatchesSegment(string legId, string segmentId) => MatchesLeg(legId) &&
            ActiveLeg.IntroState == DayIntroState.Consumed && ActiveLeg.CurrentSegment.SegmentId == segmentId;

        private void EnsureCheckpoint()
        {
            EnsureActive();
            if (ActiveLeg != null)
                throw new InvalidOperationException("Checkpoint-only APIs cannot mutate an active leg.");
        }

        /// <summary>Allocates one run-local instance with the definition's initial charges, without equipping or spending a turn.</summary>
        /// <param name="definition">Shared immutable content; charges are copied, never stored back in this definition.</param>
        /// <returns>A detached immutable instance allocated by this run.</returns>
        /// <exception cref="OverflowException">The persisted instance sequence is exhausted.</exception>
        public ToolInstanceState CreateToolInstance(ToolDefinition definition)
        {
            EnsureActive();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return AllocateTool(definition.Id, definition.MaximumCharges);
        }

        /// <summary>Equips an allocated instance only into an empty slot; replacement requires an explicit operation.</summary>
        /// <exception cref="InvalidOperationException">The run is terminal or the slot is occupied.</exception>
        /// <exception cref="ArgumentException">The instance was not allocated by this run or is already equipped.</exception>
        public void EquipTool(int slotIndex, ToolInstanceState instance)
        {
            EnsureActive();
            ValidateSlotIndex(slotIndex);
            if (!toolSlots[slotIndex].IsEmpty) throw new InvalidOperationException("Replacing an occupied tool requires ReplaceTool.");
            ValidateToolInstance(slotIndex, instance);
            toolSlots[slotIndex] = new ToolSlotState(instance);
        }

        /// <summary>Explicitly replaces a slot and returns the displaced immutable instance; no item is silently destroyed.</summary>
        /// <param name="slotIndex">One of the two generic equipment cells.</param>
        /// <param name="instance">An allocated instance not equipped in the other slot.</param>
        /// <returns>The displaced instance, or null for an empty cell. The caller owns its subsequent transfer decision.</returns>
        public ToolInstanceState ReplaceTool(int slotIndex, ToolInstanceState instance)
        {
            EnsureActive();
            ValidateSlotIndex(slotIndex);
            ValidateToolInstance(slotIndex, instance);
            ToolInstanceState displaced = toolSlots[slotIndex].Instance;
            toolSlots[slotIndex] = new ToolSlotState(instance);
            return displaced;
        }

        /// <summary>Compatibility adapter for legacy callers; assigns identity and explicitly replaces the specified slot.</summary>
        /// <remarks>Gameplay pickup must use the explicit instance/replace flow; this adapter does not resolve a turn.</remarks>
        public void EquipTool(int slotIndex, ToolSlotState tool)
        {
            EnsureActive();
            ValidateSlotIndex(slotIndex);
            if (tool.IsEmpty)
                throw new ArgumentException("Use ClearTool to empty a slot.", nameof(tool));

            ToolInstanceState instance = tool.Instance ?? AllocateTool(tool.ToolId, tool.RemainingUses);
            ReplaceTool(slotIndex, instance);
        }

        /// <summary>Removes and returns one equipped instance without resetting its allocator or touching profile knowledge.</summary>
        /// <returns>The displaced instance, or null when already empty; this operation does not resolve a turn.</returns>
        public ToolInstanceState RemoveTool(int slotIndex)
        {
            EnsureActive();
            ValidateSlotIndex(slotIndex);
            ToolInstanceState removed = toolSlots[slotIndex].Instance;
            toolSlots[slotIndex] = ToolSlotState.Empty;
            return removed;
        }

        /// <summary>Clears one generic cell; kept for legacy callers that do not need the removed instance.</summary>
        public void ClearTool(int slotIndex)
        {
            RemoveTool(slotIndex);
        }

        /// <summary>Replaces only the matching instance's charge snapshot; stale, empty or depleted slots remain unchanged.</summary>
        /// <param name="slotIndex">The generic equipment cell.</param>
        /// <param name="expectedInstanceId">The identity whose charge precondition was checked.</param>
        /// <returns>Whether exactly one charge was spent. Commands own turn costs and effects separately.</returns>
        public bool TrySpendToolCharge(int slotIndex, string expectedInstanceId)
        {
            EnsureActive();
            ValidateSlotIndex(slotIndex);
            ToolInstanceState instance = toolSlots[slotIndex].Instance;
            if (instance == null || instance.InstanceId != expectedInstanceId || !instance.TrySpendCharge(out ToolInstanceState next))
                return false;
            toolSlots[slotIndex] = new ToolSlotState(next);
            return true;
        }

        private ToolInstanceState AllocateTool(string definitionId, int charges)
        {
            int next = checked(ToolInstanceSequence + 1);
            var instance = new ToolInstanceState(ToolIdFor(next), definitionId, charges);
            ToolInstanceSequence = next;
            return instance;
        }

        /// <summary>Restores exactly two detached equipment cells and their durable allocator, including terminal save boundaries.</summary>
        /// <param name="sequence">Nonnegative last allocated run-local number; it may exceed all surviving IDs.</param>
        /// <param name="instances">Exactly two entries; null entries are empty slots, unknown definitions are retained.</param>
        /// <exception cref="ArgumentException">Count, duplicate IDs or allocator membership is invalid; no state changes.</exception>
        public void RestoreToolState(int sequence, IEnumerable<ToolInstanceState> instances)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            var copy = new List<ToolInstanceState>(instances);
            if (sequence < 0 || copy.Count != ToolSlotCount)
                throw new ArgumentException("Equipment requires two cells and a nonnegative allocator.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ToolInstanceState instance in copy)
                if (instance != null && (!IsAllocatedToolId(instance.InstanceId, sequence) || !ids.Add(instance.InstanceId)))
                    throw new ArgumentException("Equipment contains a duplicate or unallocated instance.");
            for (int index = 0; index < ToolSlotCount; index++)
                toolSlots[index] = copy[index] == null ? ToolSlotState.Empty : new ToolSlotState(copy[index]);
            ToolInstanceSequence = sequence;
        }

        private void ValidateToolInstance(int slotIndex, ToolInstanceState instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!IsAllocatedToolId(instance.InstanceId, ToolInstanceSequence))
                throw new ArgumentException("The instance identity was not allocated by this run.", nameof(instance));
            for (int index = 0; index < ToolSlotCount; index++)
                if (index != slotIndex && toolSlots[index].Instance?.InstanceId == instance.InstanceId)
                    throw new ArgumentException("An instance cannot occupy two equipment slots.", nameof(instance));
        }

        private static string ToolIdFor(int sequence) => "tool." + sequence.ToString(CultureInfo.InvariantCulture);

        private static bool IsAllocatedToolId(string id, int maximum) => id.StartsWith("tool.", StringComparison.Ordinal) &&
            int.TryParse(id.Substring(5), NumberStyles.None, CultureInfo.InvariantCulture, out int sequence) &&
            sequence > 0 && sequence <= maximum && id == ToolIdFor(sequence);

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