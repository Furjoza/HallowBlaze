using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HallowBlaze.Core.Board.State;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Immutable opt-in rules for soft obstacles; manual clearing never requires a tool or knowledge unlock.</summary>
    public sealed class ObstacleClearingRules
    {
        /// <summary>Stable tool definition used for chopping, distinct from instance and authored source IDs.</summary>
        public const string AxeDefinitionId = "axe.basic";
        /// <summary>Stable observable fact; documenting it never enables the underlying action.</summary>
        public const string AxeNoiseFactId = "AXE_CREATES_LOUD_NOISE";
        private readonly HashSet<string> obstacleIds;

        /// <summary>Creates rules that make every fresh supported obstacle slower by hand than with one axe use.</summary>
        /// <param name="obstacleContentIds">Explicit soft obstacle definitions; no other tree, actor or terrain is affected.</param>
        /// <param name="requiredWork">At least two manual actions; one axe action supplies all required work.</param>
        /// <param name="manualNoiseStrength">Nonnegative manual emission strength.</param>
        /// <param name="axeNoiseStrength">Axe emission strength, strictly greater than the manual strength.</param>
        /// <param name="noiseLifetimeTurns">Positive lifetime shared by both emissions.</param>
        public ObstacleClearingRules(IEnumerable<string> obstacleContentIds, int requiredWork,
            int manualNoiseStrength, int axeNoiseStrength, int noiseLifetimeTurns)
        {
            if (obstacleContentIds == null) throw new ArgumentNullException(nameof(obstacleContentIds));
            if (requiredWork < 2) throw new ArgumentOutOfRangeException(nameof(requiredWork));
            if (manualNoiseStrength < 0) throw new ArgumentOutOfRangeException(nameof(manualNoiseStrength));
            if (axeNoiseStrength <= manualNoiseStrength) throw new ArgumentOutOfRangeException(nameof(axeNoiseStrength));
            if (noiseLifetimeTurns < 1) throw new ArgumentOutOfRangeException(nameof(noiseLifetimeTurns));
            obstacleIds = new HashSet<string>(StringComparer.Ordinal);
            var ids = new List<string>();
            foreach (string id in obstacleContentIds)
            {
                if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || !obstacleIds.Add(id))
                    throw new ArgumentException("Soft obstacle IDs must be stable and unique.", nameof(obstacleContentIds));
                ids.Add(id);
            }
            if (ids.Count == 0) throw new ArgumentException("At least one soft obstacle is required.", nameof(obstacleContentIds));
            ObstacleContentIds = new ReadOnlyCollection<string>(ids);
            RequiredWork = requiredWork;
            ManualNoiseStrength = manualNoiseStrength;
            AxeNoiseStrength = axeNoiseStrength;
            NoiseLifetimeTurns = noiseLifetimeTurns;
        }

        /// <summary>Gets the detached opt-in content list; boundary decorations and unrelated obstacles are excluded.</summary>
        public IReadOnlyList<string> ObstacleContentIds { get; }
        /// <summary>Gets manual actions for a fresh obstacle; one axe use completes the same work.</summary>
        public int RequiredWork { get; }
        /// <summary>Gets the smaller path-budget strength for one manual action.</summary>
        public int ManualNoiseStrength { get; }
        /// <summary>Gets the larger path-budget strength for one axe action.</summary>
        public int AxeNoiseStrength { get; }
        /// <summary>Gets accepted subsequent commands before the new noise expires.</summary>
        public int NoiseLifetimeTurns { get; }

        /// <summary>Tests exact opt-in obstacle content without consulting profile facts or mutable view components.</summary>
        public bool Supports(BoardEntityState target) => target != null && target.Definition.Layer == BoardLayer.Obstacle &&
            !target.Definition.Traits.IsWalkable && obstacleIds.Contains(target.Definition.ContentId);
    }
}