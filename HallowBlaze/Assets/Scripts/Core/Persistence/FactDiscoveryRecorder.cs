using System;
using System.Collections.Generic;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence
{
    /// <summary>Checkpoints new mechanical discoveries before feedback, retrying failed writes without creating duplicate discovery events.</summary>
    public sealed class FactDiscoveryRecorder
    {
        private readonly ProfileState profile;
        private readonly FactKnowledge knowledge;
        private readonly Func<ProfileState, SaveStoreResult> save;
        private readonly List<FactDiscovery> pending = new List<FactDiscovery>();

        /// <summary>Binds one profile and immutable fact registry to its existing profile checkpoint; no run or source-location data is written.</summary>
        public FactDiscoveryRecorder(ProfileState profile, FactKnowledge knowledge, Func<ProfileState, SaveStoreResult> save)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            this.save = save ?? throw new ArgumentNullException(nameof(save));
        }

        /// <summary>Records an actual presented observation and returns only discoveries whose checkpoint has succeeded.</summary>
        /// <remarks>A failed write leaves pending events available for the next observation or explicit retry; repeated observations add no second event.</remarks>
        public IReadOnlyList<FactDiscovery> Observe(FactObservation observation)
        {
            pending.AddRange(knowledge.Observe(profile, observation));
            return CommitPending();
        }

        /// <summary>Retries only already observed facts; it cannot discover a new rule or infer an observation from opening notes.</summary>
        /// <exception cref="InvalidOperationException">The profile checkpoint failed; no successful discovery feedback is returned.</exception>
        public IReadOnlyList<FactDiscovery> CommitPending()
        {
            if (pending.Count == 0) return Array.Empty<FactDiscovery>();
            SaveStoreResult result = save(profile);
            if (result == null || result.IsFailure)
                throw new InvalidOperationException("Observed fact checkpoint failed: " + result?.ErrorMessage);
            var committed = pending.ToArray();
            pending.Clear();
            return Array.AsReadOnly(committed);
        }
    }
}