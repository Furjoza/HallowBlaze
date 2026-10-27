using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Core.World;
using Newtonsoft.Json.Linq;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies the actual published landmark data and intended shared-resolver command paths, not a substitute test layout.</summary>
    public sealed class RadioTowerLandmarkTests
    {
        /// <summary>Exactly three structurally distinct curated layouts retain one shared content contract and both outcome identities.</summary>
        [Test]
        public void CuratedCatalogAndSelectionVectorsAreStable()
        {
            LandmarkTemplate template = Template();
            Assert.That(template.VariantIds, Is.EqualTo(new[] { "base", "east-guard", "north-push" }));
            Assert.That(template.VariantIds.Select(id => template.CreateBoard(Address(), id).Blueprint.ContentHash).Distinct().Count(), Is.EqualTo(3));
            WorldDefinition world = WorldDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            LandmarkPlacement placement = RadioTowerLandmark.Load().CreatePlacement(world);
            var selected = new List<string>();
            for (int seed = 0; seed < 8; seed++) selected.Add(placement.SelectVariant(Address(), seed).VariantId);
            Assert.That(selected, Is.EqualTo(new[] { "north-push", "base", "east-guard", "north-push", "north-push", "east-guard", "north-push", "base" }));
            TestContext.WriteLine("radio-tower-test address seed=64, root seeds 0..7: " + string.Join(",", selected));
            Assert.That(selected.Distinct(), Is.EquivalentTo(template.VariantIds));
        }

        /// <summary>Moderator fixtures use independent external profiles and exact persisted variants without touching real player storage.</summary>
        [Test]
        public void CuratedPlaytestSavesAreIsolatedAndPinned()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M66-export-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                WritePlaytestBoundaries(root);
                foreach (string variantId in Template().VariantIds)
                {
                    var store = new FileSystemSaveStore(Path.Combine(root, variantId));
                    Assert.That(store.LoadProfile().IsSuccess, Is.True);
                    RunState run = store.LoadRun().Data;
                    Assert.That(run.ActiveLandmark.VariantId, Is.EqualTo(variantId));
                    Assert.That(run.ActiveLandmark.Address, Is.EqualTo(BoardAddressFactory.CreateFromState(run)));
                    Assert.That(run.Health, Is.EqualTo(100));
                    Assert.That(run.Food, Is.EqualTo(100));
                    Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
                    Assert.That(run.ToolInstanceSequence, Is.Zero);
                    Assert.That(run.CompletedLandmarks, Is.Empty);
                    Assert.That(store.LoadProfile().Data.GetNodeDiscoveryState("landmark.radio-tower"), Is.EqualTo(NodeDiscoveryState.Unknown));
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        /// <summary>Exports fresh moderator-only boundaries outside the project; does not launch gameplay or change persistence overrides.</summary>
        [MenuItem("HallowBlaze/Playtests/M6.6/Export isolated sessions")]
        public static void ExportCuratedPlaytestSessions()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M66-sessions-" + Guid.NewGuid().ToString("N"));
            WritePlaytestBoundaries(root);
            Debug.Log("M6.6 isolated session boundaries: " + root);
        }

        private static void WritePlaytestBoundaries(string root)
        {
            WorldDefinition world = WorldDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            world.TryGetEdge("road.ridge-radio-tower", out WorldEdgeDefinition edge);
            foreach (string variantId in Template().VariantIds)
            {
                var run = new RunState("m66-session", 64, new RunStateConfiguration(100, 100, 4, "forest.ridge"));
                run.StartLeg(edge.EdgeId, edge.FromNodeId, edge.ToNodeId, edge.Segments.Select(segment =>
                    new RouteSegmentState(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion)));
                run.TryConsumeIntro(run.ActiveLeg.LegId);
                while (!run.ActiveLeg.IsFinalSegment) run.TryAdvanceSegment(run.ActiveLeg.LegId, run.ActiveLeg.CurrentSegment.SegmentId);
                if (!run.TrySelectLandmark(new LandmarkRecord(BoardAddressFactory.Create(run, world), "landmark.radio-tower", 1, variantId)))
                    throw new InvalidOperationException("The playtest boundary could not pin its authored variant.");
                var store = new FileSystemSaveStore(Path.Combine(root, variantId));
                if (!store.SaveProfile(new ProfileState("m66-test-profile", world.WorldDefinitionId, world.Version)).IsSuccess || !store.SaveRun(run).IsSuccess)
                    throw new IOException("The isolated playtest boundary could not be saved.");
            }
        }

        /// <summary>Only an optional gated exit is exempt from ordinary static reachability; all other catalog/budget/opening rules remain enforced.</summary>
        [Test]
        public void AuthoredContentPassesSharedValidationWithToolFreeFallback()
        {
            LandmarkBoard board = Template().CreateBoard(Address(), "base");
            BoardValidationReport ordinary = BoardValidator.Validate(board.Blueprint, RadioTowerLandmark.CreateCatalog());
            Assert.That(ordinary.Errors.Select(error => error.Code), Is.EqualTo(new[] { "UnreachableExit" }));
            BoardValidationReport authored = board.Validate(RadioTowerLandmark.CreateCatalog());
            Assert.That(authored.IsValid, Is.True, string.Join("\n", authored.Errors.Select(error => error.Message)));
            Assert.That(board.CreateState().GetGates().Single().IsOpen, Is.False);
            Assert.That(board.Blueprint.Items.Select(item => item.ItemDefinitionId), Does.Contain(RadioTowerLandmark.RationsId));
        }

        /// <summary>The mandatory route is an ordinary seven-move exit without loot, work, charges or a level-only action.</summary>
        [Test]
        public void FallbackReachesTowerWithNoToolOrPuzzleDependency()
        {
            var scenario = new Scenario();
            var before = scenario.Board.GetEntities().ToArray();
            Assert.That(scenario.Controller.Resolve(new MoveCommand(Direction.West)).Accepted, Is.False);
            Assert.That(scenario.Run.Food, Is.EqualTo(100));
            Assert.That(scenario.Board.GetEntities(), Is.EqualTo(before));
            List<GameEvent> events = scenario.Execute("NNNNNEE");
            Assert.That(events.OfType<ExitReachedEvent>().Single().ExitId, Is.EqualTo(new EntityId(151)));
            Assert.That(scenario.Run.Food, Is.EqualTo(93));
            Assert.That(scenario.Run.Health, Is.EqualTo(100));
            Assert.That(scenario.Run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(scenario.Board.GetGates().Single().IsOpen, Is.False);
            Assert.That(scenario.Controller.IsTerminal, Is.True);
            Assert.That(scenario.Controller.Resolve(new MoveCommand(Direction.East)).Accepted, Is.False);
        }

        /// <summary>Both resource routes push a boulder, open a linked gate, clear wood and collect one stable concrete ration through the same resolver.</summary>
        [TestCase(false, 108)]
        [TestCase(true, 109)]
        public void SupplyRoutesHaveDistinctMeasuredCosts(bool useAxe, int finalFood)
        {
            var scenario = new Scenario();
            List<GameEvent> events = scenario.Execute(useAxe ? "TNNPNEEAENE" : "NNPNEECCCENE");
            Assert.That(events.OfType<ExitReachedEvent>().Single().ExitId, Is.EqualTo(new EntityId(146)));
            Assert.That(events.OfType<BoulderPushedEvent>().Count(), Is.EqualTo(1));
            Assert.That(events.OfType<GateChangedEvent>().Any(change => change.Gate.IsOpen), Is.True);
            Assert.That(scenario.Board.TryGetEntity(new EntityId(21), out _), Is.False);
            Assert.That(scenario.Board.TryGetEntity(new EntityId(11), out _), Is.False);
            Assert.That(scenario.Run.Food, Is.EqualTo(finalFood));
            Assert.That(scenario.Run.Health, Is.GreaterThan(0));
            Assert.That(scenario.Run.ToolSlots[0].IsEmpty, Is.EqualTo(!useAxe));
            if (useAxe) Assert.That(scenario.Run.ToolSlots[0].RemainingUses, Is.EqualTo(scenario.Axe.Charges - 1));
        }

        /// <summary>A discarded boulder position cannot block the mandatory route; only the optional resource route depends on its placement.</summary>
        [Test]
        public void MisplacedBoulderStillLeavesToolFreeFallback()
        {
            var scenario = new Scenario();
            scenario.Execute("NEP");
            Assert.That(scenario.Board.GetGates().Single().IsOpen, Is.False);
            List<GameEvent> events = scenario.Execute("WNNNEE");
            Assert.That(events.OfType<ExitReachedEvent>().Single().ExitId, Is.EqualTo(new EntityId(151)));
            Assert.That(scenario.Run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
        }

        /// <summary>Even a boulder abandoned against the upper boundary cannot seal the mandatory exit's second approach.</summary>
        [Test]
        public void BoulderAtUpperBoundaryCannotSealMandatoryExit()
        {
            var scenario = new Scenario();
            scenario.Execute("NEPPP");
            Assert.That(scenario.Board.GetEntities().Single(entity => entity.Id.Equals(new EntityId(10))).Position, Is.EqualTo(new GridPosition(2, 6)));
            Assert.That(scenario.Board.GetGates().Single().IsOpen, Is.False);
            List<GameEvent> events = scenario.Execute("EN");
            Assert.That(events.OfType<ExitReachedEvent>().Single().ExitId, Is.EqualTo(new EntityId(151)));
            Assert.That(scenario.Run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
        }

        /// <summary>Real enemy attacks and starvation terminate the actual layout without emitting an exit or any landmark reward.</summary>
        [TestCase(true)]
        [TestCase(false)]
        public void DeathAndStarvationCannotCompleteLandmark(bool starvation)
        {
            var scenario = new Scenario();
            if (starvation) scenario.Run.ConsumeFood(99);
            else
            {
                scenario.Execute("NNPNEECCC");
                scenario.Run.TakeDamage(90);
            }
            var events = new List<GameEvent>();
            for (int index = 0; index < 8 && !scenario.Controller.IsTerminal; index++)
                events.AddRange(scenario.Controller.Resolve(starvation ? (PlayerCommand)new MoveCommand(Direction.North) :
                    new WaitCommand()).Events);
            Assert.That(scenario.Controller.IsTerminal, Is.True);
            Assert.That(events.Any(gameEvent => gameEvent is PlayerStarvedEvent || gameEvent is PlayerDiedEvent), Is.True);
            Assert.That(events.OfType<ExitReachedEvent>(), Is.Empty);
            Assert.That(scenario.Run.CompletedLandmarks, Is.Empty);
        }

        /// <summary>Published selection uses the final inbound segment; earlier saved ordinary/legacy generation retains its exact address and seed.</summary>
        [TestCase("ordinary.board.v1", 1)]
        [TestCase("ordinary.board.v1", 2)]
        [TestCase("legacy.board.v1", 1)]
        public void OldActiveArrivalKeepsItsPublishedGeneration(string configurationId, int version)
        {
            WorldDefinition world = WorldDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            var run = new RunState("old-arrival", 64, new RunStateConfiguration(100, 100, 4, "forest.ridge"));
            world.TryGetEdge("road.ridge-radio-tower", out WorldEdgeDefinition edge);
            run.StartLeg(edge.EdgeId, edge.FromNodeId, edge.ToNodeId, edge.Segments.Select(segment => new RouteSegmentState(
                segment.SegmentId, "ordinary.board.v1", 2)).Select(segment => segment.SegmentId == "segment.ridge-radio-tower.arrival" ?
                    new RouteSegmentState(segment.SegmentId, configurationId, version) : segment));
            for (int index = 0; index < 4; index++)
            {
                run.TryConsumeIntro(run.ActiveLeg.LegId);
                run.TryAdvanceSegment(run.ActiveLeg.LegId, run.ActiveLeg.CurrentSegment.SegmentId);
            }
            BoardAddress before = BoardAddressFactory.CreateFromState(run);
            Assert.That(BoardAddressFactory.Create(run, world), Is.EqualTo(before));
            Assert.That(before.GenerationConfigurationId, Is.EqualTo(configurationId));
            Assert.That(before.GenerationVersion, Is.EqualTo(version));
            Assert.That(RadioTowerLandmark.Load().CreatePlacement(world).SegmentId, Is.EqualTo(before.SegmentId));
            Assert.That(run.ActiveLandmark, Is.Null);
        }

        /// <summary>Malformed authored rule scalars, null/duplicate bindings and ambiguous outcomes fail before publication.</summary>
        [TestCase("null")]
        [TestCase("numeric")]
        [TestCase("case")]
        [TestCase("orphan")]
        [TestCase("duplicate")]
        [TestCase("exit")]
        public void MalformedAuthoredLinksAreRejected(string invalid)
        {
            JObject json = JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json")));
            JToken variant = json["variants"][0];
            if (invalid == "null") variant["plates"] = JValue.CreateNull();
            else if (invalid == "numeric") variant["plates"][0]["occupants"] = 2;
            else if (invalid == "case") variant["gates"][0]["rule"] = "all";
            else if (invalid == "orphan") variant["gates"][0]["linkId"] = "B";
            else if (invalid == "duplicate") ((JArray)variant["plates"]).Add(variant["plates"][0].DeepClone());
            else json["outcomes"][1]["exitEntityId"] = json["outcomes"][0]["exitEntityId"].DeepClone();
            Assert.That(() => LandmarkTemplateJson.Deserialize(json.ToString(), RadioTowerLandmark.CreateCatalog()), Throws.Exception);
        }

        /// <summary>Missing authored special references fail closed rather than silently using ordinary floor or wall art.</summary>
        [TestCase("landmark.boulder")]
        [TestCase("landmark.plate")]
        [TestCase("landmark.gate")]
        [TestCase("axe.basic")]
        public void MissingSpecialPrefabNeverUsesOrdinaryFallback(string contentId)
        {
            RadioTowerLandmark empty = ScriptableObject.CreateInstance<RadioTowerLandmark>();
            try { Assert.Throws<System.InvalidOperationException>(() => empty.FindPrefab(contentId)); }
            finally { UnityEngine.Object.DestroyImmediate(empty); }
            Assert.That(RadioTowerLandmark.Load().FindPrefab(contentId), Is.Not.Null);
        }

        private sealed class Scenario
        {
            internal readonly RunState Run = new RunState("radio-tower-test", 64, new RunStateConfiguration(100, 100, 4, "forest.ridge"));
            internal readonly BoardState Board = Template().CreateBoard(Address(), "base").CreateState();
            internal readonly ToolInstanceState Axe;
            internal readonly TurnController Controller;
            private readonly InteractionQuery interactions;
            private readonly ToolUseQuery tools;

            internal Scenario()
            {
                Axe = Run.CreateToolInstance(ToolContentCatalog.Load()["axe.basic"]);
                Board.TryBindGroundTool(new GroundToolState(new EntityId(20), Axe.InstanceId, Axe.DefinitionId, Axe.Charges, RadioTowerLandmark.AxeSourceId));
                var phases = new MixedEnemyTurnPhases(new[] { new ShamblerState(new EntityId(2), new ShamblerDefinition(10)) },
                    System.Array.Empty<ListenerState>());
                phases.PlanInitialIntents(Board, new EntityId(1));
                ObstacleClearingRules clearing = AxeContent.LoadRules();
                ShovelRules digging = ShovelContent.LoadRules();
                interactions = ShovelContent.CreateInteractions(clearing, digging);
                tools = ShovelContent.CreateTools(clearing, digging);
                Controller = new TurnController(Board, Run, new EntityId(1),
                    new TurnResolver(ActionNoiseProfile.Prototype, interactions, tools, new ToolPickupQuery()), phases.Handlers);
            }

            internal List<GameEvent> Execute(string sequence)
            {
                var events = new List<GameEvent>();
                foreach (char step in sequence)
                {
                    PlayerCommand command;
                    if (step == 'P' || step == 'C') command = interactions.GetOptions(Board, Run, new EntityId(1))
                        .Single(option => option.TargetId.Equals(new EntityId(step == 'P' ? 10 : 11))).CreateCommand();
                    else if (step == 'A') command = tools.GetOptions(Board, Run, new EntityId(1))
                        .Single(option => option.TargetId.Equals(new EntityId(11))).CreateCommand();
                    else if (step == 'T') command = new ToolPickupQuery().GetOptions(Board, Run, new EntityId(1)).Single().Choices.Single();
                    else command = new MoveCommand(step == 'N' ? Direction.North : step == 'E' ? Direction.East :
                        step == 'W' ? Direction.West : Direction.South);
                    TurnResult result = Controller.Resolve(command);
                    Assert.That(result.Accepted, Is.True, "landmark.radio-tower/base seed=64 sequence=" + sequence + " step=" + step);
                    events.AddRange(result.Events);
                }
                return events;
            }
        }

        /// <summary>Explicitly authors build-included references and an optional axe view through Unity APIs, never during test setup.</summary>
        [MenuItem("HallowBlaze/Authoring/Rebuild Radio Tower References")]
        public static void BuildReferences()
        {
            const string folder = "Assets/GameData/Landmarks/Resources";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/GameData/Landmarks", "Resources");
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try
            {
                for (int horizontal = 0; horizontal < 32; horizontal++)
                    for (int vertical = 0; vertical < 32; vertical++)
                    {
                        bool handle = horizontal >= 13 && horizontal <= 16 && vertical >= 4 && vertical <= 25;
                        bool head = horizontal >= 7 && horizontal <= 24 && vertical >= 20 && vertical <= 27;
                        texture.SetPixel(horizontal, vertical, head ? new Color32(212, 226, 218, 255) :
                            handle ? new Color32(188, 137, 82, 255) : new Color32(0, 0, 0, 0));
                    }
                texture.Apply();
                File.WriteAllBytes("Assets/Sprites/LandmarkAxe.png", texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset("Assets/Sprites/LandmarkAxe.png", ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Sprites/LandmarkAxe.png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            var axe = new GameObject("Landmark axe");
            try
            {
                SpriteRenderer renderer = axe.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/LandmarkAxe.png");
                renderer.sortingLayerName = "Units";
                PrefabUtility.SaveAsPrefabAsset(axe, "Assets/Prefabs/Tiles/LandmarkAxe.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(axe); }
            const string path = folder + "/RadioTowerLandmark.asset";
            RadioTowerLandmark asset = AssetDatabase.LoadAssetAtPath<RadioTowerLandmark>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<RadioTowerLandmark>(); AssetDatabase.CreateAsset(asset, path); }
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("template").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/GameData/Landmarks/radio-tower-template.json");
            serialized.FindProperty("placement").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/GameData/Landmarks/radio-tower-placement.json");
            serialized.FindProperty("boulder").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/Boulder.prefab");
            serialized.FindProperty("plate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/PressurePlate.prefab");
            serialized.FindProperty("gate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/Gate.prefab");
            serialized.FindProperty("axe").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/LandmarkAxe.prefab");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static LandmarkTemplate Template() => LandmarkTemplateJson.Deserialize(File.ReadAllText(
            Path.Combine(Application.dataPath, "GameData", "Landmarks", "radio-tower-template.json")), RadioTowerLandmark.CreateCatalog());
        private static BoardAddress Address() => new BoardAddress("radio-tower-test", "leg.1", "road.ridge-radio-tower",
            "segment.ridge-radio-tower.arrival", 5, RadioTowerLandmark.ConfigurationId, 1, 64);
    }
}