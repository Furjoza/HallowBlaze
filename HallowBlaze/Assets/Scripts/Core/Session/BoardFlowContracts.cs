using System;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;

namespace HallowBlaze.Core.Session
{
    /// <summary>
    /// Represents a request to create a board for a specific world node in a run.
    /// </summary>
    public sealed class BoardRequest
    {
        /// <summary>
        /// Initializes a new board request for the specified run and world node.
        /// </summary>
        /// <param name="runId">The unique identifier of the active run.</param>
        /// <param name="runSeed">The seed used for the run.</param>
        /// <param name="worldNodeId">The identifier of the world node to create a board for.</param>
        /// <param name="currentDay">The current day in the run.</param>
        /// <param name="boardSeed">The seed used for board generation.</param>
        /// <param name="placeKind">The kind of place represented by the world node.</param>
        /// <param name="biomeFamily">The biome family of the world node.</param>
        /// <param name="legacyDifficultyLevel">The legacy difficulty level for board generation.</param>
        public BoardRequest(
            string runId,
            int runSeed,
            string worldNodeId,
            int currentDay,
            int boardSeed,
            string placeKind,
            string biomeFamily,
            int legacyDifficultyLevel)
        {
            ValidateStableId(runId, nameof(runId));
            ValidateStableId(worldNodeId, nameof(worldNodeId));
            ValidateStableId(placeKind, nameof(placeKind));
            ValidateStableId(biomeFamily, nameof(biomeFamily));
            if (currentDay < 0)
                throw new ArgumentOutOfRangeException(nameof(currentDay), "Current day cannot be negative.");
            if (legacyDifficultyLevel < 1)
                throw new ArgumentOutOfRangeException(nameof(legacyDifficultyLevel), "Difficulty level must be at least 1.");

            RunId = runId;
            RunSeed = runSeed;
            WorldNodeId = worldNodeId;
            CurrentDay = currentDay;
            BoardSeed = boardSeed;
            PlaceKind = placeKind;
            BiomeFamily = biomeFamily;
            LegacyDifficultyLevel = legacyDifficultyLevel;
        }

        /// <summary>
        /// Gets the unique identifier of the active run.
        /// </summary>
        public string RunId { get; }

        /// <summary>
        /// Gets the seed used for the run.
        /// </summary>
        public int RunSeed { get; }

        /// <summary>
        /// Gets the identifier of the world node to create a board for.
        /// </summary>
        public string WorldNodeId { get; }

        /// <summary>
        /// Gets the current day in the run.
        /// </summary>
        public int CurrentDay { get; }

        /// <summary>
        /// Gets the seed used for board generation.
        /// </summary>
        public int BoardSeed { get; }

        /// <summary>
        /// Gets the kind of place represented by the world node.
        /// </summary>
        public string PlaceKind { get; }

        /// <summary>
        /// Gets the biome family of the world node.
        /// </summary>
        public string BiomeFamily { get; }

        /// <summary>
        /// Gets the legacy difficulty level for board generation.
        /// </summary>
        public int LegacyDifficultyLevel { get; }

        /// <summary>
        /// Creates a board request from the current run state and world definition.
        /// </summary>
        /// <param name="run">The active run state.</param>
        /// <param name="worldDefinition">The world definition containing node information.</param>
        /// <returns>A request containing the stable board identity and legacy generation inputs.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the run is not active.</exception>
        /// <exception cref="ArgumentException">Thrown when the run references an unknown world node.</exception>
        public static BoardRequest CreateFromRun(RunState run, WorldDefinition worldDefinition)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (worldDefinition == null)
                throw new ArgumentNullException(nameof(worldDefinition));

            if (run.Status != RunStatus.Active)
                throw new InvalidOperationException("A board can only be created for an active run.");
            if (!worldDefinition.TryGetNode(run.WorldNodeId, out WorldNodeDefinition nodeDefinition))
            {
                throw new ArgumentException(
                    $"World node '{run.WorldNodeId}' does not exist in the active world definition.",
                    nameof(run));
            }

            int legacyDifficultyLevel = Math.Max(1, run.CurrentDay);
            return new BoardRequest(
                run.RunId,
                run.RunSeed,
                run.WorldNodeId,
                run.CurrentDay,
                run.GetBoardSeed(),
                nodeDefinition.PlaceKind,
                nodeDefinition.BiomeFamily,
                legacyDifficultyLevel);
        }

        /// <summary>Checks whether another request identifies the same board boundary.</summary>
        /// <param name="other">The request to compare.</param>
        /// <returns><c>true</c> when the run, node, day, and board seed match.</returns>
        public bool HasSameIdentity(BoardRequest other)
        {
            return other != null
                && string.Equals(RunId, other.RunId, StringComparison.Ordinal)
                && string.Equals(WorldNodeId, other.WorldNodeId, StringComparison.Ordinal)
                && CurrentDay == other.CurrentDay
                && BoardSeed == other.BoardSeed;
        }

        private static void ValidateStableId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)
                || char.IsWhiteSpace(value[0])
                || char.IsWhiteSpace(value[value.Length - 1]))
            {
                throw new ArgumentException("Stable value cannot be empty or have edge whitespace.", parameterName);
            }
        }
    }

    /// <summary>
    /// Represents the outcome of a board session (exit reached or player died).
    /// </summary>
    public sealed class BoardOutcome
    {
        /// <summary>
        /// Creates a board outcome for when the player reaches the exit.
        /// </summary>
        /// <param name="request">The original board request that produced this outcome.</param>
        public static BoardOutcome ExitReached(BoardRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            return new BoardOutcome(request, BoardOutcomeType.ExitReached, null);
        }

        /// <summary>
        /// Creates a board outcome for when the player dies.
        /// </summary>
        /// <param name="request">The original board request that produced this outcome.</param>
        /// <param name="deathReason">The reason for the player's death.</param>
        public static BoardOutcome PlayerDied(BoardRequest request, DeathReason deathReason)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (deathReason != HallowBlaze.Core.Session.DeathReason.Starvation
                && deathReason != HallowBlaze.Core.Session.DeathReason.HealthDepleted)
                throw new ArgumentOutOfRangeException(nameof(deathReason), "Unknown board death reason.");
            return new BoardOutcome(request, BoardOutcomeType.PlayerDied, deathReason);
        }

        private BoardOutcome(BoardRequest request, BoardOutcomeType type, DeathReason? deathReason)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Type = type;
            DeathReason = deathReason;
        }

        /// <summary>
        /// Gets the original board request that produced this outcome.
        /// </summary>
        public BoardRequest Request { get; }

        /// <summary>
        /// Gets the type of outcome (exit reached or player died).
        /// </summary>
        public BoardOutcomeType Type { get; }

        /// <summary>
        /// Gets the reason for death, or null if the outcome is exit reached.
        /// </summary>
        public DeathReason? DeathReason { get; }
    }

    /// <summary>
    /// Represents the type of board outcome.
    /// </summary>
    public enum BoardOutcomeType
    {
        /// <summary>The player reached the exit.</summary>
        ExitReached,

        /// <summary>The player died.</summary>
        PlayerDied
    }

    /// <summary>
    /// Represents the reason for a player's death.
    /// </summary>
    public enum DeathReason
    {
        /// <summary>The player starved to death.</summary>
        Starvation,

        /// <summary>The player's health was depleted.</summary>
        HealthDepleted
    }
}