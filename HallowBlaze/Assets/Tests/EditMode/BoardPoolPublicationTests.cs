using System;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Mapping;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Checks exact segment-pool publication, durable choices and compatibility using detached disposable data.</summary>
    public sealed class BoardPoolPublicationTests
    {
        /// <summary>Intro and segment progress retain the entire plan; final arrival clears it with the active leg.</summary>
        [Test]
        public void BoardPlanRetainsUnvisitedChoicesThroughProgressAndClearsAtArrival()
        {
            var run = NewRun();
            run.StartLeg("road", "start", "goal", Segments());
            var plan = Plan(run);
            run.PinBoardPlan(plan);
            Assert.That(run.TryConsumeIntro(run.ActiveLeg.LegId), Is.True);
            Assert.That(run.ActiveLeg.BoardPlan, Is.SameAs(plan));
            Assert.That(run.TryAdvanceSegment(run.ActiveLeg.LegId, "entry"), Is.True);
            Assert.That(run.ActiveLeg.BoardPlan, Is.SameAs(plan));
            Assert.That(run.ActiveLeg.BoardPlan.Selections[0].SegmentId, Is.EqualTo("final"));
            Assert.That(() => run.PinBoardPlan(plan), Throws.InvalidOperationException);
            Assert.That(run.TryCompleteLeg(run.ActiveLeg.LegId, "final"), Is.True);
            Assert.That(run.ActiveLeg, Is.Null);
            Assert.That(run.CompletedDayCount, Is.EqualTo(1));
        }

        /// <summary>Foreign, duplicate and mismatched choices cannot mutate an unpinned active leg.</summary>
        [TestCase("run")]
        [TestCase("configuration")]
        [TestCase("duplicate")]
        public void InvalidBoardPlanDoesNotReplaceTravel(string fault)
        {
            var run = NewRun();
            run.StartLeg("road", "start", "goal", Segments());
            var before = run.ActiveLeg;
            Assert.That(() => run.PinBoardPlan(Plan(run, fault)), Throws.InstanceOf<ArgumentException>());
            Assert.That(run.ActiveLeg, Is.SameAs(before));
            Assert.That(run.ActiveLeg.BoardPlan, Is.Null);
        }

        /// <summary>Entry/file order and duplicate template copies do not affect canonical equal-probability selection or unrelated RNG.</summary>
        [Test]
        public void PublicationSelectsCanonicallyWithoutAdvancingUnrelatedStreams()
        {
            string worldJson = WorldJson();
            JObject manifest = Manifest();
            var publication = BoardPoolPublication.Read(worldJson, manifest.ToString(), Templates(), OrdinaryBoardContent.CreateAuthoringCatalog());
            ((JArray)manifest["sources"][0]["entries"]).Replace(new JArray(((JArray)manifest["sources"][0]["entries"]).Reverse()));
            var reordered = BoardPoolPublication.Read(worldJson, manifest.ToString(), Templates().Reverse().Concat(Templates().Take(1)), OrdinaryBoardContent.CreateAuthoringCatalog());
            Assert.That(reordered.ContentHash, Is.EqualTo(publication.ContentHash));
            Assert.That(reordered.ToJson(), Is.EqualTo(publication.ToJson()));
            var stream = new RandomStreamProvider(7).Create("unrelated");
            ulong before = stream.State;
            var variants = new System.Collections.Generic.HashSet<string>();
            for (int seed = 0; seed < 16; seed++)
            {
                var run = PublishedRun(seed);
                var selected = publication.Select(run.RunId, seed, run.ActiveLeg);
                var repeated = reordered.Select(run.RunId, seed, run.ActiveLeg);
                Assert.That(repeated.Selections[0].TemplateId, Is.EqualTo(selected.Selections[0].TemplateId));
                variants.Add(selected.Selections[0].TemplateId);
                run.PinBoardPlan(selected);
                Assert.That(publication.Resolve(run, Address(run)), Is.Null, "Unassigned entry must remain generated.");
                run.TryConsumeIntro(run.ActiveLeg.LegId);
                run.TryAdvanceSegment(run.ActiveLeg.LegId, run.ActiveLeg.CurrentSegment.SegmentId);
                var board = publication.Resolve(run, Address(run));
                Assert.That(board.TemplateId, Is.EqualTo(selected.Selections[0].TemplateId));
                Assert.That(reordered.Resolve(run, Address(run)).CreateState(), Is.Not.SameAs(board.CreateState()));
            }
            Assert.That(variants.Count, Is.EqualTo(2), "Fixed small seed fixtures must exercise both candidates.");
            Assert.That(stream.State, Is.EqualTo(before));
            Assert.That(stream.DrawCount, Is.Zero);
        }

        /// <summary>No configured source means generation, while one exact entry remains fixed.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedAndFixedSourcesHaveExplicitSemantics(bool fixedSource)
        {
            JObject manifest = Manifest();
            if (fixedSource)
            {
                manifest["sources"][0]["poolId"] = JValue.CreateNull();
                manifest["sources"][0]["poolVersion"] = 0;
                ((JArray)manifest["sources"][0]["entries"]).RemoveAt(1);
            }
            else manifest["sources"] = new JArray();
            var publication = BoardPoolPublication.Read(WorldJson(), manifest.ToString(), Templates(), OrdinaryBoardContent.CreateAuthoringCatalog());
            var run = PublishedRun(7);
            var plan = publication.Select(run.RunId, run.RunSeed, run.ActiveLeg);
            Assert.That(plan.Selections.Count, Is.EqualTo(fixedSource ? 1 : 0));
            if (fixedSource) Assert.That(plan.Selections[0].PoolId, Is.Null);
        }

        /// <summary>Full preflight refuses invalid candidates even before any seed can select a supposedly valid alternative.</summary>
        [TestCase("empty")]
        [TestCase("duplicate")]
        [TestCase("missing")]
        [TestCase("variant")]
        [TestCase("configuration")]
        [TestCase("duplicate-segment")]
        [TestCase("foreign")]
        [TestCase("scalar")]
        [TestCase("unknown-field")]
        [TestCase("conflicting-template")]
        public void PublicationPreflightsEveryCandidateBeforeSelecting(string fault)
        {
            JObject manifest = Manifest();
            var entries = (JArray)manifest["sources"][0]["entries"];
            string[] templates = Templates();
            if (fault == "empty") entries.Clear();
            if (fault == "duplicate") entries.Add(entries[0].DeepClone());
            if (fault == "missing") entries[1]["templateId"] = "missing";
            if (fault == "variant") entries[1]["variantId"] = "missing";
            if (fault == "configuration") manifest["sources"][0]["generationVersion"] = 99;
            if (fault == "duplicate-segment") ((JArray)manifest["sources"]).Add(manifest["sources"][0].DeepClone());
            if (fault == "foreign") manifest["edgeId"] = "road.start-east-creek";
            if (fault == "scalar") manifest["publicationVersion"] = "1";
            if (fault == "unknown-field") manifest["weights"] = new JArray(1, 1);
            if (fault == "conflicting-template")
            {
                JObject changed = JObject.Parse(templates[0]);
                changed["outcomes"][0]["name"] = "Conflicting payload";
                templates = templates.Concat(new[] { changed.ToString() }).ToArray();
            }
            Assert.That(() => BoardPoolPublication.Read(WorldJson(), manifest.ToString(), templates, OrdinaryBoardContent.CreateAuthoringCatalog()), Throws.Exception);
        }

        /// <summary>A different publication, even with the same textual ID/version, cannot reinterpret an existing selection plan.</summary>
        [Test]
        public void ChangedPublicationCannotResolvePinnedOldChoices()
        {
            var publication = BoardPoolPublication.Read(WorldJson(), Manifest().ToString(), Templates(), OrdinaryBoardContent.CreateAuthoringCatalog());
            var run = PublishedRun(7);
            Assert.That(() => publication.Resolve(run, Address(run)), Throws.InvalidOperationException);
            run.PinBoardPlan(publication.Select(run.RunId, run.RunSeed, run.ActiveLeg));
            JObject manifest = Manifest();
            ((JArray)manifest["sources"][0]["entries"]).RemoveAt(0);
            var changed = BoardPoolPublication.Read(WorldJson(), manifest.ToString(), Templates(), OrdinaryBoardContent.CreateAuthoringCatalog());
            Assert.That(changed.ContentHash, Is.Not.EqualTo(publication.ContentHash));
            Assert.That(() => changed.Resolve(run, Address(run)), Throws.InvalidOperationException);
            Assert.That(publication.Resolve(run, Address(run)), Is.Null);
        }

        /// <summary>Every active and unvisited choice survives v6 at Pending, intro-consumed and later segment boundaries.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SavedPlanRoundTripsWithoutRerollOrMutableAliasing(int progress)
        {
            var publication = BoardPoolPublication.Read(WorldJson(), Manifest().ToString(), Templates(), OrdinaryBoardContent.CreateAuthoringCatalog());
            var run = PublishedRun(7);
            run.PinBoardPlan(publication.Select(run.RunId, run.RunSeed, run.ActiveLeg));
            if (progress > 0) run.TryConsumeIntro(run.ActiveLeg.LegId);
            if (progress > 1) run.TryAdvanceSegment(run.ActiveLeg.LegId, run.ActiveLeg.CurrentSegment.SegmentId);
            var tool = run.CreateToolInstance(new ToolDefinition("axe.basic", "Axe", 3));
            run.EquipTool(0, tool);
            run.TrySpendToolCharge(0, tool.InstanceId);
            string json = PersistenceJsonSerializer.SerializeTravelRun(run);
            Assert.That((int)JObject.Parse(json)["schemaVersion"], Is.EqualTo(6));
            var restored = PersistenceJsonSerializer.DeserializeTravelRun(json);
            Assert.That(PersistenceJsonSerializer.SerializeTravelRun(restored), Is.EqualTo(json));
            Assert.That(restored.ActiveLeg.BoardPlan, Is.Not.SameAs(run.ActiveLeg.BoardPlan));
            Assert.That(Address(restored), Is.EqualTo(Address(run)));
            Assert.That(restored.Health, Is.EqualTo(run.Health));
            Assert.That(restored.Satiety, Is.EqualTo(run.Satiety));
            Assert.That(restored.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
            Assert.That(restored.ToolInstanceSequence, Is.EqualTo(run.ToolInstanceSequence));
            Assert.That(publication.Resolve(restored, Address(restored))?.TemplateId, Is.EqualTo(publication.Resolve(run, Address(run))?.TemplateId));
        }

        /// <summary>Old schemas keep generation/fixed-assignment meaning; reading never adds a pool to unvisited segments.</summary>
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void OlderTravelSchemasRemainWithoutPoolPlans(int schema)
        {
            JObject data = JObject.Parse(PersistenceJsonSerializer.SerializeTravelRun(PublishedRun(7)));
            data["schemaVersion"] = schema;
            data.Property("boardSelectionPlan").Remove();
            if (schema < 5) { data.Property("activeLandmark").Remove(); data.Property("completedLandmarks").Remove(); }
            if (schema < 4)
            {
                data.Property("toolInstanceSequence").Remove();
                data["toolSlots"] = new JArray(new JObject { ["toolId"] = "", ["remainingUses"] = 0 }, new JObject { ["toolId"] = "", ["remainingUses"] = 0 });
            }
            if (schema < 3) { data.Property("randomAlgorithmId").Remove(); data.Property("randomAlgorithmVersion").Remove(); }
            var restored = PersistenceJsonSerializer.DeserializeTravelRun(data.ToString());
            Assert.That(restored.ActiveLeg.BoardPlan, Is.Null);
            Assert.That(restored.ActiveLeg.SegmentIndex, Is.Zero);
            Assert.That((string)JObject.Parse(PersistenceJsonSerializer.SerializeTravelRun(restored))["boardSelectionPlan"], Is.Null);
        }

        /// <summary>Corrupt or partially missing plans are rejected rather than treated as generated travel.</summary>
        [TestCase("missing-field")]
        [TestCase("run")]
        [TestCase("configuration")]
        [TestCase("policy")]
        [TestCase("duplicate")]
        [TestCase("no-leg")]
        [TestCase("hash")]
        public void CorruptSavedSelectionPlanFailsClosed(string fault)
        {
            var publication = BoardPoolPublication.Read(WorldJson(), Manifest().ToString(), Templates(), OrdinaryBoardContent.CreateAuthoringCatalog());
            var run = PublishedRun(7);
            run.PinBoardPlan(publication.Select(run.RunId, run.RunSeed, run.ActiveLeg));
            JObject data = JObject.Parse(PersistenceJsonSerializer.SerializeTravelRun(run));
            JToken plan = data["boardSelectionPlan"];
            if (fault == "missing-field") data.Property("boardSelectionPlan").Remove();
            if (fault == "run") plan["runId"] = "foreign";
            if (fault == "configuration") plan["selections"][0]["configurationVersion"] = 99;
            if (fault == "policy") plan["policyVersion"] = 99;
            if (fault == "duplicate") ((JArray)plan["selections"]).Add(plan["selections"][0].DeepClone());
            if (fault == "no-leg") data["activeLeg"] = JValue.CreateNull();
            if (fault == "hash") plan["publicationHash"] = "bad";
            Assert.That(() => PersistenceJsonSerializer.DeserializeTravelRun(data.ToString()), Throws.InstanceOf<PersistenceDataException>());
        }

        /// <summary>An actual filesystem checkpoint failure rolls back both leg and plan; retry pins identical choices before any board composition.</summary>
        [Test]
        public void StartRoutePinsWholePlanAtomicallyAndRetriesSameChoice()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-PoolCheckpoint-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "run.json"));
            try
            {
                var world = WorldDefinitionJson.Deserialize(WorldJson());
                var publication = BoardPoolPublication.Read(WorldJson(), Manifest().ToString(), Templates(), OrdinaryBoardContent.CreateAuthoringCatalog());
                var session = new GameSession(new ProfileState("profile.pool", world.WorldDefinitionId, world.Version));
                RunState run = session.StartNewRun("pool-test", 7, new RunStateConfiguration(100, 100, 0, "shelter.warm-up"));
                var attempts = new System.Collections.Generic.List<string>();
                var travel = new RouteTravelService(session, world, new FileSystemSaveStore(root), (runId, seed, leg) =>
                {
                    RouteBoardPlan plan = publication.Select(runId, seed, leg);
                    attempts.Add(plan.LegId + ":" + string.Join(",", plan.Selections.Select(selection => selection.TemplateId)));
                    return plan;
                });
                Assert.That(travel.StartRoute("road.shelter-warm-up").IsFailure, Is.True);
                Assert.That(run.ActiveLeg, Is.Null);
                Assert.That(run.TraversalSequence, Is.Zero);
                Directory.Delete(Path.Combine(root, "run.json"));
                Assert.That(travel.StartRoute("road.shelter-warm-up").IsSuccess, Is.True);
                Assert.That(attempts, Has.Count.EqualTo(2));
                Assert.That(attempts[1], Is.EqualTo(attempts[0]));
                var saved = PersistenceJsonSerializer.DeserializeTravelRun(File.ReadAllText(Path.Combine(root, "run.json")));
                Assert.That(saved.ActiveLeg.BoardPlan.Selections[0].TemplateId, Is.EqualTo(run.ActiveLeg.BoardPlan.Selections[0].TemplateId));
                Assert.That(travel.ConsumeIntro(out bool show).IsSuccess, Is.True);
                Assert.That(show, Is.True);
                Assert.That(run.ActiveLeg.BoardPlan, Is.Not.Null);
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>A selector failure cannot leave a newly allocated leg or allow a later retry to skip traversal identity.</summary>
        [Test]
        public void InvalidSelectorRollsBackNewLegBeforeAnyCheckpoint()
        {
            var world = WorldDefinitionJson.Deserialize(WorldJson());
            var session = new GameSession(new ProfileState("profile.pool", world.WorldDefinitionId, world.Version));
            RunState run = session.StartNewRun("pool-test", 7, new RunStateConfiguration(100, 100, 0, "shelter.warm-up"));
            var travel = new RouteTravelService(session, world, new FileSystemSaveStore(Path.Combine(Path.GetTempPath(), "HB-Unused-" + Guid.NewGuid().ToString("N"))),
                (runId, seed, leg) => throw new ArgumentException("Invalid publication"));
            Assert.That(() => travel.StartRoute("road.shelter-warm-up"), Throws.ArgumentException);
            Assert.That(run.ActiveLeg, Is.Null);
            Assert.That(run.TraversalSequence, Is.Zero);
        }

        /// <summary>Publishing a newer pool leaves saved/unvisited choices resolvable from the retained old publication.</summary>
        [Test]
        public void ArchiveResolvesOldUnvisitedChoiceAfterNewPublicationAndContinue()
        {
            var catalog = OrdinaryBoardContent.CreateAuthoringCatalog();
            var old = BoardPoolPublication.Read(WorldJson(), Manifest().ToString(), Templates(), catalog);
            var run = PublishedRun(7);
            run.PinBoardPlan(old.Select(run.RunId, run.RunSeed, run.ActiveLeg));
            string saved = PersistenceJsonSerializer.SerializeTravelRun(run);
            JObject manifest = Manifest();
            manifest["publicationVersion"] = 2;
            manifest["sources"][0]["poolVersion"] = 2;
            ((JArray)manifest["sources"][0]["entries"]).RemoveAt(0);
            var latest = BoardPoolPublication.Read(WorldJson(), manifest.ToString(), Templates(), catalog);
            var archive = new BoardPoolPublication.Archive();
            archive.Add(latest);
            var continued = PersistenceJsonSerializer.DeserializeTravelRun(saved);
            Assert.That(() => archive.Resolve(continued, Address(continued)), Throws.InvalidOperationException);
            archive.Add(old);
            archive.Add(old);
            Assert.That(archive.Count, Is.EqualTo(2));
            Assert.That(PersistenceJsonSerializer.SerializeTravelRun(continued), Is.EqualTo(saved));
            continued.TryConsumeIntro(continued.ActiveLeg.LegId);
            continued.TryAdvanceSegment(continued.ActiveLeg.LegId, continued.ActiveLeg.CurrentSegment.SegmentId);
            var board = archive.Resolve(continued, Address(continued));
            Assert.That(board.TemplateId, Is.EqualTo(run.ActiveLeg.BoardPlan.Selections[0].TemplateId));
            Assert.That(continued.ActiveLeg.BoardPlan.PublicationVersion, Is.EqualTo(1));
            var address = Address(continued);
            Assert.That(() => archive.Resolve(continued, new BoardAddress(address.RunId, "foreign.leg", address.EdgeId, address.SegmentId,
                address.DayNumber, address.GenerationConfigurationId, address.GenerationVersion, address.BoardSeed)), Throws.ArgumentException);
            continued.Reset("new", 9, new RunStateConfiguration(100, 100, 0, "shelter.warm-up"));
            Assert.That(continued.ActiveLeg, Is.Null);
        }

        /// <summary>Archive registration is atomic and refuses mutations beneath retained publication/pool/template keys.</summary>
        [TestCase("publication")]
        [TestCase("pool")]
        [TestCase("template")]
        public void ArchiveRefusesIdentityMutationWithoutPartialRegistration(string fault)
        {
            var catalog = OrdinaryBoardContent.CreateAuthoringCatalog();
            var original = BoardPoolPublication.Read(WorldJson(), Manifest().ToString(), Templates(), catalog);
            var archive = new BoardPoolPublication.Archive();
            archive.Add(original);
            JObject manifest = Manifest();
            string[] templates = Templates();
            if (fault != "publication") manifest["publicationVersion"] = 2;
            if (fault == "template")
            {
                manifest["sources"][0]["poolVersion"] = 2;
                JObject changed = JObject.Parse(templates[0]);
                changed["outcomes"][0]["name"] = "Changed archived board";
                templates[0] = changed.ToString();
            }
            else ((JArray)manifest["sources"][0]["entries"]).RemoveAt(0);
            var candidate = BoardPoolPublication.Read(WorldJson(), manifest.ToString(), templates, catalog);
            Assert.That(() => archive.Add(candidate), Throws.ArgumentException);
            Assert.That(archive.Count, Is.EqualTo(1));
            var run = PublishedRun(7);
            run.PinBoardPlan(original.Select(run.RunId, run.RunSeed, run.ActiveLeg));
            Assert.That(archive.Resolve(run, Address(run)), Is.Null);
        }

        private static string WorldJson() => File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));

        private static JObject Manifest()
        {
            var world = WorldDefinitionJson.Deserialize(WorldJson());
            var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
            var segment = edge.Segments[1];
            return new JObject
            {
                ["schemaVersion"] = 1, ["publicationId"] = "route.publication", ["publicationVersion"] = 1,
                ["worldDefinitionId"] = world.WorldDefinitionId, ["worldVersion"] = world.Version, ["edgeId"] = edge.EdgeId,
                ["sources"] = new JArray(new JObject
                {
                    ["segmentId"] = segment.SegmentId, ["generationConfigurationId"] = segment.GenerationConfigurationId,
                    ["generationVersion"] = segment.GenerationVersion, ["poolId"] = "middle.pool", ["poolVersion"] = 1,
                    ["entries"] = new JArray(new[] { "board.a", "board.b" }.Select(id => new JObject
                    { ["templateId"] = id, ["templateVersion"] = 1, ["variantId"] = "base" }))
                })
            };
        }

        private static string[] Templates() => new[] { "board.a", "board.b" }.Select(id =>
        {
            JObject data = UnifiedBoardAuthoringTests.Minimal();
            data["templateId"] = id;
            return data.ToString();
        }).ToArray();

        private static RunState PublishedRun(int seed)
        {
            var world = WorldDefinitionJson.Deserialize(WorldJson());
            var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
            var run = new RunState("pool-test", seed, new RunStateConfiguration(100, 100, 0, edge.FromNodeId));
            run.StartLeg(edge.EdgeId, edge.FromNodeId, edge.ToNodeId, edge.Segments.Select(segment =>
                new RouteSegmentState(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion)));
            return run;
        }

        private static BoardAddress Address(RunState run) => HallowBlaze.Core.Session.BoardAddressFactory.CreateFromState(run);

        private static RunState NewRun() => new RunState("pool-test", 7, new RunStateConfiguration(100, 100, 0, "start"));
        private static RouteSegmentState[] Segments() => new[]
        {
            new RouteSegmentState("entry", "ordinary.board.v1", 6), new RouteSegmentState("final", "ordinary.board.v1", 6)
        };

        private static RouteBoardPlan Plan(RunState run, string fault = null)
        {
            var selected = new RouteBoardSelection("final", fault == "configuration" ? "foreign" : "ordinary.board.v1", 6,
                "board", 1, "base", new string('a', 64), "pool", 1, new string('b', 64));
            return new RouteBoardPlan(fault == "run" ? "foreign" : run.RunId, run.RunSeed, run.ActiveLeg.LegId,
                "road", "world", 1, "publication", 1, new string('c', 64),
                fault == "duplicate" ? new[] { selected, selected } : new[] { selected });
        }
    }
}