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
        /// <summary>Pool labels use saved board names without Unity menu separators; filename changes cannot change exact selection identity.</summary>
        [Test]
        public void PoolCandidateLabelsUseSavedNamesWithoutChangingPublication()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-PoolNames-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                JObject data = Minimal();
                const string templateId = "draft.generated.79740a3d685a48d285df1139849c98a4";
                data["templateId"] = templateId;
                var board = BoardTemplateDocument.ReadTemplate(data.ToString(), OrdinaryBoardContent.CreateAuthoringCatalog());
                string path = store.NamedBoardPath("North clearing");
                Assert.That(store.Save(board, path).Succeeded, Is.True);
                string world = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
                var pool = new BoardPoolAuthoring();
                pool.Create(store, world, "road.shelter-warm-up", "named.route");
                string segment = WorldDefinitionJson.Deserialize(world).Edges.Single(edge => edge.EdgeId == "road.shelter-warm-up").Segments[1].SegmentId;
                pool.SetSource(segment, "pool");
                pool.AddEntry(segment, templateId, 1, "base");
                string publicationPath = Path.Combine(root, "route.pool.json");
                Assert.That(pool.Save(store, publicationPath).Succeeded, Is.True);
                var describe = typeof(BoardPoolAuthoring).GetMethod("DescribeEntry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var label = (GUIContent)describe.Invoke(pool, new object[] { templateId, 1, "base" });
                Assert.That(label.text, Is.EqualTo("North clearing | v1 | base"));
                Assert.That(label.tooltip, Is.EqualTo(templateId + "/1 | base"));
                Assert.That(label.text, Does.Not.Contain("/"));
                string manifest = pool.Document.ToJson();
                string bytes = File.ReadAllText(publicationPath);
                var before = pool.CaptureSaved(store, out string[] templates);
                var expected = BoardAuthoringTestContext.PreviewPoolRoute(world, before.ToJson(), templates, 64, 100, 100, false);
                File.Move(path, store.NamedBoardPath("Renamed clearing"));
                pool.RefreshLibrary(store);
                label = (GUIContent)describe.Invoke(pool, new object[] { templateId, 1, "base" });
                Assert.That(label.text, Is.EqualTo("Renamed clearing | v1 | base"));
                Assert.That(pool.Document.ToJson(), Is.EqualTo(manifest));
                Assert.That(pool.Document.IsDirty, Is.False);
                Assert.That(File.ReadAllText(publicationPath), Is.EqualTo(bytes));
                var after = pool.CaptureSaved(store, out templates);
                Assert.That(after.ContentHash, Is.EqualTo(before.ContentHash));
                var repeated = BoardAuthoringTestContext.PreviewPoolRoute(world, after.ToJson(), templates, 64, 100, 100, false);
                Assert.That(HallowBlaze.Core.Persistence.PersistenceJsonSerializer.SerializeRun(repeated),
                    Is.EqualTo(HallowBlaze.Core.Persistence.PersistenceJsonSerializer.SerializeRun(expected)));
                var reopened = new BoardPoolAuthoring();
                reopened.Open(store, publicationPath, world);
                Assert.That(((GUIContent)describe.Invoke(reopened, new object[] { templateId, 1, "base" })).text,
                    Is.EqualTo("Renamed clearing | v1 | base"));
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>Removing and recreating a pool across Save/Open never reuses its archived identity/version with different entries.</summary>
        [Test]
        public void RecreatedPoolRetainsVersionHistoryAcrossGeneratedSaveAndOpen()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-PoolRecreated-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                var first = BoardTemplateDocument.ReadTemplate(Minimal().ToString(), OrdinaryBoardContent.CreateAuthoringCatalog());
                JObject data = first.Snapshot();
                data["templateId"] = "draft.second";
                var second = BoardTemplateDocument.ReadTemplate(data.ToString(), OrdinaryBoardContent.CreateAuthoringCatalog());
                Assert.That(store.Save(first, store.NamedBoardPath("First")).Succeeded, Is.True);
                Assert.That(store.Save(second, store.NamedBoardPath("Second")).Succeeded, Is.True);
                string world = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
                var authoring = new BoardPoolAuthoring();
                authoring.Create(store, world, "road.shelter-warm-up", "recreated.route");
                string segment = WorldDefinitionJson.Deserialize(world).Edges.Single(edge => edge.EdgeId == "road.shelter-warm-up").Segments[1].SegmentId;
                authoring.SetSource(segment, "pool");
                authoring.AddEntry(segment, "draft.shared", 1, "base");
                string path = Path.Combine(root, "route.pool.json");
                Assert.That(authoring.Save(store, path).Succeeded, Is.True);
                var original = authoring.CaptureSaved(store, out _);
                authoring.SetSource(segment, "generated");
                Assert.That(authoring.Save(store).Succeeded, Is.True);
                var generated = authoring.CaptureSaved(store, out _);
                var reopened = new BoardPoolAuthoring();
                reopened.Open(store, path, world);
                reopened.SetSource(segment, "pool");
                reopened.AddEntry(segment, "draft.second", 1, "base");
                Assert.That(reopened.Save(store).Succeeded, Is.True);
                var recreated = reopened.CaptureSaved(store, out _);
                Assert.That((int)reopened.Document.Snapshot()["sources"][0]["poolVersion"], Is.GreaterThan(1));
                var archive = new BoardPoolPublication.Archive();
                Assert.That(() => { archive.Add(original); archive.Add(generated); archive.Add(recreated); }, Throws.Nothing);
                Assert.That(archive.Count, Is.EqualTo(3));
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>Missing files and failed saves never erase immutable content evidence before a changed same-version candidate returns.</summary>
        [Test]
        public void RefreshCannotForgetRemovedTemplateBeforeChangedVersionReturns()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-PoolRefreshHistory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                var board = BoardTemplateDocument.ReadTemplate(Minimal().ToString(), OrdinaryBoardContent.CreateAuthoringCatalog());
                string boardPath = store.NamedBoardPath("Candidate");
                Assert.That(store.Save(board, boardPath).Succeeded, Is.True);
                string world = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
                var authoring = new BoardPoolAuthoring();
                authoring.Create(store, world, "road.shelter-warm-up", "refresh.route");
                string segment = WorldDefinitionJson.Deserialize(world).Edges.Single(edge => edge.EdgeId == "road.shelter-warm-up").Segments[1].SegmentId;
                authoring.SetSource(segment, "pool");
                authoring.AddEntry(segment, "draft.shared", 1, "base");
                string publicationPath = Path.Combine(root, "route.pool.json");
                Assert.That(authoring.Save(store, publicationPath).Succeeded, Is.True);
                string publicationBytes = File.ReadAllText(publicationPath);
                File.Delete(boardPath);
                authoring.RefreshLibrary(store);
                Assert.That(authoring.Templates.Single(), Is.EqualTo(board.ToJson()));
                Assert.That(authoring.Save(store).Status, Is.EqualTo(BoardDocumentSaveStatus.Invalid));
                JObject changed = board.Snapshot();
                changed["outcomes"][0]["name"] = "Changed after removal";
                File.WriteAllText(boardPath, changed.ToString());
                Assert.That(() => authoring.RefreshLibrary(store), Throws.InvalidOperationException);
                Assert.That(() => authoring.Save(store), Throws.InvalidOperationException);
                Assert.That(() => authoring.CaptureSaved(store, out _), Throws.InvalidOperationException);
                Assert.That(File.ReadAllText(publicationPath), Is.EqualTo(publicationBytes));
                Assert.That(authoring.Templates.Single(), Is.EqualTo(board.ToJson()));
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>Reopening retains archived exact artifacts; changing a map beneath the same tuple cannot silently create a new publication hash.</summary>
        [Test]
        public void SavedPoolEnvelopeRetainsExactArtifactsAcrossReopen()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-PoolArchiveReopen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                var board = BoardTemplateDocument.ReadTemplate(Minimal().ToString(), OrdinaryBoardContent.CreateAuthoringCatalog());
                string boardPath = store.NamedBoardPath("Candidate");
                Assert.That(store.Save(board, boardPath).Succeeded, Is.True);
                string world = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
                var authoring = new BoardPoolAuthoring();
                authoring.Create(store, world, "road.shelter-warm-up", "archived.route");
                string segment = WorldDefinitionJson.Deserialize(world).Edges.Single(edge => edge.EdgeId == "road.shelter-warm-up").Segments[1].SegmentId;
                authoring.SetSource(segment, "pool");
                authoring.AddEntry(segment, "draft.shared", 1, "base");
                string path = Path.Combine(root, "route.pool.json");
                Assert.That(authoring.Save(store, path).Succeeded, Is.True);
                string hash = authoring.CaptureSaved(store, out _).ContentHash;
                var reopened = new BoardPoolAuthoring();
                reopened.Open(store, path, world);
                Assert.That(reopened.CaptureSaved(store, out _).ContentHash, Is.EqualTo(hash));
                JObject changed = board.Snapshot();
                changed["outcomes"][0]["name"] = "Different payload under the same version";
                File.WriteAllText(boardPath, changed.ToString());
                var afterChange = new BoardPoolAuthoring();
                afterChange.Open(store, path, world);
                Assert.That(afterChange.Document.ToJson(), Is.EqualTo(authoring.Document.ToJson()));
                Assert.That(afterChange.Templates.Single(), Is.EqualTo(board.ToJson()));
                Assert.That(() => afterChange.CaptureSaved(store, out _), Throws.InvalidOperationException);
                Assert.That(() => afterChange.RefreshLibrary(store), Throws.InvalidOperationException);
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>A missing live library candidate cannot be saved using stale captured data; the invalid draft and undo remain recoverable.</summary>
        [Test]
        public void PoolSaveRechecksLibraryWithoutDiscardingMembership()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-PoolMissingSave-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                var board = BoardTemplateDocument.ReadTemplate(Minimal().ToString(), OrdinaryBoardContent.CreateAuthoringCatalog());
                string path = store.NamedBoardPath("Candidate");
                Assert.That(store.Save(board, path).Succeeded, Is.True);
                string world = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
                var authoring = new BoardPoolAuthoring();
                authoring.Create(store, world, "road.shelter-warm-up", "live.route");
                string segment = WorldDefinitionJson.Deserialize(world).Edges.Single(edge => edge.EdgeId == "road.shelter-warm-up").Segments[1].SegmentId;
                authoring.SetSource(segment, "pool");
                authoring.AddEntry(segment, "draft.shared", 1, "base");
                File.Delete(path);
                Assert.That(authoring.Save(store, Path.Combine(root, "route.pool.json")).Status, Is.EqualTo(BoardDocumentSaveStatus.Invalid));
                Assert.That(authoring.Document.Snapshot()["sources"][0]["entries"].Count(), Is.EqualTo(1));
                Assert.That(authoring.Document.CanUndo, Is.True);
                Assert.That(authoring.Document.IsDirty, Is.True);
                Assert.That(File.Exists(Path.Combine(root, "route.pool.json")), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>Explicit reuse across two segment pools preserves the grid and exact versions, and removal/undo do not silently generate.</summary>
        [Test]
        public void PoolMembershipReuseAndSavedVersioningAreExplicit()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-PoolMembership-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var catalog = OrdinaryBoardContent.CreateAuthoringCatalog();
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                var board = BoardTemplateDocument.ReadTemplate(Minimal().ToString(), catalog);
                Assert.That(store.Save(board, store.NamedBoardPath("Reusable clearing")).Succeeded, Is.True);
                string grid = board.ToJson();
                string worldJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
                var edge = WorldDefinitionJson.Deserialize(worldJson).Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
                var authoring = new BoardPoolAuthoring();
                authoring.Create(store, worldJson, edge.EdgeId, "explicit.route");
                foreach (var segment in edge.Segments.Skip(1))
                {
                    authoring.SetSource(segment.SegmentId, "pool");
                    authoring.AddEntry(segment.SegmentId, "draft.shared", 1, "base");
                }
                Assert.That(authoring.Document.Snapshot()["sources"].Count(), Is.EqualTo(2));
                Assert.That(() => authoring.AddEntry(edge.Segments[1].SegmentId, "draft.shared", 1, "base"), Throws.ArgumentException);
                Assert.That(() => authoring.AddEntry(edge.Segments[1].SegmentId, "draft.shared", 99, "base"), Throws.ArgumentException);
                Assert.That(() => authoring.AddEntry(edge.Segments[1].SegmentId, "draft.shared", 1, "missing"), Throws.InstanceOf<ArgumentException>());
                Assert.That(authoring.Save(store, Path.Combine(root, "route.pool.json")).Succeeded, Is.True);
                string saved = authoring.Document.ToJson();
                authoring.RemoveEntry(edge.Segments[1].SegmentId, (JObject)authoring.Document.Snapshot()["sources"][0]["entries"][0]);
                Assert.That((int)authoring.Document.Snapshot()["publicationVersion"], Is.EqualTo(2));
                Assert.That(authoring.Document.Diagnostics, Is.Not.Empty);
                Assert.That(authoring.Document.Undo(), Is.True);
                Assert.That(authoring.Document.ToJson(), Is.EqualTo(saved));
                Assert.That(authoring.Document.IsDirty, Is.False);
                Assert.That(authoring.CaptureSaved(store, out _).ToJson(), Is.EqualTo(saved));
                Assert.That(authoring.Document.Undo(), Is.True);
                authoring.SetSource(edge.Segments[2].SegmentId, "generated");
                Assert.That((int)authoring.Document.Snapshot()["publicationVersion"], Is.EqualTo(2), "Undo behind the saved checkpoint must not reuse its version for changed content.");
                Assert.That(authoring.Save(store).Succeeded, Is.True);
                Assert.That(board.ToJson(), Is.EqualTo(grid));
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>Repeated preview is side-effect free and Start uses the identical run identity, seed and choices.</summary>
        [Test]
        public void PoolRoutePreviewAndStartUseIdenticalPinnedPlan()
        {
            string worldJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
            var world = WorldDefinitionJson.Deserialize(worldJson);
            var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
            string[] templates = { Minimal().ToString() };
            var document = BoardTemplateDocument.NewPool(worldJson, edge.EdgeId, "preview.route", templates, OrdinaryBoardContent.CreateAuthoringCatalog());
            document.Edit(data => ((JArray)data["sources"]).Add(new JObject
            {
                ["segmentId"] = edge.Segments[1].SegmentId, ["generationConfigurationId"] = edge.Segments[1].GenerationConfigurationId,
                ["generationVersion"] = edge.Segments[1].GenerationVersion, ["poolId"] = "middle.pool", ["poolVersion"] = 1,
                ["entries"] = new JArray(new JObject { ["templateId"] = "draft.shared", ["templateVersion"] = 1, ["variantId"] = "base" })
            }));
            string manifest = document.ToJson();
            string root = BoardAuthoringTestContext.CreateSaveRootPath();
            try
            {
                var first = BoardAuthoringTestContext.PreviewPoolRoute(worldJson, manifest, templates, 7, 88, 999, true);
                var repeated = BoardAuthoringTestContext.PreviewPoolRoute(worldJson, manifest, templates, 7, 88, 999, true);
                string expected = HallowBlaze.Core.Persistence.PersistenceJsonSerializer.SerializeRun(first);
                Assert.That(HallowBlaze.Core.Persistence.PersistenceJsonSerializer.SerializeRun(repeated), Is.EqualTo(expected));
                Assert.That(BoardAuthoringTestContext.IsRequested, Is.False);
                Assert.That(Directory.Exists(root), Is.False);
                var context = BoardAuthoringTestContext.PreparePoolRoute(worldJson, manifest, templates, 7, 88, 999, true, root);
                Assert.That(HallowBlaze.Core.Persistence.PersistenceJsonSerializer.SerializeRun(context.CreateInitialRun()), Is.EqualTo(expected));
                Assert.That(File.ReadAllText(Path.Combine(root, "pool-publication.json")), Is.EqualTo(manifest));
                Assert.That(() => BoardAuthoringTestContext.PreparePoolRoute(worldJson, manifest, templates, 8, 88, 999, true,
                    BoardAuthoringTestContext.CreateSaveRootPath()), Throws.InvalidOperationException);
                first.TryConsumeIntro(first.ActiveLeg.LegId);
                first.TryAdvanceSegment(first.ActiveLeg.LegId, first.ActiveLeg.CurrentSegment.SegmentId);
                var request = HallowBlaze.Core.Session.BoardRequest.CreateFromRun(first, world);
                Assert.That(context.IsSharedBoard(request), Is.True);
                Assert.That(context.CreateBoard(request, first).TemplateId, Is.EqualTo("draft.shared"));
                Assert.That(first.ActiveLandmark, Is.Null);
            }
            finally { BoardAuthoringTestContext.Cancel(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        /// <summary>Pool persistence and undo are independent of a reused board; stale files and invalid membership cannot be saved.</summary>
        [Test]
        public void PoolDraftRoundTripUndoAndConflictDoNotChangeGrid()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-PoolDraft-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string worldJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
                var world = WorldDefinitionJson.Deserialize(worldJson);
                var edge = world.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
                var catalog = OrdinaryBoardContent.CreateAuthoringCatalog();
                var board = BoardTemplateDocument.ReadTemplate(Minimal().ToString(), catalog);
                string gridState = board.ToJson();
                var pool = BoardTemplateDocument.NewPool(worldJson, edge.EdgeId, "draft.route-pool", new[] { board.ToJson() }, catalog);
                string generated = pool.ToJson();
                pool.Edit(data => ((JArray)data["sources"]).Add(new JObject
                {
                    ["segmentId"] = edge.Segments[1].SegmentId,
                    ["generationConfigurationId"] = edge.Segments[1].GenerationConfigurationId,
                    ["generationVersion"] = edge.Segments[1].GenerationVersion,
                    ["poolId"] = "pool.middle", ["poolVersion"] = 1,
                    ["entries"] = new JArray(new JObject { ["templateId"] = "draft.shared", ["templateVersion"] = 1, ["variantId"] = "base" })
                }));
                string pooled = pool.ToJson();
                Assert.That(pool.Undo(), Is.True);
                Assert.That(pool.ToJson(), Is.EqualTo(generated));
                Assert.That(pool.Redo(), Is.True);
                Assert.That(pool.ToJson(), Is.EqualTo(pooled));
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                string path = Path.Combine(root, "opening.pool.json");
                Assert.That(store.Save(pool, path).Succeeded, Is.True);
                var reopened = store.OpenPool(path, worldJson, new[] { board.ToJson() }, catalog);
                Assert.That(reopened.IsPool, Is.True);
                Assert.That(reopened.IsDirty, Is.False);
                Assert.That(reopened.ToJson(), Is.EqualTo(pooled));
                reopened.Edit(data => data["publicationVersion"] = 2);
                var capture = typeof(BoardTemplateDocument).GetMethod("CaptureEditorState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var restore = typeof(BoardTemplateDocument).GetMethod("RestoreEditorState", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                string recovery = capture.Invoke(reopened, null).ToString();
                var restored = (BoardTemplateDocument)restore.Invoke(null, new object[] { JObject.Parse(recovery), catalog });
                Assert.That(capture.Invoke(restored, null).ToString(), Is.EqualTo(recovery));
                File.AppendAllText(path, " ");
                Assert.That(store.Save(reopened).Status, Is.EqualTo(BoardDocumentSaveStatus.Conflict));
                Assert.That(reopened.IsDirty, Is.True);
                Assert.That(reopened.CanUndo, Is.True);
                reopened.Edit(data => ((JArray)data["sources"][0]["entries"]).RemoveAll());
                Assert.That(reopened.Diagnostics, Is.Not.Empty);
                Assert.That(store.Save(reopened, Path.Combine(root, "invalid.pool.json")).Status, Is.EqualTo(BoardDocumentSaveStatus.Invalid));
                Assert.That(board.ToJson(), Is.EqualTo(gridState));
                Assert.That(board.CanUndo, Is.False);
            }
            finally { Directory.Delete(root, true); }
        }

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