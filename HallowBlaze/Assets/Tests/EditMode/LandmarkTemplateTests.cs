using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using HallowBlaze.Core.World.Validation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies authored templates use ordinary blueprints without scene state, runtime outcomes or tool-dependent mandatory exits.</summary>
    public sealed class LandmarkTemplateTests
    {
        /// <summary>Authored enumeration order and unrelated RNG cursors cannot reroll the full-address landmark decision.</summary>
        [Test]
        public void LandmarkVariantSelectionIsAddressedAndOrderIndependent()
        {
            WorldDefinition world = WorldDefinitionJson.Deserialize(FixtureText("sanity-world.json"));
            JObject json = JObject.Parse(FixtureText("sanity-template.json"));
            JObject original = (JObject)json["variants"][0];
            var variants = new JArray();
            foreach (string id in new[] { "base", "upper-guard", "south-rations" })
            {
                var variant = (JObject)original.DeepClone();
                variant["id"] = id;
                variants.Add(variant);
            }
            json["variants"] = variants;
            LandmarkTemplate first = LandmarkTemplateJson.Deserialize(json.ToString(), Catalog());
            json["variants"] = new JArray(variants.Reverse().Select(variant => variant.DeepClone()));
            LandmarkTemplate reversed = LandmarkTemplateJson.Deserialize(json.ToString(), Catalog());
            LandmarkPlacement placement = LandmarkTemplateJson.DeserializePlacement(FixtureText("sanity-placement.json"), world, first);
            LandmarkPlacement reordered = LandmarkTemplateJson.DeserializePlacement(FixtureText("sanity-placement.json"), world, reversed);
            var address = new BoardAddress("selection-test", "leg.1", placement.EdgeId, placement.SegmentId, 5, first.Config.Id, first.Config.Version, 64);
            var seen = new HashSet<string>();
            for (int seed = 0; seed < 64; seed++)
            {
                LandmarkRecord selected = placement.SelectVariant(address, seed);
                LandmarkRecord reorderedSelection = reordered.SelectVariant(address, seed);
                Assert.That(reorderedSelection.Address, Is.EqualTo(selected.Address));
                Assert.That(reorderedSelection.TemplateId, Is.EqualTo(selected.TemplateId));
                Assert.That(reorderedSelection.TemplateVersion, Is.EqualTo(selected.TemplateVersion));
                Assert.That(reorderedSelection.VariantId, Is.EqualTo(selected.VariantId));
                var unrelated = new RandomStreamProvider(seed).Create(RandomStreamProvider.StreamId("cosmetic-board", "selection-test"));
                for (int draw = 0; draw < 20; draw++) unrelated.NextInt(0, 100);
                Assert.That(placement.SelectVariant(address, seed).VariantId, Is.EqualTo(selected.VariantId));
                Assert.That(selected.Address, Is.SameAs(address));
                seen.Add(selected.VariantId);
            }
            Assert.That(seen, Is.EquivalentTo(first.VariantIds));
            Assert.Throws<ArgumentException>(() => placement.SelectVariant(new BoardAddress("selection-test", "leg.1", "foreign",
                placement.SegmentId, 5, first.Config.Id, first.Config.Version, 64), 64));
        }

        /// <summary>Authored pressure links survive independent reconstruction and never leak mutations between boards.</summary>
        [Test]
        public void ExplicitPlateGateBindingsSurviveTemplateReconstruction()
        {
            var entities = Entities();
            entities.Add(new BoardEntityState(new EntityId(50), PlateGateValidator.PlateContent, new GridPosition(2, 1)));
            entities.Add(new BoardEntityState(new EntityId(51), PlateGateValidator.GateContent, new GridPosition(3, 1)));
            var config = new BoardGenerationConfig("landmark.links", 1, new GenerationRange(1, 63), new GenerationRange(0, 64),
                new GenerationRange(0, 0), new GenerationRange(0, 0), 0, 0,
                entities.Select(entity => entity.Definition.ContentId).Distinct(), Array.Empty<ResourceGenerationBudget>());
            var variant = new LandmarkVariant("base", entities, Array.Empty<BoardItemSpawn>(),
                new[] { new PressurePlateDefinition(new EntityId(50), "A") }, new[] { new GateDefinition(new EntityId(51), "A") });
            var template = new LandmarkTemplate("landmark.links", 1, config, new[] { variant }, Outcomes());
            var address = new BoardAddress("run", "leg", "edge", "segment", 5, config.Id, 1, 64);
            LandmarkBoard authored = template.CreateBoard(address, "base");
            BoardState first = authored.CreateState();
            BoardState second = authored.CreateState();
            Assert.That(first.GetGates().Single().IsOpen, Is.False);
            Assert.That(first.TryMove(new EntityId(1), new GridPosition(2, 1)), Is.True);
            Assert.That(first.GetGates().Single().IsOpen, Is.True);
            Assert.That(second.GetGates().Single().IsOpen, Is.False);
            Assert.That(authored.Plates.Single().LinkId, Is.EqualTo("A"));
        }

        /// <summary>Template construction and runtime state round-trip retain the exact address/layers and isolate every reconstruction.</summary>
        [Test]
        public void ExplicitVariantBuildsTheSameBlueprintAndIndependentState()
        {
            var entities = Entities();
            var variant = new LandmarkVariant("base", entities, Array.Empty<BoardItemSpawn>());
            var template = new LandmarkTemplate("landmark.sanity", 1, Config(), new[] { variant }, Outcomes());
            var address = new BoardAddress("run", "leg", "edge", "final-segment", 5, Config().Id, 1, 61);
            var ordinary = new BoardBlueprint(address, Config(), entities, Array.Empty<BoardItemSpawn>());
            entities.Clear();
            LandmarkBoard authored = template.CreateBoard(address, "base");
            Assert.That(authored.TemplateId, Is.EqualTo("landmark.sanity"));
            Assert.That(authored.TemplateVersion, Is.EqualTo(1));
            Assert.That(authored.VariantId, Is.EqualTo("base"));
            Assert.That(authored.Blueprint.Address, Is.SameAs(address));
            Assert.That(authored.Blueprint, Is.EqualTo(ordinary));
            BoardState state = authored.Blueprint.CreateState();
            Assert.That(BoardBlueprint.FromState(address, Config(), state, authored.Blueprint.Items), Is.EqualTo(ordinary));
            state.TryRemove(new EntityId(1));
            Assert.That(template.CreateBoard(address, "base").Blueprint.CreateState().TryGetEntity(new EntityId(1), out _), Is.True);
            Assert.Throws<NotSupportedException>(() => ((IList<LandmarkOutcomeDefinition>)template.Outcomes).Clear());
            Assert.Throws<ArgumentException>(() => template.CreateBoard(address, "missing"));
        }

        /// <summary>Bad goal references are rejected before any authored variant is published.</summary>
        [Test]
        public void MissingOrNonExitGoalReportsTemplateVariantAndOutcome()
        {
            var outcomes = Outcomes();
            outcomes[1] = new LandmarkOutcomeDefinition("invalid.goal", "Invalid goal", new EntityId(1));
            ArgumentException error = Assert.Throws<ArgumentException>(() => new LandmarkTemplate("landmark.sanity", 1, Config(),
                new[] { new LandmarkVariant("base", Entities(), Array.Empty<BoardItemSpawn>()) }, outcomes));
            Assert.That(error.Message, Does.Contain("landmark.sanity").And.Contain("base").And.Contain("invalid.goal"));
            Assert.Throws<ArgumentException>(() => new LandmarkOutcomeDefinition("mandatory", "Mandatory", new EntityId(2), false, "axe.basic"));
            Assert.DoesNotThrow(() => new LandmarkOutcomeDefinition("optional", "Optional", new EntityId(2), true, "axe.basic"));
        }

                /// <summary>JSON references authoritative definitions rather than serialized traits, transforms or prefabs.</summary>
                [Test]
                public void JsonLoadBuildsTheSameBlueprintWithoutMutableContentCopies()
                {
                        string json = JsonFixture().ToString();
                        var template = LandmarkTemplateJson.Deserialize(json, Catalog());
                        var address = new BoardAddress("run", "leg", "edge", "segment", 5, Config().Id, 1, 61);
                        Assert.That(template.CreateBoard(address, "base").Blueprint,
                                Is.EqualTo(new BoardBlueprint(address, Config(), Entities(), Array.Empty<BoardItemSpawn>())));
                        Assert.That(template.Outcomes.Count, Is.EqualTo(2));
                        Assert.That(LandmarkTemplateJson.Deserialize(json, Catalog()).CreateBoard(address, "base").Blueprint.ContentHash,
                                Is.EqualTo(template.CreateBoard(address, "base").Blueprint.ContentHash));
                }

                /// <summary>Unknown fields, coercions and unresolved content cannot publish a partial template.</summary>
                [TestCase("schema")]
                [TestCase("case")]
                [TestCase("unknown-content")]
                [TestCase("wrong-layer")]
                [TestCase("float-id")]
                [TestCase("string-version")]
                [TestCase("null-variants")]
                [TestCase("missing-flag")]
                [TestCase("overflow")]
                public void InvalidJsonReferencesAndScalarTypesAreReported(string invalid)
                {
                        JObject root = JsonFixture();
                        switch (invalid)
                        {
                                case "schema": root["schemaVersion"] = 2; break;
                                case "case": root["TemplateId"] = root["templateId"]; root.Remove("templateId"); break;
                                case "unknown-content": root["variants"][0]["entities"][0]["contentId"] = "missing"; break;
                                case "wrong-layer": root["variants"][0]["entities"][0]["layer"] = "0"; break;
                                case "float-id": root["variants"][0]["entities"][0]["id"] = 1.0; break;
                                case "string-version": root["templateVersion"] = "1"; break;
                                case "null-variants": root["variants"] = null; break;
                                case "missing-flag": ((JObject)root["outcomes"][0]).Remove("optional"); break;
                                case "overflow": root["templateVersion"] = (long)int.MaxValue + 1; break;
                        }
                        Assert.Throws<JsonSerializationException>(() => LandmarkTemplateJson.Deserialize(root.ToString(), Catalog()));
                }

                /// <summary>The sanity template is the final board on the fifth inbound road, not an extra day or a node-owned board.</summary>
                [Test]
                public void AuthoredFixtureUsesFinalSegmentAndPreservesIdentityThroughExistingRunSave()
                {
                    WorldDefinition world = WorldDefinitionJson.Deserialize(FixtureText("sanity-world.json"));
                    WorldGraphValidationResult graph = WorldGraphValidator.Validate(WorldGraphValidationInput.FromWorldDefinition(world), 5);
                    Assert.That(graph.IsValid, Is.True, string.Join("\n", graph.Diagnostics.Select(diagnostic => diagnostic.ToString())));
                    LandmarkTemplate template = LandmarkTemplateJson.Deserialize(FixtureText("sanity-template.json"), Catalog());
                    LandmarkPlacement placement = LandmarkTemplateJson.DeserializePlacement(FixtureText("sanity-placement.json"), world, template);
                    var run = new RunState("landmark-fixture", 61, new RunStateConfiguration(100, 100, 0, world.StartNodeId));
                    int boards = 0;
                    for (int day = 1; day <= 5; day++)
                    {
                        WorldEdgeDefinition edge = world.GetOutgoingEdges(run.WorldNodeId).Single();
                        ActiveRouteLegState leg = run.StartLeg(edge.EdgeId, edge.FromNodeId, edge.ToNodeId,
                            edge.Segments.Select(segment => new RouteSegmentState(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion)));
                        Assert.That(leg.DayNumber, Is.EqualTo(day));
                        Assert.That(run.TryConsumeIntro(leg.LegId), Is.True);
                        while (!run.ActiveLeg.IsFinalSegment)
                        {
                            BoardAddress preceding = BoardAddressFactory.Create(run, world);
                            Assert.Throws<ArgumentException>(() => placement.CreateBoard(preceding));
                            Assert.That(run.TryAdvanceSegment(leg.LegId, preceding.SegmentId), Is.True);
                            boards++;
                        }
                        BoardAddress address = BoardAddressFactory.Create(run, world);
                        if (day == 5)
                        {
                            Assert.That(run.WorldNodeId, Is.EqualTo("sanity.day4"));
                            Assert.That(run.CompletedDayCount, Is.EqualTo(4));
                            Assert.That(run.ActiveLeg.Segments.Count, Is.EqualTo(2));
                            LandmarkBoard board = placement.CreateBoard(address);
                            Assert.That(board.Blueprint.Entities.Count, Is.EqualTo(37));
                            Assert.That(board.Blueprint.Exits.Count, Is.EqualTo(2));
                            BoardValidationReport report = BoardValidator.Validate(board.Blueprint, Catalog());
                            Assert.That(report.Errors, Is.Empty, string.Join("\n", report.Errors.Select(error => error.Code + ": " + error.Message)));
                            Assert.That(BoardBlueprint.FromState(address, template.Config, board.Blueprint.CreateState(), board.Blueprint.Items), Is.EqualTo(board.Blueprint));
                            string saved = PersistenceJsonSerializer.SerializeRun(run);
                            RunState restored = PersistenceJsonSerializer.DeserializeRun(saved);
                            LandmarkBoard reconstructed = placement.CreateBoard(BoardAddressFactory.Create(restored, world));
                            Assert.That(reconstructed.Blueprint, Is.EqualTo(board.Blueprint));
                            Assert.That(reconstructed.TemplateId, Is.EqualTo(board.TemplateId));
                            Assert.That(reconstructed.TemplateVersion, Is.EqualTo(board.TemplateVersion));
                            Assert.That(reconstructed.VariantId, Is.EqualTo(board.VariantId));
                            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
                        }
                        Assert.That(run.TryCompleteLeg(leg.LegId, address.SegmentId), Is.True);
                        boards++;
                    }
                    Assert.That(boards, Is.EqualTo(8));
                    Assert.That(run.CompletedDayCount, Is.EqualTo(5));
                    Assert.That(run.WorldNodeId, Is.EqualTo(world.GoalNodeId));
                    Assert.That(template.Outcomes.Select(outcome => outcome.Id), Is.EqualTo(new[] { "arrival.alternate", "arrival.safe" }));
                }

                /// <summary>Placement rejects foreign segments, wrong versions, variants and configuration mismatches rather than silently substituting content.</summary>
                [TestCase("world")]
                [TestCase("world-version")]
                [TestCase("template")]
                [TestCase("template-version")]
                [TestCase("variant")]
                [TestCase("segment")]
                [TestCase("preceding")]
                [TestCase("config")]
                [TestCase("generation-version")]
                [TestCase("destination")]
                [TestCase("from-node")]
                public void InvalidPlacementReferencesAreRejected(string invalid)
                {
                    JObject worldJson = JObject.Parse(FixtureText("sanity-world.json"));
                    if (invalid == "config") worldJson["edges"][4]["segments"][1]["generationConfigurationId"] = "foreign";
                    if (invalid == "generation-version") worldJson["edges"][4]["segments"][1]["generationVersion"] = 2;
                    if (invalid == "destination") worldJson["nodes"][5]["placeKind"] = "trail";
                    if (invalid == "from-node") worldJson["edges"][4]["fromNodeId"] = "missing";
                    WorldDefinition world = WorldDefinitionJson.Deserialize(worldJson.ToString());
                    LandmarkTemplate template = LandmarkTemplateJson.Deserialize(FixtureText("sanity-template.json"), Catalog());
                    JObject root = JObject.Parse(FixtureText("sanity-placement.json"));
                    switch (invalid)
                    {
                        case "world": root["worldDefinitionId"] = "foreign"; break;
                        case "world-version": root["worldVersion"] = 2; break;
                        case "template": root["templateId"] = "foreign"; break;
                        case "template-version": root["templateVersion"] = 2; break;
                        case "variant": root["variantId"] = "missing"; break;
                        case "segment": root["segmentId"] = "missing"; break;
                        case "preceding": root["segmentId"] = "segment.sanity.day5.entry"; break;
                    }
                    if (invalid == "world" || invalid == "world-version" || invalid == "template" || invalid == "template-version")
                        Assert.Throws<JsonSerializationException>(() => LandmarkTemplateJson.DeserializePlacement(root.ToString(), world, template));
                    else
                        Assert.Throws<ArgumentException>(() => LandmarkTemplateJson.DeserializePlacement(root.ToString(), world, template));
                }

                /// <summary>A claimed mandatory tool must exist, match, be observable and be reachable before an exit in every allowed variant.</summary>
                [TestCase("valid")]
                [TestCase("missing")]
                [TestCase("wrong-instance")]
                [TestCase("wrong-definition")]
                [TestCase("hidden")]
                [TestCase("behind-blocker")]
                [TestCase("on-exit")]
                [TestCase("second-variant")]
                public void MandatoryToolGuaranteesAreConcreteAndVariantComplete(string condition)
                {
                    JObject root = JObject.Parse(FixtureText("sanity-template.json"));
                    ((JArray)root["configuration"]["allowedContentIds"]).Add("axe.basic");
                    JObject outcome = (JObject)root["outcomes"][0];
                    outcome["requiredToolDefinitionId"] = "axe.basic";
                    outcome["guaranteedToolInstanceId"] = "tool.guaranteed";
                    JObject variant = (JObject)root["variants"][0];
                    ((JArray)variant["entities"]).Add(new JObject { ["id"] = 200, ["layer"] = "Item", ["contentId"] = "axe.basic", ["x"] = 2, ["y"] = 1 });
                    JObject item = new JObject { ["instanceId"] = "tool.guaranteed", ["entityId"] = 200,
                        ["itemDefinitionId"] = "axe.basic", ["resourceFamilyId"] = "tools", ["initiallyObservable"] = true };
                    ((JArray)variant["items"]).Add(item);
                    switch (condition)
                    {
                        case "missing": item["entityId"] = 201; break;
                        case "wrong-instance": item["instanceId"] = "different"; break;
                        case "wrong-definition": outcome["requiredToolDefinitionId"] = "floor"; break;
                        case "hidden": item["initiallyObservable"] = false; break;
                        case "behind-blocker": ((JObject)variant["entities"].Last)["x"] = 0; break;
                        case "on-exit": ((JObject)variant["entities"].Last)["x"] = 6; break;
                        case "second-variant":
                            JObject other = (JObject)variant.DeepClone();
                            other["id"] = "unsupported.guarantee";
                            other["items"][0]["initiallyObservable"] = false;
                            ((JArray)root["variants"]).Add(other);
                            break;
                    }
                    if (condition == "valid") Assert.DoesNotThrow(() => LandmarkTemplateJson.Deserialize(root.ToString(), Catalog()));
                    else if (condition == "wrong-definition") Assert.Throws<JsonSerializationException>(() => LandmarkTemplateJson.Deserialize(root.ToString(), Catalog()));
                    else Assert.Throws<ArgumentException>(() => LandmarkTemplateJson.Deserialize(root.ToString(), Catalog()));
                }

                /// <summary>Duplicate JSON keys or domain IDs cannot alias one result/variant/entity, and array ordering is not variant identity.</summary>
                [Test]
                public void DuplicateAndConflictingDataAreRejectedAndOrderingIsDeterministic()
                {
                    Assert.Throws<JsonReaderException>(() => LandmarkTemplateJson.Deserialize("{\"schemaVersion\":1,\"schemaVersion\":1}", Catalog()));
                    Assert.Throws<JsonReaderException>(() => LandmarkTemplateJson.Deserialize(JsonFixture() + " {}", Catalog()));
                    foreach (string field in new[] { "variants", "outcomes" })
                    {
                        JObject root = JsonFixture();
                        ((JArray)root[field]).Add(root[field][0].DeepClone());
                        Assert.Throws<ArgumentException>(() => LandmarkTemplateJson.Deserialize(root.ToString(), Catalog()));
                    }
                    JObject conflicts = JsonFixture();
                    ((JArray)conflicts["variants"][0]["entities"]).Add(conflicts["variants"][0]["entities"][0].DeepClone());
                    Assert.Throws<ArgumentException>(() => LandmarkTemplateJson.Deserialize(conflicts.ToString(), Catalog()));
                    JObject original = JsonFixture();
                    LandmarkTemplate first = LandmarkTemplateJson.Deserialize(original.ToString(), Catalog());
                    ((JObject)original["variants"][0])["entities"] = new JArray(((JArray)original["variants"][0]["entities"]).Reverse());
                    original["outcomes"] = new JArray(((JArray)original["outcomes"]).Reverse());
                    LandmarkTemplate reordered = LandmarkTemplateJson.Deserialize(original.ToString(), Catalog());
                    var address = new BoardAddress("run", "leg", "edge", "segment", 5, Config().Id, 1, 61);
                    Assert.That(reordered.CreateBoard(address, "base").Blueprint, Is.EqualTo(first.CreateBoard(address, "base").Blueprint));
                    Assert.That(reordered.Outcomes.Select(outcome => outcome.Id), Is.EqualTo(first.Outcomes.Select(outcome => outcome.Id)));
                }

                /// <summary>Optional tool goals need no guarantee, but every claimed spawn source/guarantee must resolve to the right definition.</summary>
                [TestCase("optional")]
                [TestCase("guaranteed")]
                [TestCase("valid-source")]
                [TestCase("unknown-source")]
                [TestCase("wrong-source-definition")]
                public void OptionalToolGoalsAndFixedSourcesResolveAgainstTheExternalCatalog(string condition)
                {
                    JObject root = JObject.Parse(FixtureText("sanity-template.json"));
                    ((JArray)root["configuration"]["allowedContentIds"]).Add("axe.basic");
                    JObject goal = (JObject)root["outcomes"][1];
                    goal["requiredToolDefinitionId"] = "axe.basic";
                    if (condition == "guaranteed") goal["guaranteedToolInstanceId"] = "tool.optional";
                    JObject variant = (JObject)root["variants"][0];
                    ((JArray)variant["entities"]).Add(new JObject { ["id"] = 200, ["layer"] = "Item", ["contentId"] = "axe.basic", ["x"] = 2, ["y"] = 1 });
                    var item = new JObject { ["instanceId"] = "tool.optional", ["entityId"] = 200, ["itemDefinitionId"] = "axe.basic",
                        ["resourceFamilyId"] = "tools", ["initiallyObservable"] = true };
                    ((JArray)variant["items"]).Add(item);
                    if (condition == "valid-source") item["sourceId"] = "source.axe";
                    if (condition == "unknown-source") item["sourceId"] = "source.missing";
                    if (condition == "wrong-source-definition") item["sourceId"] = "source.shovel";
                    var sources = new[] { new KeyValuePair<string, string>("source.axe", "axe.basic"),
                        new KeyValuePair<string, string>("source.shovel", "shovel.basic") };
                    if (condition == "unknown-source" || condition == "wrong-source-definition")
                        Assert.Throws<JsonSerializationException>(() => LandmarkTemplateJson.Deserialize(root.ToString(), Catalog(sources)));
                    else
                        Assert.DoesNotThrow(() => LandmarkTemplateJson.Deserialize(root.ToString(), Catalog(sources)));
                }

                /// <summary>Equal seeds cannot replace an assigned segment/configuration; valid run/leg/seed differences keep distinct full identities.</summary>
                [Test]
                public void PlacementRetainsEveryAddressFieldAndRejectsConfigurationDrift()
                {
                    WorldDefinition world = WorldDefinitionJson.Deserialize(FixtureText("sanity-world.json"));
                    LandmarkTemplate template = LandmarkTemplateJson.Deserialize(FixtureText("sanity-template.json"), Catalog());
                    LandmarkPlacement placement = LandmarkTemplateJson.DeserializePlacement(FixtureText("sanity-placement.json"), world, template);
                    foreach (string field in new[] { "edge", "segment", "config", "version" })
                    {
                        var foreign = new BoardAddress("run", "leg", field == "edge" ? "foreign" : placement.EdgeId,
                            field == "segment" ? "foreign" : placement.SegmentId, 5, field == "config" ? "foreign" : template.Config.Id,
                            field == "version" ? 2 : 1, 61);
                        Assert.Throws<ArgumentException>(() => placement.CreateBoard(foreign));
                    }
                    var address = new BoardAddress("run", "leg", placement.EdgeId, placement.SegmentId, 5, template.Config.Id, 1, 61);
                    LandmarkBoard first = placement.CreateBoard(address);
                    var different = new BoardAddress("another-run", "another-leg", placement.EdgeId, placement.SegmentId, 6, template.Config.Id, 1, 62);
                    LandmarkBoard second = placement.CreateBoard(different);
                    Assert.That(second.Blueprint.Address, Is.SameAs(different));
                    Assert.That(second.Blueprint.ContentHash, Is.Not.EqualTo(first.Blueprint.ContentHash));
                    Assert.That(second.Blueprint.Entities, Is.EqualTo(first.Blueprint.Entities));
                    Assert.That(second.VariantId, Is.EqualTo(first.VariantId));
                }

                private static string FixtureText(string file) => File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "GameData", "Landmarks", file));

                private static BoardValidationCatalog Catalog(IEnumerable<KeyValuePair<string, string>> sources = null) => new BoardValidationCatalog(new[]
                {
                        Entities()[0].Definition, Entities()[1].Definition,
                    new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false)),
                    new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "axe.basic", new BoardEntityTraits(true, false, true)),
                    new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "shovel.basic", new BoardEntityTraits(true, false, true))
                }, new[] { new KeyValuePair<string, string>("axe.basic", "tools"), new KeyValuePair<string, string>("shovel.basic", "tools") },
                    sources ?? Array.Empty<KeyValuePair<string, string>>());

                private static JObject JsonFixture() => JObject.Parse(@"{
                    'schemaVersion':1,'templateId':'landmark.sanity','templateVersion':1,
                    'configuration':{'id':'landmark.sanity.board','version':1,
                        'pathLength':{'minimum':1,'maximum':63},'branchPoints':{'minimum':0,'maximum':64},
                        'obstacleCount':{'minimum':0,'maximum':64},'enemyCount':{'minimum':0,'maximum':0},
                        'maximumConcurrentThreats':0,'minimumSafeStartMoves':0,'allowedContentIds':['player','exit','axe.basic'],'resourceBudgets':[]},
                    'outcomes':[{'id':'safe','name':'Safe arrival','exitEntityId':2,'optional':false},
                        {'id':'alternate','name':'Alternate arrival','exitEntityId':3,'optional':true}],
                    'variants':[{'id':'base','entities':[
                        {'id':1,'layer':'Actor','contentId':'player','x':1,'y':1},
                        {'id':2,'layer':'Terrain','contentId':'exit','x':6,'y':6},
                        {'id':3,'layer':'Terrain','contentId':'exit','x':6,'y':1}], 'items':[]}]
                }");

                private static List<BoardEntityState> Entities() => new List<BoardEntityState>
        {
            new BoardEntityState(new EntityId(1), new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player", new BoardEntityTraits(false, false, false)), new GridPosition(1, 1)),
            new BoardEntityState(new EntityId(2), new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "exit", new BoardEntityTraits(true, true, false)), new GridPosition(6, 6)),
            new BoardEntityState(new EntityId(3), new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "exit", new BoardEntityTraits(true, true, false)), new GridPosition(6, 1))
        };

        private static LandmarkOutcomeDefinition[] Outcomes() => new[]
        {
            new LandmarkOutcomeDefinition("safe", "Safe arrival", new EntityId(2)),
            new LandmarkOutcomeDefinition("alternate", "Alternate arrival", new EntityId(3), true)
        };

        private static BoardGenerationConfig Config() => new BoardGenerationConfig("landmark.sanity.board", 1,
            new GenerationRange(1, 63), new GenerationRange(0, 64), new GenerationRange(0, 64), new GenerationRange(0, 0),
            0, 0, new[] { "player", "exit", "axe.basic" }, Array.Empty<ResourceGenerationBudget>());
    }
}