using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Generation.Validation
{
    /// <summary>Describes one rejected rule with reproducible board identity and optional cell context.</summary>
    public sealed class BoardValidationError
    {
        internal BoardValidationError(string code, string message, string context, GridPosition? position = null)
        {
            Code = code;
            Position = position;
            Message = context + " | " + code + ": " + message +
                (position.HasValue ? " at " + position.Value : string.Empty);
        }

        /// <summary>Gets the stable, non-localized rule identifier.</summary>
        public string Code { get; }
        /// <summary>Gets the failure reason including address, seed and configuration when supplied.</summary>
        public string Message { get; }
        /// <summary>Gets the affected cell when the rule is cell-specific.</summary>
        public GridPosition? Position { get; }
    }

    /// <summary>Contains static topology measurements; these do not prove survival against dynamic enemies.</summary>
    public sealed class BoardValidationMetrics
    {
        internal BoardValidationMetrics(int reachableCells, int branchPoints, int obstacleCount, int enemyCount,
            IDictionary<GridPosition, int> exitDistances, int? safeStartMoves, int? maximumConcurrentThreats,
            int? minimumEnemyStartDistance, IDictionary<GridPosition, int> initialThreats)
        {
            ReachableCells = reachableCells;
            BranchPoints = branchPoints;
            ObstacleCount = obstacleCount;
            EnemyCount = enemyCount;
            ExitDistances = new ReadOnlyDictionary<GridPosition, int>(new Dictionary<GridPosition, int>(exitDistances));
            SafeStartMoves = safeStartMoves;
            MaximumConcurrentThreats = maximumConcurrentThreats;
            MinimumEnemyStartDistance = minimumEnemyStartDistance;
            InitialThreatsByCell = new ReadOnlyDictionary<GridPosition, int>(new Dictionary<GridPosition, int>(initialThreats));
        }

        /// <summary>Gets the number of terrain/obstacle-passable cells connected to the start, ignoring actors and loot.</summary>
        public int ReachableCells { get; }
        /// <summary>Gets all passable cells with at least three passable cardinal neighbors, including disconnected regions.</summary>
        public int BranchPoints { get; }
        /// <summary>Gets the occupied obstacle-layer cell count, including walkable obstacles.</summary>
        public int ObstacleCount { get; }
        /// <summary>Gets the initial enemy actor count.</summary>
        public int EnemyCount { get; }
        /// <summary>Gets shortest cardinal distances to every logical exit; -1 denotes unreachable.</summary>
        public IReadOnlyDictionary<GridPosition, int> ExitDistances { get; }
        /// <summary>Gets legal unoccupied first-step cells not targeted by initial intents; null means enemy rules could not be resolved.</summary>
        public int? SafeStartMoves { get; }
        /// <summary>Gets the maximum number of distinct initial intents threatening one cell; null means incomplete enemy-rule resolution.</summary>
        public int? MaximumConcurrentThreats { get; }
        /// <summary>Gets the nearest enemy's initial Manhattan distance, or null when there are no enemies.</summary>
        public int? MinimumEnemyStartDistance { get; }
        /// <summary>Gets initial attack/conditional-move target counts; counts are partial when enemy-rule resolution fails.</summary>
        public IReadOnlyDictionary<GridPosition, int> InitialThreatsByCell { get; }
    }

    /// <summary>Owns all validation errors and detached measurements without changing the candidate.</summary>
    public sealed class BoardValidationReport
    {
        internal BoardValidationReport(BoardAddress address, BoardGenerationConfig config,
            List<BoardValidationError> errors, BoardValidationMetrics metrics)
        {
            Address = address;
            Configuration = config;
            var ordered = new List<BoardValidationError>(errors);
            ordered.Sort((left, right) => string.CompareOrdinal(left.Message, right.Message));
            Errors = ordered.AsReadOnly();
            Metrics = metrics;
        }

        /// <summary>Gets the supplied full board identity, or null when missing from an invalid candidate.</summary>
        public BoardAddress Address { get; }
        /// <summary>Gets the supplied budget configuration, or null when missing from an invalid candidate.</summary>
        public BoardGenerationConfig Configuration { get; }
        /// <summary>Gets every detected error in ordinal message order, not just the first failure.</summary>
        public IReadOnlyList<BoardValidationError> Errors { get; }
        /// <summary>Gets topology metrics, or null when structural errors prevent meaningful measurement.</summary>
        public BoardValidationMetrics Metrics { get; }
        /// <summary>Gets whether every applicable validation rule passed.</summary>
        public bool IsValid => Errors.Count == 0;

        internal static string Context(BoardAddress address, BoardGenerationConfig config)
        {
            string configuration = config == null ? "<missing>" : config.Id + "/" + config.Version.ToString(CultureInfo.InvariantCulture);
            if (address == null) return "address=<missing>, config=" + configuration;
            return string.Format(CultureInfo.InvariantCulture,
                "run={0}, leg={1}, edge={2}, segment={3}, day={4}, seed={5}, address-config={6}/{7}, config={8}",
                address.RunId, address.LegId, address.EdgeId, address.SegmentId, address.DayNumber,
                address.BoardSeed, address.GenerationConfigurationId, address.GenerationVersion, configuration);
        }
    }
}