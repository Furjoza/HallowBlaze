using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HallowBlaze.Core.State
{
    /// <summary>
    /// Describes the durable amount of knowledge a profile has about a world node.
    /// </summary>
    public enum NodeDiscoveryState
    {
        /// <summary>The profile has no durable knowledge of the node.</summary>
        Unknown = 0,

        /// <summary>The profile retains an incomplete clue that the node exists.</summary>
        Rumored = 1,

        /// <summary>The profile retains the node's observed direction or outline.</summary>
        Sighted = 2,

        /// <summary>The profile records that a run reached the node.</summary>
        Visited = 3
    }

    /// <summary>
    /// Describes the durable amount of knowledge a profile has about a directed world edge.
    /// </summary>
    public enum EdgeDiscoveryState
    {
        /// <summary>The profile has no durable knowledge of the directed edge.</summary>
        Unknown = 0,

        /// <summary>The profile retains that the directed edge was observed.</summary>
        Sighted = 1,

        /// <summary>The profile records that a run traversed the directed edge.</summary>
        Traversed = 2
    }

    /// <summary>
    /// Associates a stable world node ID with its current durable discovery state.
    /// </summary>
    public sealed class NodeDiscovery
    {
        internal NodeDiscovery(string nodeId, NodeDiscoveryState state)
        {
            NodeId = nodeId;
            State = state;
        }

        /// <summary>Gets the stable ID of the known world node.</summary>
        public string NodeId { get; }

        /// <summary>Gets the greatest durable knowledge reached for the node.</summary>
        public NodeDiscoveryState State { get; }
    }

    /// <summary>
    /// Associates a stable directed edge ID with its current durable discovery state.
    /// </summary>
    public sealed class EdgeDiscovery
    {
        internal EdgeDiscovery(string edgeId, EdgeDiscoveryState state)
        {
            EdgeId = edgeId;
            State = state;
        }

        /// <summary>Gets the stable ID of the known directed edge.</summary>
        public string EdgeId { get; }

        /// <summary>Gets the greatest durable knowledge reached for the edge.</summary>
        public EdgeDiscoveryState State { get; }
    }

    /// <summary>
    /// Owns durable knowledge and aggregate results shared by every run in one profile.
    /// </summary>
    public sealed class ProfileState
    {
        private readonly Dictionary<string, int> nodeDiscoveryIndexById =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<NodeDiscovery> nodeDiscoveries = new List<NodeDiscovery>();
        private readonly ReadOnlyCollection<NodeDiscovery> nodeDiscoveriesView;
        private readonly Dictionary<string, int> edgeDiscoveryIndexById =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<EdgeDiscovery> edgeDiscoveries = new List<EdgeDiscovery>();
        private readonly ReadOnlyCollection<EdgeDiscovery> edgeDiscoveriesView;
        private readonly HashSet<string> discoveredFactIdSet = new HashSet<string>();
        private readonly List<string> discoveredFactIds = new List<string>();
        private readonly ReadOnlyCollection<string> discoveredFactIdsView;
        private readonly HashSet<string> persistentNoteIdSet = new HashSet<string>();
        private readonly List<string> persistentNoteIds = new List<string>();
        private readonly ReadOnlyCollection<string> persistentNoteIdsView;
        private readonly List<ProfileRunSummary> runSummaries = new List<ProfileRunSummary>();
        private readonly ReadOnlyCollection<ProfileRunSummary> runSummariesView;

        /// <summary>
        /// Creates an empty durable profile bound to one versioned world definition.
        /// </summary>
        /// <param name="profileId">The stable profile identity.</param>
        /// <param name="worldDefinitionId">The stable world definition identity.</param>
        /// <param name="worldDefinitionVersion">The positive world definition version.</param>
        public ProfileState(string profileId, string worldDefinitionId, int worldDefinitionVersion)
        {
            ValidateStableId(profileId, nameof(profileId));
            ValidateStableId(worldDefinitionId, nameof(worldDefinitionId));
            if (worldDefinitionVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(worldDefinitionVersion), "World definition version must be positive.");

            ProfileId = profileId;
            WorldDefinitionId = worldDefinitionId;
            WorldDefinitionVersion = worldDefinitionVersion;
            nodeDiscoveriesView = nodeDiscoveries.AsReadOnly();
            edgeDiscoveriesView = edgeDiscoveries.AsReadOnly();
            discoveredFactIdsView = discoveredFactIds.AsReadOnly();
            persistentNoteIdsView = persistentNoteIds.AsReadOnly();
            runSummariesView = runSummaries.AsReadOnly();
        }

        /// <summary>Gets the stable identity of this profile.</summary>
        public string ProfileId { get; }

        /// <summary>Gets the stable identity of the world this profile describes.</summary>
        public string WorldDefinitionId { get; }

        /// <summary>Gets the version of the world definition this profile describes.</summary>
        public int WorldDefinitionVersion { get; }

        /// <summary>
        /// Gets discovered nodes in the order in which they first became known.
        /// </summary>
        public IReadOnlyList<NodeDiscovery> NodeDiscoveries => nodeDiscoveriesView;

        /// <summary>
        /// Gets discovered directed edges in the order in which they first became known.
        /// </summary>
        public IReadOnlyList<EdgeDiscovery> EdgeDiscoveries => edgeDiscoveriesView;

        /// <summary>Gets stable fact IDs in first-discovery order.</summary>
        public IReadOnlyList<string> DiscoveredFactIds => discoveredFactIdsView;

        /// <summary>Gets stable persistent note IDs in insertion order.</summary>
        public IReadOnlyList<string> PersistentNoteIds => persistentNoteIdsView;

        /// <summary>Gets terminal run summaries in completion order.</summary>
        public IReadOnlyList<ProfileRunSummary> RunSummaries => runSummariesView;

        /// <summary>Gets the number of uniquely recorded terminal runs.</summary>
        public int CompletedRunCount { get; private set; }

        /// <summary>Gets the number of uniquely recorded won runs.</summary>
        public int WonRunCount { get; private set; }

        /// <summary>Gets the number of uniquely recorded dead runs.</summary>
        public int LostRunCount { get; private set; }

        /// <summary>Gets the sum of days survived across uniquely recorded terminal runs.</summary>
        public int TotalDaysSurvived { get; private set; }

        /// <summary>
        /// Gets the current node knowledge, returning <see cref="NodeDiscoveryState.Unknown"/>
        /// when the node has no durable entry.
        /// </summary>
        /// <param name="nodeId">The stable node ID to query.</param>
        /// <returns>The current durable discovery state.</returns>
        public NodeDiscoveryState GetNodeDiscoveryState(string nodeId)
        {
            ValidateStableId(nodeId, nameof(nodeId));
            return nodeDiscoveryIndexById.TryGetValue(nodeId, out int index)
                ? nodeDiscoveries[index].State
                : NodeDiscoveryState.Unknown;
        }

        /// <summary>
        /// Advances durable node knowledge without allowing a downgrade.
        /// </summary>
        /// <param name="nodeId">The stable node ID to update.</param>
        /// <param name="state">The discovery state that has been observed.</param>
        /// <returns><c>true</c> only when durable profile knowledge changed.</returns>
        public bool AdvanceNodeDiscovery(string nodeId, NodeDiscoveryState state)
        {
            ValidateStableId(nodeId, nameof(nodeId));
            ValidateNodeDiscoveryState(state, nameof(state));

            if (nodeDiscoveryIndexById.TryGetValue(nodeId, out int index))
            {
                if ((int)nodeDiscoveries[index].State >= (int)state)
                    return false;

                nodeDiscoveries[index] = new NodeDiscovery(nodeId, state);
                return true;
            }

            if (state == NodeDiscoveryState.Unknown)
                return false;

            nodeDiscoveryIndexById.Add(nodeId, nodeDiscoveries.Count);
            nodeDiscoveries.Add(new NodeDiscovery(nodeId, state));
            return true;
        }

        /// <summary>
        /// Gets the current edge knowledge, returning <see cref="EdgeDiscoveryState.Unknown"/>
        /// when the edge has no durable entry.
        /// </summary>
        /// <param name="edgeId">The stable directed edge ID to query.</param>
        /// <returns>The current durable discovery state.</returns>
        public EdgeDiscoveryState GetEdgeDiscoveryState(string edgeId)
        {
            ValidateStableId(edgeId, nameof(edgeId));
            return edgeDiscoveryIndexById.TryGetValue(edgeId, out int index)
                ? edgeDiscoveries[index].State
                : EdgeDiscoveryState.Unknown;
        }

        /// <summary>
        /// Advances durable edge knowledge without allowing a downgrade.
        /// </summary>
        /// <param name="edgeId">The stable directed edge ID to update.</param>
        /// <param name="state">The discovery state that has been observed.</param>
        /// <returns><c>true</c> only when durable profile knowledge changed.</returns>
        public bool AdvanceEdgeDiscovery(string edgeId, EdgeDiscoveryState state)
        {
            ValidateStableId(edgeId, nameof(edgeId));
            ValidateEdgeDiscoveryState(state, nameof(state));

            if (edgeDiscoveryIndexById.TryGetValue(edgeId, out int index))
            {
                if ((int)edgeDiscoveries[index].State >= (int)state)
                    return false;

                edgeDiscoveries[index] = new EdgeDiscovery(edgeId, state);
                return true;
            }

            if (state == EdgeDiscoveryState.Unknown)
                return false;

            edgeDiscoveryIndexById.Add(edgeId, edgeDiscoveries.Count);
            edgeDiscoveries.Add(new EdgeDiscovery(edgeId, state));
            return true;
        }

        /// <summary>Adds a stable fact once without changing existing discovery order.</summary>
        /// <param name="factId">The stable fact ID to retain.</param>
        /// <returns><c>true</c> only when the fact was newly added.</returns>
        public bool DiscoverFact(string factId)
        {
            return AddStableId(
                factId,
                nameof(factId),
                discoveredFactIdSet,
                discoveredFactIds);
        }

        /// <summary>Adds a stable persistent note once in insertion order.</summary>
        /// <param name="noteId">The stable note ID to retain.</param>
        /// <returns><c>true</c> only when the note was newly added.</returns>
        public bool AddPersistentNote(string noteId)
        {
            return AddStableId(
                noteId,
                nameof(noteId),
                persistentNoteIdSet,
                persistentNoteIds);
        }

        /// <summary>
        /// Records one terminal outcome per run ID and updates profile aggregates atomically.
        /// </summary>
        /// <param name="runSummary">The terminal run summary to record.</param>
        /// <returns><c>true</c> when a new run was recorded; otherwise <c>false</c>.</returns>
        public bool RecordRunSummary(ProfileRunSummary runSummary)
        {
            if (runSummary == null)
                throw new ArgumentNullException(nameof(runSummary));

            foreach (ProfileRunSummary existingSummary in runSummaries)
            {
                if (!string.Equals(existingSummary.RunId, runSummary.RunId, StringComparison.Ordinal))
                    continue;

                if (existingSummary.DaysSurvived == runSummary.DaysSurvived &&
                    existingSummary.Status == runSummary.Status)
                    return false;

                throw new InvalidOperationException(
                    $"Run summary for run '{runSummary.RunId}' already exists with different data.");
            }

            int nextCompletedRunCount;
            int nextWonRunCount = WonRunCount;
            int nextLostRunCount = LostRunCount;
            int nextTotalDaysSurvived;
            checked
            {
                nextCompletedRunCount = CompletedRunCount + 1;
                nextTotalDaysSurvived = TotalDaysSurvived + runSummary.DaysSurvived;
                if (runSummary.Status == RunStatus.Won)
                    nextWonRunCount = WonRunCount + 1;
                else
                    nextLostRunCount = LostRunCount + 1;
            }

            runSummaries.Add(runSummary);
            CompletedRunCount = nextCompletedRunCount;
            WonRunCount = nextWonRunCount;
            LostRunCount = nextLostRunCount;
            TotalDaysSurvived = nextTotalDaysSurvived;
            return true;
        }

        private static bool AddStableId(
            string value,
            string parameterName,
            HashSet<string> valueSet,
            List<string> orderedValues)
        {
            ValidateStableId(value, parameterName);
            if (!valueSet.Add(value))
                return false;

            orderedValues.Add(value);
            return true;
        }

        private static void ValidateStableId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                char.IsWhiteSpace(value[0]) ||
                char.IsWhiteSpace(value[value.Length - 1]))
                throw new ArgumentException("Stable ID cannot be empty or have leading/trailing whitespace.", parameterName);
        }

        private static void ValidateNodeDiscoveryState(
            NodeDiscoveryState state,
            string parameterName)
        {
            if (state < NodeDiscoveryState.Unknown || state > NodeDiscoveryState.Visited)
                throw new ArgumentOutOfRangeException(parameterName, "Unknown node discovery state.");
        }

        private static void ValidateEdgeDiscoveryState(
            EdgeDiscoveryState state,
            string parameterName)
        {
            if (state < EdgeDiscoveryState.Unknown || state > EdgeDiscoveryState.Traversed)
                throw new ArgumentOutOfRangeException(parameterName, "Unknown edge discovery state.");
        }
    }

    /// <summary>
    /// Describes the immutable terminal outcome retained for one completed run.
    /// </summary>
    public sealed class ProfileRunSummary
    {
        /// <summary>Creates a validated terminal run summary.</summary>
        /// <param name="runId">The stable identity of the completed run.</param>
        /// <param name="daysSurvived">The non-negative number of survived days.</param>
        /// <param name="status">The terminal <c>Dead</c> or <c>Won</c> outcome.</param>
        public ProfileRunSummary(string runId, int daysSurvived, RunStatus status)
        {
            ValidateStableId(runId, nameof(runId));
            if (daysSurvived < 0)
                throw new ArgumentOutOfRangeException(nameof(daysSurvived));
            if (status != RunStatus.Dead && status != RunStatus.Won)
                throw new ArgumentException("Run status must be either Dead or Won.", nameof(status));

            RunId = runId;
            DaysSurvived = daysSurvived;
            Status = status;
        }

        /// <summary>Gets the stable identity of the completed run.</summary>
        public string RunId { get; }

        /// <summary>Gets the non-negative number of survived days.</summary>
        public int DaysSurvived { get; }

        /// <summary>Gets the terminal run outcome.</summary>
        public RunStatus Status { get; }

        private static void ValidateStableId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                char.IsWhiteSpace(value[0]) ||
                char.IsWhiteSpace(value[value.Length - 1]))
                throw new ArgumentException("Stable ID cannot be empty or have leading/trailing whitespace.", parameterName);
        }
    }
}