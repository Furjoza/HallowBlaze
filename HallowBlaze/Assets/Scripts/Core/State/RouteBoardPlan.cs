using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.State
{
    /// <summary>Retains one exact authored choice, independently of mutable board state and legacy landmark records.</summary>
    public sealed class RouteBoardSelection
    {
        /// <summary>Validates a fixed choice or a choice from one immutable pool publication.</summary>
        /// <param name="poolId">Null for a fixed assignment; otherwise the exact pool identity.</param>
        /// <param name="poolVersion">Zero for a fixed assignment; otherwise a positive content version.</param>
        /// <param name="poolHash">Null for a fixed assignment; otherwise its lowercase SHA-256 content hash.</param>
        public RouteBoardSelection(string segmentId, string configurationId, int configurationVersion,
            string templateId, int templateVersion, string variantId, string templateHash,
            string poolId = null, int poolVersion = 0, string poolHash = null)
        {
            TravelHistoryEntry.RequireId(segmentId, nameof(segmentId));
            TravelHistoryEntry.RequireId(configurationId, nameof(configurationId));
            TravelHistoryEntry.RequireId(templateId, nameof(templateId));
            TravelHistoryEntry.RequireId(variantId, nameof(variantId));
            if (configurationVersion < 1 || templateVersion < 1) throw new ArgumentException("Content versions must be positive.");
            RouteBoardPlan.RequireHash(templateHash);
            if (poolId == null)
            {
                if (poolVersion != 0 || poolHash != null) throw new ArgumentException("A fixed choice cannot carry pool metadata.");
            }
            else
            {
                TravelHistoryEntry.RequireId(poolId, nameof(poolId));
                if (poolVersion < 1) throw new ArgumentException("Pool version must be positive.");
                RouteBoardPlan.RequireHash(poolHash);
            }
            SegmentId = segmentId; ConfigurationId = configurationId; ConfigurationVersion = configurationVersion;
            TemplateId = templateId; TemplateVersion = templateVersion; VariantId = variantId; TemplateHash = templateHash;
            PoolId = poolId; PoolVersion = poolVersion; PoolHash = poolHash;
        }

        /// <summary>Gets the stable segment key, never its list position.</summary>
        public string SegmentId { get; }
        /// <summary>Gets the retained segment generation family.</summary>
        public string ConfigurationId { get; }
        /// <summary>Gets the retained segment generation version.</summary>
        public int ConfigurationVersion { get; }
        /// <summary>Gets the exact selected template identity.</summary>
        public string TemplateId { get; }
        /// <summary>Gets the exact selected template content version.</summary>
        public int TemplateVersion { get; }
        /// <summary>Gets the explicit selected variant, with no further selection at composition.</summary>
        public string VariantId { get; }
        /// <summary>Gets the immutable template content fingerprint.</summary>
        public string TemplateHash { get; }
        /// <summary>Gets the source pool identity, or null for a fixed assignment.</summary>
        public string PoolId { get; }
        /// <summary>Gets the source pool version, or zero for a fixed assignment.</summary>
        public int PoolVersion { get; }
        /// <summary>Gets the source pool fingerprint, or null for a fixed assignment.</summary>
        public string PoolHash { get; }
    }

    /// <summary>Immutable choices for a complete leg, including unvisited segments; no cursor or RNG state is shared.</summary>
    public sealed class RouteBoardPlan
    {
        /// <summary>Identifies the canonical-order, addressed PCG32 segment-selection policy.</summary>
        public const string PolicyId = "hallowblaze.segment-pool.v1";
        /// <summary>Versions entry ordering, stream identity and equal-probability selection together.</summary>
        public const int PolicyVersion = 1;

        /// <summary>Validates publication provenance and copies unique choices before they can be checkpointed.</summary>
        /// <exception cref="ArgumentException">Identity, policy, fingerprint or entry membership is invalid.</exception>
        public RouteBoardPlan(string runId, int runSeed, string legId, string edgeId, string worldId, int worldVersion,
            string publicationId, int publicationVersion, string publicationHash, IEnumerable<RouteBoardSelection> selections,
            string policyId = PolicyId, int policyVersion = PolicyVersion)
        {
            foreach (string id in new[] { runId, legId, edgeId, worldId, publicationId }) TravelHistoryEntry.RequireId(id, nameof(id));
            if (worldVersion < 1 || publicationVersion < 1 || policyId != PolicyId || policyVersion != PolicyVersion)
                throw new ArgumentException("Unsupported publication version or selection policy.");
            RequireHash(publicationHash);
            if (selections == null) throw new ArgumentNullException(nameof(selections));
            var copy = new List<RouteBoardSelection>(selections);
            var segments = new HashSet<string>(StringComparer.Ordinal);
            foreach (RouteBoardSelection selection in copy)
                if (selection == null || !segments.Add(selection.SegmentId)) throw new ArgumentException("Selections must have unique non-null segments.");
            RunId = runId; RunSeed = runSeed; LegId = legId; EdgeId = edgeId; WorldId = worldId; WorldVersion = worldVersion;
            PublicationId = publicationId; PublicationVersion = publicationVersion; PublicationHash = publicationHash;
            SelectionPolicyId = policyId; SelectionPolicyVersion = policyVersion; Selections = copy.AsReadOnly();
        }

        /// <summary>Gets the owning run identity.</summary>
        public string RunId { get; }
        /// <summary>Gets the root seed used for this leg's immutable selection.</summary>
        public int RunSeed { get; }
        /// <summary>Gets the exact run-local traversal identity.</summary>
        public string LegId { get; }
        /// <summary>Gets the directed route identity.</summary>
        public string EdgeId { get; }
        /// <summary>Gets the captured world publication identity.</summary>
        public string WorldId { get; }
        /// <summary>Gets the captured world publication version.</summary>
        public int WorldVersion { get; }
        /// <summary>Gets the retained route publication identity.</summary>
        public string PublicationId { get; }
        /// <summary>Gets the retained route publication version.</summary>
        public int PublicationVersion { get; }
        /// <summary>Gets the complete publication fingerprint, including candidates not selected.</summary>
        public string PublicationHash { get; }
        /// <summary>Gets the frozen selection algorithm identity.</summary>
        public string SelectionPolicyId { get; }
        /// <summary>Gets the frozen selection algorithm version.</summary>
        public int SelectionPolicyVersion { get; }
        /// <summary>Gets detached exact selections; omitted segments retain their captured generation identity.</summary>
        public IReadOnlyList<RouteBoardSelection> Selections { get; }

        internal static void RequireHash(string hash)
        {
            if (hash == null || hash.Length != 64) throw new ArgumentException("A lowercase SHA-256 hash is required.");
            foreach (char digit in hash)
                if (!(digit >= '0' && digit <= '9') && !(digit >= 'a' && digit <= 'f')) throw new ArgumentException("Invalid SHA-256 hash.");
        }
    }
}