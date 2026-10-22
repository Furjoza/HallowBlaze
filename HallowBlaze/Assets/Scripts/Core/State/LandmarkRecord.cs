using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.State
{
    /// <summary>Persists an exact authored board selection or completed exit result, never a mid-board snapshot.</summary>
    public sealed class LandmarkRecord
    {
        /// <summary>Creates immutable identity data; null outcome means selected but not completed.</summary>
        public LandmarkRecord(BoardAddress address, string templateId, int templateVersion, string variantId, string outcomeId = null)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
            RequireId(templateId);
            RequireId(variantId);
            if (outcomeId != null) RequireId(outcomeId);
            if (templateVersion <= 0) throw new ArgumentOutOfRangeException(nameof(templateVersion));
            TemplateId = templateId;
            TemplateVersion = templateVersion;
            VariantId = variantId;
            OutcomeId = outcomeId;
        }

        /// <summary>Gets the complete segment address, including run/traversal/seed/configuration.</summary>
        public BoardAddress Address { get; }
        /// <summary>Gets the migration-sensitive template key.</summary>
        public string TemplateId { get; }
        /// <summary>Gets the selected content version, not the save version.</summary>
        public int TemplateVersion { get; }
        /// <summary>Gets the exact selected variant; Continue must not reroll it.</summary>
        public string VariantId { get; }
        /// <summary>Gets the completed result or null for an active selection.</summary>
        public string OutcomeId { get; }

        internal bool SameSelection(LandmarkRecord other) => other != null && Address.Equals(other.Address) &&
            TemplateId == other.TemplateId && TemplateVersion == other.TemplateVersion && VariantId == other.VariantId;

        private static void RequireId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) throw new ArgumentException("A stable landmark ID is required.");
        }
    }

    public sealed partial class RunState
    {
        private readonly List<LandmarkRecord> completedLandmarks = new List<LandmarkRecord>();

        /// <summary>Gets the durably selected authored board, or null for ordinary travel and completed boundaries.</summary>
        public LandmarkRecord ActiveLandmark { get; private set; }
        /// <summary>Gets completed authored outcomes in travel order; repeated callbacks cannot append the same board twice.</summary>
        public IReadOnlyList<LandmarkRecord> CompletedLandmarks => completedLandmarks.AsReadOnly();

        /// <summary>Selects an exact current board once without resources or arrival effects; Session must checkpoint before rendering.</summary>
        /// <returns>False without mutation for a foreign/completed selection or a conflicting existing selection.</returns>
        public bool TrySelectLandmark(LandmarkRecord selection)
        {
            if (Status != RunStatus.Active || selection == null || selection.OutcomeId != null || !MatchesLandmarkAddress(selection.Address)) return false;
            if (ActiveLandmark != null) return ActiveLandmark.SameSelection(selection);
            foreach (LandmarkRecord completed in completedLandmarks) if (completed.Address.Equals(selection.Address)) return false;
            ActiveLandmark = selection;
            return true;
        }

        /// <summary>Records one trusted resolver exit against the current selection; travel completion and saving form the same Session boundary.</summary>
        /// <returns>False without mutation for a stale/duplicate/foreign result.</returns>
        public bool TryCompleteLandmark(LandmarkRecord result)
        {
            if (Status != RunStatus.Active || result?.OutcomeId == null || ActiveLandmark == null ||
                !ActiveLandmark.SameSelection(result) || !MatchesLandmarkAddress(result.Address)) return false;
            foreach (LandmarkRecord completed in completedLandmarks) if (completed.Address.Equals(result.Address)) return false;
            completedLandmarks.Add(result);
            ActiveLandmark = null;
            return true;
        }

        /// <summary>Restores validated boundary data atomically, preserving unknown stable content IDs without substituting or rerolling.</summary>
        /// <exception cref="ArgumentException">Active identity, completed travel association, ordering or uniqueness is invalid.</exception>
        public void RestoreLandmarkState(LandmarkRecord active, IEnumerable<LandmarkRecord> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            if (active != null && (active.OutcomeId != null || !MatchesLandmarkAddress(active.Address)))
                throw new ArgumentException("Active landmark does not match the current segment.");
            var restored = new List<LandmarkRecord>();
            var addresses = new HashSet<BoardAddress>();
            int previousDay = 0;
            foreach (LandmarkRecord record in completed)
            {
                if (record?.OutcomeId == null || record.Address.RunId != RunId || !addresses.Add(record.Address) ||
                    record.Address.DayNumber <= previousDay) throw new ArgumentException("Invalid completed landmark history.");
                bool matched = false;
                foreach (TravelHistoryEntry entry in travelHistory)
                    if (entry.LegId == record.Address.LegId && entry.EdgeId == record.Address.EdgeId && entry.DayNumber == record.Address.DayNumber)
                        matched = true;
                if (!matched) throw new ArgumentException("Landmark result has no completed travel boundary.");
                previousDay = record.Address.DayNumber;
                restored.Add(record);
            }
            if (active != null && addresses.Contains(active.Address)) throw new ArgumentException("Completed landmark cannot be active.");
            ActiveLandmark = active;
            completedLandmarks.Clear();
            completedLandmarks.AddRange(restored);
        }

        private bool MatchesLandmarkAddress(BoardAddress address) => ActiveLeg != null && ActiveLeg.IsFinalSegment &&
            ActiveLeg.IntroState == DayIntroState.Consumed &&
            address.RunId == RunId && address.LegId == ActiveLeg.LegId && address.EdgeId == ActiveLeg.EdgeId &&
            address.DayNumber == ActiveLeg.DayNumber && address.SegmentId == ActiveLeg.CurrentSegment.SegmentId &&
            address.GenerationConfigurationId == ActiveLeg.CurrentSegment.GenerationConfigurationId &&
            address.GenerationVersion == ActiveLeg.CurrentSegment.GenerationVersion;
    }
}