using System;
using System.Linq;
using System.IO;
using HallowBlaze.Core.World;
using UnityEngine;
using UnityEditor;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.State;
using HallowBlaze.Editor.BoardAuthoring;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Checks the owner's minimal shared board and schema-1 compatibility without scene or player-data writes.</summary>
    public sealed class UnifiedBoardAuthoringTests
    {
        /// <summary>An open floor/player/exit board is valid without an artificial second goal or generator perimeter.</summary>
        [Test]
        public void SingleExitOpenBoardIsAcceptedWithoutWeakeningLegacyReader()
        {
            JObject data = Minimal();
            BoardTemplate template = BoardTemplateJson.Deserialize(data.ToString(), RadioTowerLandmark.CreateCatalog());
            var board = template.CreateBoard(new BoardAddress("shared-test", "leg.1", "route", "segment", 1,
                template.Config.Id, template.Config.Version, 17), "base");
            Assert.That(board.IsShared, Is.True);
            Assert.That(board.Validate(RadioTowerLandmark.CreateCatalog()).IsValid, Is.True);
            Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => LandmarkTemplateJson.Deserialize(data.ToString(), RadioTowerLandmark.CreateCatalog()));
            data["schemaVersion"] = 1;
            Assert.Throws<ArgumentException>(() => LandmarkTemplateJson.Deserialize(data.ToString(), RadioTowerLandmark.CreateCatalog()));
        }

        /// <summary>Fruit rewards depend on species, not legacy IDs, shared placement or the Radio Tower catalog.</summary>
        [TestCase("legacy.bush-food", 10)]
        [TestCase("ordinary.berries", 10)]
        [TestCase("legacy.food", 5)]
        [TestCase("landmark.radio-tower.rations", 5)]
        public void FruitRewardsAreIndependentOfMapAndLegacyIdentity(string contentId, int reward)
        {
            JObject data = Minimal();
            var allowed = (JArray)data["configuration"]["allowedContentIds"];
            if (!allowed.Values<string>().Contains(contentId)) allowed.Add(contentId);
            ((JArray)data["variants"][0]["entities"]).Add(new JObject
            { ["id"] = 74, ["layer"] = "Item", ["contentId"] = contentId, ["x"] = 1, ["y"] = 0 });
            ((JArray)data["variants"][0]["items"]).Add(new JObject
            { ["instanceId"] = "fruit.74", ["entityId"] = 74, ["itemDefinitionId"] = contentId,
                ["resourceFamilyId"] = "food", ["sourceId"] = JValue.CreateNull(), ["initiallyObservable"] = true });
            var template = BoardTemplateJson.Deserialize(data.ToString(), OrdinaryBoardContent.CreateAuthoringCatalog());
            var board = template.CreateBoard(new BoardAddress("fruit-test", "leg.1", "route", "segment", 1,
                template.Config.Id, template.Config.Version, 64), "base");
            Assert.That(board.Blueprint.Entities.Single(entity => entity.Definition.ContentId == contentId).Definition.AutomaticFoodReward, Is.EqualTo(reward));
            if (contentId == RadioTowerLandmark.RationsId)
            {
                var tower = RadioTowerLandmark.Load().ReadTemplate();
                var arrival = tower.CreateBoard(new BoardAddress("fruit-tower", "leg.1", "route", "segment", 1,
                    tower.Config.Id, tower.Config.Version, 64), "base");
                Assert.That(arrival.Blueprint.Entities.Single(entity => entity.Definition.ContentId == contentId).Definition.AutomaticFoodReward, Is.EqualTo(reward));
            }
        }

        /// <summary>Shared optional labels cannot prevent reading, saving or playing an otherwise executable board.</summary>
        [TestCase(1)]
        [TestCase(3)]
        public void SharedAllOptionalExitsRoundTripSaveAndRemainPlayable(int exitCount)
        {
            JObject data = Minimal();
            var entities = (JArray)data["variants"][0]["entities"];
            var outcomes = (JArray)data["outcomes"];
            outcomes[0]["optional"] = true;
            for (int index = 1; index < exitCount; index++)
            {
                int exitId = 10 + index * 8;
                entities.OfType<JObject>().Single(entity => (int)entity["id"] == exitId)["contentId"] = "ordinary.exit-terrain";
                outcomes.Add(new JObject { ["id"] = "exit." + exitId, ["name"] = "Exit " + index, ["exitEntityId"] = exitId, ["optional"] = true });
            }
            string root = Path.Combine(Path.GetTempPath(), "HB-SharedOptional-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var catalog = OrdinaryBoardContent.CreateAuthoringCatalog();
                var document = BoardTemplateDocument.ReadTemplate(data.ToString(), catalog);
                string snapshot = document.ToJson();
                var grid = new BoardGridView();
                grid.ChooseContent("legacy.wall");
                Assert.That(grid.Activate(document, "base", new HallowBlaze.Core.Board.Primitives.GridPosition(3, 3)), Is.True);
                Assert.That(document.Snapshot()["outcomes"].All(outcome => (bool)outcome["optional"]), Is.True);
                Assert.That(document.Undo(), Is.True);
                Assert.That(document.ToJson(), Is.EqualTo(snapshot));
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                string path = Path.Combine(root, "board.json");
                Assert.That(store.Save(document, path).Succeeded, Is.True);
                var reopened = store.OpenTemplate(path, catalog);
                Assert.That(reopened.ToJson(), Is.EqualTo(snapshot));
                var report = BoardSolverAdapter.CheckPlayable(reopened.ToJson(), "base", new BoardTestPlaySettings());
                Assert.That(report.Status, Is.EqualTo("Solved"), report.Message);
                Assert.That(report.Replayed, Is.True);
                data["schemaVersion"] = 1;
                Assert.Throws<ArgumentException>(() => LandmarkTemplateJson.Deserialize(data.ToString(), catalog));
            }
            finally { Directory.Delete(root, true); }
        }

        internal static JObject Minimal()
        {
            JObject data = BoardTemplateDocument.NewTemplate("draft.shared", "draft.shared.config", RadioTowerLandmark.CreateCatalog()).Snapshot();
            data["schemaVersion"] = 2;
            var entities = (JArray)data["variants"][0]["entities"];
            for (int horizontal = 0; horizontal < 8; horizontal++)
                for (int vertical = 0; vertical < 8; vertical++)
                    entities.Add(new JObject { ["id"] = 10 + horizontal * 8 + vertical, ["layer"] = "Terrain",
                        ["contentId"] = horizontal == 7 && vertical == 7 ? "ordinary.exit-terrain" : "legacy.floor", ["x"] = horizontal, ["y"] = vertical });
            entities.Add(new JObject { ["id"] = 1, ["layer"] = "Actor", ["contentId"] = "legacy.player", ["x"] = 0, ["y"] = 0 });
            data["outcomes"] = new JArray(new JObject { ["id"] = "exit.73", ["name"] = "Exit", ["exitEntityId"] = 73, ["optional"] = false });
            return data;
        }

        /// <summary>The painted exit owns its default completion and content enablement is one undo operation.</summary>
        [Test]
        public void SharedPaintingEnablesContentAndCreatesExitAtomically()
        {
            var document = BoardTemplateDocument.NewBoard("draft.paint", "draft.paint.config", OrdinaryBoardContent.CreateAuthoringCatalog());
            var grid = new BoardGridView();
            grid.ChooseContent("legacy.wall");
            Assert.That(grid.Activate(document, "base", new HallowBlaze.Core.Board.Primitives.GridPosition(3, 3)), Is.True);
            Assert.That(document.Snapshot()["configuration"]["allowedContentIds"].ToString(), Does.Contain("legacy.wall"));
            Assert.That(document.Undo(), Is.True);
            Assert.That(document.Snapshot()["configuration"]["allowedContentIds"].ToString(), Does.Not.Contain("legacy.wall"));
            grid.ChooseContent("ordinary.exit-terrain");
            grid.Activate(document, "base", new HallowBlaze.Core.Board.Primitives.GridPosition(7, 7));
            Assert.That(document.Snapshot()["outcomes"].Count(), Is.EqualTo(1));
            Assert.That((bool)document.Snapshot()["outcomes"][0]["optional"], Is.False);
            string painted = document.Snapshot().ToString();
            grid.Mode = BoardPaintMode.Erase;
            Assert.That(grid.Activate(document, "base", new HallowBlaze.Core.Board.Primitives.GridPosition(7, 7)), Is.True);
            Assert.That(document.Snapshot()["outcomes"].Count(), Is.Zero);
            Assert.That(document.Undo(), Is.True);
            Assert.That(document.Snapshot().ToString(), Is.EqualTo(painted));
            document.Edit(data => data["outcomes"][0]["name"] = "Authored destination");
            Assert.That(grid.Activate(document, "base", new HallowBlaze.Core.Board.Primitives.GridPosition(7, 7)), Is.False);
            Assert.That(document.Undo(), Is.True);
            Assert.That(document.Undo(), Is.True);
            Assert.That(document.Snapshot()["outcomes"].Count(), Is.Zero);
        }

        /// <summary>The same production inputs reproduce identical editable data and round-trip through the shared strict decoder.</summary>
        [Test]
        public void ProductionGenerationBecomesDeterministicEditableSnapshot()
        {
            var first = BoardGenerationAdapter.Generate(64, 1, "draft.generated", "draft.generated.config");
            var second = BoardGenerationAdapter.Generate(64, 1, "draft.generated", "draft.generated.config");
            Assert.That(first.ToJson(), Is.EqualTo(second.ToJson()));
            var reopened = BoardTemplateDocument.ReadTemplate(first.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
            Assert.That(reopened.ToJson(), Is.EqualTo(first.ToJson()));
            Assert.That(reopened.Snapshot()["variants"][0]["items"].Count(), Is.GreaterThan(0));
        }

        /// <summary>One shared map can target an intermediate segment or a concrete landmark without altering world generation identity.</summary>
        [Test]
        public void AssignmentRoundTripsAndRejectsVersionDriftForEveryPrototypeSegment()
        {
            var world = WorldDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            var template = BoardTemplateJson.ReadCompatible(Minimal().ToString(), OrdinaryBoardContent.CreateAuthoringCatalog());
            foreach (var edge in world.Edges)
                foreach (var segment in edge.Segments)
                {
                    var assignment = new BoardAssignment(world, segment.SegmentId, template, "base");
                    var reopened = BoardAssignment.Read(assignment.ToJson(), world, template);
                    var board = reopened.CreateBoard(new BoardAddress("assigned-test", "leg.1", edge.EdgeId, segment.SegmentId, 1,
                        segment.GenerationConfigurationId, segment.GenerationVersion, 64));
                    Assert.That(board.Blueprint.Address.SegmentId, Is.EqualTo(segment.SegmentId));
                    Assert.That(board.Blueprint.Config.Id, Is.EqualTo(segment.GenerationConfigurationId));
                    Assert.That(board.Validate(OrdinaryBoardContent.CreateAuthoringCatalog()).IsValid, Is.True);
                    var drift = JObject.Parse(assignment.ToJson());
                    drift["templateVersion"] = 999;
                    Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => BoardAssignment.Read(drift.ToString(), world, template));
                }
        }

        /// <summary>Readable filenames identify saved boards while independent exact bindings preserve reuse across routes.</summary>
        [Test]
        public void NamedLibraryListsBoardAndEveryExactRouteAssignment()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-NamedBoards-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var catalog = OrdinaryBoardContent.CreateAuthoringCatalog();
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                var world = WorldDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
                var document = BoardTemplateDocument.ReadTemplate(Minimal().ToString(), catalog);
                string path = store.NamedBoardPath("Northern clearing");
                Assert.That(store.Save(document, path).Succeeded, Is.True);
                var template = BoardTemplateJson.ReadCompatible(document.ToJson(), catalog);
                foreach (var edge in world.Edges.Take(2))
                {
                    var assignment = new BoardAssignment(world, edge.Segments[0].SegmentId, template, "base");
                    var binding = BoardTemplateDocument.ReadAssignment(assignment.ToJson(), world, template);
                    Assert.That(store.Save(binding, store.NamedAssignmentPath("Northern clearing", assignment)).Succeeded, Is.True);
                }
                var entries = store.ListBoards(world, catalog, out var diagnostics);
                Assert.That(diagnostics, Is.Empty);
                Assert.That(entries.Count, Is.EqualTo(2));
                Assert.That(entries.All(entry => entry.Name == "Northern clearing" && entry.TemplateId == template.Id && entry.Path == path), Is.True);
                Assert.That(entries.Select(entry => entry.Assignment.EdgeId), Is.EquivalentTo(world.Edges.Take(2).Select(edge => edge.EdgeId)));
                foreach (var entry in entries)
                {
                    Assert.That(store.OpenTemplate(entry.Path, catalog).ToJson(), Is.EqualTo(document.ToJson()));
                    Assert.That(store.OpenAssignment(entry.AssignmentPath, world, template).ToJson(), Is.EqualTo(entry.Assignment.ToJson()));
                }
                var imported = new BoardAssignment(world, world.Edges[2].Segments[0].SegmentId, template, "base");
                Assert.That(store.Save(BoardTemplateDocument.ReadAssignment(imported.ToJson(), world, template), Path.Combine(root, "old-manual-assignment.json")).Succeeded, Is.True);
                Assert.That(store.ListBoards(world, catalog, out diagnostics).Count, Is.EqualTo(3));
                Assert.That(diagnostics, Is.Empty);
                Assert.Throws<ArgumentException>(() => store.NamedBoardPath("../outside"));
                Assert.Throws<ArgumentException>(() => store.NamedBoardPath("Trailing "));
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>A geometric exit must also be executable with the selected food/health, and clearable barriers use actual interactions.</summary>
        [Test]
        public void PlayabilityUsesRealResourcesAndSolvesManualClearingWithoutFakeOutcomes()
        {
            JObject data = Minimal();
            var inputs = new BoardTestPlaySettings { Health = 100, Food = 100, Seed = 64 };
            Assert.That(BoardSolverAdapter.CheckPlayable(data.ToString(), "base", inputs).Replayed, Is.True);
            inputs.Food = 1;
            Assert.That(BoardSolverAdapter.CheckPlayable(data.ToString(), "base", inputs).Status, Is.Not.EqualTo("Solved"));
            inputs.Food = 100;
            var entities = (JArray)data["variants"][0]["entities"];
            foreach (JObject entity in entities.OfType<JObject>().ToArray())
                if ((string)entity["layer"] == "Terrain" && (int)entity["y"] != 0) entity.Remove();
            var exit = entities.OfType<JObject>().Single(entity => (int)entity["id"] == 66);
            exit["contentId"] = "ordinary.exit-terrain";
            data["outcomes"][0]["exitEntityId"] = 66;
            ((JArray)data["configuration"]["allowedContentIds"]).Add("legacy.wall");
            entities.Add(new JObject { ["id"] = 100, ["layer"] = "Obstacle", ["contentId"] = "legacy.wall", ["x"] = 1, ["y"] = 0 });
            Assert.That(BoardTemplateDocument.ReadTemplate(data.ToString(), OrdinaryBoardContent.CreateAuthoringCatalog()).Diagnostics, Is.Empty);
            var cleared = BoardSolverAdapter.CheckPlayable(data.ToString(), "base", inputs);
            Assert.That(cleared.Status, Is.EqualTo("Solved"), cleared.Message);
            Assert.That(cleared.Replayed, Is.True);
        }

        /// <summary>Shared acceptance requires one realizable exit, not whichever exit happened to be painted first.</summary>
        [Test]
        public void ReachableAlternativeExitIsSufficientForSharedBoardAcceptance()
        {
            JObject data = Minimal();
            var entities = (JArray)data["variants"][0]["entities"];
            foreach (JObject entity in entities.OfType<JObject>().Where(entity =>
                (int)entity["x"] == 6 && (int)entity["y"] == 7 || (int)entity["x"] == 7 && (int)entity["y"] == 6).ToArray())
                entity.Remove();
            entities.OfType<JObject>().Single(entity => (int)entity["id"] == 18)["contentId"] = "ordinary.exit-terrain";
            ((JArray)data["outcomes"]).Add(new JObject
            { ["id"] = "exit.18", ["name"] = "Alternative", ["exitEntityId"] = 18, ["optional"] = true });
            var report = BoardSolverAdapter.CheckPlayable(data.ToString(), "base", new BoardTestPlaySettings());
            Assert.That(report.Status, Is.EqualTo("Solved"), report.Message);
            Assert.That(report.Replayed, Is.True);
        }

        /// <summary>Palette uses source names and intact visuals; renamed prefab contents have no missing scripts.</summary>
        [Test]
        public void SourcePrefabNamesAndPaletteReferencesAreConsistent()
        {
            Assert.That(BoardGridView.ContentName("legacy.wall"), Is.EqualTo("Bush01"));
            Assert.That(BoardGridView.ContentName("ordinary.shambler.10"), Is.EqualTo("Shambler10"));
            Assert.That(BoardGridView.ContentName("ordinary.shambler.20"), Is.EqualTo("Shambler20"));
            Assert.That(BoardGridView.ContentName("ordinary.berries"), Is.EqualTo("Blueberries"));
            Assert.That(BoardGridView.ContentName("legacy.food"), Is.EqualTo("Raspberries"));
            Assert.That(BoardGridView.Palette.Count(id => BoardGridView.ContentName(id) == "Blueberries"), Is.EqualTo(1));
            Assert.That(BoardGridView.Palette.Count(id => BoardGridView.ContentName(id) == "Raspberries"), Is.EqualTo(1));
            var recoveredBrush = new BoardGridView();
            recoveredBrush.ChooseContent("legacy.bush-food");
            Assert.That(recoveredBrush.ContentId, Is.EqualTo("legacy.bush-food"));
            recoveredBrush.ChooseContent(RadioTowerLandmark.RationsId);
            Assert.That(recoveredBrush.ContentId, Is.EqualTo(RadioTowerLandmark.RationsId));
            Assert.That(BoardGridView.ContentName("axe.basic"), Is.EqualTo("Axe"));
            foreach (string content in BoardGridView.Palette) Assert.That(BoardGridView.Preview(content), Is.Not.Null, content);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero, path);
            }
        }

        /// <summary>A cancellable regeneration cannot lose dirty edits or undo, and malformed generation inputs cannot replace them.</summary>
        [Test]
        public void RegenerationRequiresConsentAndAssignmentUsesExactEditedSnapshot()
        {
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            try
            {
                Assert.That(window.TryGenerate(64, 1, "draft.generated", "draft.generated.config", () => BoardDocumentDecision.Discard), Is.True);
                window.Document.Edit(data => data["templateVersion"] = 3);
                var original = window.Document;
                string json = original.ToJson();
                Assert.That(window.TryGenerate(-17, 2, "draft.next", "draft.next.config", () => BoardDocumentDecision.Cancel), Is.False);
                Assert.That(window.Document, Is.SameAs(original));
                Assert.That(window.Document.ToJson(), Is.EqualTo(json));
                Assert.That(window.Document.CanUndo, Is.True);
                Assert.That(window.TryGenerate(-17, 0, "draft.next", "draft.next.config", () => BoardDocumentDecision.Discard), Is.False);
                Assert.That(window.Document, Is.SameAs(original));
                var world = WorldDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
                var template = BoardTemplateJson.ReadCompatible(json, OrdinaryBoardContent.CreateAuthoringCatalog());
                var segment = world.Edges[0].Segments[0];
                var binding = new BoardAssignment(world, segment.SegmentId, template, "base");
                var address = new BoardAddress("snapshot", "leg.1", world.Edges[0].EdgeId, segment.SegmentId, 1,
                    segment.GenerationConfigurationId, segment.GenerationVersion, 64);
                var before = binding.CreateBoard(address).Blueprint;
                window.Document.Edit(data => data["variants"][0]["entities"][0]["contentId"] = "ordinary.exit-terrain");
                Assert.That(binding.CreateBoard(address).Blueprint, Is.EqualTo(before));
            }
            finally { window.DiscardChanges(); UnityEngine.Object.DestroyImmediate(window); }
        }

        /// <summary>Opening a legacy publication is lossless; the explicit new fork adopts shared policy without an undo backdoor.</summary>
        [Test]
        public void ExplicitLegacyForkUsesSharedPolicyAndKeepsSourceUntouched()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-M69e-Fork-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            string path = Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json");
            string original = File.ReadAllText(path);
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                Assert.That(window.TryOpen(path, () => BoardDocumentDecision.Discard), Is.True);
                Assert.That((int)window.Document.Snapshot()["schemaVersion"], Is.EqualTo(1));
                Assert.That(window.TryFork("draft.shared-fork", "draft.shared-fork.config", () => BoardDocumentDecision.Discard), Is.True);
                Assert.That((int)window.Document.Snapshot()["schemaVersion"], Is.EqualTo(2));
                Assert.That(window.Document.CanUndo, Is.False);
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            }
            finally { window.DiscardChanges(); UnityEngine.Object.DestroyImmediate(window); Directory.Delete(root, true); }
        }

        /// <summary>Gate solutions use linked occupancy rules; a boulder trapped immediately before the exit is not accepted as a solution.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GateAndBlockedExitAreDistinguishedByRealRules(bool blocked)
        {
            JObject data = Minimal();
            var entities = (JArray)data["variants"][0]["entities"];
            foreach (JObject entity in entities.OfType<JObject>().ToArray())
                if ((string)entity["layer"] == "Terrain" && ((int)entity["y"] != 0 || (int)entity["x"] > 2)) entity.Remove();
            entities.OfType<JObject>().Single(entity => (int)entity["id"] == 26)["contentId"] = "ordinary.exit-terrain";
            data["outcomes"][0]["exitEntityId"] = 26;
            var allowed = (JArray)data["configuration"]["allowedContentIds"];
            if (blocked)
            {
                allowed.Add("landmark.boulder");
                entities.Add(new JObject { ["id"] = 100, ["layer"] = "Obstacle", ["contentId"] = "landmark.boulder", ["x"] = 1, ["y"] = 0 });
            }
            else
            {
                allowed.Add("landmark.plate"); allowed.Add("landmark.gate");
                entities.OfType<JObject>().Single(entity => (int)entity["id"] == 10)["contentId"] = "landmark.plate";
                entities.OfType<JObject>().Single(entity => (int)entity["id"] == 18)["contentId"] = "landmark.gate";
                data["variants"][0]["plates"] = new JArray(new JObject { ["terrainId"] = 10, ["linkId"] = "A", ["occupants"] = "Actors" });
                data["variants"][0]["gates"] = new JArray(new JObject { ["terrainId"] = 18, ["linkId"] = "A", ["rule"] = "All" });
            }
            var report = BoardSolverAdapter.CheckPlayable(data.ToString(), "base", new BoardTestPlaySettings { Health = 100, Food = 100 });
            Assert.That(report.Status, Is.EqualTo(blocked ? "NoSolution" : "Solved"), report.Message);
            Assert.That(report.Replayed, Is.EqualTo(!blocked));
        }
    }
}