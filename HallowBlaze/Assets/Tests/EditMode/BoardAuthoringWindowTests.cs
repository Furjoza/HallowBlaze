using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Editor.BoardAuthoring;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies visual authoring gestures preserve the document contract and existing board identities.</summary>
    public sealed class BoardAuthoringWindowTests
    {
        /// <summary>North is screen top; labels and outside pixels cannot activate a board cell.</summary>
        [Test]
        public void CanvasUsesXYWithNorthAtTheTop()
        {
            var canvas = new Rect(20, 30, 320, 320);
            Assert.That(BoardGridView.TryCell(canvas, new Vector2(21, 31), out GridPosition top), Is.True);
            Assert.That(top, Is.EqualTo(new GridPosition(0, 7)));
            Assert.That(BoardGridView.TryCell(canvas, new Vector2(339, 349), out GridPosition bottom), Is.True);
            Assert.That(bottom, Is.EqualTo(new GridPosition(7, 0)));
            Assert.That(BoardGridView.CellRect(canvas, new GridPosition(3, 6)), Is.EqualTo(new Rect(140, 70, 40, 40)));
            Assert.That(BoardGridView.TryCell(canvas, new Vector2(340, 350), out _), Is.False);
        }

        /// <summary>Checking proves an exit without entering Play Mode, publishing recovery or modifying document/history.</summary>
        [Test]
        public void CheckingBoardPreservesDraftAndDoesNotStartPlay()
        {
            WithWindow((window, root) =>
            {
                string path = Path.Combine(root, "check.json");
                File.WriteAllText(path, UnifiedBoardAuthoringTests.Minimal().ToString());
                Assert.That(window.TryOpen(path, () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                window.Grid.ChooseContent("legacy.wall");
                Assert.That(window.ActivateCell(new GridPosition(3, 3)), Is.True);
                var capture = typeof(BoardTemplateDocument).GetMethod("CaptureEditorState", BindingFlags.Instance | BindingFlags.NonPublic);
                string before = capture.Invoke(window.Document, null).ToString();
                var report = window.CheckBoard();
                Assert.That(report.Status, Is.EqualTo("Solved"), report.Message);
                Assert.That(report.Replayed, Is.True);
                Assert.That(window.BoardCheck, Is.SameAs(report));
                Assert.That(capture.Invoke(window.Document, null).ToString(), Is.EqualTo(before));
                Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
                Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False);
            });
        }

        /// <summary>A previous proof cannot remain current after a layout, variant or runtime-input change.</summary>
        [TestCase("board")]
        [TestCase("variant")]
        [TestCase("seed")]
        [TestCase("health")]
        [TestCase("food")]
        [TestCase("axe")]
        public void BoardCheckExpiresWhenItsInputsChange(string changed)
        {
            WithWindow((window, root) =>
            {
                JObject data = UnifiedBoardAuthoringTests.Minimal();
                var alternate = (JObject)data["variants"][0].DeepClone();
                alternate["id"] = "alternate";
                ((JArray)data["variants"]).Add(alternate);
                string path = Path.Combine(root, "check.json");
                File.WriteAllText(path, data.ToString());
                Assert.That(window.TryOpen(path, () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                Assert.That(window.CheckBoard().Replayed, Is.True);
                var inputs = (BoardTestPlaySettings)typeof(BoardAuthoringWindow).GetField("testSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                switch (changed)
                {
                    case "board": window.Document.Edit(snapshot => snapshot["outcomes"][0]["name"] = "Changed exit"); break;
                    case "variant": Assert.That(window.SelectVariant("alternate"), Is.True); break;
                    case "seed": inputs.Seed++; break;
                    case "health": inputs.Health++; break;
                    case "food": inputs.Food++; break;
                    case "axe": inputs.EquipAxe = !inputs.EquipAxe; break;
                }
                Assert.That(window.BoardCheck, Is.Null, changed);
                Assert.That(window.CheckBoard().Replayed, Is.True);
                Assert.That(window.BoardCheck, Is.Not.Null);
            });
        }

        /// <summary>Invalid starting resources and an exhausted living player remain distinct failures without launching a game.</summary>
        [TestCase(0, 100, "Invalid")]
        [TestCase(1, 1, "NoSolution")]
        public void BoardCheckReportsStartingInputFailures(int health, int food, string status)
        {
            WithWindow((window, root) =>
            {
                string path = Path.Combine(root, "check.json");
                File.WriteAllText(path, UnifiedBoardAuthoringTests.Minimal().ToString());
                Assert.That(window.TryOpen(path, () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                var inputs = (BoardTestPlaySettings)typeof(BoardAuthoringWindow).GetField("testSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                inputs.Health = health;
                inputs.Food = food;
                var report = window.CheckBoard();
                Assert.That(report.Status, Is.EqualTo(status), report.Message);
                Assert.That(report.Replayed, Is.False);
                Assert.That(window.BoardCheck, Is.SameAs(report));
                Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
                Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False);
            });
        }

        /// <summary>A partial draft reports data errors without invoking gameplay or discarding the draft.</summary>
        [Test]
        public void BoardCheckReportsInvalidDraft()
        {
            WithWindow((window, root) =>
            {
                Assert.That(window.TryNew("draft.invalid-check", "draft.invalid-check.config", () => BoardDocumentDecision.Discard), Is.True);
                var retained = window.Document;
                Assert.That(window.CheckBoard().Status, Is.EqualTo("Invalid"));
                Assert.That(window.Document, Is.SameAs(retained));
                Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False);
            });
        }

        /// <summary>Wide/narrow windows expose separate Check, selected-variant and route commands; existing pointer actions retain their guards.</summary>
        [UnityTest]
        public IEnumerator CheckAndPlayButtonsAreVisibleAndUseSeparateActions()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-CheckPlay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                string path = Path.Combine(root, "check.json");
                File.WriteAllText(path, UnifiedBoardAuthoringTests.Minimal().ToString());
                Assert.That(window.TryOpen(path, () => BoardDocumentDecision.Discard), Is.True);
                var inputs = (BoardTestPlaySettings)typeof(BoardAuthoringWindow).GetField("testSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                string evidence = Path.Combine(Path.GetTempPath(), "HB-CheckPlay-window-20261119");
                Directory.CreateDirectory(evidence);
                foreach (int width in new[] { 1280, 640 })
                {
                    inputs.Health = inputs.Food = 100;
                    inputs.Seed++;
                    window.position = new Rect(40, 50, width, 900);
                    window.Repaint();
                    yield return null;
                    DrawWindow(window);
                    Rect check = (Rect)typeof(BoardAuthoringWindow).GetField("checkBoardRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    Rect play = (Rect)typeof(BoardAuthoringWindow).GetField("playVariantRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    Rect route = (Rect)typeof(BoardAuthoringWindow).GetField("playRouteRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    Assert.That(check.width, Is.EqualTo(120));
                    Assert.That(play.width, Is.EqualTo(180));
                    Assert.That(check.xMin, Is.GreaterThanOrEqualTo(0));
                    Assert.That(check.yMin, Is.GreaterThanOrEqualTo(0));
                    Assert.That(play.xMin, Is.GreaterThanOrEqualTo(check.xMax));
                    Assert.That(play.xMax, Is.LessThanOrEqualTo(window.position.width));
                    Assert.That(play.yMax, Is.LessThanOrEqualTo(window.position.height));
                    Assert.That(route.width, Is.EqualTo(120));
                    Assert.That(route.xMin, Is.GreaterThanOrEqualTo(play.xMax));
                    Assert.That(route.xMax, Is.LessThanOrEqualTo(window.position.width));
                    Assert.That(route.yMax, Is.LessThanOrEqualTo(window.position.height));
                    window.Grid.Select(new GridPosition(0, 7));
                    DrawWindow(window);
                    CaptureWindow(window, Path.Combine(evidence, "before-" + width + ".png"));
                    ClickWindow(window, check.center);
                    yield return WaitForEditor(() => window.BoardCheck != null);
                    Assert.That(window.BoardCheck, Is.Not.Null, "No current result after clicking " + check + "; pending callbacks=" +
                        ((HashSet<EditorApplication.CallbackFunction>)typeof(BoardAuthoringWindow).GetField("pendingCallbacks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window)).Count);
                    Assert.That(window.BoardCheck.Replayed, Is.True);
                    Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
                    DrawWindow(window);
                    CaptureWindow(window, Path.Combine(evidence, "checked-" + width + ".png"));
                    inputs.Health = 0;
                    Assert.That(window.BoardCheck, Is.Null);
                    DrawWindow(window);
                    CaptureWindow(window, Path.Combine(evidence, "outdated-" + width + ".png"));
                    typeof(BoardAuthoringWindow).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, "");
                    ClickWindow(window, play.center);
                    yield return WaitForEditor(() => window.LastError.Contains("Starting health and food must be positive"));
                    Assert.That(window.LastError, Does.Contain("Starting health and food must be positive"), "Each Play click must produce a fresh refusal.");
                    Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
                    Assert.That(BoardTestPlayLauncher.HasRecovery, Is.False);
                }
            }
            finally
            {
                window.CloseAfterDecision(BoardDocumentDecision.Discard);
                Directory.Delete(root, true);
            }
        }

        /// <summary>The route command requires both saved data and a clean exact target; disabled native clicks enqueue no launch.</summary>
        [UnityTest]
        public IEnumerator RouteButtonRequiresCleanSavedAssignment()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-RouteButton-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            try
            {
                window.position = new Rect(40, 50, 640, 900);
                Assert.That(window.TryConfigureRoot(root), Is.True);
                string path = Path.Combine(root, "route.json");
                File.WriteAllText(path, UnifiedBoardAuthoringTests.Minimal().ToString());
                Assert.That(window.TryOpen(path, () => BoardDocumentDecision.Discard), Is.True);
                var enabled = typeof(BoardAuthoringWindow).GetProperty("CanPlayRoute", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(enabled.GetValue(window), Is.False);
                var template = HallowBlaze.Core.Generation.BoardTemplateJson.ReadCompatible(window.Document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                var edge = window.Assignment.World.Edges.Single(candidate => candidate.EdgeId == "road.shelter-warm-up");
                window.Assignment.Select(new HallowBlaze.Core.Generation.BoardAssignment(window.Assignment.World, edge.Segments[1].SegmentId, template, "base"));
                Assert.That(enabled.GetValue(window), Is.False);
                Assert.That(window.TrySaveNamed("Route Middle"), Is.True, window.LastError);
                Assert.That(enabled.GetValue(window), Is.True);
                window.Document.Edit(data => data["outcomes"][0]["name"] = "Unsaved route board");
                Assert.That(enabled.GetValue(window), Is.False);
                yield return null;
                DrawWindow(window);
                Rect route = (Rect)typeof(BoardAuthoringWindow).GetField("playRouteRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                var callbacks = (HashSet<EditorApplication.CallbackFunction>)typeof(BoardAuthoringWindow).GetField("pendingCallbacks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                ClickWindow(window, route.center);
                Assert.That(callbacks, Is.Empty, "A disabled route command must not enqueue a launch.");
                Assert.That(BoardAuthoringTestContext.IsRequested || BoardTestPlayLauncher.HasRecovery, Is.False);
                Assert.That(window.Document.Undo(), Is.True);
                Assert.That(enabled.GetValue(window), Is.True);
            }
            finally
            {
                window.CloseAfterDecision(BoardDocumentDecision.Discard);
                Directory.Delete(root, true);
            }
        }

        /// <summary>A named save includes its independent target and library Open restores the exact variant/snapshot.</summary>
        [Test]
        public void NamedBoardSaveAndLibraryOpenRestoreExactRouteTarget()
        {
            WithWindow((window, root) =>
            {
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.named", "draft.named.config", () => BoardDocumentDecision.Discard), Is.True);
                var route = window.Assignment.World.Edges.First();
                var template = HallowBlaze.Core.Generation.BoardTemplateJson.ReadCompatible(window.Document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                window.Assignment.Select(new HallowBlaze.Core.Generation.BoardAssignment(window.Assignment.World, route.Segments[1].SegmentId, template, "east-guard"));
                Assert.That(window.SelectVariant("east-guard"), Is.True);
                string snapshot = window.Document.ToJson();
                Assert.That(window.TrySaveNamed("North bridge"), Is.True, window.LastError);
                var store = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                var entries = store.ListBoards(window.Assignment.World, OrdinaryBoardContent.CreateAuthoringCatalog(), out var diagnostics);
                Assert.That(diagnostics, Is.Empty);
                Assert.That(entries.Count, Is.EqualTo(1));
                Assert.That(window.TryNew("draft.other", "draft.other.config", () => BoardDocumentDecision.Discard), Is.True);
                var retained = window.Document;
                Assert.That(window.TryOpenSaved(entries[0], () => BoardDocumentDecision.Cancel), Is.False);
                Assert.That(window.Document, Is.SameAs(retained));
                Assert.That(window.TryOpenSaved(entries[0], () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                Assert.That(window.Document.ToJson(), Is.EqualTo(snapshot));
                Assert.That(window.SelectedVariant, Is.EqualTo("east-guard"));
                Assert.That(window.Assignment.Enabled, Is.True);
                Assert.That(window.Assignment.SegmentId, Is.EqualTo(route.Segments[1].SegmentId));
                typeof(BoardAuthoringWindow).GetField("boardName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, "Renamed bridge");
                Assert.That(window.ResolveDirty(() => BoardDocumentDecision.Cancel), Is.False, "An unsaved name must participate in navigation consent.");
                typeof(BoardAuthoringWindow).GetField("boardName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, "North bridge");
                File.AppendAllText(entries[0].AssignmentPath, " ");
                string previous = File.ReadAllText(entries[0].Path);
                Assert.That(window.TrySaveNamed("North bridge"), Is.False, "A changed binding must be rejected before writing the board.");
                Assert.That(File.ReadAllText(entries[0].Path), Is.EqualTo(previous));
            });
        }

        /// <summary>Test recovery retains an unsaved board label, exact assignment, saved fingerprints and document undo/redo.</summary>
        [Test]
        public void NamedBoardRecoveryPreservesUnsavedLabelAndExactAssignment()
        {
            WithWindow((window, root) =>
            {
                string path = Path.Combine(root, "original.json");
                File.WriteAllText(path, UnifiedBoardAuthoringTests.Minimal().ToString());
                Assert.That(window.TryOpen(path, () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                var route = window.Assignment.World.Edges.First();
                var template = HallowBlaze.Core.Generation.BoardTemplateJson.ReadCompatible(window.Document.ToJson(), OrdinaryBoardContent.CreateAuthoringCatalog());
                window.Assignment.Select(new HallowBlaze.Core.Generation.BoardAssignment(window.Assignment.World, route.Segments[1].SegmentId, template, "base"));
                Assert.That(window.TrySaveNamed("Saved crossing"), Is.True, window.LastError);
                window.Document.Edit(data => data["outcomes"][0]["name"] = "Edited destination");
                window.Document.Edit(data => data["outcomes"][0]["name"] = "Redo destination");
                Assert.That(window.Document.Undo(), Is.True);
                typeof(BoardAuthoringWindow).GetField("boardName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, "Unsaved crossing");
                window.Grid.Select(new GridPosition(4, 5));
                var capture = typeof(BoardAuthoringWindow).GetMethod("CaptureTestRecovery", BindingFlags.Instance | BindingFlags.NonPublic);
                var restore = typeof(BoardAuthoringWindow).GetMethod("RestoreTestRecovery", BindingFlags.Static | BindingFlags.NonPublic);
                string before = (string)capture.Invoke(window, null);
                string[] files = Directory.GetFiles(root, "*.json");
                string[] contents = files.Select(File.ReadAllText).ToArray();
                Assert.That(restore.Invoke(null, new object[] { before }), Is.SameAs(window));
                Assert.That(JToken.DeepEquals(JObject.Parse((string)capture.Invoke(window, null)), JObject.Parse(before)), Is.True);
                Assert.That(window.ResolveDirty(() => BoardDocumentDecision.Cancel), Is.False);
                Assert.That(window.Assignment.SegmentId, Is.EqualTo(route.Segments[1].SegmentId));
                Assert.That(window.Document.Redo(), Is.True);
                Assert.That((string)window.Document.Snapshot()["outcomes"][0]["name"], Is.EqualTo("Redo destination"));
                Assert.That(window.Document.Undo(), Is.True);
                Assert.That((string)window.Document.Snapshot()["outcomes"][0]["name"], Is.EqualTo("Edited destination"));
                Assert.That(files.Select(File.ReadAllText), Is.EqualTo(contents), "Recovery must not write saved board or assignment files.");
            });
        }

        /// <summary>A new start moves the original actor ID, and an erase affects only the active occupancy layer.</summary>
        [Test]
        public void PaintingMovesStartAndErasesOnlyOneLayer()
        {
            var document = Draft();
            var grid = new BoardGridView();
            grid.ChooseContent("legacy.player");
            Assert.That(grid.Activate(document, "base", new GridPosition(2, 2)), Is.True);
            var variant = Variant(document);
            var starts = variant["entities"].OfType<JObject>().Where(row => (string)row["contentId"] == "legacy.player").ToArray();
            Assert.That(starts.Length, Is.EqualTo(1));
            Assert.That((int)starts[0]["id"], Is.EqualTo(1));
            Assert.That((int)starts[0]["x"], Is.EqualTo(2));
            grid.Mode = BoardPaintMode.Erase;
            Assert.That(grid.Activate(document, "base", new GridPosition(2, 2)), Is.True);
            Assert.That(Variant(document)["entities"].OfType<JObject>().Any(row => (string)row["layer"] == "Terrain" && (int)row["x"] == 2 && (int)row["y"] == 2), Is.True);
            Assert.That(document.Undo(), Is.True);
            Assert.That((int)Variant(document)["entities"].OfType<JObject>().Single(row => (string)row["contentId"] == "legacy.player")["id"], Is.EqualTo(1));
        }

        /// <summary>Deleting a linked plate can be cancelled and confirmed deletion restores all data with one undo.</summary>
        [Test]
        public void DependentDeletionIsConfirmedAndOneUndoUnit()
        {
            var document = Draft();
            string original = document.ToJson();
            var grid = new BoardGridView { Layer = BoardLayer.Terrain, Mode = BoardPaintMode.Erase };
            string paths = null;
            Assert.That(grid.Activate(document, "base", new GridPosition(3, 3), text => { paths = text; return false; }), Is.False);
            Assert.That(paths, Does.Contain("plates[0]"));
            Assert.That(document.ToJson(), Is.EqualTo(original));
            Assert.That(document.CanUndo, Is.False);
            Assert.That(grid.Activate(document, "base", new GridPosition(3, 3), text => true), Is.True);
            Assert.That(Variant(document)["plates"].Count(), Is.EqualTo(0));
            Assert.That(document.Undo(), Is.True);
            Assert.That(document.ToJson(), Is.EqualTo(original));
            Assert.That(document.CanUndo, Is.False);
        }

        /// <summary>Property movement preserves ID and referenced terrain cannot be silently renamed or moved into occupied terrain.</summary>
        [Test]
        public void PropertyEditsMoveWithoutRenumberingAndConfirmReferenceChanges()
        {
            var document = Draft();
            var properties = new BoardPropertyPanel();
            Assert.That(properties.SetEntity(document, "base", 1, 1, new GridPosition(2, 2)), Is.True);
            Assert.That((int)Variant(document)["entities"].OfType<JObject>().Single(row => (int)row["id"] == 1)["x"], Is.EqualTo(2));
            Assert.That(properties.SetEntity(document, "base", 127, 999, new GridPosition(3, 3)), Is.False);
            Assert.That(properties.SetEntity(document, "base", 127, 999, new GridPosition(3, 3), text => true), Is.True);
            Assert.That((int)Variant(document)["plates"][0]["terrainId"], Is.EqualTo(999));
            Assert.That(document.Undo(), Is.True);
            Assert.That((int)Variant(document)["plates"][0]["terrainId"], Is.EqualTo(127));
            Assert.That(properties.SetEntity(document, "base", 127, 127, new GridPosition(1, 1)), Is.False);
        }

        /// <summary>Failed import/save and Cancel retain the document, pending edits and history; Discard intentionally creates a blank draft.</summary>
        [Test]
        public void NavigationAndFailedIoKeepTheCurrentDocument()
        {
            WithWindow((window, root) =>
            {
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                Assert.That(window.Document.IsReadOnly, Is.True);
                Assert.That(window.TryFork("draft.navigation", "draft.navigation.config", () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                BoardTemplateDocument retained = window.Document;
                window.Grid.ChooseContent("legacy.player");
                Assert.That(window.ActivateCell(new GridPosition(2, 2)), Is.True);
                string snapshot = retained.Snapshot().ToString();
                string invalid = Path.Combine(root, "invalid.json");
                File.WriteAllText(invalid, "{\"schemaVersion\":99}");
                Assert.That(window.TryOpen(invalid, () => BoardDocumentDecision.Discard), Is.False);
                Assert.That(window.Document, Is.SameAs(retained));
                Assert.That(window.Document.Snapshot().ToString(), Is.EqualTo(snapshot));
                Assert.That(window.Document.CanUndo, Is.True);
                Assert.That(window.TrySave(Path.Combine(Application.dataPath, "forbidden-test-write.json")), Is.False);
                Assert.That(window.Document.IsDirty, Is.True);
                Assert.That(window.TryNew("cancelled.new", "cancelled.config", () => BoardDocumentDecision.Save, () => false), Is.False);
                Assert.That(window.TryNew("cancelled.new", "cancelled.config", () => BoardDocumentDecision.Cancel), Is.False);
                Assert.That(window.Document, Is.SameAs(retained));
                Assert.That(window.ResolveDirty(() => BoardDocumentDecision.Save, () => true), Is.False);
                Assert.That(window.TryNew("accepted.new", "accepted.config", () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                Assert.That(window.Document.Snapshot()["variants"][0]["entities"].Count(), Is.Zero);
                Assert.That(window.Document.Diagnostics.Count, Is.GreaterThan(0));
            });
        }

        /// <summary>All variants survive Save As and reopen, while save establishes a clean checkpoint reached by undo but left by redo.</summary>
        [Test]
        public void SaveAsKeepsIdentitiesAndAllVariantsWithCleanUndoCheckpoint()
        {
            WithWindow((window, root) =>
            {
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                Assert.That(window.TrySave(Path.Combine(root, "copy-without-fork.json")), Is.False);
                Assert.That(window.TryFork("draft.variants", "draft.variants.config", () => BoardDocumentDecision.Discard), Is.True);
                int originalVariantCount = window.Document.Snapshot()["variants"].Count();
                window.Document.Edit(data =>
                {
                    var duplicate = (JObject)data["variants"][0].DeepClone();
                    duplicate["id"] = "alternate";
                    ((JArray)data["variants"]).Add(duplicate);
                });
                Assert.That(window.SelectVariant("alternate"), Is.True);
                string beforeSelection = window.Document.Snapshot().ToString();
                window.Grid.Select(new GridPosition(2, 2));
                Assert.That(window.Document.Snapshot().ToString(), Is.EqualTo(beforeSelection));
                string first = Path.Combine(root, "first.json");
                Assert.That(window.TrySave(first), Is.True, window.LastError);
                Assert.That(window.Document.IsDirty, Is.False);
                window.Grid.ChooseContent("legacy.player");
                Assert.That(window.ActivateCell(new GridPosition(2, 2)), Is.True);
                Assert.That(window.Document.IsDirty, Is.True);
                Assert.That(window.Document.Undo(), Is.True);
                Assert.That(window.Document.IsDirty, Is.False);
                Assert.That(window.Document.Redo(), Is.True);
                Assert.That(window.Document.IsDirty, Is.True);
                string second = Path.Combine(root, "second.json");
                string edited = window.Document.ToJson();
                Assert.That(window.TrySave(second), Is.True, window.LastError);
                Assert.That(window.TryOpen(second, () => BoardDocumentDecision.Cancel), Is.True, window.LastError);
                Assert.That(window.Document.ToJson(), Is.EqualTo(edited));
                Assert.That((string)window.Document.Snapshot()["templateId"], Is.EqualTo("draft.variants"));
                Assert.That(window.Document.Snapshot()["variants"].Count(), Is.EqualTo(originalVariantCount + 1));
                Assert.That(File.ReadAllText(first), Does.Not.Contain("cancelled.new"));
            });
        }

        /// <summary>Open validates before navigation but cannot publish a stale candidate when the accepted Save rewrites that same file.</summary>
        [Test]
        public void OpenCurrentFileAfterAcceptedSaveReadsTheSavedBytesAndFingerprint()
        {
            WithWindow((window, root) =>
            {
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.open.saved", "draft.open.saved.config", () => BoardDocumentDecision.Discard), Is.True);
                string path = Path.Combine(root, "current.json");
                Assert.That(window.TrySave(path), Is.True, window.LastError);
                window.Grid.ChooseContent("legacy.player");
                window.ActivateCell(new GridPosition(2, 2));
                string edited = window.Document.ToJson();
                Assert.That(window.TryOpen(path, () => BoardDocumentDecision.Save, () => window.TrySave()), Is.True, window.LastError);
                Assert.That(window.Document.ToJson(), Is.EqualTo(edited));
                Assert.That(File.ReadAllText(path), Is.EqualTo(edited));
                var verifier = new BoardDocumentStore(root, Path.GetDirectoryName(Application.dataPath));
                Assert.That(window.Document.FileFingerprint, Is.EqualTo(verifier.CaptureFingerprint(path)));
                Assert.That(window.Document.IsDirty, Is.False);
                Assert.That(window.TrySave(), Is.True, window.LastError);
            });
        }

        /// <summary>Link/spawn renames include textual dependants, cancellation is free, and accepted reference updates undo atomically.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TextualBindingIdentityChangesRequireConfirmedAtomicReferences(bool item)
        {
            BoardTemplateDocument document = Draft();
            JObject snapshot = document.Snapshot();
            JObject variant = snapshot["variants"].OfType<JObject>().Single(row => (string)row["id"] == "base");
            JObject binding = item ? variant["items"].OfType<JObject>().Single(row => (string)row["itemDefinitionId"] == "axe.basic") : (JObject)variant["plates"][0];
            string field = item ? "instanceId" : "linkId";
            if (item)
            {
                string identifier = (string)binding["instanceId"];
                document.Edit(data => data["outcomes"][0]["guaranteedToolInstanceId"] = identifier);
            }
            string before = document.Snapshot().ToString();
            var replacement = (JObject)binding.DeepClone();
            replacement[field] = "renamed.identity";
            var panel = new BoardPropertyPanel();
            string notice = null;
            Assert.That(panel.SetBinding(document, "base", binding.Path, replacement, text => { notice = text; return false; }), Is.False);
            Assert.That(notice, Does.Contain(item ? "guaranteedToolInstanceId" : "gates[0].linkId"));
            Assert.That(document.Snapshot().ToString(), Is.EqualTo(before));
            bool priorUndo = document.CanUndo;
            Assert.That(panel.SetBinding(document, "base", binding.Path, replacement, text => true), Is.True);
            JObject after = document.Snapshot();
            if (item) Assert.That((string)after["outcomes"][0]["guaranteedToolInstanceId"], Is.EqualTo("renamed.identity"));
            else Assert.That((string)after["variants"][0]["gates"][0]["linkId"], Is.EqualTo("renamed.identity"));
            Assert.That(document.Undo(), Is.True);
            Assert.That(document.Snapshot().ToString(), Is.EqualTo(before));
            Assert.That(document.CanUndo, Is.EqualTo(priorUndo));
        }

        /// <summary>Serialized window recovery preserves an invalid intermediate draft, source fingerprint, undo order and redo order.</summary>
        [Test]
        public void EditorReloadRetainsInvalidDraftAndHistory()
        {
            WithWindow((window, root) =>
            {
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.reload", "draft.reload.config", () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TrySave(Path.Combine(root, "reload.json")), Is.True, window.LastError);
                string clean = window.Document.ToJson();
                window.Grid.ChooseContent("legacy.player");
                window.ActivateCell(new GridPosition(2, 2));
                string first = window.Document.ToJson();
                window.Grid.Mode = BoardPaintMode.Erase;
                window.ActivateCell(new GridPosition(2, 2));
                Assert.That(window.Document.Diagnostics.Count, Is.GreaterThan(0));
                window.Document.Undo();
                MethodInfo disable = typeof(BoardAuthoringWindow).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo enable = typeof(BoardAuthoringWindow).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
                disable.Invoke(window, null);
                typeof(BoardAuthoringWindow).GetField("document", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, null);
                enable.Invoke(window, null);
                Assert.That(window.Document.ToJson(), Is.EqualTo(first));
                Assert.That(window.Document.FileFingerprint, Is.Not.Null.And.Not.Empty);
                Assert.That(window.Document.Redo(), Is.True);
                Assert.That(window.Document.Diagnostics.Count, Is.GreaterThan(0));
                Assert.That(window.Document.Undo(), Is.True);
                Assert.That(window.Document.ToJson(), Is.EqualTo(first));
                Assert.That(window.Document.Undo(), Is.True);
                Assert.That(window.Document.ToJson(), Is.EqualTo(clean));
                Assert.That(window.Document.IsDirty, Is.False);
            });
        }

        /// <summary>Every supported palette entry has existing visual art and no unsupported content is admitted by painting.</summary>
        [Test]
        public void PaletteHasExistingSpritesAndExplicitAllowedContent()
        {
            Assert.That(BoardGridView.Palette.Count, Is.EqualTo(15));
            Assert.That(BoardGridView.Palette.Distinct().Count(), Is.EqualTo(BoardGridView.Palette.Count));
            Assert.That(BoardGridView.Palette, Does.Not.Contain("legacy.bush-food").And.Not.Contain("landmark.radio-tower.rations"));
            foreach (string id in BoardGridView.Palette) Assert.That(BoardGridView.Preview(id), Is.Not.Null, id);
            var grid = new BoardGridView();
            var document = BoardTemplateDocument.NewTemplate("draft.blank", "draft.blank.config", RadioTowerLandmark.CreateCatalog());
            grid.ChooseContent("landmark.boulder");
            Assert.That(grid.Activate(document, "base", new GridPosition(3, 3)), Is.True);
            Assert.That(document.CanUndo, Is.True);
            Assert.That(document.Snapshot()["configuration"]["allowedContentIds"].ToString(), Does.Contain("landmark.boulder"));
            Assert.That(document.Undo(), Is.True);
            Assert.That(document.Snapshot()["configuration"]["allowedContentIds"].ToString(), Does.Not.Contain("landmark.boulder"));
            Assert.Throws<ArgumentException>(() => grid.ChooseContent("enemy.listener"));
        }

        /// <summary>Exercises a shown Editor window with real pointer/key events at wide/narrow sizes, then saves and reopens without JSON editing.</summary>
        [UnityTest]
        public IEnumerator ShownWindowPointerKeyboardAndNarrowLayoutSmoke()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-Smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            window.position = new Rect(40, 50, 1280, 900);
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True, window.LastError);
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                Assert.That(window.TryFork("draft.smoke", "draft.smoke.config", () => BoardDocumentDecision.Discard), Is.True, window.LastError);
                var targetRoute = window.Assignment.World.Edges.First(edge => edge.ToNodeId == window.Assignment.World.GoalNodeId);
                window.Assignment.Select(new HallowBlaze.Core.Generation.BoardAssignment(window.Assignment.World,
                    targetRoute.Segments[targetRoute.SegmentCount - 1].SegmentId,
                    HallowBlaze.Core.Generation.BoardTemplateJson.ReadCompatible(window.Document.ToJson(),
                        HallowBlaze.Presentation.Runtime.OrdinaryBoardContent.CreateAuthoringCatalog()), window.SelectedVariant));
                Assert.That(window.Assignment.Enabled, Is.True);
                window.Repaint();
                yield return null;
                DrawWindow(window);
                Assert.That(window.position.width, Is.GreaterThanOrEqualTo(1060), "The wide-layout gate requires an actually wide Editor window.");
                Rect wide = window.CanvasRect;
                Assert.That(wide.width, Is.GreaterThanOrEqualTo(280));
                Assert.That(wide.xMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(wide.xMax, Is.LessThanOrEqualTo(window.position.width));
                Rect inspector = PropertyControl(window, "Viewport");
                Assert.That(inspector.height, Is.GreaterThanOrEqualTo(wide.height), "Inspector must use the board's height, not a 200-point strip.");
                Assert.That(inspector.yMax, Is.GreaterThanOrEqualTo(wide.yMax));
                Assert.That(inspector.xMin, Is.GreaterThanOrEqualTo(wide.xMax));
                AssertWorkspaceCentered(window);
                window.Grid.ChooseContent("legacy.player");
                Vector2 target = BoardGridView.CellRect(wide, new GridPosition(2, 2)).center;
                Rect viewport = (Rect)typeof(BoardAuthoringWindow).GetField("workspaceViewportRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                for (int attempt = 0; attempt < 5 && target.y >= window.position.height - 24; attempt++)
                {
                    window.SendEvent(new Event { type = EventType.ScrollWheel, delta = new Vector2(0, 12), mousePosition = new Vector2(wide.center.x, Mathf.Clamp(wide.y + 10, viewport.yMin + 10, viewport.yMax - 20)) });
                    DrawWindow(window);
                    wide = window.CanvasRect;
                    target = BoardGridView.CellRect(wide, new GridPosition(2, 2)).center;
                }
                Assert.That(target.y, Is.InRange(Mathf.Max(44, viewport.yMin), Mathf.Min(window.position.height, viewport.yMax) - 24));
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = target });
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = target });
                yield return WaitForEditor(() => window.Document.CanUndo);
                Assert.That(window.Document.CanUndo, Is.True, "The pointer gesture must reach the actual scroll-transformed canvas.");
                AssertPlayerAt(window.Document, 2, 2);
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.RightArrow });
                yield return WaitForEditor(() => window.Grid.SelectedCell.Equals(new GridPosition(3, 2)));
                Assert.That(window.Grid.SelectedCell, Is.EqualTo(new GridPosition(3, 2)));
                AssertPlayerAt(window.Document, 2, 2);
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return });
                yield return WaitForEditor(() => Variant(window.Document)["entities"].OfType<JObject>().Any(row => (string)row["contentId"] == "legacy.player" && (int)row["x"] == 3));
                AssertPlayerAt(window.Document, 3, 2);
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, modifiers = EventModifiers.Control });
                yield return WaitForEditor(() => Variant(window.Document)["entities"].OfType<JObject>().Any(row => (string)row["contentId"] == "legacy.player" && (int)row["x"] == 2));
                AssertPlayerAt(window.Document, 2, 2);
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Y, modifiers = EventModifiers.Control });
                yield return WaitForEditor(() => Variant(window.Document)["entities"].OfType<JObject>().Any(row => (string)row["contentId"] == "legacy.player" && (int)row["x"] == 3));
                AssertPlayerAt(window.Document, 3, 2);
                CaptureWindow(window, Path.Combine(root, "wide.png"));
                window.position = new Rect(40, 50, 640, 900);
                window.Repaint();
                yield return null;
                DrawWindow(window);
                Rect narrow = window.CanvasRect;
                Assert.That(narrow.width, Is.GreaterThanOrEqualTo(280));
                Assert.That(narrow.xMax, Is.LessThanOrEqualTo(window.position.width));
                Assert.That(narrow.width, Is.EqualTo(narrow.height));
                Assert.That(PropertyControl(window, "Viewport").yMin, Is.GreaterThanOrEqualTo(narrow.yMax));
                AssertWorkspaceCentered(window);
                target = BoardGridView.CellRect(narrow, window.Grid.SelectedCell).center;
                viewport = (Rect)typeof(BoardAuthoringWindow).GetField("workspaceViewportRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                string scrollEvidence = "canvas=" + narrow + "; palette=" +
                    typeof(BoardAuthoringWindow).GetField("paletteViewportRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window) +
                    "; viewport=" + viewport +
                    "; pixelsPerPoint=" + EditorGUIUtility.pixelsPerPoint + "; window=" + window.position;
                for (int attempt = 0; attempt < 5 && target.y >= window.position.height - 24; attempt++)
                {
                    window.SendEvent(new Event { type = EventType.ScrollWheel, delta = new Vector2(0, 12), mousePosition = new Vector2(narrow.center.x, Mathf.Clamp(narrow.y + 10, viewport.yMin + 10, viewport.yMax - 20)) });
                    window.Repaint();
                    yield return null;
                    DrawWindow(window);
                    narrow = window.CanvasRect;
                    target = BoardGridView.CellRect(narrow, window.Grid.SelectedCell).center;
                    scrollEvidence += "; attempt=" + attempt + " scroll=" +
                        typeof(BoardAuthoringWindow).GetField("scroll", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window) + " canvas=" + narrow;
                }
                Assert.That(target.y, Is.InRange(Mathf.Max(44, viewport.yMin), Mathf.Min(window.position.height, viewport.yMax) - 24), scrollEvidence);
                window.Repaint();
                yield return null;
                DrawWindow(window);
                CaptureWindow(window, Path.Combine(root, "narrow.png"));
                window.position = new Rect(40, 50, 1600, 1000);
                window.Repaint();
                yield return null;
                DrawWindow(window);
                AssertWorkspaceCentered(window);
                Assert.That(PropertyControl(window, "Viewport").height, Is.GreaterThanOrEqualTo(window.CanvasRect.height));
                CaptureWindow(window, Path.Combine(root, "large.png"));
                string saved = Path.Combine(root, "smoke.json");
                Assert.That(window.TrySave(saved), Is.True, window.LastError);
                Assert.That(window.TryOpen(saved, () => BoardDocumentDecision.Cancel), Is.True, window.LastError);
                AssertPlayerAt(window.Document, 3, 2);
                Assert.That(window.Document.IsDirty, Is.False);
                string evidence = Path.Combine(Path.GetTempPath(), "HB-M68b-window-smoke");
                Directory.CreateDirectory(evidence);
                File.Copy(Path.Combine(root, "wide.png"), Path.Combine(evidence, "wide.png"), true);
                File.Copy(Path.Combine(root, "narrow.png"), Path.Combine(evidence, "narrow.png"), true);
                File.Copy(Path.Combine(root, "large.png"), Path.Combine(evidence, "large.png"), true);
            }
            finally
            {
                window.CloseAfterDecision(BoardDocumentDecision.Discard);
                Directory.Delete(root, true);
            }
        }

        /// <summary>The palette scrolls independently and its final brush remains fully visible and selectable at both layouts.</summary>
        [UnityTest]
        public IEnumerator PaletteScrollReachesLastBrushWithoutMovingCanvas()
        {
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            try
            {
                Assert.That(window.TryNew("draft.palette-scroll", "draft.palette-scroll.config", () => BoardDocumentDecision.Discard), Is.True);
                foreach (int width in new[] { 1280, 640 })
                {
                    window.position = new Rect(40, 50, width, 1000);
                    window.Repaint();
                    yield return null;
                    DrawWindow(window);
                    Rect canvas = window.CanvasRect;
                    Rect viewport = (Rect)typeof(BoardAuthoringWindow).GetField("paletteViewportRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    Assert.That(viewport.height, Is.GreaterThan(100));
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        window.SendEvent(new Event { type = EventType.ScrollWheel, delta = new Vector2(0, 14), mousePosition = viewport.center });
                        DrawWindow(window);
                    }
                    window.Repaint();
                    yield return null;
                    DrawWindow(window);
                    Rect last = (Rect)typeof(BoardAuthoringWindow).GetField("paletteLastButtonRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    Assert.That(last.yMin, Is.GreaterThanOrEqualTo(viewport.yMin));
                    Assert.That(last.yMax, Is.LessThanOrEqualTo(viewport.yMax));
                    Assert.That(window.CanvasRect, Is.EqualTo(canvas), "Wheel input over the palette must not move the board workspace.");
                    ClickWindow(window, last.center);
                    yield return WaitForEditor(() => window.Grid.ContentId == BoardGridView.Palette.Last());
                    Assert.That(window.Grid.ContentId, Is.EqualTo(BoardGridView.Palette.Last()));
                }
            }
            finally { window.CloseAfterDecision(BoardDocumentDecision.Discard); }
        }

        /// <summary>Native failed save and Cancel cannot close an invalid draft; unexpected destruction recovers the same edits and history.</summary>
        [UnityTest]
        public IEnumerator DirtyCloseFailureAndUnexpectedDestructionRetainTheDraft()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-Close-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            BoardAuthoringWindow recovered = null;
            window.Show();
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                Assert.That(window.TryNew("draft.close.recovery", "draft.close.config", () => BoardDocumentDecision.Discard), Is.True);
                window.Grid.ChooseContent("legacy.floor");
                window.ActivateCell(new GridPosition(2, 2));
                string retained = window.Document.Snapshot().ToString();
                window.SaveChanges();
                Assert.That(window.hasUnsavedChanges, Is.True);
                window.CloseAfterDecision(BoardDocumentDecision.Cancel);
                Assert.That(window != null, Is.True);
                Assert.That(window.Document.Snapshot().ToString(), Is.EqualTo(retained));
                UnityEngine.Object.DestroyImmediate(window);
                double recoveryDeadline = EditorApplication.timeSinceStartup + 10;
                while (recovered == null && EditorApplication.timeSinceStartup < recoveryDeadline)
                {
                    yield return null;
                    recovered = Resources.FindObjectsOfTypeAll<BoardAuthoringWindow>().FirstOrDefault(candidate =>
                        (string)candidate.Document?.Snapshot()["templateId"] == "draft.close.recovery");
                }
                Assert.That(recovered, Is.Not.Null, "An uncommitted draft destroyed without a Save/Discard decision must be recovered.");
                Assert.That(recovered.Document.Snapshot().ToString(), Is.EqualTo(retained));
                Assert.That(recovered.Document.IsDirty, Is.True);
                Assert.That(recovered.Document.Undo(), Is.True);
                Assert.That(recovered.Document.Snapshot()["variants"][0]["entities"].Count(), Is.Zero);
            }
            finally
            {
                if (window != null) window.CloseAfterDecision(BoardDocumentDecision.Discard);
                if (recovered != null) recovered.CloseAfterDecision(BoardDocumentDecision.Discard);
                Directory.Delete(root, true);
            }
        }

        /// <summary>A real delayed-field edit is committed against the old entity before a pointer changes the inspector tab.</summary>
        [UnityTest]
        public IEnumerator DelayedCellEditCommitsBeforeTabSwitch()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-Focus-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            window.position = new Rect(40, 50, 1280, 900);
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.focus", "draft.focus.config", () => BoardDocumentDecision.Discard), Is.True);
                window.Grid.Layer = BoardLayer.Actor;
                window.Grid.Select(new GridPosition(1, 1));
                yield return null;
                DrawWindow(window);
                Rect field = PropertyControl(window, "Cell.X");
                ClickWindow(window, new Vector2(field.xMax - 30, field.center.y));
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = EventModifiers.Control });
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Alpha2, character = '2' });
                AssertPlayerAt(window.Document, 1, 1);
                Rect tabs = PropertyControl(window, "Tabs");
                ClickWindow(window, tabs.center);
                for (int frame = 0; frame < 20 && !window.Document.CanUndo; frame++) yield return null;
                AssertPlayerAt(window.Document, 2, 1);
                Assert.That(window.Document.Undo(), Is.True);
                AssertPlayerAt(window.Document, 1, 1);
                Assert.That(window.Document.CanUndo, Is.False, "The property commit must remain one undo operation.");
            }
            finally { window.CloseAfterDecision(BoardDocumentDecision.Discard); Directory.Delete(root, true); }
        }

        /// <summary>Keyboard-only New, Escape, Fork and inspector/outcome commands complete without passive-button mouse activation.</summary>
        [UnityTest]
        public IEnumerator KeyboardCompletesIdentityAndOutcomeCommandsWithoutPointer()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-Keys-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            window.position = new Rect(40, 50, 1280, 900);
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                SendKey(window, KeyCode.N, EventModifiers.Control);
                yield return WaitForEditor(() => IdentityVisible(window));
                Assert.That(IdentityVisible(window), Is.True);
                DrawWindow(window);
                SendKey(window, KeyCode.Return);
                yield return WaitForEditor(() => window.Document != null);
                Assert.That(window.Document, Is.Not.Null);
                Assert.That((string)window.Document.Snapshot()["templateId"], Is.EqualTo("draft.board"));
                SendKey(window, KeyCode.N, EventModifiers.Control);
                yield return WaitForEditor(() => IdentityVisible(window));
                Assert.That(IdentityVisible(window), Is.True);
                DrawWindow(window);
                SendKey(window, KeyCode.Escape);
                yield return WaitForEditor(() => !IdentityVisible(window));
                Assert.That(IdentityVisible(window), Is.False);
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                DrawWindow(window);
                SendKey(window, KeyCode.D, EventModifiers.Control);
                yield return WaitForEditor(() => IdentityVisible(window));
                Assert.That(IdentityVisible(window), Is.True);
                DrawWindow(window);
                SendKey(window, KeyCode.Return);
                yield return WaitForEditor(() => !window.Document.IsReadOnly);
                Assert.That(window.Document.IsReadOnly, Is.False, "Fork failed: " + window.LastError + "; panel=" + IdentityVisible(window));
                Assert.That((string)window.Document.Snapshot()["templateId"], Is.EqualTo("draft.board"));
                int count = window.Document.Snapshot()["outcomes"].Count();
                SendKey(window, KeyCode.Alpha3, EventModifiers.Control);
                yield return null;
                SendKey(window, KeyCode.Insert, EventModifiers.Alt);
                yield return WaitForEditor(() => window.Document.Snapshot()["outcomes"].Count() != count);
                Assert.That(window.Document.Snapshot()["outcomes"].Count(), Is.EqualTo(count + 1));
                Assert.That(window.Document.Undo(), Is.True);
                Assert.That(window.Document.Snapshot()["outcomes"].Count(), Is.EqualTo(count));
            }
            finally { window.CloseAfterDecision(BoardDocumentDecision.Discard); Directory.Delete(root, true); }
        }

        private static void SendKey(BoardAuthoringWindow window, KeyCode key, EventModifiers modifiers = EventModifiers.None) =>
            window.SendEvent(new Event { type = EventType.KeyDown, keyCode = key, modifiers = modifiers });

        /// <summary>A deferred pointer action immediately updates the host's native dirty flag and blocking diagnostics after a clean save.</summary>
        [UnityTest]
        public IEnumerator PointerOutcomeActionRefreshesNativeDirtyState()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-Outcome-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            window.position = new Rect(40, 50, 1280, 900);
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.outcome.pointer", "draft.outcome.config", () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TrySave(Path.Combine(root, "outcome.json")), Is.True, window.LastError);
                Assert.That(window.hasUnsavedChanges, Is.False);
                var panel = (BoardPropertyPanel)typeof(BoardAuthoringWindow).GetField("properties", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                panel.FocusField("outcomes");
                yield return null;
                DrawWindow(window);
                int count = window.Document.Snapshot()["outcomes"].Count();
                Rect viewport = PropertyControl(window, "Viewport");
                for (int attempt = 0; attempt < 8 && !viewport.Contains(PropertyControl(window, "Outcomes.Add").center); attempt++)
                {
                    window.SendEvent(new Event { type = EventType.ScrollWheel, delta = new Vector2(0, 20), mousePosition = viewport.center });
                    DrawWindow(window);
                }
                Assert.That(viewport.Contains(PropertyControl(window, "Outcomes.Add").center), Is.True, "The Add button must be visible inside the inspector viewport before clicking.");
                ClickWindow(window, PropertyControl(window, "Outcomes.Add").center);
                yield return WaitForEditor(() => window.Document.Snapshot()["outcomes"].Count() != count);
                Assert.That(window.Document.Snapshot()["outcomes"].Count(), Is.EqualTo(count + 1));
                Assert.That(window.hasUnsavedChanges, Is.True);
                var reports = (System.Collections.Generic.IReadOnlyList<BoardAuthoringDiagnostic>)typeof(BoardAuthoringWindow)
                    .GetField("diagnostics", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                Assert.That(reports.Count, Is.GreaterThan(0));
                Assert.That(window.Document.Undo(), Is.True);
                Assert.That(window.Document.IsDirty, Is.False);
            }
            finally { window.CloseAfterDecision(BoardDocumentDecision.Discard); Directory.Delete(root, true); }
        }

        private static IEnumerator WaitForEditor(Func<bool> completed)
        {
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!completed() && EditorApplication.timeSinceStartup < deadline) yield return null;
        }

        /// <summary>Paint cannot silently target another occupancy layer after an explicit layer change.</summary>
        [Test]
        public void PaintingRejectsPaletteLayerMismatchWithoutChangingAnyOccupant()
        {
            BoardTemplateDocument document = Draft();
            var grid = new BoardGridView();
            grid.ChooseContent("landmark.boulder");
            grid.Layer = BoardLayer.Terrain;
            string before = document.Snapshot().ToString();
            Assert.That(grid.Activate(document, "base", new GridPosition(5, 4)), Is.False);
            Assert.That(grid.Message, Does.Contain("active Terrain layer"));
            Assert.That(document.Snapshot().ToString(), Is.EqualTo(before));
            Assert.That(document.CanUndo, Is.False);
        }

        /// <summary>A pointer gesture keeps its accepted mode/content despite subsequent keyboard changes, and L cannot redirect paint to another layer.</summary>
        [UnityTest]
        public IEnumerator PendingPointerKeepsAcceptedBrushAndLayer()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-Gesture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            window.position = new Rect(40, 50, 1280, 900);
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.gesture", "draft.gesture.config", () => BoardDocumentDecision.Discard), Is.True);
                yield return null;
                DrawWindow(window);
                Vector2 target = BoardGridView.CellRect(window.CanvasRect, new GridPosition(2, 2)).center;
                window.Grid.ChooseContent("ordinary.exit-terrain");
                ClickWindow(window, target);
                SendKey(window, KeyCode.E);
                yield return WaitForEditor(() => window.Document.CanUndo);
                Assert.That((string)Variant(window.Document)["entities"].OfType<JObject>().Single(row => (string)row["layer"] == "Terrain" && (int)row["x"] == 2 && (int)row["y"] == 2)["contentId"], Is.EqualTo("ordinary.exit-terrain"));
                Assert.That(window.Grid.Mode, Is.EqualTo(BoardPaintMode.Erase));
                window.Document.Undo();
                window.Grid.ChooseContent("ordinary.exit-terrain");
                ClickWindow(window, target);
                SendKey(window, KeyCode.Alpha1);
                yield return WaitForEditor(() => window.Document.CanUndo);
                Assert.That((string)Variant(window.Document)["entities"].OfType<JObject>().Single(row => (string)row["layer"] == "Terrain" && (int)row["x"] == 2 && (int)row["y"] == 2)["contentId"], Is.EqualTo("ordinary.exit-terrain"));
                window.Document.Undo();
                window.Grid.ChooseContent("landmark.boulder");
                SendKey(window, KeyCode.L);
                Assert.That(window.Grid.Layer, Is.Not.EqualTo(BoardLayer.Obstacle));
                string before = window.Document.Snapshot().ToString();
                Vector2 rejectedTarget = BoardGridView.CellRect(window.CanvasRect, new GridPosition(5, 4)).center;
                var callbacks = (HashSet<EditorApplication.CallbackFunction>)typeof(BoardAuthoringWindow).GetField("pendingCallbacks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                int pendingBefore = callbacks.Count;
                ClickWindow(window, rejectedTarget);
                Assert.That(callbacks.Count, Is.EqualTo(pendingBefore + 1), "Layer-mismatch click must reach the canvas: target=" + rejectedTarget + "; canvas=" + window.CanvasRect +
                    "; viewport=" + typeof(BoardAuthoringWindow).GetField("workspaceViewportRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window));
                yield return WaitForEditor(() => callbacks.Count == 0);
                Assert.That(callbacks, Is.Empty, "The accepted layer-mismatch gesture must finish before its result is checked.");
                Assert.That(window.Document.Snapshot().ToString(), Is.EqualTo(before));
                Assert.That(window.Document.CanUndo, Is.False);
                Assert.That(window.Grid.Message, Does.Contain("active"));
            }
            finally { window.CloseAfterDecision(BoardDocumentDecision.Discard); Directory.Delete(root, true); }
        }

        /// <summary>Closing/disabling the host or replacing its document invalidates queued edits and refreshes before they can touch retained data.</summary>
        [UnityTest]
        public IEnumerator ClosingOrReplacingWindowDocumentInvalidatesPendingActions()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-Pending-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (bool close in new[] { true, false })
                {
                    var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
                    window.ShowUtility();
                    window.position = new Rect(40, 50, 1280, 900);
                    try
                    {
                        Assert.That(window.TryConfigureRoot(root), Is.True);
                        Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                        Assert.That(window.TryFork("draft.pending", "draft.pending.config", () => BoardDocumentDecision.Discard), Is.True);
                        Assert.That(window.TrySave(Path.Combine(root, close ? "closed.json" : "replaced.json")), Is.True);
                        yield return null;
                        DrawWindow(window);
                        BoardTemplateDocument retained = window.Document;
                        string before = retained.Snapshot().ToString();
                        SendKey(window, KeyCode.Insert, EventModifiers.Alt);
                        window.Grid.ChooseContent("ordinary.exit-terrain");
                        ClickWindow(window, BoardGridView.CellRect(window.CanvasRect, new GridPosition(2, 2)).center);
                        SendKey(window, KeyCode.Alpha2, EventModifiers.Control);
                        if (close) window.CloseAfterDecision(BoardDocumentDecision.Discard);
                        else Assert.That(window.TryNew("draft.replaced", "draft.replaced.config", () => BoardDocumentDecision.Discard), Is.True);
                        bool drained = false;
                        EditorApplication.CallbackFunction tick = null;
                        tick = () => { EditorApplication.update -= tick; drained = true; };
                        EditorApplication.update += tick;
                        try { yield return WaitForEditor(() => drained); }
                        finally { EditorApplication.update -= tick; }
                        Assert.That(drained, Is.True);
                        Assert.That(retained.Snapshot().ToString(), Is.EqualTo(before));
                        Assert.That(retained.CanUndo, Is.False);
                        if (!close) Assert.That(window.Document.Snapshot()["variants"][0]["entities"].Count(), Is.Zero);
                        LogAssert.NoUnexpectedReceived();
                    }
                    finally { if (window != null) window.CloseAfterDecision(BoardDocumentDecision.Discard); }
                }
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>A pending field is committed against its original actor before a pointer selection changes the inspected cell.</summary>
        [UnityTest]
        public IEnumerator DelayedCellEditCommitsBeforePointerSelection()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-CellFocus-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.ShowUtility();
            window.position = new Rect(40, 50, 1280, 900);
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True);
                Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                Assert.That(window.TryFork("draft.cell.focus", "draft.cell.focus.config", () => BoardDocumentDecision.Discard), Is.True);
                window.Grid.Layer = BoardLayer.Actor;
                window.Grid.Mode = BoardPaintMode.Select;
                window.Grid.Select(new GridPosition(1, 1));
                yield return null;
                DrawWindow(window);
                Rect field = PropertyControl(window, "Cell.X");
                ClickWindow(window, new Vector2(field.xMax - 30, field.center.y));
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = EventModifiers.Control });
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Alpha2, character = '2' });
                AssertPlayerAt(window.Document, 1, 1);
                ClickWindow(window, BoardGridView.CellRect(window.CanvasRect, new GridPosition(4, 1)).center);
                SendKey(window, KeyCode.Delete);
                yield return WaitForEditor(() => window.Grid.SelectedCell.Equals(new GridPosition(4, 1)));
                bool drained = false;
                EditorApplication.delayCall += () => drained = true;
                yield return WaitForEditor(() => drained);
                AssertPlayerAt(window.Document, 2, 1);
                Assert.That(window.Document.Undo(), Is.True);
                AssertPlayerAt(window.Document, 1, 1);
                Assert.That(window.Document.CanUndo, Is.False);
            }
            finally { window.CloseAfterDecision(BoardDocumentDecision.Discard); Directory.Delete(root, true); }
        }

        /// <summary>Delete and undo after an accepted pointer paint execute after that paint, without an intervening queue drain.</summary>
        [UnityTest]
        public IEnumerator PendingPointerDeleteAndUndoKeepInputOrder()
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-Order-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (bool undo in new[] { false, true })
                {
                    var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
                    window.ShowUtility();
                    window.position = new Rect(40, 50, 1280, 900);
                    try
                    {
                        Assert.That(window.TryConfigureRoot(root), Is.True);
                        Assert.That(window.TryOpen(TemplatePath, () => BoardDocumentDecision.Discard), Is.True);
                        Assert.That(window.TryFork("draft.input.order", "draft.input.order.config", () => BoardDocumentDecision.Discard), Is.True);
                        yield return null;
                        DrawWindow(window);
                        string before = window.Document.Snapshot().ToString();
                        window.Grid.ChooseContent("ordinary.exit-terrain");
                        Vector2 target = BoardGridView.CellRect(window.CanvasRect, new GridPosition(2, 2)).center;
                        var callbacks = (HashSet<EditorApplication.CallbackFunction>)typeof(BoardAuthoringWindow).GetField("pendingCallbacks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                        ClickWindow(window, target);
                        Assert.That(callbacks.Count, Is.EqualTo(1), "Paint must reach the canvas: target=" + target + "; viewport=" +
                            typeof(BoardAuthoringWindow).GetField("workspaceViewportRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window));
                        SendKey(window, undo ? KeyCode.Z : KeyCode.Delete, undo ? EventModifiers.Control : EventModifiers.None);
                        Assert.That(callbacks.Count, Is.EqualTo(2), "The following key must enqueue after paint: undo=" + undo + "; editingText=" + EditorGUIUtility.editingTextField);
                        yield return WaitForEditor(() => callbacks.Count == 0);
                        Assert.That(callbacks, Is.Empty, "Both accepted gestures must finish before their order is checked.");
                        if (!undo)
                        {
                            Assert.That(Variant(window.Document)["entities"].OfType<JObject>().Any(row => (string)row["layer"] == "Terrain" && (int)row["x"] == 2 && (int)row["y"] == 2), Is.False);
                            Assert.That(window.Document.Undo(), Is.True);
                            Assert.That((string)Variant(window.Document)["entities"].OfType<JObject>().Single(row => (string)row["layer"] == "Terrain" && (int)row["x"] == 2 && (int)row["y"] == 2)["contentId"], Is.EqualTo("ordinary.exit-terrain"));
                            Assert.That(window.Document.Undo(), Is.True);
                        }
                        Assert.That(window.Document.Snapshot().ToString(), Is.EqualTo(before));
                        Assert.That(window.Document.CanUndo, Is.False);
                        LogAssert.NoUnexpectedReceived();
                    }
                    finally { window.CloseAfterDecision(BoardDocumentDecision.Discard); }
                }
            }
            finally { Directory.Delete(root, true); }
        }

        private static bool IdentityVisible(BoardAuthoringWindow window) =>
            (bool)typeof(BoardAuthoringWindow).GetField("identityPanel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);

        private static Rect PropertyControl(BoardAuthoringWindow window, string name)
        {
            var panel = (BoardPropertyPanel)typeof(BoardAuthoringWindow).GetField("properties", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            var controls = (Dictionary<string, Rect>)typeof(BoardPropertyPanel).GetField("controlRects", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
            var origin = (Vector2)typeof(BoardAuthoringWindow).GetField("guiOrigin", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            Rect rect = controls[name];
            return new Rect(rect.position - origin, rect.size);
        }

        private static void AssertWorkspaceCentered(BoardAuthoringWindow window)
        {
            Rect workspace = (Rect)typeof(BoardAuthoringWindow).GetField("workspaceRect", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            Assert.That(workspace.width, Is.GreaterThan(0));
            Assert.That(workspace.xMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(workspace.xMax, Is.LessThanOrEqualTo(window.position.width));
            Assert.That(workspace.center.x, Is.EqualTo(window.position.width / 2).Within(12), "Center the complete palette/canvas/inspector workspace, allowing for the scrollbar.");
        }

        private static void ClickWindow(BoardAuthoringWindow window, Vector2 point)
        {
            window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
            window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
        }

        private static void DrawWindow(BoardAuthoringWindow window)
        {
            window.Focus();
            window.SendEvent(new Event { type = EventType.Layout });
            window.SendEvent(new Event { type = EventType.Repaint });
        }

        private static void AssertPlayerAt(BoardTemplateDocument document, int horizontal, int vertical)
        {
            JObject player = Variant(document)["entities"].OfType<JObject>().Single(row => (string)row["contentId"] == "legacy.player");
            Assert.That((int)player["id"], Is.EqualTo(1));
            Assert.That((int)player["x"], Is.EqualTo(horizontal));
            Assert.That((int)player["y"], Is.EqualTo(vertical));
        }

        private static void CaptureWindow(BoardAuthoringWindow window, string path)
        {
            Vector2 origin = EditorGUIUtility.PointsToPixels(window.position.position);
            Vector2 size = EditorGUIUtility.PointsToPixels(window.position.size);
            int width = Mathf.RoundToInt(size.x);
            int height = Mathf.RoundToInt(size.y);
            Color[] pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(origin, width, height);
            Assert.That(pixels.Length, Is.EqualTo(width * height));
            Assert.That(pixels.Select(color => (Color32)color).Distinct().Count(), Is.GreaterThan(16), "The shown editor must not be blank.");
            Assert.That(pixels.Count(color => color.g > 0.6f && color.b > 0.43f && color.r < 0.48f && color.g > color.b),
                Is.GreaterThan(50), "The capture must include the canvas's teal selection border, not just a nonblank desktop.");
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try { image.SetPixels(pixels); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        private static string TemplatePath => Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json");

        private static void WithWindow(Action<BoardAuthoringWindow, string> test)
        {
            string root = Path.Combine(Path.GetTempPath(), "HB-BoardWindow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = ScriptableObject.CreateInstance<BoardAuthoringWindow>();
            window.Show();
            try
            {
                Assert.That(window.TryConfigureRoot(root), Is.True, window.LastError);
                test(window, root);
            }
            finally
            {
                window.CloseAfterDecision(BoardDocumentDecision.Discard);
                Directory.Delete(root, true);
            }
        }

        private static JObject Variant(BoardTemplateDocument document) => document.Snapshot()["variants"].OfType<JObject>().Single(row => (string)row["id"] == "base");

        private static BoardTemplateDocument Draft() => BoardTemplateDocument.ReadTemplate(
            File.ReadAllText(Path.Combine(Application.dataPath, "GameData/Landmarks/radio-tower-template.json")), RadioTowerLandmark.CreateCatalog())
            .Fork("draft.window", "draft.window.board", RadioTowerLandmark.CreateCatalog());
    }
}