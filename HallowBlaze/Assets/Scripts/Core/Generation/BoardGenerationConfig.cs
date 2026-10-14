using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.Generation
{
    /// <summary>Describes an inclusive nonnegative generation target, not a sampled value.</summary>
    public readonly struct GenerationRange
    {
        /// <summary>Creates an inclusive range; invalid ranges are rejected rather than clamped.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A bound is negative or maximum is below minimum.</exception>
        public GenerationRange(int minimum, int maximum)
        {
            if (minimum < 0 || maximum < minimum)
                throw new ArgumentOutOfRangeException(nameof(minimum));
            Minimum = minimum;
            Maximum = maximum;
        }

        /// <summary>Gets the inclusive lower target.</summary>
        public int Minimum { get; }

        /// <summary>Gets the inclusive upper target.</summary>
        public int Maximum { get; }
    }

    /// <summary>Associates an initial item-count target with a stable resource family.</summary>
    public sealed class ResourceGenerationBudget
    {
        /// <summary>Creates a family-specific target; counts are items, not food restoration or stacks.</summary>
        /// <exception cref="ArgumentException">The family ID is empty or has surrounding whitespace.</exception>
        public ResourceGenerationBudget(string resourceFamilyId, GenerationRange itemCount)
        {
            GenerationContract.RequireId(resourceFamilyId, nameof(resourceFamilyId));
            if (itemCount.Maximum > 64)
                throw new ArgumentOutOfRangeException(nameof(itemCount));
            ResourceFamilyId = resourceFamilyId;
            ItemCount = itemCount;
        }

        /// <summary>Gets the stable content key, not a localized label.</summary>
        public string ResourceFamilyId { get; }

        /// <summary>Gets the inclusive number of initially placed items in this family, including hidden items.</summary>
        public GenerationRange ItemCount { get; }
    }

    /// <summary>
    /// Defines immutable generation targets and allowed content for an ordinary 8x8 board.
    /// Targets describe intended pressure; constructing a config does not generate or validate a layout.
    /// </summary>
    public sealed class BoardGenerationConfig
    {
        /// <summary>Creates detached, deterministically ordered generation targets.</summary>
        /// <param name="id">Stable configuration key matching BoardAddress.</param>
        /// <param name="version">Positive generation contract version matching BoardAddress.</param>
        /// <param name="pathLength">Shortest required start-to-exit path length in cardinal moves, ignoring actors.</param>
        /// <param name="branchPoints">Traversable cells with at least three traversable cardinal neighbors, ignoring actors.</param>
        /// <param name="obstacleCount">Number of occupied obstacle-layer cells.</param>
        /// <param name="enemyCount">Number of initially placed enemy actors.</param>
        /// <param name="maximumConcurrentThreats">Upper target for distinct enemies threatening the same cell in one intent phase.</param>
        /// <param name="minimumSafeStartMoves">Minimum legal, non-threatened cardinal destinations from the initial start.</param>
        /// <param name="allowedContentIds">Stable entity content IDs permitted by this configuration, including item definition IDs.</param>
        /// <param name="resourceBudgets">At most one initial item-count range per resource family.</param>
        /// <exception cref="ArgumentNullException">A collection is null.</exception>
        /// <exception cref="ArgumentException">An ID is invalid or a content/family ID is duplicated.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A target exceeds the capacity of an ordinary board.</exception>
        public BoardGenerationConfig(string id, int version, GenerationRange pathLength,
            GenerationRange branchPoints, GenerationRange obstacleCount, GenerationRange enemyCount,
            int maximumConcurrentThreats, int minimumSafeStartMoves, IEnumerable<string> allowedContentIds,
            IEnumerable<ResourceGenerationBudget> resourceBudgets)
        {
            GenerationContract.RequireId(id, nameof(id));
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
            if (pathLength.Minimum < 1 || pathLength.Maximum > 63 || branchPoints.Maximum > 64 ||
                obstacleCount.Maximum > 64 || enemyCount.Maximum > 63 ||
                maximumConcurrentThreats < 0 || maximumConcurrentThreats > enemyCount.Maximum ||
                minimumSafeStartMoves < 0 || minimumSafeStartMoves > 4)
                throw new ArgumentOutOfRangeException(nameof(pathLength), "Targets exceed ordinary 8x8 board capacity.");
            if (allowedContentIds == null) throw new ArgumentNullException(nameof(allowedContentIds));
            if (resourceBudgets == null) throw new ArgumentNullException(nameof(resourceBudgets));

            Id = id;
            Version = version;
            PathLength = pathLength;
            BranchPoints = branchPoints;
            ObstacleCount = obstacleCount;
            EnemyCount = enemyCount;
            MaximumConcurrentThreats = maximumConcurrentThreats;
            MinimumSafeStartMoves = minimumSafeStartMoves;

            var content = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string contentId in allowedContentIds)
            {
                GenerationContract.RequireId(contentId, nameof(allowedContentIds));
                if (!content.Add(contentId))
                    throw new ArgumentException("Allowed content IDs must be unique.", nameof(allowedContentIds));
            }
            if (content.Count == 0)
                throw new ArgumentException("At least one allowed content ID is required.", nameof(allowedContentIds));
            AllowedContentIds = new List<string>(content).AsReadOnly();

            var budgets = new SortedDictionary<string, ResourceGenerationBudget>(StringComparer.Ordinal);
            foreach (ResourceGenerationBudget budget in resourceBudgets)
            {
                if (budget == null || budgets.ContainsKey(budget.ResourceFamilyId))
                    throw new ArgumentException("Resource budgets must be non-null and unique by family.", nameof(resourceBudgets));
                budgets.Add(budget.ResourceFamilyId, budget);
            }
            ResourceBudgets = new List<ResourceGenerationBudget>(budgets.Values).AsReadOnly();
        }

        /// <summary>Gets the stable generation configuration key.</summary>
        public string Id { get; }
        /// <summary>Gets the positive generation contract version.</summary>
        public int Version { get; }
        /// <summary>Gets the target shortest cardinal path length, ignoring actors.</summary>
        public GenerationRange PathLength { get; }
        /// <summary>Gets the target count of traversable cells with at least three traversable neighbors.</summary>
        public GenerationRange BranchPoints { get; }
        /// <summary>Gets the target obstacle-layer occupancy count.</summary>
        public GenerationRange ObstacleCount { get; }
        /// <summary>Gets the target initial enemy count; this is not the sole difficulty control.</summary>
        public GenerationRange EnemyCount { get; }
        /// <summary>Gets the upper target for enemies threatening a single cell in one intent phase.</summary>
        public int MaximumConcurrentThreats { get; }
        /// <summary>Gets the lower target for legal, non-threatened first-step destinations.</summary>
        public int MinimumSafeStartMoves { get; }
        /// <summary>Gets allowed entity/item definition IDs in ordinal order.</summary>
        public IReadOnlyList<string> AllowedContentIds { get; }
        /// <summary>Gets initial resource targets in ordinal family order.</summary>
        public IReadOnlyList<ResourceGenerationBudget> ResourceBudgets { get; }
    }

    internal static class GenerationContract
    {
        internal static void RequireId(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("A stable ID must be non-empty and have no surrounding whitespace.", parameter);
        }
    }
}