using System;
using System.Globalization;

namespace HallowBlaze.Core.State
{
    /// <summary>Full segment identity; equal seeds alone never establish equal boards.</summary>
    public sealed class BoardAddress : IEquatable<BoardAddress>
    {
        /// <summary>Creates an immutable address from validated stable identity and generation context.</summary>
        public BoardAddress(string runId, string legId, string edgeId, string segmentId, int dayNumber,
            string generationConfigurationId, int generationVersion, int boardSeed)
        {
            TravelHistoryEntry.RequireId(runId, nameof(runId));
            TravelHistoryEntry.RequireId(legId, nameof(legId));
            TravelHistoryEntry.RequireId(edgeId, nameof(edgeId));
            TravelHistoryEntry.RequireId(segmentId, nameof(segmentId));
            TravelHistoryEntry.RequireId(generationConfigurationId, nameof(generationConfigurationId));
            if (dayNumber <= 0 || generationVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(dayNumber));
            RunId = runId;
            LegId = legId;
            EdgeId = edgeId;
            SegmentId = segmentId;
            DayNumber = dayNumber;
            GenerationConfigurationId = generationConfigurationId;
            GenerationVersion = generationVersion;
            BoardSeed = boardSeed;
        }
        /// <summary>Gets the owning run ID.</summary>
        public string RunId { get; }
        /// <summary>Gets the run-local traversal ID.</summary>
        public string LegId { get; }
        /// <summary>Gets the directed road ID.</summary>
        public string EdgeId { get; }
        /// <summary>Gets the stable segment ID, never its ordinal cursor.</summary>
        public string SegmentId { get; }
        /// <summary>Gets the one-based day shared by the leg.</summary>
        public int DayNumber { get; }
        /// <summary>Gets the stable generation configuration key.</summary>
        public string GenerationConfigurationId { get; }
        /// <summary>Gets the positive generation contract version.</summary>
        public int GenerationVersion { get; }
        /// <summary>Gets a deterministic finite seed, not a collision-free identity.</summary>
        public int BoardSeed { get; }
        /// <summary>Compares every identity field using ordinal textual equality.</summary>
        public bool Equals(BoardAddress other) => other != null && RunId == other.RunId &&
            LegId == other.LegId && EdgeId == other.EdgeId && SegmentId == other.SegmentId &&
            DayNumber == other.DayNumber && GenerationConfigurationId == other.GenerationConfigurationId &&
            GenerationVersion == other.GenerationVersion && BoardSeed == other.BoardSeed;
        /// <summary>Compares another value with this address.</summary>
        public override bool Equals(object obj) => Equals(obj as BoardAddress);
        /// <summary>Returns an ordinary dictionary hash; equality still checks the complete address.</summary>
        public override int GetHashCode() => HashCode.Combine(RunId, LegId, EdgeId, SegmentId,
            DayNumber, GenerationConfigurationId, GenerationVersion, BoardSeed);
        /// <summary>Formats all fields with invariant numeric values for diagnostics, not portable replay.</summary>
        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "run={0};leg={1};edge={2};segment={3};day={4};config={5};version={6};seed={7}",
            RunId, LegId, EdgeId, SegmentId, DayNumber, GenerationConfigurationId, GenerationVersion, BoardSeed);
    }
}