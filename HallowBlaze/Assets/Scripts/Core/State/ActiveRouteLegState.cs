using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.State
{
    /// <summary>Durable at-most-once intro state; Consumed must be saved before rendering.</summary>
    public enum DayIntroState
    {
        /// <summary>The service has not committed consumption.</summary>
        Pending,
        /// <summary>Recovery must not render the intro again.</summary>
        Consumed
    }

    /// <summary>State-owned scalar copy of a segment's published generation identity.</summary>
    public sealed class RouteSegmentState
    {
        /// <summary>Copies validated identity without a World or Unity dependency.</summary>
        public RouteSegmentState(string segmentId, string generationConfigurationId, int generationVersion)
        {
            TravelHistoryEntry.RequireId(segmentId, nameof(segmentId));
            TravelHistoryEntry.RequireId(generationConfigurationId, nameof(generationConfigurationId));
            if (generationVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(generationVersion));
            SegmentId = segmentId;
            GenerationConfigurationId = generationConfigurationId;
            GenerationVersion = generationVersion;
        }
        /// <summary>Gets the published segment ID, not its list position.</summary>
        public string SegmentId { get; }
        /// <summary>Gets the stable generation configuration.</summary>
        public string GenerationConfigurationId { get; }
        /// <summary>Gets the positive generation version.</summary>
        public int GenerationVersion { get; }
    }

    /// <summary>
    /// Detached travel snapshot. Cursor equals completed segments; destination remains unreached.
    /// Future observation accumulation belongs to this leg, not a separate travel ledger.
    /// </summary>
    public sealed class ActiveRouteLegState
    {
        /// <summary>Validates and copies a whole active travel snapshot before publication.</summary>
        /// <exception cref="ArgumentException">Identity, progress or intro fields contradict each other.</exception>
        public ActiveRouteLegState(string legId, string edgeId, string fromNodeId, string toNodeId,
            IEnumerable<RouteSegmentState> segments, int dayNumber, int segmentIndex = 0,
            int completedSegmentCount = 0, DayIntroState introState = DayIntroState.Pending)
        {
            TravelHistoryEntry.RequireId(legId, nameof(legId));
            TravelHistoryEntry.RequireId(edgeId, nameof(edgeId));
            TravelHistoryEntry.RequireId(fromNodeId, nameof(fromNodeId));
            TravelHistoryEntry.RequireId(toNodeId, nameof(toNodeId));
            if (segments == null)
                throw new ArgumentNullException(nameof(segments));
            List<RouteSegmentState> copy = new List<RouteSegmentState>(segments);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RouteSegmentState segment in copy)
                if (segment == null || !ids.Add(segment.SegmentId))
                    throw new ArgumentException("Segments must be non-null and unique.", nameof(segments));
            if (copy.Count == 0 || dayNumber <= 0 || segmentIndex < 0 || segmentIndex >= copy.Count ||
                completedSegmentCount != segmentIndex || !Enum.IsDefined(typeof(DayIntroState), introState) ||
                (introState == DayIntroState.Pending && segmentIndex != 0))
                throw new ArgumentException("Invalid active-leg progress, day or intro state.");
            LegId = legId;
            EdgeId = edgeId;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            Segments = copy.AsReadOnly();
            DayNumber = dayNumber;
            SegmentIndex = segmentIndex;
            CompletedSegmentCount = completedSegmentCount;
            IntroState = introState;
        }
        /// <summary>Gets the unique run-local traversal identity.</summary>
        public string LegId { get; }
        /// <summary>Gets the directed road being traversed.</summary>
        public string EdgeId { get; }
        /// <summary>Gets the still-reached source checkpoint.</summary>
        public string FromNodeId { get; }
        /// <summary>Gets the unreached destination.</summary>
        public string ToNodeId { get; }
        /// <summary>Gets copied, ordered generation identities.</summary>
        public IReadOnlyList<RouteSegmentState> Segments { get; }
        /// <summary>Gets the one-based day shared by every segment.</summary>
        public int DayNumber { get; }
        /// <summary>Gets the zero-based cursor used for order, never identity.</summary>
        public int SegmentIndex { get; }
        /// <summary>Gets progress, exactly equal to the current cursor.</summary>
        public int CompletedSegmentCount { get; }
        /// <summary>Gets durable intro consumption.</summary>
        public DayIntroState IntroState { get; }
        /// <summary>Gets the current stable segment.</summary>
        public RouteSegmentState CurrentSegment => Segments[SegmentIndex];
        /// <summary>Gets whether the current segment is final.</summary>
        public bool IsFinalSegment => SegmentIndex == Segments.Count - 1;
        internal ActiveRouteLegState WithProgress(int index, DayIntroState intro) =>
            new ActiveRouteLegState(LegId, EdgeId, FromNodeId, ToNodeId, Segments, DayNumber, index, index, intro);
    }
}