using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.World;
using HallowBlaze.Editor.BoardAuthoring;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Checks Editor authoring preserves the runtime schema and never writes published content.</summary>
    public sealed class BoardAuthoringDocumentTests
    {
        private string root;
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>Uses only a unique external temporary directory for all authored test files.</summary>
        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "HB-M68a-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        /// <summary>Removes only the test-owned external draft directory.</summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        /// <summary>All variants, bindings and optional absence/null values survive deterministic template and placement writes.</summary>
        [Test]
        public void RuntimeReadersAcceptDeterministicLosslessRoundTrip()
        {
            var catalog = RadioTowerLandmark.CreateCatalog();
            JObject source = BoardAuthoringJson.ReadTemplate(Fixture("radio-tower-template.json"), catalog);
            source["outcomes"][0]["requiredToolDefinitionId"] = JValue.CreateNull();
            string first = BoardAuthoringJson.WriteTemplate(source, catalog);
            JObject restored = BoardAuthoringJson.ReadTemplate(first, catalog);
            AssertSameFields(source, restored);
            Assert.That(restored["variants"].Count(), Is.EqualTo(3));
            Assert.That(restored["outcomes"].Single(value => (string)value["id"] == "tower.arrival")["requiredToolDefinitionId"].Type,
                Is.EqualTo(JTokenType.Null));
            Assert.That(restored["outcomes"].Single(value => (string)value["id"] == "tower.supplies")["requiredToolDefinitionId"], Is.Null);
            source["variants"] = new JArray(((JArray)source["variants"]).Reverse().Select(value => value.DeepClone()));
            Assert.That(BoardAuthoringJson.WriteTemplate(source, catalog), Is.EqualTo(first));
            Assert.That(BoardAuthoringJson.WriteTemplate(restored, catalog), Is.EqualTo(first));
            WorldDefinition world = WorldDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            LandmarkTemplate template = LandmarkTemplateJson.Deserialize(first, catalog);
            JObject placement = BoardAuthoringJson.ReadPlacement(Fixture("radio-tower-placement.json"), world, template);
            string placed = BoardAuthoringJson.WritePlacement(placement, world, template);
            Assert.That(BoardAuthoringJson.WritePlacement(BoardAuthoringJson.ReadPlacement(placed, world, template), world, template), Is.EqualTo(placed));
            Assert.That(placement.Property("edgeId"), Is.Null);
        }

        /// <summary>Unknown fields never disappear through a transforming save.</summary>
        [Test]
        public void UnsupportedFieldsCannotBeReadOrWritten()
        {
            JObject source = JObject.Parse(Fixture("radio-tower-template.json"));
            source["editorMetadata"] = true;
            Assert.Throws<JsonSerializationException>(() => BoardAuthoringJson.ReadTemplate(source.ToString(), RadioTowerLandmark.CreateCatalog()));
            Assert.Throws<JsonSerializationException>(() => BoardAuthoringJson.WriteTemplate(source, RadioTowerLandmark.CreateCatalog()));
        }

        /// <summary>Draft edits/snapshots are detached and invalid intermediate data stays undoable rather than being lost.</summary>
        [Test]
        public void DetachedDocumentKeepsInvalidEditsAndExactUndoHistory()
        {
            var document = BoardTemplateDocument.ReadTemplate(Fixture("radio-tower-template.json"), RadioTowerLandmark.CreateCatalog());
            string original = document.ToJson();
            JObject detached = document.Snapshot();
            detached["templateVersion"] = 99;
            Assert.That(document.ToJson(), Is.EqualTo(original));
            JObject retained = null;
            document.Edit(data => { retained = data; data["templateVersion"] = 2; });
            retained["templateVersion"] = 100;
            Assert.That((int)document.Snapshot()["templateVersion"], Is.EqualTo(2));
            Assert.That(document.Undo(), Is.True);
            Assert.That(document.ToJson(), Is.EqualTo(original));
            Assert.That(document.Redo(), Is.True);
            document.Edit(data => data["variants"][0]["entities"][0]["id"] = "invalid");
            Assert.That(document.Diagnostics, Is.Not.Empty);
            Assert.Throws<InvalidOperationException>(() => document.ToJson());
            Assert.That(document.Undo(), Is.True);
            Assert.That((int)document.Snapshot()["templateVersion"], Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => document.Edit(data => { data["templateVersion"] = 77; throw new InvalidOperationException(); }));
            Assert.That((int)document.Snapshot()["templateVersion"], Is.EqualTo(2));
        }

        /// <summary>Full landmark validation permits only its declared optional reachability exemption and checks hidden variants too.</summary>
        [Test]
        public void ValidatesAllVariantsAndRetainsOwningRuleAndCell()
        {
            var document = BoardTemplateDocument.ReadTemplate(Fixture("radio-tower-template.json"), RadioTowerLandmark.CreateCatalog());
            Assert.That(document.Diagnostics, Is.Empty);
            document.Edit(data => data["configuration"]["pathLength"]["minimum"] = 30);
            Assert.That(document.Diagnostics.Any(error => error.Code == "PathLengthBudget" && error.Position.HasValue), Is.True);
            Assert.That(document.Diagnostics.Select(error => error.VariantId).Distinct().Count(), Is.EqualTo(3));
            document.Undo();
            document.Edit(data => data["variants"][2]["entities"][0]["x"] = 99);
            Assert.That(document.Diagnostics.Single().Message, Does.Contain("OutOfBounds").And.Contain("east-guard"));
            Assert.That(document.Diagnostics.Single().Position.Value.X, Is.EqualTo(99));
            Assert.That(document.Diagnostics.Single().Code, Is.EqualTo("OutOfBounds"));
            var located = document.Snapshot().SelectToken(document.Diagnostics.Single().FieldPath);
            Assert.That(located, Is.TypeOf<JObject>());
            Assert.That((int)located["id"], Is.EqualTo(1));
        }

        /// <summary>A new empty document is intentionally incomplete and cannot be saved or implicitly discarded.</summary>
        [Test]
        public void NewDraftAndDirtyNavigationRequireExplicitDecisions()
        {
            var draft = BoardTemplateDocument.NewTemplate("draft.test", "draft.test.board", RadioTowerLandmark.CreateCatalog());
            Assert.That(draft.IsDirty, Is.True);
            Assert.That(draft.Diagnostics, Is.Not.Empty);
            Assert.That(draft.SourcePath, Is.Null);
            Assert.Throws<InvalidOperationException>(() => draft.ToJson());
            Assert.That(draft.CanLeave(BoardDocumentDecision.Cancel), Is.False);
            Assert.That(draft.CanLeave(BoardDocumentDecision.Save, () => false), Is.False);
            Assert.That(draft.CanLeave(BoardDocumentDecision.Save, () => true), Is.False);
            Assert.That(draft.CanLeave(BoardDocumentDecision.Discard), Is.True);
            Assert.That(draft.IsDirty, Is.True);
        }

        /// <summary>Successful Save/Save As retain IDs, clear only their document's dirty state and keep undo history.</summary>
        [Test]
        public void SafeSaveAndSaveAsPreserveIdentityAndHistory()
        {
            var store = new BoardDocumentStore(root, ProjectRoot);
            var draft = EditableDraft();
            string first = Path.Combine(root, "first.json");
            Assert.That(store.Save(draft, first).Succeeded, Is.True);
            Assert.That(draft.IsDirty, Is.False);
            string original = draft.ToJson();
            draft.Edit(data => data["templateVersion"] = 2);
            Assert.That(draft.IsDirty, Is.True);
            Assert.That(store.Save(draft).Succeeded, Is.True);
            Assert.That(draft.IsDirty, Is.False);
            Assert.That(draft.Undo(), Is.True);
            Assert.That(draft.IsDirty, Is.True);
            Assert.That(draft.ToJson(), Is.EqualTo(original));
            Assert.That(draft.Redo(), Is.True);
            Assert.That(draft.IsDirty, Is.False);
            string second = Path.Combine(root, "second.json");
            Assert.That(store.Save(draft, second).Succeeded, Is.True);
            Assert.That(File.ReadAllBytes(second), Is.EqualTo(File.ReadAllBytes(first)));
            Assert.That(draft.SourcePath, Is.EqualTo(second));
            Assert.That(store.OpenTemplate(second, RadioTowerLandmark.CreateCatalog()).Snapshot()["templateId"].Value<string>(), Is.EqualTo("draft.test"));
            Assert.That(draft.CanLeave(BoardDocumentDecision.Cancel), Is.True);
            draft.Edit(data => data["templateVersion"] = 3);
            Assert.That(draft.CanLeave(BoardDocumentDecision.Save, () => store.Save(draft).Succeeded), Is.True);
            Assert.That(draft.IsDirty, Is.False);
        }

        /// <summary>Cancel, I/O failures and staged corruption leave prior bytes and all document bookkeeping unchanged.</summary>
        [TestCase("cancel")]
        [TestCase("permission")]
        [TestCase("disk-full")]
        [TestCase("replace")]
        [TestCase("corrupt-stage")]
        public void FailedSavePreservesOldFileAndDirtyHistory(string failure)
        {
            var store = new BoardDocumentStore(root, ProjectRoot);
            var draft = EditableDraft();
            string target = Path.Combine(root, "draft.json");
            Assert.That(store.Save(draft, target).Succeeded, Is.True);
            byte[] original = File.ReadAllBytes(target);
            string fingerprint = draft.FileFingerprint;
            draft.Edit(data => data["templateVersion"] = 2);
            Action<string, byte[]> writer = null;
            Action<string, string> installer = null;
            if (failure == "permission") writer = (path, bytes) => throw new UnauthorizedAccessException("Test permission failure");
            if (failure == "disk-full") writer = (path, bytes) => { File.WriteAllBytes(path, new byte[] { 1 }); throw new IOException("Test disk full"); };
            if (failure == "corrupt-stage") writer = (path, bytes) => File.WriteAllBytes(path, new byte[] { 1, 2 });
            if (failure == "replace") installer = (path, destination) => throw new IOException("Test replacement failure");
            var failing = new BoardDocumentStore(root, ProjectRoot, writeTemporary: writer, replace: installer);
            BoardDocumentSaveResult result = failing.Save(draft, cancellationToken: new CancellationToken(failure == "cancel"));
            Assert.That(result.Status, Is.EqualTo(failure == "cancel" ? BoardDocumentSaveStatus.Cancelled : BoardDocumentSaveStatus.IoError));
            Assert.That(result.Message, Does.Contain(target));
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(original));
            Assert.That(draft.FileFingerprint, Is.EqualTo(fingerprint));
            Assert.That(draft.SourcePath, Is.EqualTo(target));
            Assert.That(draft.IsDirty, Is.True);
            Assert.That(draft.CanUndo, Is.True);
            Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
            Assert.That(store.Save(draft).Succeeded, Is.True, result.Message);
        }

        /// <summary>Save As overwrite needs consent plus the exact preview, and normal Save never wins over outside edits.</summary>
        [Test]
        public void ExternalChangesAndSaveAsConflictsAreNeverOverwritten()
        {
            var store = new BoardDocumentStore(root, ProjectRoot);
            var draft = EditableDraft();
            string target = Path.Combine(root, "draft.json");
            File.WriteAllText(target, "outside");
            string outside = store.CaptureFingerprint(target);
            Assert.That(store.Save(draft, target).Status, Is.EqualTo(BoardDocumentSaveStatus.Conflict));
            Assert.That(store.Save(draft, target, true).Status, Is.EqualTo(BoardDocumentSaveStatus.Conflict));
            Assert.That(store.Save(draft, target, true, outside).Succeeded, Is.True);
            draft.Edit(data => data["templateVersion"] = 2);
            File.WriteAllText(target, "changed again");
            Assert.That(store.Save(draft).Status, Is.EqualTo(BoardDocumentSaveStatus.Conflict));
            Assert.That(File.ReadAllText(target), Is.EqualTo("changed again"));
            File.Delete(target);
            Assert.That(store.Save(draft).Status, Is.EqualTo(BoardDocumentSaveStatus.Conflict));
            Assert.That(draft.IsDirty, Is.True);
            Assert.That(store.Save(draft, Path.Combine(root, "new.json")).Succeeded, Is.True);
        }

        /// <summary>Outside changes or cancellation during staging are checked again immediately before atomic installation.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ChangesDuringStagingAbortInstallation(bool cancel)
        {
            var store = new BoardDocumentStore(root, ProjectRoot);
            var draft = EditableDraft();
            string target = Path.Combine(root, "draft.json");
            Assert.That(store.Save(draft, target).Succeeded, Is.True);
            string before = File.ReadAllText(target);
            draft.Edit(data => data["templateVersion"] = 2);
            using (var cancellation = new CancellationTokenSource())
            {
                var changed = new BoardDocumentStore(root, ProjectRoot, writeTemporary: (path, bytes) =>
                {
                    File.WriteAllBytes(path, bytes);
                    if (cancel) cancellation.Cancel();
                    else File.WriteAllText(target, "external edit during stage");
                });
                Assert.That(changed.Save(draft, cancellationToken: cancellation.Token).Status,
                    Is.EqualTo(cancel ? BoardDocumentSaveStatus.Cancelled : BoardDocumentSaveStatus.Conflict));
                Assert.That(File.ReadAllText(target), Is.EqualTo(cancel ? before : "external edit during stage"));
                Assert.That(draft.IsDirty, Is.True);
                Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
            }
        }

        /// <summary>Published originals, project files, player data and paths escaping the draft root cannot be written.</summary>
        [Test]
        public void ProtectedOriginalRequiresForkAndWritableTargetsStayFenced()
        {
            var store = new BoardDocumentStore(root, ProjectRoot);
            string original = Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json");
            byte[] bytes = File.ReadAllBytes(original);
            var published = store.OpenTemplate(original, RadioTowerLandmark.CreateCatalog());
            Assert.That(published.IsReadOnly, Is.True);
            Assert.That(published.IsDirty, Is.False);
            Assert.Throws<InvalidOperationException>(() => published.Edit(data => data["templateVersion"] = 2));
            Assert.That(store.Save(published, Path.Combine(root, "copy.json")).Status, Is.EqualTo(BoardDocumentSaveStatus.Protected));
            var fork = published.Fork("draft.test", "draft.test.board", RadioTowerLandmark.CreateCatalog());
            Assert.That(store.Save(fork, Path.Combine(root, "fork.json")).Succeeded, Is.True);
            Assert.That(store.Save(fork, original, true, store.CaptureFingerprint(original)).Status, Is.EqualTo(BoardDocumentSaveStatus.Protected));
            Assert.That(store.Save(fork, Path.Combine(root, "..", "escape.json")).Status, Is.EqualTo(BoardDocumentSaveStatus.Protected));
            Assert.That(store.Save(fork, Path.Combine(root, "not-json.txt")).Status, Is.EqualTo(BoardDocumentSaveStatus.Protected));
            Assert.That(File.ReadAllBytes(original), Is.EqualTo(bytes));
            Assert.Throws<ArgumentException>(() => new BoardDocumentStore(ProjectRoot, ProjectRoot));
            Assert.Throws<ArgumentException>(() => new BoardDocumentStore(Application.persistentDataPath, ProjectRoot));
        }

        /// <summary>Optional arrays and nullable bindings retain absence, empty and explicit null instead of being normalized away.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OptionalFieldPresenceSurvivesRealFileRoundTrip(bool explicitEmptyAndNull)
        {
            JObject source = JObject.Parse(Fixture("radio-tower-template.json"));
            foreach (JObject variant in source["variants"])
            {
                foreach (JObject entity in variant["entities"])
                    if ((string)entity["contentId"] == "landmark.plate" || (string)entity["contentId"] == "landmark.gate")
                        entity["contentId"] = "legacy.floor";
                foreach (string field in new[] { "plates", "gates" })
                {
                    if (explicitEmptyAndNull) variant[field] = new JArray();
                    else variant.Remove(field);
                }
                foreach (JObject item in variant["items"])
                {
                    if (explicitEmptyAndNull) item["sourceId"] = JValue.CreateNull();
                    else item.Remove("sourceId");
                }
            }
            foreach (JObject outcome in source["outcomes"])
                foreach (string field in new[] { "requiredToolDefinitionId", "guaranteedToolInstanceId" })
                {
                    if (explicitEmptyAndNull) outcome[field] = JValue.CreateNull();
                    else outcome.Remove(field);
                }
            var document = BoardTemplateDocument.ReadTemplate(source.ToString(), RadioTowerLandmark.CreateCatalog());
            var store = new BoardDocumentStore(root, ProjectRoot);
            string target = Path.Combine(root, "optional.json");
            Assert.That(store.Save(document, target).Succeeded, Is.True);
            AssertSameFields(source, store.OpenTemplate(target, RadioTowerLandmark.CreateCatalog()).Snapshot());
        }

        /// <summary>Malformed or unsupported input cannot replace a working document or become a lossy editable conversion.</summary>
        [TestCase("duplicate-field")]
        [TestCase("case")]
        [TestCase("schema")]
        [TestCase("dimensions")]
        [TestCase("listener")]
        [TestCase("float")]
        [TestCase("string-version")]
        [TestCase("overflow")]
        [TestCase("null-array")]
        [TestCase("invalid-variant-id")]
        public void InvalidImportsLeaveTheCurrentDocumentUnchanged(string invalid)
        {
            var current = EditableDraft();
            current.Edit(data => data["templateVersion"] = 2);
            string before = current.ToJson();
            JObject source = JObject.Parse(Fixture("radio-tower-template.json"));
            switch (invalid)
            {
                case "case": source["TemplateId"] = source["templateId"]; source.Remove("templateId"); break;
                case "schema": source["schemaVersion"] = 2; break;
                case "dimensions": source["width"] = 12; break;
                case "listener": source["variants"][0]["entities"][1]["contentId"] = "enemy.listener"; break;
                case "float": source["variants"][0]["entities"][0]["id"] = 1.0; break;
                case "string-version": source["templateVersion"] = "1"; break;
                case "overflow": source["templateVersion"] = (long)int.MaxValue + 1; break;
                case "null-array": source["variants"][0]["plates"] = JValue.CreateNull(); break;
                case "invalid-variant-id": source["variants"][0]["id"] = new JObject(); break;
            }
            string input = source.ToString();
            if (invalid == "duplicate-field") input = input.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1");
            string path = Path.Combine(root, "invalid.json");
            File.WriteAllText(path, input);
            var store = new BoardDocumentStore(root, ProjectRoot);
            Assert.That(() => store.OpenTemplate(path, RadioTowerLandmark.CreateCatalog()), Throws.InstanceOf<JsonException>());
            Assert.That(File.ReadAllText(path), Is.EqualTo(input));
            Assert.That(current.ToJson(), Is.EqualTo(before));
            Assert.That(current.IsDirty && current.CanUndo, Is.True);
            if (invalid != "duplicate-field")
                Assert.That(BoardAuthoringValidation.Template(source, RadioTowerLandmark.CreateCatalog()), Is.Not.Empty);
        }

        /// <summary>Invalid board/link/resource inputs are rejected by their existing owners, without saving or repairing the draft.</summary>
        [TestCase("missing-start", "MissingStart")]
        [TestCase("multiple-start", "MultipleStarts")]
        [TestCase("start-exit", "StartExitOverlap")]
        [TestCase("duplicate-id", "DuplicateEntity")]
        [TestCase("layer-conflict", "LayerConflict")]
        [TestCase("missing-outcome", "outcome")]
        [TestCase("item-binding", "InvalidItemBinding")]
        [TestCase("item-family", "family")]
        [TestCase("source", "Fixed-source")]
        [TestCase("plate-link", "Plate link has no gate")]
        [TestCase("gate-binding", "Unsupported gate terrain")]
        [TestCase("unbound-gate", "Unbound plate/gate")]
        [TestCase("guarantee", "guaranteed tool")]
        public void InvalidDraftsRetainOwningErrorsAndCannotSave(string invalid, string expected)
        {
            var document = EditableDraft();
            document.Edit(data =>
            {
                var variant = (JObject)data["variants"][0];
                var entities = (JArray)variant["entities"];
                var start = entities.OfType<JObject>().Single(entity => (string)entity["contentId"] == "legacy.player");
                switch (invalid)
                {
                    case "missing-start": start.Remove(); break;
                    case "multiple-start": var extra = (JObject)start.DeepClone(); extra["id"] = 500; extra["x"] = 2; entities.Add(extra); break;
                    case "start-exit": start["x"] = 3; start["y"] = 6; break;
                    case "duplicate-id": entities[1]["id"] = 1; break;
                    case "layer-conflict": entities[1]["x"] = 1; entities[1]["y"] = 1; break;
                    case "missing-outcome": data["outcomes"][0]["exitEntityId"] = 999; break;
                    case "item-binding": variant["items"][0]["entityId"] = 1; break;
                    case "item-family": variant["items"][0]["resourceFamilyId"] = "food"; break;
                    case "source": variant["items"][0]["sourceId"] = "unknown.source"; break;
                    case "plate-link": variant["plates"][0]["linkId"] = "orphan"; break;
                    case "gate-binding": variant["gates"][0]["terrainId"] = 127; break;
                    case "unbound-gate": variant.Remove("gates"); break;
                    case "guarantee": data["outcomes"][0]["requiredToolDefinitionId"] = "axe.basic"; data["outcomes"][0]["guaranteedToolInstanceId"] = "missing.tool"; break;
                }
            });
            Assert.That(document.Diagnostics.Any(error => error.Message.Contains(expected)), Is.True,
                string.Join("\n", document.Diagnostics.Select(error => error.Message)));
            var store = new BoardDocumentStore(root, ProjectRoot);
            string target = Path.Combine(root, "rejected.json");
            Assert.That(store.Save(document, target).Status, Is.EqualTo(BoardDocumentSaveStatus.Invalid));
            Assert.That(File.Exists(target), Is.False);
            Assert.That(document.IsDirty && document.CanUndo, Is.True);
            Assert.That(document.Undo(), Is.True);
            Assert.That(document.Diagnostics, Is.Empty);
        }

        /// <summary>Assignment persistence is deterministic and isolated from template edits and active campaign references.</summary>
        [Test]
        public void PlacementDocumentHasIndependentDirtyUndoAndSaveState()
        {
            var catalog = RadioTowerLandmark.CreateCatalog();
            var template = LandmarkTemplateJson.Deserialize(Fixture("radio-tower-template.json"), catalog);
            var world = WorldDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")));
            var placement = BoardTemplateDocument.ReadPlacement(Fixture("radio-tower-placement.json"), world, template);
            var board = EditableDraft();
            var store = new BoardDocumentStore(root, ProjectRoot);
            string target = Path.Combine(root, "placement.json");
            Assert.That(store.Save(placement, target).Succeeded, Is.True);
            Assert.That(board.IsDirty, Is.True);
            placement.Edit(data => data["variantId"] = "east-guard");
            Assert.That(placement.IsDirty, Is.True);
            Assert.That(store.Save(placement).Succeeded, Is.True);
            var reopened = store.OpenPlacement(target, world, template);
            Assert.That(reopened.IsPlacement, Is.True);
            Assert.That((string)reopened.Snapshot()["variantId"], Is.EqualTo("east-guard"));
            Assert.That(reopened.IsDirty, Is.False);
            placement.Edit(data => data["segmentId"] = "segment.ridge-radio-tower.entry");
            Assert.That(placement.Diagnostics.Single().Message, Does.Contain("final segment"));
            Assert.That(store.Save(placement).Status, Is.EqualTo(BoardDocumentSaveStatus.Invalid));
            Assert.That(placement.Undo(), Is.True);
            Assert.That(placement.IsDirty, Is.False);
            Assert.That(store.OpenPlacement(target, world, template).ToJson(), Is.EqualTo(reopened.ToJson()));
            Assert.That(board.IsDirty && !board.CanUndo, Is.True);
        }

        /// <summary>Explicitly registered external publication files stay read-only even inside the selected draft directory.</summary>
        [Test]
        public void ExternalPublicationsAndInvalidSaveAsRemainProtected()
        {
            string target = Path.Combine(root, "published.json");
            File.WriteAllText(target, Fixture("radio-tower-template.json"));
            var store = new BoardDocumentStore(root, ProjectRoot, new[] { target });
            string before = File.ReadAllText(target);
            Assert.That(store.OpenTemplate(target, RadioTowerLandmark.CreateCatalog()).IsReadOnly, Is.True);
            Assert.That(store.Save(EditableDraft(), target, true, store.CaptureFingerprint(target)).Status,
                Is.EqualTo(BoardDocumentSaveStatus.Protected));
            var invalid = BoardTemplateDocument.NewTemplate("draft.empty", "draft.empty.board", RadioTowerLandmark.CreateCatalog());
            Assert.That(store.Save(invalid, Path.Combine(root, "empty.json")).Status, Is.EqualTo(BoardDocumentSaveStatus.Invalid));
            Assert.That(invalid.SourcePath, Is.Null);
            Assert.That(File.ReadAllText(target), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
        }

        /// <summary>A document changed during staging cannot be marked clean using a different serialized snapshot.</summary>
        [Test]
        public void DraftChangesDuringStagingAbortWithoutClearingUndo()
        {
            var document = EditableDraft();
            var store = new BoardDocumentStore(root, ProjectRoot, writeTemporary: (path, bytes) =>
            {
                File.WriteAllBytes(path, bytes);
                document.Edit(data => data["templateVersion"] = 2);
            });
            string target = Path.Combine(root, "changed.json");
            Assert.That(store.Save(document, target).Status, Is.EqualTo(BoardDocumentSaveStatus.Conflict));
            Assert.That(File.Exists(target), Is.False);
            Assert.That(document.SourcePath, Is.Null);
            Assert.That(document.IsDirty && document.CanUndo, Is.True);
            Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
        }

        /// <summary>A cell-only error with several layers never pretends to identify one arbitrary entity.</summary>
        [Test]
        public void CellDiagnosticsDoNotGuessAnEntityAcrossLayers()
        {
            var document = EditableDraft();
            document.Edit(data => data["configuration"]["minimumSafeStartMoves"] = 4);
            var errors = document.Diagnostics.Where(error => error.Code == "SafeStartMovesBudget").ToArray();
            Assert.That(errors.Length, Is.EqualTo(3));
            Assert.That(errors.All(error => error.Position.HasValue && error.EntityId == null && error.Severity == "Error"), Is.True);
        }

        /// <summary>Windows device/extended-length aliases are rejected before they can bypass any protected-root comparison.</summary>
        [TestCase(@"\\?\")]
        [TestCase(@"\\.\")]
        [TestCase(@"\??\")]
        [TestCase(@"\\??\")]
        [TestCase("//?/")]
        public void DevicePathsCannotAliasProtectedRootsOrPublications(string prefix)
        {
            Assert.Throws<SecurityException>(() => new BoardDocumentStore(prefix + ProjectRoot, ProjectRoot));
            Assert.Throws<SecurityException>(() => new BoardDocumentStore(prefix + Application.persistentDataPath, ProjectRoot));
            Assert.Throws<SecurityException>(() => new BoardDocumentStore(root, prefix + ProjectRoot));
            string publication = Path.Combine(root, "published.json");
            File.WriteAllText(publication, Fixture("radio-tower-template.json"));
            byte[] original = File.ReadAllBytes(publication);
            var store = new BoardDocumentStore(root, ProjectRoot, new[] { publication });
            Assert.Throws<SecurityException>(() => store.OpenTemplate(prefix + publication, RadioTowerLandmark.CreateCatalog()));
            Assert.Throws<SecurityException>(() => store.CaptureFingerprint(prefix + publication));
            Assert.That(store.Save(EditableDraft(), prefix + publication, true, store.CaptureFingerprint(publication)).Status,
                Is.EqualTo(BoardDocumentSaveStatus.Protected));
            Assert.That(File.ReadAllBytes(publication), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(root), Has.Length.EqualTo(1));
        }

        /// <summary>Constructor contexts are not JSON paths; available variant paths resolve even when no cell was supplied.</summary>
        [Test]
        public void ConstructorErrorWithoutCellUsesTheActualVariantPath()
        {
            var document = EditableDraft();
            document.Edit(data => data["variants"][0]["plates"][0]["linkId"] = "orphan");
            var diagnostic = document.Diagnostics.Single();
            Assert.That(diagnostic.Position, Is.Null);
            Assert.That(diagnostic.VariantId, Is.EqualTo("base"));
            Assert.That(diagnostic.FieldPath, Is.EqualTo("variants[0]"));
            Assert.That(document.Snapshot().SelectToken(diagnostic.FieldPath), Is.TypeOf<JObject>());
            document.Undo();
            document.Edit(data => data["templateVersion"] = "invalid");
            Assert.That(document.Diagnostics.Single().FieldPath, Is.EqualTo("templateVersion"));
            Assert.That(document.Snapshot().SelectToken(document.Diagnostics.Single().FieldPath), Is.Not.Null);
        }

        /// <summary>SUBST-like mappings are refused, while two letters naming one real volume cannot hide a protected directory or publication.</summary>
        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public void DriveAliasesCannotHideProtectedRootsOrPublications(bool wholeVolumeAlias)
        {
            string drive = UnusedDrive();
            var volume = new StringBuilder(32768);
            string rootDrive = Path.GetPathRoot(root);
            Assert.That(QueryDosDevice(rootDrive.Substring(0, 2), volume, volume.Capacity), Is.GreaterThan(0));
            string nativeTarget = wholeVolumeAlias ? volume.ToString().Split('\0')[0] : @"\??\" + root;
            bool acquired = DefineDosDevice(0x9, drive, nativeTarget);
            Assert.That(acquired, Is.True, "Could not create the test-owned temporary drive alias.");
            try
            {
                string aliasRoot = wholeVolumeAlias ? drive + "\\" + root.Substring(rootDrive.Length) : drive + "\\";
                string publication = Path.Combine(root, "published.json");
                string aliasPublication = Path.Combine(aliasRoot, "published.json");
                File.WriteAllText(publication, Fixture("radio-tower-template.json"));
                byte[] before = File.ReadAllBytes(publication);
                if (wholeVolumeAlias)
                    Assert.Throws<ArgumentException>(() => new BoardDocumentStore(aliasRoot, root));
                else
                    Assert.Throws<SecurityException>(() => new BoardDocumentStore(aliasRoot, root));
                var store = new BoardDocumentStore(root, ProjectRoot, new[] { publication });
                if (wholeVolumeAlias)
                    Assert.That(store.OpenTemplate(aliasPublication, RadioTowerLandmark.CreateCatalog()).IsReadOnly, Is.True);
                else
                    Assert.Throws<SecurityException>(() => store.OpenTemplate(aliasPublication, RadioTowerLandmark.CreateCatalog()));
                Assert.That(store.Save(EditableDraft(), aliasPublication, true, store.CaptureFingerprint(publication)).Status,
                    Is.EqualTo(BoardDocumentSaveStatus.Protected));
                Assert.That(File.ReadAllBytes(publication), Is.EqualTo(before));
                Assert.That(Directory.GetFiles(root), Has.Length.EqualTo(1));
            }
            finally
            {
                if (acquired) Assert.That(DefineDosDevice(0xF, drive, nativeTarget), Is.True, "Could not remove the exact test-owned drive mapping.");
            }
        }

        /// <summary>Platforms without verified physical file identity cannot opt into a weaker lexical-only store.</summary>
        [TestCase(RuntimePlatform.WindowsEditor, true)]
        [TestCase(RuntimePlatform.OSXEditor, false)]
        [TestCase(RuntimePlatform.LinuxEditor, false)]
        [TestCase(RuntimePlatform.WindowsPlayer, false)]
        public void FileStorageIsExplicitlyLimitedToVerifiedEditorPlatform(RuntimePlatform platform, bool supported)
        {
            Assert.That(BoardDocumentStore.IsSupportedPlatform(platform), Is.EqualTo(supported));
        }

        /// <summary>Different filename casing on a case-insensitive filesystem cannot make a registered publication writable.</summary>
        [Test]
        [Platform("Win")]
        public void CaseAliasesRetainPublicationProtection()
        {
            string publication = Path.Combine(root, "published.json");
            File.WriteAllText(publication, Fixture("radio-tower-template.json"));
            string alternate = Path.Combine(root.ToUpperInvariant(), "PUBLISHED.JSON");
            byte[] original = File.ReadAllBytes(publication);
            var store = new BoardDocumentStore(root, ProjectRoot, new[] { publication });
            var aliased = store.OpenTemplate(alternate, RadioTowerLandmark.CreateCatalog());
            Assert.That(aliased.IsReadOnly, Is.True);
            Assert.That(store.Save(aliased).Status, Is.EqualTo(BoardDocumentSaveStatus.Protected));
            Assert.That(store.Save(EditableDraft(), alternate, true, store.CaptureFingerprint(publication)).Status,
                Is.EqualTo(BoardDocumentSaveStatus.Protected));
            Assert.That(File.ReadAllBytes(publication), Is.EqualTo(original));
        }

        private static string UnusedDrive()
        {
            for (char letter = 'Z'; letter >= 'P'; letter--)
            {
                string drive = letter + ":";
                var target = new StringBuilder(32768);
                if (QueryDosDevice(drive, target, target.Capacity) == 0 && Marshal.GetLastWin32Error() == 2) return drive;
            }
            Assert.Fail("An unused drive letter is required for the isolated alias regression.");
            return null;
        }

        [DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int maximumLength);

        [DllImport("kernel32.dll", EntryPoint = "DefineDosDeviceW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DefineDosDevice(uint flags, string deviceName, string targetPath);

        /// <summary>Serialization is invariant to culture, enumeration and cosmetic RNG state, and Editor assemblies never enter Player builds.</summary>
        [Test]
        public void SerializationIsCultureIndependentAndAssemblyIsEditorOnly()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var randomBefore = UnityEngine.Random.state;
            try
            {
                var document = EditableDraft();
                string expected = document.ToJson();
                CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
                Assert.That(document.ToJson(), Is.EqualTo(expected));
                Assert.That(UnityEngine.Random.state, Is.EqualTo(randomBefore));
                Assert.That(UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player)
                    .Any(assembly => assembly.name == "HallowBlaze.Editor.BoardAuthoring"), Is.False);
                Assert.That(UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor)
                    .Any(assembly => assembly.name == "HallowBlaze.Editor.BoardAuthoring"), Is.True);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                UnityEngine.Random.state = randomBefore;
            }
        }

        private static void AssertSameFields(JToken expected, JToken actual)
        {
            Assert.That(actual.Type, Is.EqualTo(expected.Type), expected.Path);
            if (expected is JObject expectedObject)
            {
                Assert.That(((JObject)actual).Properties().Select(property => property.Name),
                    Is.EquivalentTo(expectedObject.Properties().Select(property => property.Name)), expected.Path);
                foreach (JProperty property in expectedObject.Properties()) AssertSameFields(property.Value, actual[property.Name]);
            }
            else if (expected is JArray expectedArray)
            {
                var expectedRows = expectedArray.OrderBy(ArrayKey, StringComparer.Ordinal).ToArray();
                var actualRows = ((JArray)actual).OrderBy(ArrayKey, StringComparer.Ordinal).ToArray();
                Assert.That(actualRows.Length, Is.EqualTo(expectedRows.Length), expected.Path);
                for (int index = 0; index < expectedRows.Length; index++) AssertSameFields(expectedRows[index], actualRows[index]);
            }
            else Assert.That(JToken.DeepEquals(expected, actual), Is.True, expected.Path);
        }

        private static string ArrayKey(JToken token)
        {
            if (!(token is JObject row)) return token.ToString(Formatting.None);
            return (row["id"] ?? row["instanceId"] ?? row["terrainId"] ?? row["resourceFamilyId"]).ToString();
        }

        private static BoardTemplateDocument EditableDraft() => BoardTemplateDocument
            .ReadTemplate(Fixture("radio-tower-template.json"), RadioTowerLandmark.CreateCatalog())
            .Fork("draft.test", "draft.test.board", RadioTowerLandmark.CreateCatalog());

        private static string Fixture(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "GameData/Landmarks", name));
    }
}