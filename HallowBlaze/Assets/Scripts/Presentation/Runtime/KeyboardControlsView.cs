using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private Transform host;
        private Action closed;
        private Vector2 viewport;
        private Vector2 lastScreen;
        private int selectedBinding;

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

        /// <summary>Opens a scene-owned fullscreen modal independently of its host's layout, without gameplay effects.</summary>
        public static KeyboardControlsView Open(Transform owner, Action onClose = null)
        {
            if (Current != null && Current.IsOpen) return Current;
            var root = new GameObject("Keyboard Controls", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 140;
            root.GetComponent<Image>().color = Paper;
            KeyboardControlsView view = root.AddComponent<KeyboardControlsView>();
            view.host = owner;
            view.closed = onClose;
            view.IsOpen = true;
            Current = view;
            view.Build();
            view.lastScreen = new Vector2(Screen.width, Screen.height);
            view.Resize(view.lastScreen);
            KeyboardMenuFocus focus = KeyboardMenuFocus.Attach(root, view.modes[0].gameObject, view.Close);
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
            if (prototype != null)
            {
                label.font = prototype.font;
                label.fontSize = prototype.fontSize;
                label.color = prototype.color;
                entry.colors = existing[0].colors;
                entry.transition = Selectable.Transition.ColorTint;
                entry.targetGraphic = label;
            }
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
            Image scrollSurface = scrollViewport.gameObject.AddComponent<Image>();
            scrollSurface.color = Color.white;
            scrollViewport.gameObject.AddComponent<RectMask2D>();
            Selectable scrollFocus = scrollViewport.gameObject.AddComponent<Selectable>();
            scrollFocus.targetGraphic = scrollSurface;
            ColorBlock scrollColors = scrollFocus.colors;
            scrollColors.normalColor = scrollColors.highlightedColor = Paper;
            scrollColors.selectedColor = new Color32(206, 225, 213, 255);
            scrollColors.fadeDuration = 0;
            scrollFocus.colors = scrollColors;
            content = RectAt("Controls content", scrollViewport);
            ScrollRect scroll = scrollViewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = scrollViewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32;
            diagram = RectAt("Keyboard diagram", transform);
            descriptions = RectAt("Context bindings", content);
            SelectContext(PcInputContext.Walking);
        }

        /// <summary>Changes only help context, resetting its binding selection and scroll without changing gameplay input.</summary>
        public void SelectContext(PcInputContext context)
        {
            Context = context;
            Hints = PcInputBindings.GetHints(context);
            selectedBinding = 0;
            foreach (GameObject row in hintRows) { row.SetActive(false); Destroy(row); }
            hintRows.Clear();
            for (int index = 0; index < Hints.Count; index++)
            {
                int binding = index;
                Button row = ButtonAt("Binding " + Hints[index].Label, descriptions, () => SelectBinding(binding));
                Text action = row.GetComponentInChildren<Text>();
                action.name = "Binding action";
                action.text = Hints[index].Label;
                action.fontSize = 18;
                action.alignment = TextAnchor.MiddleLeft;
                var keys = new List<string>();
                foreach (KeyCode key in Hints[index].Keys) keys.Add(KeyName(key));
                TextAt("Binding keys", row.transform, string.Join(" / ", keys), 14, Accent);
                RectTransform marker = RectAt("Selected binding", row.transform);
                Image markerImage = marker.gameObject.AddComponent<Image>();
                markerImage.color = Accent;
                markerImage.raycastTarget = false;
                marker.anchorMin = Vector2.zero;
                marker.anchorMax = new Vector2(0, 1);
                marker.offsetMin = new Vector2(0, 4);
                marker.offsetMax = new Vector2(3, -4);
                row.gameObject.AddComponent<ControlBindingFocus>().Selected = () => SelectBinding(binding);
                ColorBlock colors = row.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = colors.selectedColor = new Color32(206, 225, 213, 255);
                colors.fadeDuration = 0;
                row.colors = colors;
                row.GetComponent<Image>().color = Paper;
                hintRows.Add(row.gameObject);
            }
            if (viewport.x > 0) Resize(viewport);
            ScrollRect scroll = scrollViewport.GetComponent<ScrollRect>();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
        }

        /// <summary>Reflows fixed-size type around a stationary diagram and independently scrollable binding list, preserving focus.</summary>
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
            bool wide = size.x >= 960;
            float diagramWidth = wide ? 520 : Mathf.Min(600, size.x - 32);
            Place(diagram, 16, 120, diagramWidth, 200);
            BuildKeyboard(diagramWidth);
            float descriptionWidth = wide ? size.x - diagramWidth - 64 : size.x - 32;
            float descriptionTop = wide ? 120 : 336;
            Place(scrollViewport, wide ? diagramWidth + 48 : 16, descriptionTop, descriptionWidth, size.y - descriptionTop - 16);
            Place(descriptions, 0, 0, descriptionWidth, 0);
            float height = 0;
            for (int index = 0; index < hintRows.Count; index++)
            {
                RectTransform row = (RectTransform)hintRows[index].transform;
                Text keysText = row.Find("Binding keys").GetComponent<Text>();
                Text action = row.Find("Binding action").GetComponent<Text>();
                float textWidth = descriptionWidth - 24;
                Place(keysText.rectTransform, 12, 8, textWidth, 24);
                float keysHeight = Mathf.Max(24, keysText.preferredHeight + 4);
                Place(keysText.rectTransform, 12, 8, textWidth, keysHeight);
                Place(action.rectTransform, 12, keysHeight + 8, textWidth, 48);
                float actionHeight = Mathf.Max(26, action.preferredHeight + 4);
                Place(action.rectTransform, 12, keysHeight + 8, textWidth, actionHeight);
                float rowHeight = keysHeight + actionHeight + 20;
                Place(row, 0, height, descriptionWidth, rowHeight);
                height += rowHeight + 4;
            }
            descriptions.sizeDelta = new Vector2(descriptionWidth, height);
            Place(content, 0, 0, descriptionWidth, height);
            HighlightKeys();
            RevealSelectedBinding();
        }

        private void BuildKeyboard(float width)
        {
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
        }

        private void SelectBinding(int index)
        {
            selectedBinding = index;
            HighlightKeys();
            RevealSelectedBinding();
        }

        private void RevealSelectedBinding()
        {
            GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            int index = hintRows.IndexOf(focused);
            if (index < 0 || viewport.x <= 0) return;
            RectTransform row = (RectTransform)hintRows[index].transform;
            float top = -row.anchoredPosition.y;
            float bottom = top + row.rect.height;
            float offset = content.anchoredPosition.y;
            if (top < offset) offset = top;
            else if (bottom > offset + scrollViewport.rect.height) offset = bottom - scrollViewport.rect.height;
            scrollViewport.GetComponent<ScrollRect>().StopMovement();
            content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, content.rect.height - scrollViewport.rect.height)));
        }

        private void HighlightKeys()
        {
            for (int index = 0; index < hintRows.Count; index++)
            {
                hintRows[index].GetComponent<Image>().color = index == selectedBinding ? new Color32(206, 225, 213, 255) : Paper;
                hintRows[index].transform.Find("Selected binding").gameObject.SetActive(index == selectedBinding);
            }
            foreach (Image cap in keycaps.Values)
            {
                cap.color = new Color32(206, 218, 212, 255);
                cap.GetComponentInChildren<Text>().color = Ink;
                cap.GetComponent<Outline>().effectColor = Ink;
                cap.GetComponent<Outline>().effectDistance = new Vector2(1, -1);
            }
            foreach (PcInputBindings.ControlHint hint in Hints)
                foreach (KeyCode key in hint.Keys)
                    if (keycaps.TryGetValue(key, out Image cap))
                    {
                        cap.color = new Color32(173, 207, 187, 255);
                        cap.GetComponentInChildren<Text>().color = Ink;
                    }
            if (selectedBinding >= 0 && selectedBinding < Hints.Count)
                foreach (KeyCode key in Hints[selectedBinding].Keys)
                    if (keycaps.TryGetValue(key, out Image cap))
                    {
                        cap.color = Accent;
                        cap.GetComponentInChildren<Text>().color = Color.white;
                        cap.GetComponent<Outline>().effectDistance = new Vector2(2, -2);
                    }
        }

        private void Keycap(KeyCode key, float column, int row, float span, float unit)
        {
            if (keycaps.TryGetValue(key, out Image existing))
            {
                Place((RectTransform)existing.transform, column * unit, row * 34, span * unit - 3, 30);
                return;
            }
            RectTransform rect = RectAt("Key " + key, diagram);
            Place(rect, column * unit, row * 34, span * unit - 3, 30);
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
            if (host == null || !host.gameObject.activeInHierarchy) { Close(); return; }
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

    internal sealed class ControlBindingFocus : MonoBehaviour, ISelectHandler, IPointerEnterHandler
    {
        internal Action Selected { get; set; }

        void ISelectHandler.OnSelect(BaseEventData eventData) => Selected?.Invoke();
        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData) => GetComponent<Button>().Select();
    }
}