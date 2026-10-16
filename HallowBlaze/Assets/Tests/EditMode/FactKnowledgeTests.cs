using System;
using System.Linq;
using System.IO;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies explicit observation, idempotent durable discovery and spoiler-safe missing/unknown projections.</summary>
    public sealed class FactKnowledgeTests
    {
        /// <summary>Only the matching presented consequence records a fact; preview/unsupported/repeated observations do not.</summary>
        [Test]
        public void ExplicitObservationRecordsFactExactlyOnce()
        {
            var profile = new ProfileState("notes", "forest", 1);
            FactKnowledge knowledge = Knowledge();
            Assert.That(knowledge.Observe(profile, FactObservation.None), Is.Empty);
            Assert.That(knowledge.Observe(profile, (FactObservation)999), Is.Empty);
            Assert.That(knowledge.Observe(profile, FactObservation.ShamblerTrappedPresented), Is.Empty);
            Assert.That(profile.DiscoveredFactIds, Is.Empty);
            FactDiscovery discovery = knowledge.Observe(profile, FactObservation.AxeNoisePresented).Single();
            Assert.That(discovery.FactId, Is.EqualTo("AXE_CREATES_LOUD_NOISE"));
            Assert.That(discovery.Observation, Is.EqualTo(FactObservation.AxeNoisePresented));
            Assert.That(knowledge.Observe(profile, FactObservation.AxeNoisePresented), Is.Empty);
            Assert.That(profile.DiscoveredFactIds, Is.EqualTo(new[] { discovery.FactId }));
        }

        /// <summary>Unknown content has no fact-specific text, icon, ID or provenance, and never appears as a checklist.</summary>
        [Test]
        public void UnknownProjectionCannotRevealFactContent()
        {
            var profile = new ProfileState("notes", "forest", 1);
            FactKnowledge knowledge = Knowledge();
            FieldNoteEntry entry = knowledge.GetEntry(profile, "AXE_CREATES_LOUD_NOISE");
            Assert.That(entry.IsDiscovered, Is.False);
            Assert.That(entry.FactId, Is.Null);
            Assert.That(entry.Title, Is.EqualTo("Unknown observation"));
            Assert.That(entry.Description, Is.Empty);
            Assert.That(entry.IconId, Is.EqualTo("unknown"));
            Assert.That(entry.SourceSystem, Is.Null);
            Assert.That(knowledge.GetEntries(profile), Is.Empty);
            Assert.That(profile.DiscoveredFactIds, Is.Empty);
        }

        /// <summary>Saved unknown IDs keep their place and ownership even when a definition disappears or later returns.</summary>
        [Test]
        public void MissingDefinitionUsesPlaceholderWithoutDiscardingSavedIdentity()
        {
            var profile = new ProfileState("notes", "forest", 1);
            profile.DiscoverFact("future.fact");
            FieldNoteEntry missing = Knowledge().GetEntries(profile).Single();
            Assert.That(missing.IsMissingDefinition, Is.True);
            Assert.That(missing.Title, Is.EqualTo("Uncatalogued observation"));
            var restored = new FactKnowledge(new[] { new FactDefinition("future.fact", FactObservation.AxeNoisePresented,
                "Restored title", "Restored text", "axe", "Tools") });
            Assert.That(restored.GetEntries(profile).Single().Title, Is.EqualTo("Restored title"));
            Assert.That(profile.DiscoveredFactIds, Is.EqualTo(new[] { "future.fact" }));
        }

        /// <summary>Malformed registries cannot partially register content, and no definition can use an unspecified trigger.</summary>
        [Test]
        public void InvalidDefinitionsAndDuplicatesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FactDefinition("id", FactObservation.None, "Title", "Text", "axe", "Tools"));
            var definition = new FactDefinition("id", FactObservation.AxeNoisePresented, "Title", "Text", "axe", "Tools");
            Assert.Throws<ArgumentException>(() => new FactKnowledge(new[] { definition, definition }));
            Assert.Throws<ArgumentException>(() => new FactKnowledge(new FactDefinition[] { null }));
        }

        /// <summary>Authored facts use explicit mechanical triggers and independent stable IDs; no tool-source event is a trigger.</summary>
        [Test]
        public void AuthoredRegistryMapsThreeDistinctPresentedConsequences()
        {
            FactKnowledge knowledge = FactContentCatalog.Load();
            var profile = new ProfileState("content", "forest", 1);
            Assert.That(knowledge.Observe(profile, FactObservation.None), Is.Empty);
            Assert.That(knowledge.Observe(profile, FactObservation.AxeNoisePresented).Single().FactId, Is.EqualTo("AXE_CREATES_LOUD_NOISE"));
            Assert.That(knowledge.Observe(profile, FactObservation.ShamblerTrappedPresented).Single().FactId, Is.EqualTo("PIT_TRAPS_SHAMBLER"));
            Assert.That(knowledge.Observe(profile, FactObservation.ListenerInvestigationPresented).Single().FactId, Is.EqualTo("LISTENER_FOLLOWS_NOISE"));
            Assert.That(knowledge.GetEntries(profile).Select(entry => entry.IconId), Is.EqualTo(new[] { "axe", "pit", "hearing" }));
            Assert.That(knowledge.Observe(profile, FactObservation.ListenerInvestigationPresented), Is.Empty);
            Assert.Throws<ArgumentException>(() => FactContentCatalog.Read("{}"));
            Assert.Throws<ArgumentException>(() => FactContentCatalog.Read("{\"facts\":[{\"id\":\"source\",\"observation\":\"ToolSourceSighted\",\"iconId\":\"axe\"}]}"));
        }

        /// <summary>Successful checkpoints alone release discovery feedback, retrying a failed write exactly once without losing the observed ID.</summary>
        [Test]
        public void FailedCheckpointRetriesPendingDiscoveryWithoutDuplicateFeedback()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M57-facts-" + Guid.NewGuid().ToString("N"));
            try
            {
                var profile = new ProfileState("notes", "forest", 1);
                var store = new FileSystemSaveStore(root);
                bool fail = true;
                int writes = 0;
                var recorder = new FactDiscoveryRecorder(profile, Knowledge(), current =>
                {
                    writes++;
                    return fail ? SaveStoreResult.IoError("Controlled test failure") : store.SaveProfile(current);
                });
                Assert.Throws<InvalidOperationException>(() => recorder.Observe(FactObservation.AxeNoisePresented));
                Assert.That(profile.DiscoveredFactIds, Has.Count.EqualTo(1));
                Assert.That(store.LoadProfile().Type, Is.EqualTo(SaveStoreResultType.Missing));
                fail = false;
                Assert.That(recorder.Observe(FactObservation.AxeNoisePresented).Single().FactId, Is.EqualTo("AXE_CREATES_LOUD_NOISE"));
                Assert.That(recorder.Observe(FactObservation.AxeNoisePresented), Is.Empty);
                Assert.That(recorder.CommitPending(), Is.Empty);
                Assert.That(writes, Is.EqualTo(2));
                ProfileState restored = store.LoadProfile().Data;
                Assert.That(restored.DiscoveredFactIds, Is.EqualTo(profile.DiscoveredFactIds));
                Assert.That(new FactDiscoveryRecorder(restored, Knowledge(), store.SaveProfile).Observe(FactObservation.AxeNoisePresented), Is.Empty);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        /// <summary>Existing profile schema stores only stable fact IDs, retaining missing content across reload and a new run.</summary>
        [Test]
        public void ProfileRoundTripAndNewRunPreserveKnownAndMissingFactIds()
        {
            var profile = new ProfileState("notes", "forest", 1);
            Knowledge().Observe(profile, FactObservation.AxeNoisePresented);
            profile.DiscoverFact("removed.fact");
            string json = PersistenceJsonSerializer.SerializeProfile(profile);
            Assert.That(json, Does.Contain("\"schemaVersion\": 2"));
            Assert.That(json, Does.Not.Contain("A loud shortcut").And.Not.Contain("AxeNoisePresented"));
            ProfileState restored = PersistenceJsonSerializer.DeserializeProfile(json);
            var session = new HallowBlaze.Core.Session.GameSession(restored);
            session.StartNewRun("first", 57, new RunStateConfiguration(100, 20, 0, "forest.start"));
            session.ActiveRun.MarkDead();
            session.StartNewRun("second", 58, new RunStateConfiguration(100, 20, 0, "forest.start"));
            Assert.That(restored.DiscoveredFactIds, Is.EqualTo(profile.DiscoveredFactIds));
            Assert.That(Knowledge().GetEntries(restored).Last().IsMissingDefinition, Is.True);
        }

        /// <summary>The pre-Field-Notes v2 profile needs no schema migration or ID rewrite and does not replay an old discovery notification.</summary>
        [Test]
        public void ExistingProfileV2FactsBecomeNotesWithoutRediscoveryOrSchemaChange()
        {
            const string existing = "{\"schemaVersion\":2,\"profileId\":\"existing\",\"worldDefinitionId\":\"forest\",\"worldDefinitionVersion\":1," +
                "\"nodeDiscoveries\":[],\"edgeDiscoveries\":[],\"discoveredFactIds\":[\"AXE_CREATES_LOUD_NOISE\",\"PIT_TRAPS_SHAMBLER\",\"unavailable.fact\"]," +
                "\"persistentNoteIds\":[],\"runSummaries\":[]}";
            ProfileState profile = PersistenceJsonSerializer.DeserializeProfile(existing);
            FactKnowledge knowledge = FactContentCatalog.Load();
            int writes = 0;
            var recorder = new FactDiscoveryRecorder(profile, knowledge, _ => { writes++; return SaveStoreResult.Success(); });
            Assert.That(recorder.Observe(FactObservation.AxeNoisePresented), Is.Empty);
            Assert.That(recorder.Observe(FactObservation.ShamblerTrappedPresented), Is.Empty);
            Assert.That(writes, Is.Zero);
            Assert.That(knowledge.GetEntries(profile).Select(entry => entry.Title), Is.EqualTo(new[]
                { "A loud shortcut", "Held by the ground", "Uncatalogued observation" }));
            Assert.That(knowledge.GetEntries(profile).Last().IsMissingDefinition, Is.True);
            Assert.That(PersistenceJsonSerializer.DeserializeProfile(PersistenceJsonSerializer.SerializeProfile(profile)).DiscoveredFactIds,
                Is.EqualTo(profile.DiscoveredFactIds));
        }

        private static FactKnowledge Knowledge() => new FactKnowledge(new[]
        {
            new FactDefinition("AXE_CREATES_LOUD_NOISE", FactObservation.AxeNoisePresented, "A loud shortcut",
                "Using an axe makes a loud noise.", "axe", "Tools")
        });
    }
}