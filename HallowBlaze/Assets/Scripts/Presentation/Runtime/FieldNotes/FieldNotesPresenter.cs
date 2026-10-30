using System;
using System.Collections.Generic;
using HallowBlaze.Core.State;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Profile-owned read-only notes and durable discovery feedback; independent from the current board/run and all gameplay commands.</summary>
    [DefaultExecutionOrder(-60)]
    public sealed class FieldNotesPresenter : MonoBehaviour, IDisposable
    {
        private ProfileState profile;
        private FactKnowledge knowledge;
        private Func<bool> mayOpen;
        private Action<bool> ownInput;
        private readonly Queue<FieldNoteEntry> notices = new Queue<FieldNoteEntry>();
        private readonly HashSet<string> notified = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<GameObject> rows = new List<GameObject>();
        private readonly List<GameObject> hints = new List<GameObject>();
        private readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private GameObject panel;
        private GameObject notice;
        private RectTransform listViewport;
        private RectTransform listContent;
        private RectTransform detailViewport;
        private Text detail;
        private Text empty;
        private Text noticeTitle;
        private Text noticeLabel;
        private Image noticeIcon;
        private Button openButton;
        private string selectedId;
        private Vector2 viewport;
        private bool available = true;
        private bool discoveryObscured;
        private int releaseAfterFrame = -1;

        /// <summary>Gets the transient canvas, never a profile serialization container.</summary>
        public GameObject Root { get; private set; }
        /// <summary>Gets whether notes currently own input.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>Gets whether this display lifetime has ended.</summary>
        public bool IsDisposed { get; private set; }
        /// <summary>Gets the current spoiler-safe known entries; the profile remains authoritative.</summary>
        public IReadOnlyList<FieldNoteEntry> Entries { get; private set; } = Array.Empty<FieldNoteEntry>();
        /// <summary>Gets the selected known text, or the empty state without undiscovered rule hints.</summary>
        public string SelectedText => detail != null ? detail.text : "";
        /// <summary>Gets the active persistent discovery title, or empty after acknowledgement.</summary>
        public string FeedbackTitle => notice != null && notice.activeSelf ? noticeTitle.text : "";
        /// <summary>Gets the number of unique committed discoveries received during this UI lifetime.</summary>
        public int DiscoveryFeedbackCount { get; private set; }

        /// <summary>Binds once to a profile and explicit input owner; opening notes never observes facts, saves, or resolves a turn.</summary>
        public void Bind(ProfileState profile, FactKnowledge knowledge, Func<bool> mayOpen, Action<bool> ownInput, Vector2 size)
        {
            if (IsDisposed || this.profile != null) throw new InvalidOperationException("Field Notes belongs to one profile lifetime.");
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            this.mayOpen = mayOpen ?? throw new ArgumentNullException(nameof(mayOpen));
            this.ownInput = ownInput ?? throw new ArgumentNullException(nameof(ownInput));
            Root = new GameObject("Field Notes canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)) { hideFlags = HideFlags.DontSave };
            Root.transform.SetParent(transform, false);
            Root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Root.GetComponent<Canvas>().sortingOrder = 110;
            openButton = ButtonAt("Open Field Notes", Root.transform, OpenFromButton, "book");
            openButton.transition = Selectable.Transition.None;
            Place((RectTransform)openButton.transform, 8, 76, 44, 44);
            Hint(openButton, "Field Notes");
            panel = new GameObject("Field Notes", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(Root.transform, false);
            panel.GetComponent<Image>().color = new Color32(239, 244, 241, 255);
            Text heading = TextAt("Title", panel.transform, "Field Notes", 26);
            Place(heading.rectTransform, 16, 14, 240, 38);
            Button close = ButtonAt("Close Field Notes", panel.transform, Close, "close");
            RectTransform closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1, 1);
            closeRect.anchoredPosition = new Vector2(-12, -12);
            closeRect.sizeDelta = new Vector2(40, 40);
            Hint(close, "Close Field Notes");
            listViewport = ScrollArea("Recorded observations", panel.transform, out listContent);
            detailViewport = ScrollArea("Observation text", panel.transform, out RectTransform detailContent);
            detail = TextAt("Known observation", detailContent, "", 18);
            detail.rectTransform.anchorMin = new Vector2(0, 1);
            detail.rectTransform.anchorMax = Vector2.one;
            detail.rectTransform.pivot = new Vector2(0, 1);
            detail.rectTransform.sizeDelta = Vector2.zero;
            empty = TextAt("No observations", panel.transform, "No observations recorded", 18);
            notice = new GameObject("Committed discovery", typeof(RectTransform), typeof(Image));
            notice.transform.SetParent(Root.transform, false);
            notice.GetComponent<Image>().color = new Color32(26, 54, 46, 255);
            noticeIcon = new GameObject("Observation icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            noticeIcon.transform.SetParent(notice.transform, false);
            Place(noticeIcon.rectTransform, 8, 12, 28, 28);
            noticeLabel = TextAt("Discovery", notice.transform, "Observation recorded", 13);
            noticeLabel.color = new Color32(163, 231, 205, 255);
            Place(noticeLabel.rectTransform, 44, 6, 150, 22);
            noticeTitle = TextAt("Discovery title", notice.transform, "", 16);
            noticeTitle.color = Color.white;
            Button acknowledge = ButtonAt("Acknowledge observation", notice.transform, Acknowledge, "close");
            RectTransform ackRect = (RectTransform)acknowledge.transform;
            ackRect.anchorMin = ackRect.anchorMax = ackRect.pivot = new Vector2(1, 1);
            ackRect.anchoredPosition = new Vector2(-4, -4);
            ackRect.sizeDelta = new Vector2(28, 28);
            Resize(size);
            Refresh();
            panel.SetActive(false);
            notice.SetActive(false);
        }

        /// <summary>Opens recorded knowledge without a turn, resource cost, default selection of unknown content or implicit discovery.</summary>
        public bool Open()
        {
            if (!Usable() || IsOpen || releaseAfterFrame >= 0 || !mayOpen()) return false;
            IsOpen = true;
            HideHints();
            ownInput(true);
            try { Refresh(); panel.SetActive(true); openButton.gameObject.SetActive(false); notice.SetActive(false); }
            catch { CloseImmediately(); throw; }
            return true;
        }

        /// <summary>Closes notes without releasing any other input owner and holds its own block until the frame ends.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            HideHints();
            panel.SetActive(false);
            openButton.gameObject.SetActive(true);
            RefreshNotice();
            releaseAfterFrame = Time.frameCount;
        }

        /// <summary>Rebuilds only already recorded entries; unknown rules never create rows, tooltips or completion statistics.</summary>
        public void Refresh()
        {
            if (IsDisposed || profile == null) return;
            Entries = knowledge.GetEntries(profile);
            foreach (GameObject row in rows) Release(row);
            rows.Clear();
            empty.gameObject.SetActive(Entries.Count == 0);
            if (Entries.Count == 0) { selectedId = null; detail.text = ""; }
            float width = Mathf.Max(40, listViewport.rect.width);
            for (int index = 0; index < Entries.Count; index++)
            {
                FieldNoteEntry entry = Entries[index];
                Button row = ButtonAt("Recorded " + entry.FactId, listContent, () => Select(entry.FactId), entry.IconId);
                Place((RectTransform)row.transform, 0, index * 74, width, 68);
                Image rowIcon = row.transform.Find("Icon").GetComponent<Image>();
                Place(rowIcon.rectTransform, 10, 18, 30, 30);
                Text label = TextAt("Recorded title", row.transform, entry.Title, 16);
                Place(label.rectTransform, 52, 6, width - 60, 56);
                rows.Add(row.gameObject);
            }
            listContent.sizeDelta = new Vector2(0, Entries.Count * 74);
            bool retained = false;
            foreach (FieldNoteEntry entry in Entries) retained |= entry.FactId == selectedId;
            if (Entries.Count > 0) Select(retained ? selectedId : Entries[0].FactId);
        }

        /// <summary>Selects only a profile-owned entry; requesting an unknown ID neither reveals it nor records it.</summary>
        public bool Select(string factId)
        {
            if (IsDisposed) return false;
            foreach (FieldNoteEntry entry in Entries)
            {
                if (entry.FactId != factId) continue;
                selectedId = factId;
                detail.text = entry.Title + "\n\n" + entry.Description +
                    (entry.SourceSystem == null ? "" : "\n\n" + entry.SourceSystem);
                float height = detail.preferredHeight + 24;
                detail.rectTransform.sizeDelta = new Vector2(0, height);
                ((RectTransform)detail.transform.parent).sizeDelta = new Vector2(0, height);
                foreach (GameObject row in rows) row.GetComponent<Image>().color = row.name == "Recorded " + factId
                    ? new Color32(186, 224, 209, 255) : new Color32(219, 230, 224, 255);
                return true;
            }
            return false;
        }

        /// <summary>Queues one persistent, non-color-only feedback per committed discovery; this method cannot discover or persist a fact.</summary>
        public void ShowDiscovery(FactDiscovery discovery)
        {
            if (IsDisposed) return;
            if (discovery == null) throw new ArgumentNullException(nameof(discovery));
            FieldNoteEntry entry = knowledge.GetEntry(profile, discovery.FactId);
            if (!entry.IsDiscovered) throw new InvalidOperationException("Only profile-owned discoveries can be presented.");
            if (!notified.Add(discovery.FactId)) return;
            notices.Enqueue(entry);
            DiscoveryFeedbackCount++;
            Refresh();
            RefreshNotice();
        }

        /// <summary>Acknowledges the current discovery only; subsequent queued discoveries stay visible until read.</summary>
        public void Acknowledge()
        {
            if (notices.Count > 0) notices.Dequeue();
            RefreshNotice();
        }

        /// <summary>Shows or hides access with the owning scene lifecycle, closing only this screen's input block.</summary>
        public void SetAvailable(bool value)
        {
            if (IsDisposed || Root == null) return;
            available = value;
            if (!value) CloseImmediately();
            Root.SetActive(value);
        }

        /// <summary>Applies bounded desktop/portrait layout with fixed font sizes and scrollable content, never scaling fonts by viewport width.</summary>
        public void Resize(Vector2 size)
        {
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x < 320 || size.y < 480)
                throw new ArgumentException("Field Notes requires at least 320 by 480 pixels.", nameof(size));
            viewport = size;
            Place((RectTransform)panel.transform, 0, 0, size.x, size.y);
            bool wide = size.x >= 720;
            float listWidth = wide ? 280 : size.x - 32;
            float listHeight = wide ? size.y - 88 : Mathf.Min(208, size.y * 0.34f);
            Place(listViewport, 16, 72, listWidth, listHeight);
            Place(detailViewport, wide ? 320 : 16, wide ? 72 : 88 + listHeight,
                wide ? size.x - 336 : size.x - 32, wide ? size.y - 88 : size.y - listHeight - 104);
            Place(empty.rectTransform, 16, 76, size.x - 32, 52);
            float noticeWidth = Mathf.Min(360, size.x - 72);
            Place((RectTransform)notice.transform, size.x - noticeWidth - 8, 76, noticeWidth, 104);
            Place(noticeTitle.rectTransform, 44, 30, noticeWidth - 56, 66);
            if (profile != null && detail != null) Refresh();
        }

        /// <summary>Places durable feedback in a borrowed free panel area; an informational overlay may hide it temporarily without acknowledging any queued discovery.</summary>
        /// <param name="area">Free bounds in the most recently supplied viewport's bottom-left pixels.</param>
        /// <param name="obscured">Whether the legend currently owns that area; queued feedback resumes when false.</param>
        public void SetDiscoveryArea(Rect area, bool obscured)
        {
            if (IsDisposed || notice == null) return;
            discoveryObscured = obscured;
            float height = Mathf.Min(104, area.height);
            Place((RectTransform)notice.transform, area.xMin, viewport.y - area.yMin - height, area.width, height);
            Place(noticeLabel.rectTransform, 44, 6, area.width - 80, 22);
            Place(noticeTitle.rectTransform, 44, 30, area.width - 56, height - 38);
            RefreshNotice();
        }

        /// <summary>Closes input ownership and releases owned UI/icon resources only; profile and run state remain untouched.</summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            CloseImmediately();
            IsDisposed = true;
            Release(Root);
            foreach (Sprite icon in icons.Values) Release(icon);
            foreach (Texture2D texture in textures) Release(texture);
            icons.Clear();
            textures.Clear();
            rows.Clear();
        }

        private void Update()
        {
            if (!Usable()) return;
            Vector2 size = new Vector2(Screen.width, Screen.height);
            if (size != viewport) Resize(size);
            openButton.interactable = !IsOpen && releaseAfterFrame < 0 && mayOpen();
            if (IsOpen && !mayOpen()) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.N)) { if (IsOpen) Close(); else Open(); }
            else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void LateUpdate()
        {
            if (releaseAfterFrame < 0) return;
            releaseAfterFrame = -1;
            ownInput?.Invoke(false);
        }

        private void OnDisable() => CloseImmediately();
        private void OnDestroy() => Dispose();
        private bool Usable() => !IsDisposed && available && isActiveAndEnabled && profile != null;
        private void OpenFromButton() { Open(); }
        private void CloseImmediately()
        {
            bool held = IsOpen || releaseAfterFrame >= 0;
            IsOpen = false;
            releaseAfterFrame = -1;
            HideHints();
            if (panel != null) panel.SetActive(false);
            if (openButton != null) openButton.gameObject.SetActive(true);
            if (held) ownInput?.Invoke(false);
        }

        private void RefreshNotice()
        {
            if (notice == null) return;
            notice.SetActive(notices.Count > 0 && !IsOpen && !discoveryObscured);
            if (notices.Count == 0) return;
            noticeTitle.text = notices.Peek().Title;
            noticeIcon.sprite = Icon(notices.Peek().IconId);
        }

        private Button ButtonAt(string name, Transform parent, Action click, string iconId)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            owner.transform.SetParent(parent, false);
            owner.GetComponent<Image>().color = new Color32(219, 230, 224, 255);
            Button button = owner.GetComponent<Button>();
            button.targetGraphic = owner.GetComponent<Image>();
            button.onClick.AddListener(() => click());
            var imageOwner = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            imageOwner.transform.SetParent(owner.transform, false);
            Image image = imageOwner.GetComponent<Image>();
            image.sprite = Icon(iconId);
            image.color = new Color32(28, 72, 55, 255);
            image.raycastTarget = false;
            Place(image.rectTransform, 8, 8, 28, 28);
            return button;
        }

        private static Text TextAt(string name, Transform parent, string value, int size)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(Text));
            owner.transform.SetParent(parent, false);
            Text text = owner.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = new Color32(24, 43, 35, 255);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static RectTransform ScrollArea(string name, Transform parent, out RectTransform content)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            owner.transform.SetParent(parent, false);
            owner.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var child = new GameObject("Content", typeof(RectTransform));
            child.transform.SetParent(owner.transform, false);
            content = (RectTransform)child.transform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0, 1);
            content.sizeDelta = Vector2.zero;
            ScrollRect scroll = owner.GetComponent<ScrollRect>();
            scroll.viewport = (RectTransform)owner.transform;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return (RectTransform)owner.transform;
        }

        private void Hint(Button button, string value)
        {
            Text hint = TextAt("Tooltip", button.transform, value, 14);
            Place(hint.rectTransform, button == openButton ? 56 : -242,
                button == openButton ? 0 : 42, 230, button == openButton ? 44 : 18);
            hint.gameObject.SetActive(false);
            hints.Add(hint.gameObject);
            EventTrigger trigger = button.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => hint.gameObject.SetActive(true));
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => hint.gameObject.SetActive(false));
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        private void HideHints()
        {
            foreach (GameObject hint in hints)
                if (hint != null) hint.SetActive(false);
        }

        private Sprite Icon(string id)
        {
            if (icons.TryGetValue(id, out Sprite existing)) return existing;
            var texture = new Texture2D(24, 24, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point };
            var pixels = new Color32[24 * 24];
            Action<int, int> dot = (horizontal, vertical) =>
            {
                if (horizontal >= 0 && horizontal < 24 && vertical >= 0 && vertical < 24) pixels[vertical * 24 + horizontal] = Color.white;
            };
            for (int horizontal = 3; horizontal <= 20; horizontal++)
            for (int vertical = 3; vertical <= 20; vertical++)
            {
                bool ink = id == "axe" ? Math.Abs(horizontal - vertical) <= 1 && horizontal < 17 ||
                    horizontal >= 11 && vertical >= 14 && vertical <= 19 :
                    id == "pit" ? (vertical == 5 || vertical == 6) && horizontal >= 5 && horizontal <= 19 ||
                    horizontal == 5 && vertical <= 10 || horizontal == 19 && vertical <= 10 || horizontal == 12 && vertical >= 10 && vertical <= 19 :
                    id == "hearing" ? horizontal >= 5 && horizontal <= 17 && (vertical == 19 || vertical == 20) ||
                    horizontal == 18 && vertical >= 9 && vertical <= 18 || horizontal == 12 && vertical >= 5 && vertical <= 13 ||
                    vertical == 5 && horizontal >= 8 && horizontal <= 12 || vertical == 13 && horizontal >= 9 && horizontal <= 14 :
                    id == "close" ? Math.Abs(horizontal - vertical) <= 1 || Math.Abs(horizontal + vertical - 23) <= 1 :
                    id == "book" ? horizontal == 3 || horizontal == 11 || horizontal == 20 || vertical == 3 || vertical == 20 :
                    horizontal == 11 || vertical == 5 && horizontal >= 10 && horizontal <= 12;
                if (ink) dot(horizontal, vertical);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 24, 24), new Vector2(0.5f, 0.5f), 24);
            sprite.name = "Field Notes " + id;
            sprite.hideFlags = HideFlags.DontSave;
            icons.Add(id, sprite);
            textures.Add(texture);
            return sprite;
        }

        private static void Place(RectTransform rect, float horizontal, float vertical, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(horizontal, -vertical);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Release(UnityEngine.Object instance)
        {
            if (instance == null) return;
            if (instance is GameObject owner) owner.SetActive(false);
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
        }
    }
}