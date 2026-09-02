using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HallowBlaze.Core.State
{
    public sealed class ProfileState
    {
        private readonly HashSet<string> discoveredNodeIdSet = new HashSet<string>();
        private readonly List<string> discoveredNodeIds = new List<string>();
        private readonly ReadOnlyCollection<string> discoveredNodeIdsView;
        private readonly HashSet<string> discoveredEdgeIdSet = new HashSet<string>();
        private readonly List<string> discoveredEdgeIds = new List<string>();
        private readonly ReadOnlyCollection<string> discoveredEdgeIdsView;
        private readonly HashSet<string> discoveredFactIdSet = new HashSet<string>();
        private readonly List<string> discoveredFactIds = new List<string>();
        private readonly ReadOnlyCollection<string> discoveredFactIdsView;
        private readonly HashSet<string> persistentNoteIdSet = new HashSet<string>();
        private readonly List<string> persistentNoteIds = new List<string>();
        private readonly ReadOnlyCollection<string> persistentNoteIdsView;
        private readonly List<ProfileRunSummary> runSummaries = new List<ProfileRunSummary>();
        private readonly ReadOnlyCollection<ProfileRunSummary> runSummariesView;

        public ProfileState(string profileId, string worldDefinitionId, int worldDefinitionVersion)
        {
            ValidateStableId(profileId, nameof(profileId));
            ValidateStableId(worldDefinitionId, nameof(worldDefinitionId));
            if (worldDefinitionVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(worldDefinitionVersion), "World definition version must be positive.");

            ProfileId = profileId;
            WorldDefinitionId = worldDefinitionId;
            WorldDefinitionVersion = worldDefinitionVersion;
            discoveredNodeIdsView = discoveredNodeIds.AsReadOnly();
            discoveredEdgeIdsView = discoveredEdgeIds.AsReadOnly();
            discoveredFactIdsView = discoveredFactIds.AsReadOnly();
            persistentNoteIdsView = persistentNoteIds.AsReadOnly();
            runSummariesView = runSummaries.AsReadOnly();
        }

        public string ProfileId { get; }
        public string WorldDefinitionId { get; }
        public int WorldDefinitionVersion { get; }
        public IReadOnlyList<string> DiscoveredNodeIds => discoveredNodeIdsView;
        public IReadOnlyList<string> DiscoveredEdgeIds => discoveredEdgeIdsView;
        public IReadOnlyList<string> DiscoveredFactIds => discoveredFactIdsView;
        public IReadOnlyList<string> PersistentNoteIds => persistentNoteIdsView;
        public IReadOnlyList<ProfileRunSummary> RunSummaries => runSummariesView;
        public int CompletedRunCount { get; private set; }
        public int WonRunCount { get; private set; }
        public int LostRunCount { get; private set; }
        public int TotalDaysSurvived { get; private set; }

        public bool DiscoverNode(string nodeId)
        {
            return AddStableId(
                nodeId,
                nameof(nodeId),
                discoveredNodeIdSet,
                discoveredNodeIds);
        }

        public bool DiscoverEdge(string edgeId)
        {
            return AddStableId(
                edgeId,
                nameof(edgeId),
                discoveredEdgeIdSet,
                discoveredEdgeIds);
        }

        public bool DiscoverFact(string factId)
        {
            return AddStableId(
                factId,
                nameof(factId),
                discoveredFactIdSet,
                discoveredFactIds);
        }

        public bool AddPersistentNote(string noteId)
        {
            return AddStableId(
                noteId,
                nameof(noteId),
                persistentNoteIdSet,
                persistentNoteIds);
        }

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
    }

    public sealed class ProfileRunSummary
    {
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

        public string RunId { get; }
        public int DaysSurvived { get; }
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