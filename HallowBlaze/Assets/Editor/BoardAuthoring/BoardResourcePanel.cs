using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HallowBlaze.Core.Generation;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Cached read-only resource inspection of current drafts and captured route artifacts, independent of gameplay or save ownership.</summary>
    public sealed class BoardResourcePanel
    {
        private string boardKey;
        private string routeKey;
        private IReadOnlyDictionary<string, BoardBlueprint> generated;
        private Func<string, string, string[], int, IReadOnlyDictionary<string, BoardBlueprint>> captureSnapshots = BoardResourceAnalysis.CaptureGenerated;
        private int mode;
        private bool segmentBoard;
        private BoardResourceAnalysis.Supply inspectedBoard;
        private bool details;
        private bool candidates;
        private Vector2 scroll;
        private Vector2 detailScroll;
        private string boardError;
        private string routeError;
        private string captureError;
        private Rect panelRect;
        private Rect tabsRect;
        private Rect boardSourceRect;
        private Rect detailsRect;
        private Rect captureRect;
        private Vector2 guiOrigin;
        private string summaryText = "";

        /// <summary>Gets the current draft's detached supply, independent of the inspected segment map, or null after invalid input.</summary>
        public BoardResourceAnalysis.Supply BoardSupply { get; private set; }
        /// <summary>Gets the captured route report, or null when its current source set is invalid or unavailable.</summary>
        public BoardResourceAnalysis.RouteSupply RouteSupply { get; private set; }
        /// <summary>Gets the last exact visible summary for read-only inspection.</summary>
        public string SummaryText => summaryText;

        internal void Update(BoardTemplateDocument board, string variant, BoardPoolAuthoring pool, BoardTestPlaySettings settings)
        {
            string boardJson = board?.Snapshot().ToString(Formatting.None);
            string content = UnityEngine.Resources.Load<TextAsset>("ToolDefinitions")?.text + "\0" + UnityEngine.Resources.Load<TextAsset>("ShovelDigging")?.text;
            string nextBoard = board == null ? null : content + "\0" + variant + "\0" + boardJson;
            if (nextBoard != boardKey)
            {
                boardKey = nextBoard;
                BoardSupply = null;
                boardError = null;
                try { if (board != null) BoardSupply = BoardResourceAnalysis.Board(boardJson, variant); }
                catch (Exception exception) when (IsInputError(exception)) { boardError = exception.Message; }
            }
            string manifestJson = pool.Document?.Snapshot().ToString(Formatting.None);
            string nextRoute = pool.Document == null ? null : content + "\0" + pool.WorldJson + "\0" + manifestJson + "\0" + settings.Seed +
                "\0" + string.Join("\0", pool.Templates);
            if (nextRoute == routeKey) return;
            generated = null;
            captureError = null;
            routeKey = nextRoute;
            RouteSupply = null;
            routeError = null;
            try
            {
                if (pool.Document != null) RouteSupply = BoardResourceAnalysis.Route(pool.WorldJson, manifestJson, pool.Templates, settings.Seed, generated);
            }
            catch (Exception exception) when (IsInputError(exception)) { routeError = exception.Message; }
        }

        internal void CaptureGenerated(BoardPoolAuthoring pool, BoardTestPlaySettings settings)
        {
            if (pool.Document == null) return;
            captureError = null;
            try
            {
                var snapshots = captureSnapshots(pool.WorldJson, pool.Document.ToJson(), pool.Templates, settings.Seed);
                generated = snapshots;
                RouteSupply = BoardResourceAnalysis.Route(pool.WorldJson, pool.Document.ToJson(), pool.Templates, settings.Seed, snapshots);
            }
            catch (Exception exception) when (IsInputError(exception)) { captureError = exception.Message; }
        }

        internal void Draw(BoardTemplateDocument board, string variant, BoardPoolAuthoring pool, BoardTestPlaySettings settings, Vector2 origin)
        {
            guiOrigin = origin;
            Update(board, variant, pool, settings);
            using (var area = new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Resources", EditorStyles.boldLabel);
                mode = GUILayout.Toolbar(mode, new[] { "Board", "Pool", "Route" });
                if (Event.current.type == EventType.Repaint) tabsRect = WindowRect(GUILayoutUtility.GetLastRect(), origin);
                if (mode == 0)
                {
                    if (board != null && pool.Document != null)
                    {
                        segmentBoard = GUILayout.Toolbar(segmentBoard ? 1 : 0, new[]
                        {
                            new GUIContent("Draft", "Current unsaved draft and variant"),
                            new GUIContent("Segment map", "Captured map assigned to the selected segment; pool choices use the test seed")
                        }) == 1;
                        if (Event.current.type == EventType.Repaint) boardSourceRect = WindowRect(GUILayoutUtility.GetLastRect(), origin);
                    }
                    inspectedBoard = null;
                    string inspectionError = boardError;
                    if (pool.Document != null && (board == null || segmentBoard))
                    {
                        var segment = RouteSupply?.Segments.FirstOrDefault(entry => entry.Id == pool.SelectedSegmentId);
                        var selected = segment?.Selected;
                        EditorGUILayout.LabelField("Captured segment map | " + pool.SelectedSegmentId, EditorStyles.wordWrappedMiniLabel);
                        if (selected != null)
                        {
                            GUIContent label = selected.TemplateId == null ? new GUIContent("Generated seed snapshot") : pool.DescribeEntry(selected.TemplateId, selected.Version, selected.VariantId);
                            EditorGUILayout.LabelField(label, EditorStyles.wordWrappedMiniLabel);
                            if (segment.Mode != "Fixed") EditorGUILayout.LabelField("Seed " + settings.Seed, EditorStyles.miniLabel);
                            inspectedBoard = selected.Supply;
                        }
                        inspectionError = routeError;
                        foreach (string missing in pool.UnavailableEntries) EditorGUILayout.HelpBox(missing, MessageType.Warning);
                        summaryText = segment == null ? "No segment" : "Unknown generated supply";
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Current draft | " + variant, EditorStyles.wordWrappedMiniLabel);
                        inspectedBoard = BoardSupply;
                        summaryText = "No board";
                    }
                    if (inspectionError != null) DrawError(inspectionError);
                    else if (inspectedBoard == null) EditorGUILayout.LabelField(summaryText, EditorStyles.miniLabel);
                    else DrawEstimate(new BoardResourceAnalysis.Estimate(inspectedBoard.Values().ToDictionary(entry => entry.Key,
                        entry => new BoardResourceAnalysis.Range(entry.Value, entry.Value, entry.Value)), inspectedBoard.Issues.Count == 0), false);
                }
                else
                {
                    EditorGUILayout.LabelField(pool.Document == null ? "No route sources" : "Captured maps" + (pool.Document.IsDirty ? " | Unsaved membership" : " | Saved membership"), EditorStyles.wordWrappedMiniLabel);
                    if (routeError != null) DrawError(routeError);
                    else if (RouteSupply != null)
                    {
                        foreach (string missing in pool.UnavailableEntries) EditorGUILayout.HelpBox(missing, MessageType.Warning);
                        var segment = RouteSupply.Segments.FirstOrDefault(entry => entry.Id == pool.SelectedSegmentId);
                        if (mode == 1 && segment == null) EditorGUILayout.LabelField("No segment", EditorStyles.miniLabel);
                        else
                        {
                            if (mode == 1) EditorGUILayout.LabelField(segment.Id + " | " + segment.Mode, EditorStyles.wordWrappedMiniLabel);
                            EditorGUILayout.LabelField("Min / Mean / Max | " + (generated == null ? "One choice per segment" : "Seed-conditioned estimate"), EditorStyles.wordWrappedMiniLabel);
                            DrawEstimate(mode == 1 ? segment.Estimate : RouteSupply.Estimate, true);
                            string primarySummary = summaryText;
                            EditorGUILayout.LabelField("Seed " + settings.Seed + " | " + (mode == 1 ? "Selected map" : "Selected route"), EditorStyles.miniBoldLabel);
                            var exact = mode == 1 ? BoardResourceAnalysis.Choices(segment.Selected == null ? Array.Empty<BoardResourceAnalysis.Supply>() : new[] { segment.Selected.Supply }) : RouteSupply.Selected;
                            DrawTotals(exact, false);
                            summaryText = primarySummary;
                            foreach (var unknown in (mode == 1 ? new[] { segment } : RouteSupply.Segments).Where(entry => !entry.Estimate.IsComplete))
                                EditorGUILayout.LabelField("Unknown: " + unknown.Id, EditorStyles.wordWrappedMiniLabel);
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField("Generated: " + (generated == null ? "Unknown" : "Seed snapshot"), EditorStyles.miniLabel);
                                using (new EditorGUI.DisabledScope(BoardTestPlayLauncher.HasRecovery))
                                    if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Capture generated boards for this exact route seed"), GUILayout.Width(27), GUILayout.Height(20)))
                                        CaptureGenerated(pool, settings);
                                    if (Event.current.type == EventType.Repaint) captureRect = WindowRect(GUILayoutUtility.GetLastRect(), origin);
                            }
                            if (captureError != null) EditorGUILayout.HelpBox(captureError, MessageType.Warning);
                            candidates = EditorGUILayout.Foldout(candidates, "Candidates", true);
                            if (candidates)
                            {
                                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(180));
                                foreach (var entry in mode == 1 ? new[] { segment } : RouteSupply.Segments)
                                {
                                    EditorGUILayout.LabelField(entry.Id + " | " + entry.Mode, EditorStyles.wordWrappedMiniLabel);
                                    if (entry.Candidates.Count == 0) EditorGUILayout.LabelField("Unknown generated supply", EditorStyles.miniLabel);
                                    foreach (var candidate in entry.Candidates)
                                    {
                                        GUIContent label = candidate.TemplateId == null ? new GUIContent("Generated seed snapshot") : pool.DescribeEntry(candidate.TemplateId, candidate.Version, candidate.VariantId);
                                        EditorGUILayout.LabelField(new GUIContent(label.text + (ReferenceEquals(candidate, entry.Selected) ? " | Selected" : ""), label.tooltip), EditorStyles.wordWrappedMiniLabel);
                                        EditorGUILayout.LabelField((100m / entry.Candidates.Count).ToString("0.##", CultureInfo.InvariantCulture) + "% | Food " + candidate.Supply.Food + " | Health " + candidate.Supply.Health, EditorStyles.wordWrappedMiniLabel);
                                        foreach (var resource in candidate.Supply.Resources)
                                            EditorGUILayout.LabelField(resource.Id + " | " + resource.Count + " units" +
                                                (resource.Charges.HasValue ? " | " + resource.Charges.Value + " charges" : resource.IsTool ? " | Reusable" : ""), EditorStyles.wordWrappedMiniLabel);
                                        foreach (string issue in candidate.Supply.Issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);
                                        foreach (string note in candidate.Supply.Notes) EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
                                    }
                                }
                                EditorGUILayout.EndScrollView();
                            }
                        }
                    }
                }
                EditorGUILayout.LabelField("Start (separate): Food " + settings.Food + " | Health " + settings.Health + " | Axe " + (settings.EquipAxe ? "1" : "0"), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Gross supply only. Reachability, collection, healing caps and turn costs are not evaluated.", EditorStyles.wordWrappedMiniLabel);
                if (Event.current.type == EventType.Repaint) panelRect = WindowRect(area.rect, origin);
            }
        }

        private void DrawError(string message)
        {
            summaryText = "Invalid data";
            EditorGUILayout.LabelField(summaryText, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        private void DrawEstimate(BoardResourceAnalysis.Estimate estimate, bool distribution)
        {
            DrawTotals(estimate, distribution);
            details = EditorGUILayout.Foldout(details, "Details", true);
            if (Event.current.type == EventType.Repaint) detailsRect = WindowRect(GUILayoutUtility.GetLastRect(), guiOrigin);
            if (!details) return;
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.Height(150));
            if (distribution) EditorGUILayout.LabelField("Min / Mean / Max per resource", EditorStyles.wordWrappedMiniLabel);
            foreach (var entry in estimate.Values.Where(entry => entry.Key != "Food" && entry.Key != "Health (nominal)" && (mode != 0 || entry.Key.StartsWith("Charges:", StringComparison.Ordinal))))
                EditorGUILayout.LabelField(entry.Key + "\n" + Format(entry.Value, distribution), EditorStyles.wordWrappedMiniLabel);
            if (mode == 0 && inspectedBoard != null)
            {
                foreach (var resource in inspectedBoard.Resources)
                    EditorGUILayout.LabelField(resource.Id + " | " + resource.Count + " units | Food " + resource.Food + " | Health " + resource.Health +
                        (resource.IsTool && !resource.Charges.HasValue ? " | Reusable" : ""), EditorStyles.wordWrappedMiniLabel);
                foreach (string issue in inspectedBoard.Issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);
                foreach (string note in inspectedBoard.Notes) EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTotals(BoardResourceAnalysis.Estimate estimate, bool distribution)
        {
            string status = estimate.IsComplete ? "Authored supply" : estimate.Values.Count == 0 ? "Unknown supply" : "Partial authored subtotal";
            string food = estimate.Values.TryGetValue("Food", out var foodValue) ? Format(foodValue, distribution) : "Unknown";
            string health = estimate.Values.TryGetValue("Health (nominal)", out var healthValue) ? Format(healthValue, distribution) : "Unknown";
            summaryText = status + "\nFood " + food + "\nHealth (nominal) " + health;
            EditorGUILayout.LabelField(summaryText, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(54));
        }

        private static string Format(BoardResourceAnalysis.Range value, bool distribution) => distribution
            ? Number(value.Minimum) + " / " + Number(value.Mean) + " / " + Number(value.Maximum) : Number(value.Mean);
        private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static Rect WindowRect(Rect rect, Vector2 origin) => new Rect(GUIUtility.GUIToScreenPoint(rect.position) - origin, rect.size);
        private static bool IsInputError(Exception exception) => exception is ArgumentException || exception is InvalidOperationException ||
            exception is JsonException || exception is KeyNotFoundException || exception is OverflowException || exception is NotSupportedException;
    }
}