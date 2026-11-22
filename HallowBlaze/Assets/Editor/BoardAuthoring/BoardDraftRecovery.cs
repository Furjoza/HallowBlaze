using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Captures a detached document and inspection state without saving or validating away unfinished edits.</summary>
    public static class BoardDraftRecovery
    {
        /// <summary>Retains saved fingerprints, dirty state, all variants and both undo stacks in Editor recovery data.</summary>
        public static string Capture(BoardTemplateDocument document, string variantId, BoardGridView grid, string draftRoot)
        {
            if (document == null || grid == null) throw new ArgumentNullException(nameof(document));
            return new JObject
            {
                ["document"] = document.CaptureEditorState(), ["variant"] = variantId, ["root"] = draftRoot,
                ["x"] = grid.SelectedCell.X, ["y"] = grid.SelectedCell.Y, ["content"] = grid.ContentId,
                ["layer"] = grid.Layer.ToString(), ["mode"] = grid.Mode.ToString()
            }.ToString(Formatting.None);
        }

        /// <summary>Creates independent owners; recovery never modifies a disk draft or shares mutable history with the running test.</summary>
        public static BoardTemplateDocument Restore(string recovery, BoardGridView grid, out string variantId, out string draftRoot)
        {
            JObject state = JObject.Parse(recovery);
            var document = BoardTemplateDocument.RestoreEditorState((JObject)state["document"], OrdinaryBoardContent.CreateAuthoringCatalog());
            variantId = (string)state["variant"];
            draftRoot = (string)state["root"];
            if (BoardGridView.Variant(document.Snapshot(), variantId) == null)
                throw new InvalidOperationException("Recovery refers to an unknown variant.");
            grid.ChooseContent((string)state["content"]);
            grid.Layer = (BoardLayer)Enum.Parse(typeof(BoardLayer), (string)state["layer"]);
            grid.Mode = (BoardPaintMode)Enum.Parse(typeof(BoardPaintMode), (string)state["mode"]);
            grid.Select(new GridPosition((int)state["x"], (int)state["y"]));
            return document;
        }
    }
}