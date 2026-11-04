using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Transient, read-only keyboard diagram shared by the main and pause menus.</summary>
    public sealed class KeyboardControlsView : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(26, 45, 37, 255);
        private static readonly Color Paper = new Color32(238, 244, 240, 255);
        private static readonly Color Accent = new Color32(24, 107, 79, 255);
        private readonly Dictionary<KeyCode, Image> keycaps = new Dictionary<KeyCode, Image>();
        private readonly List<GameObject> hintRows = new List<GameObject>();
        private readonly List<Button> modes = new List<Button>();
        private RectTransform header;
        private RectTransform modeBar;
        private RectTransform scrollViewport;
        private RectTransform content;
        private RectTransform diagram;
        private RectTransform descriptions;
        private Action closed;
        private Vector2 viewport;
        private Vector2 lastScreen;

        /// <summary>Gets the one open help view, or null; opening it never changes an input binding.</summary>
        public static KeyboardControlsView Current { get; private set; }
        /// <summary>Gets the frame consumed by closing help, preventing the same Escape from resuming pause.</summary>
        public static int ClosedFrame { get; private set; } = -1;
        /// <summary>Gets whether this view currently owns menu focus.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>Gets the illustrated context, independent of the actual gameplay mode.</summary>
        public PcInputContext Context { get; private set; }
        /// <summary>Gets the live descriptions derived from runtime binding definitions.</summary>
        public IReadOnlyList<PcInputBindings.ControlHint> Hints { get; private set; }
        /// <summary>Gets the actual keys represented by the diagram, including the keypad Enter alias.</summary>
        public IEnumerable<KeyCode> DiagramKeys => keycaps.Keys;

        /// <summary>Opens one modal view beneath its scene owner without resolving, saving or unpausing gameplay.</summary>
        public static KeyboardControlsView Open(Transform owner, Action onClose = null)
        {
            if (Current != null && Current.IsOpen) return Current;
            var root = new GameObject("Keyboard Controls", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            root.transform.SetParent(owner, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 140;
            root.GetComponent<Image>().color = Paper;
            KeyboardControlsView view = root.AddComponent<KeyboardControlsView>();
            view.closed = onClose;
            view.IsOpen = true;
            Current = view;
            view.Build();
            view.lastScreen = new Vector2(Screen.width, Screen.height);
            view.Resize(view.lastScreen);
            KeyboardMenuFocus focus = KeyboardMenuFocus.Attach(root, view.modes[0].gameObject, view.Close);
            focus.FrameColor = Ink;
            focus.RecoverFocus();
            return view;
        }

        /// <summary>Adds a keyboard-reachable Controls action to an existing menu, preserving its command callbacks and visual order.</summary>
        public static Button InstallEntry(GameObject menu, Action open)
        {
            var existing = new List<Button>();
            foreach (Button button in menu.GetComponentsInChildren<Button>(true))
                if (button.transform.parent == menu.transform) existing.Add(button);
            existing.Sort((first, second) => ((RectTransform)second.transform).anchoredPosition.y.CompareTo(((RectTransform)first.transform).anchoredPosition.y));
            Button entry = ButtonAt("Controls", menu.transform, open);
            existing.Insert(Mathf.Min(2, existing.Count), entry);
            Text prototype = existing.Count > 1 ? existing[0].GetComponentInChildren<Text>() : null;
            Text label = entry.GetComponentInChildren<Text>();
            if (prototype != null) { label.font = prototype.font; label.fontSize = prototype.fontSize; label.color = prototype.color; }
            entry.GetComponent<Image>().color = Color.clear;
            for (int index = 0; index < existing.Count; index++)
            {
                RectTransform rect = (RectTransform)existing[index].transform;
                rect.SetSiblingIndex(index);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0, (existing.Count - 1) * 28 - index * 56);
                rect.sizeDelta = new Vector2(240, 48);
            }
            return entry;
        }

        private void Build()
        {
            header = RectAt("Controls header", transform);
            TextAt("Controls title", header, "CONTROLS", 28, Ink);
            Button back = ButtonAt("Back", header, Close);
            Place((RectTransform)back.transform, 0, 0, 72, 38);
            modeBar = RectAt("Input contexts", transform);
            foreach (PcInputContext context in Enum.GetValues(typeof(PcInputContext)))
            {
                PcInputContext selected = context;
                Button mode = ButtonAt(context.ToString(), modeBar, () => SelectContext(selected));
                RectTransform mark = RectAt("Selected context", mode.transform);
                Image markImage = mark.gameObject.AddComponent<Image>();
                markImage.color = Ink;
                markImage.raycastTarget = false;
                mark.anchorMin = new Vector2(0, 0);
                mark.anchorMax = new Vector2(1, 0);
                mark.sizeDelta = new Vector2(-8, 3);
                mark.anchoredPosition = new Vector2(0, 2);
                modes.Add(mode);
            }
            scrollViewport = RectAt("Controls scroll region", transform);
            scrollViewport.gameObject.AddComponent<Image>().color = Color.clear;
            scrollViewport.gameObject.AddComponent<RectMask2D>();
            scrollViewport.gameObject.AddComponent<Selectable>();
            content = RectAt("Controls content", scrollViewport);
            ScrollRect scroll = scrollViewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = scrollViewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32;
            diagram = RectAt("Keyboard diagram", content);
            descriptions = RectAt("Context bindings", content);
            SelectContext(PcInputContext.Walking);
        }

        /// <summary>Changes only diagram highlights and runtime-derived labels, not the gameplay input context.</summary>
        public void SelectContext(PcInputContext context)
        {
            Context = context;
            Hints = PcInputBindings.GetHints(context);
            if (viewport.x > 0) Resize(viewport);
        }

        /// <summary>Reflows fixed-size type and keyboard keys into a single scroll region at narrow widths, without clipping labels.</summary>
        public void Resize(Vector2 size)
        {
            if (size.x < 320 || size.y < 480) throw new ArgumentException("Controls requires at least 320 by 480 pixels.", nameof(size));
            viewport = size;
            Place(header, 16, 12, size.x - 32, 40);
            Text title = header.GetComponentInChildren<Text>();
            Place(title.rectTransform, 0, 0, size.x - 120, 40);
            Place((RectTransform)header.GetComponentInChildren<Button>().transform, size.x - 104, 0, 72, 38);
            Place(modeBar, 16, 64, size.x - 32, 40);
            float modeWidth = (size.x - 48) / 3;
            for (int index = 0; index < modes.Count; index++)
            {
                Place((RectTransform)modes[index].transform, index * (modeWidth + 8), 0, modeWidth, 40);
                modes[index].GetComponent<Image>().color = (int)Context == index ? Accent : new Color32(196, 214, 205, 255);
                modes[index].GetComponentInChildren<Text>().color = (int)Context == index ? Color.white : Ink;
                modes[index].transform.Find("Selected context").gameObject.SetActive((int)Context == index);
            }
            Place(scrollViewport, 16, 120, size.x - 32, size.y - 136);
            bool wide = size.x >= 960;
            float diagramWidth = wide ? 520 : Mathf.Min(600, size.x - 32);
            Place(diagram, 0, 0, diagramWidth, 256);
            BuildKeyboard(diagramWidth);
            float descriptionWidth = wide ? size.x - diagramWidth - 64 : size.x - 32;
            float descriptionTop = wide ? 0 : 272;
            Place(descriptions, wide ? diagramWidth + 32 : 0, descriptionTop, descriptionWidth, 0);
            foreach (GameObject row in hintRows) { row.SetActive(false); Destroy(row); }
            hintRows.Clear();
            float height = 0;
            foreach (PcInputBindings.ControlHint hint in Hints)
            {
                RectTransform row = RectAt("Binding " + hint.Label, descriptions);
                var keys = new List<string>();
                foreach (KeyCode key in hint.Keys) keys.Add(KeyName(key));
                Text keysText = TextAt("Binding keys", row, string.Join(" / ", keys), 16, Accent);
                Text action = TextAt("Binding action", row, hint.Label, 18, Ink);
                Place(keysText.rectTransform, 0, 0, descriptionWidth, 24);
                float keysHeight = Mathf.Max(24, keysText.preferredHeight + 4);
                Place(keysText.rectTransform, 0, 0, descriptionWidth, keysHeight);
                Place(action.rectTransform, 0, keysHeight, descriptionWidth, 48);
                float actionHeight = Mathf.Max(26, action.preferredHeight + 4);
                Place(action.rectTransform, 0, keysHeight, descriptionWidth, actionHeight);
                float rowHeight = keysHeight + actionHeight + 16;
                Place(row, 0, height, descriptionWidth, rowHeight);
                height += rowHeight;
                hintRows.Add(row.gameObject);
            }
            descriptions.sizeDelta = new Vector2(descriptionWidth, height);
            Place(content, 0, 0, size.x - 32, Mathf.Max(256, descriptionTop + height));
        }

        private void BuildKeyboard(float width)
        {
            foreach (Transform child in diagram) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            keycaps.Clear();
            float unit = width / 13;
            Keycap(KeyCode.Escape, 0, 0, 1.6f, unit);
            Keycap(KeyCode.Tab, 0, 1, 2, unit);
            KeyCode[] top = { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.Y, KeyCode.U, KeyCode.I, KeyCode.O, KeyCode.P };
            for (int index = 0; index < top.Length; index++) Keycap(top[index], index + 2, 1, 1, unit);
            Keycap(KeyCode.CapsLock, 0, 2, 2, unit);
            KeyCode[] middle = { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.J, KeyCode.K, KeyCode.L };
            for (int index = 0; index < middle.Length; index++) Keycap(middle[index], index + 2, 2, 1, unit);
            Keycap(KeyCode.Return, 11, 2, 2, unit);
            Keycap(KeyCode.KeypadEnter, 11, 3, 2, unit);
            Keycap(KeyCode.LeftShift, 0, 3, 2.5f, unit);
            KeyCode[] bottom = { KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B, KeyCode.N, KeyCode.M };
            for (int index = 0; index < bottom.Length; index++) Keycap(bottom[index], index + 2.5f, 3, 1, unit);
            Keycap(KeyCode.Space, 1, 4, 6.5f, unit);
            Keycap(KeyCode.UpArrow, 10, 4, 1, unit);
            Keycap(KeyCode.LeftArrow, 9, 5, 1, unit);
            Keycap(KeyCode.DownArrow, 10, 5, 1, unit);
            Keycap(KeyCode.RightArrow, 11, 5, 1, unit);
            Keycap(KeyCode.PageUp, 0, 5, 2, unit);
            Keycap(KeyCode.PageDown, 2, 5, 2, unit);
            Keycap(KeyCode.Home, 4, 5, 2, unit);
            Keycap(KeyCode.End, 6, 5, 2, unit);
            foreach (PcInputBindings.ControlHint hint in Hints)
                foreach (KeyCode key in hint.Keys)
                    if (keycaps.TryGetValue(key, out Image cap))
                    {
                        cap.color = Accent;
                        cap.GetComponentInChildren<Text>().color = Color.white;
                        cap.GetComponent<Outline>().effectColor = Ink;
                        cap.GetComponent<Outline>().effectDistance = new Vector2(2, -2);
                    }
        }

        private void Keycap(KeyCode key, float column, int row, float span, float unit)
        {
            RectTransform rect = RectAt("Key " + key, diagram);
            Place(rect, column * unit, row * 40, span * unit - 3, 34);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color32(206, 218, 212, 255);
            rect.gameObject.AddComponent<Outline>().effectDistance = new Vector2(1, -1);
            Text text = TextAt("Key name", rect, KeyName(key), 12, Ink);
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            keycaps[key] = image;
        }

        /// <summary>Names actual key codes consistently in both diagram and binding labels.</summary>
        public static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Escape: return "Esc";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Num Enter";
                case KeyCode.LeftShift: case KeyCode.RightShift: return "Shift";
                case KeyCode.CapsLock: return "Caps";
                case KeyCode.LeftArrow: return "<";
                case KeyCode.RightArrow: return ">";
                case KeyCode.UpArrow: return "^";
                case KeyCode.DownArrow: return "v";
                case KeyCode.PageUp: return "PgUp";
                case KeyCode.PageDown: return "PgDn";
                default: return key.ToString();
            }
        }

        private void Update()
        {
            Vector2 screen = new Vector2(Screen.width, Screen.height);
            if (screen != lastScreen) { lastScreen = screen; Resize(screen); }
        }

        /// <summary>Closes only this modal and returns menu focus; never resumes gameplay or submits a command.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            if (Current == this) Current = null;
            gameObject.SetActive(false);
            Action callback = closed;
            closed = null;
            callback?.Invoke();
            Destroy(gameObject);
        }

        private void OnDestroy() { if (Current == this) Current = null; }

        private static RectTransform RectAt(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Text TextAt(string name, Transform parent, string value, int fontSize, Color color)
        {
            Text text = RectAt(name, parent).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = value;
            return text;
        }

        private static Button ButtonAt(string name, Transform parent, Action click)
        {
            RectTransform rect = RectAt(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color32(196, 214, 205, 255);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => click());
            Text text = TextAt("Label", rect, name, 16, Ink);
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(4, 2);
            text.rectTransform.offsetMax = new Vector2(-4, -2);
            return button;
        }

        private static void Place(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}