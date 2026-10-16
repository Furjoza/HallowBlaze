using System;
using System.Collections.Generic;
using HallowBlaze.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Read-only two-cell equipment display; owns transient UI, never tools, commands or inventory state.</summary>
    public sealed class ToolHudPresenter : IDisposable
    {
        /// <summary>Top viewport pixels kept clear for the two equipment cells.</summary>
        public const float ReservedHeight = 68f;
        private readonly RunState run;
        private readonly IReadOnlyDictionary<string, ToolDefinition> catalog;
        private readonly Text[] values = new Text[RunState.ToolSlotCount];
        private readonly RectTransform[] cells = new RectTransform[RunState.ToolSlotCount];
        private bool disposed;

        /// <summary>Builds a transient display of the supplied run, with no default equipment or resource mutations.</summary>
        /// <param name="run">Borrowed authoritative equipment owner.</param>
        /// <param name="parent">Borrowed lifecycle owner; hiding it hides the HUD during setup and route selection.</param>
        /// <param name="size">Viewport pixel dimensions used for responsive placement.</param>
        /// <param name="catalog">Optional immutable catalog; null loads authored placeholders.</param>
        public ToolHudPresenter(RunState run, Transform parent, Vector2 size,
            IReadOnlyDictionary<string, ToolDefinition> catalog = null)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.catalog = catalog ?? ToolContentCatalog.Load();
            Root = new GameObject("Tool equipment HUD", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.DontSave };
            Root.transform.SetParent(parent, false);
            Canvas canvas = Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 79;
            for (int index = 0; index < RunState.ToolSlotCount; index++)
            {
                var cell = new GameObject("Generic tool slot " + (index + 1), typeof(RectTransform), typeof(Image));
                cell.transform.SetParent(Root.transform, false);
                cells[index] = (RectTransform)cell.transform;
                cells[index].anchorMin = cells[index].anchorMax = new Vector2(0.5f, 1f);
                cells[index].pivot = new Vector2(0.5f, 1f);
                Image background = cell.GetComponent<Image>();
                background.color = new Color32(32, 38, 36, 245);
                background.raycastTarget = false;
                CreateText("Slot name", cell.transform, "Tool " + (index + 1), 11,
                    new Vector2(8, -4), new Vector2(120, 16));
                values[index] = CreateText("Equipment value", cell.transform, "", 16,
                    new Vector2(8, -22), new Vector2(120, 26));
            }
            Resize(size);
            Refresh();
        }

        /// <summary>Gets the owned transient canvas; disabling it cannot mutate the run.</summary>
        public GameObject Root { get; }
        /// <summary>Gets whether the display has permanently released its UI lifetime.</summary>
        public bool IsDisposed => disposed;
        /// <summary>Gets the visible label for one generic slot, for accessible read-only inspection.</summary>
        public string GetSlotText(int index) => values[index].text;

        /// <summary>Re-reads authoritative equipment without applying deltas, refilling charges or changing instance identity.</summary>
        public void Refresh()
        {
            if (disposed) return;
            for (int index = 0; index < RunState.ToolSlotCount; index++)
            {
                ToolSlotState slot = run.ToolSlots[index];
                string next = slot.IsEmpty ? "Empty" :
                    (catalog.TryGetValue(slot.ToolId, out ToolDefinition definition) ? definition.DisplayName : "Unknown tool") +
                    "  " + slot.RemainingUses;
                if (values[index].text != next) values[index].text = next;
                values[index].color = slot.IsEmpty ? new Color32(190, 196, 192, 255) :
                    slot.RemainingUses == 0 ? new Color32(255, 198, 93, 255) : Color.white;
            }
        }

        /// <summary>Places two fixed-height cells in the top band without scaling text with viewport width.</summary>
        /// <param name="size">Finite viewport dimensions, at least 320 by 480 pixels.</param>
        public void Resize(Vector2 size)
        {
            if (disposed) return;
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x < 320 || size.y < 480)
                throw new ArgumentException("Tool HUD requires a viewport of at least 320 by 480.", nameof(size));
            float total = Mathf.Min(320f, size.x - 16f);
            float width = (total - 8f) / 2f;
            Bounds = new Rect((size.x - total) / 2f, size.y - 60f, total, 52f);
            for (int index = 0; index < cells.Length; index++)
            {
                cells[index].sizeDelta = new Vector2(width, 52f);
                cells[index].anchoredPosition = new Vector2((index == 0 ? -1 : 1) * (width + 8f) / 2f, -8f);
                foreach (Text label in cells[index].GetComponentsInChildren<Text>())
                    label.rectTransform.sizeDelta = new Vector2(width - 16f, label.rectTransform.sizeDelta.y);
            }
        }

        /// <summary>Gets the occupied top-band rectangle in bottom-left viewport pixels.</summary>
        public Rect Bounds { get; private set; }

        /// <summary>Hides and releases only owned UI; borrowed run and definitions remain unchanged.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Root.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(Root);
            else UnityEngine.Object.DestroyImmediate(Root);
        }

        private static Text CreateText(string name, Transform parent, string value, int fontSize, Vector2 offset, Vector2 size)
        {
            var instance = new GameObject(name, typeof(RectTransform), typeof(Text));
            instance.transform.SetParent(parent, false);
            Text text = instance.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 11;
            text.resizeTextMaxSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            text.text = value;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0, 1);
            text.rectTransform.pivot = new Vector2(0, 1);
            text.rectTransform.anchoredPosition = offset;
            text.rectTransform.sizeDelta = size;
            return text;
        }
    }
}