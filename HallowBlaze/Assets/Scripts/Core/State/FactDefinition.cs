using System;

namespace HallowBlaze.Core.State
{
    /// <summary>Explicitly presented mechanical consequences, separate from authored tool-source location knowledge.</summary>
    public enum FactObservation
    {
        /// <summary>No observed consequence; previews, cancellation and unsupported signals cannot discover facts.</summary>
        None = 0,
        /// <summary>The actual loud axe emission has been presented.</summary>
        AxeNoisePresented = 1,
        /// <summary>An actual Shambler entry and resulting pit hold have been presented.</summary>
        ShamblerTrappedPresented = 2,
        /// <summary>A Listener's actual announced investigation step toward a live noise has been presented.</summary>
        ListenerInvestigationPresented = 3
    }

    /// <summary>Immutable working-localization content documenting a rule that already operates without player knowledge.</summary>
    public sealed class FactDefinition
    {
        /// <summary>Creates one fact with an explicit observation condition; source system is documentation, not an atlas source ID.</summary>
        public FactDefinition(string id, FactObservation observation, string title, string description, string iconId, string sourceSystem)
        {
            Require(id, nameof(id));
            Require(title, nameof(title));
            Require(description, nameof(description));
            Require(iconId, nameof(iconId));
            Require(sourceSystem, nameof(sourceSystem));
            if (observation == FactObservation.None || !Enum.IsDefined(typeof(FactObservation), observation))
                throw new ArgumentOutOfRangeException(nameof(observation));
            Id = id;
            Observation = observation;
            Title = title;
            Description = description;
            IconId = iconId;
            SourceSystem = sourceSystem;
        }
        /// <summary>Gets the stable profile key; localization never changes this identity.</summary>
        public string Id { get; }
        /// <summary>Gets the exact consequence required for discovery.</summary>
        public FactObservation Observation { get; }
        /// <summary>Gets the working localized title, available to the notes projection only after discovery.</summary>
        public string Title { get; }
        /// <summary>Gets the observed rule text; it does not enable or improve that rule.</summary>
        public string Description { get; }
        /// <summary>Gets the presentation icon key, hidden for undiscovered content.</summary>
        public string IconId { get; }
        /// <summary>Gets the owning mechanical system, not a map or tool-source location.</summary>
        public string SourceSystem { get; }

        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) throw new ArgumentException("Fact content is required.", name);
        }
    }

    /// <summary>A newly recorded mechanical fact; repeated observation returns no second discovery or feedback event.</summary>
    public sealed class FactDiscovery
    {
        internal FactDiscovery(FactDefinition definition) { FactId = definition.Id; Observation = definition.Observation; }
        /// <summary>Gets the stable fact ID newly owned by the profile.</summary>
        public string FactId { get; }
        /// <summary>Gets the presented mechanical consequence, never a tool-source sighting.</summary>
        public FactObservation Observation { get; }
    }
}