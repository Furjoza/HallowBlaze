using System;
using System.Collections.Generic;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HallowBlaze.Presentation.Atlas
{
    /// <summary>
    /// Presents the profile-owned atlas and delegates route selections to the active game session.
    /// </summary>
    public sealed class AtlasScreenController : MonoBehaviour
    {
        private const string MainSceneName = "Main";
        private const string ResourcePath = "Atlas/AtlasScreen";

        private static readonly Color BackdropColor = new Color32(20, 24, 23, 252);
        private static readonly Color BandColor = new Color32(39, 45, 42, 255);
        private static readonly Color MapColor = new Color32(231, 226, 207, 255);
        private static readonly Color InkColor = new Color32(30, 35, 32, 255);
        private static readonly Color PaperColor = new Color32(246, 242, 224, 255);
        private static readonly Color MossColor = new Color32(70, 119, 83, 255);
        private static readonly Color AmberColor = new Color32(218, 166, 67, 255);
        private static readonly Color CoralColor = new Color32(202, 91, 73, 255);
        private static readonly Color SightedColor = new Color32(93, 132, 128, 255);

        [SerializeField] private TextAsset worldDefinitionJson;
        [SerializeField] private Font atlasFont;
        [SerializeField] private bool animationsEnabled;

        private readonly List<GameObject> renderedNodes = new List<GameObject>();
        private readonly List<GameObject> renderedEdges = new List<GameObject>();
        private readonly List<Button> routeButtons = new List<Button>();
        private readonly Dictionary<string, Vector2> positionedNodes =
            new Dictionary<string, Vector2>(StringComparer.Ordinal);

        private CanvasGroup canvasGroup;
        private RectTransform mapArea;
        private RectTransform mapContent;
        private ScrollRect mapScroll;
        private RectTransform header;
        private RectTransform inspector;
        private RectTransform routeBand;
        private Text inspectorTitle;
        private Text inspectorBody;
        private ScrollRect inspectorScroll;
        private Button closeReader;
        private GameObject closeTooltip;
        private Button observationsButton;
        private bool reading;
        private bool returnToVisibleAtlas;
        private readonly List<GameObject> factRows = new List<GameObject>();
        private float factsHeight;
        private WorldMapSnapshot snapshot;
        private Vector2 viewportSize;
        private readonly Dictionary<string, Button> nodeButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> edgeButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly Dictionary<Button, Color> inspectionColors = new Dictionary<Button, Color>();
        private string inspectedNodeId;
        private string inspectedEdgeId;
        private string inspectedFactId;
        private bool showingObservations;
        private RectTransform edgeLayer;
        private RectTransform nodeLayer;
        private RectTransform rumorStrip;
        private RectTransform routeButtonArea;
        private ScrollRect routeScroll;
        private Text currentNodeText;
        private Text discoveryText;
        private GameObject revealedRoute;
        private Vector2 revealedViewportSize;
        private GameManager gameManager;
        private GameSession boundSession;
        private WorldMapService worldMapService;
        private string selectedRouteEdgeId;
        private string lastDiscoveryMessage = string.Empty;
        private KeyboardMenuFocus keyboardFocus;

        /// <summary>Gets whether the modal atlas is currently visible.</summary>
        public bool IsOpen => canvasGroup != null && canvasGroup.alpha > 0.5f;

        /// <summary>Gets the number of knowledge-filtered node visuals in the current render.</summary>
        public int RenderedNodeCount => renderedNodes.Count;

        /// <summary>Gets the number of knowledge-filtered edge visuals in the current render.</summary>
        public int RenderedEdgeCount => renderedEdges.Count;

        /// <summary>Gets the number of currently displayed legal route buttons.</summary>
        public int RouteButtonCount => routeButtons.Count;

        /// <summary>Gets the last route edge submitted by this presenter.</summary>
        public string SelectedRouteEdgeId => selectedRouteEdgeId;

        /// <summary>Gets the most recent persistent discovery message shown without animation.</summary>
        public string LastDiscoveryMessage => lastDiscoveryMessage;

        /// <summary>Gets whether optional presentation animations are enabled.</summary>
        public bool AnimationsEnabled => animationsEnabled;

        /// <summary>Gets the spoiler-filtered details of the selected known map object, without committing travel.</summary>
        public string InspectedDetails => inspectorBody != null ? inspectorBody.text : string.Empty;

        /// <summary>Opens the same atlas for free inspection during a board or after death, without observing exits or selecting a route.</summary>
        public void OpenReader()
        {
            BindToActiveGameManager();
            returnToVisibleAtlas = IsOpen;
            reading = true;
            if (keyboardFocus != null) keyboardFocus.enabled = false;
            ClearRouteButtons();
            RefreshAtlas();
            SetDiscoveryMessage(string.Empty);
            SetOpen(true);
            closeReader.gameObject.SetActive(true);
            routeBand.gameObject.SetActive(false);
            ShowObservations();
            keyboardFocus.RecoverFocus();
        }

        /// <summary>Closes voluntary reading only; a mandatory route-choice or final atlas remains visible.</summary>
        public void CloseReader()
        {
            reading = false;
            closeTooltip.SetActive(false);
            closeReader.gameObject.SetActive(false);
            routeBand.gameObject.SetActive(true);
            SetOpen(returnToVisibleAtlas || gameManager != null && gameManager.IsRouteChoiceActive);
            returnToVisibleAtlas = false;
            if (IsOpen) RefreshAtlas();
        }

        /// <summary>Displays only profile-owned facts in the atlas inspector, including their neutral missing-definition entries.</summary>
        public void ShowObservations()
        {
            ClearFactRows();
            inspectedNodeId = inspectedEdgeId = null;
            inspectedFactId = null;
            showingObservations = true;
            inspectorTitle.text = "OBSERVATIONS";
            inspectorBody.text = string.Empty;
            var reader = gameManager != null ? gameManager.FieldNotes : null;
            factsHeight = 0;
            if (reader == null || reader.Entries.Count == 0) inspectorBody.text = "No observations recorded";
            else foreach (FieldNoteEntry entry in reader.Entries)
            {
                string factId = entry.FactId;
                RectTransform row = CreateImage("AtlasObservation", inspectorScroll.content, Vector2.zero, Vector2.one, MossColor);
                Place(row, 0, factsHeight, inspectorScroll.viewport.rect.width, 64);
                factsHeight += 72;
                Button button = row.gameObject.AddComponent<Button>();
                StyleReaderControl(button);
                button.onClick.AddListener(() => { reader.Select(factId); ShowFact(factId); });
                RectTransform icon = CreateImage("ObservationIcon", row, Vector2.zero, Vector2.one, PaperColor);
                Place(icon, 8, 18, 28, 28);
                icon.GetComponent<Image>().sprite = reader.GetKnownIcon(factId);
                icon.GetComponent<Image>().raycastTarget = false;
                Text title = CreateText("ObservationTitle", row, Vector2.zero, Vector2.one, entry.Title, 14, TextAnchor.MiddleLeft, PaperColor);
                Place(title.rectTransform, 44, 4, row.rect.width - 52, 56);
                factRows.Add(row.gameObject);
            }
            inspectorScroll.verticalNormalizedPosition = 1;
            LayoutInspector();
            if (IsOpen && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(factRows.Count > 0 ? factRows[0] : inspectorScroll.viewport.gameObject);
        }

        /// <summary>Shows a recorded fact's full text and provenance inside the atlas; unknown IDs cannot expose catalogue content.</summary>
        public bool ShowFact(string factId)
        {
            var reader = gameManager != null ? gameManager.FieldNotes : null;
            if (reader == null) return false;
            foreach (FieldNoteEntry entry in reader.Entries)
            {
                if (entry.FactId != factId) continue;
                ClearFactRows();
                inspectedNodeId = inspectedEdgeId = null;
                inspectedFactId = factId;
                showingObservations = false;
                inspectorTitle.text = "OBSERVATION";
                inspectorBody.text = entry.Title + "\n\n" + entry.Description +
                    (entry.SourceSystem == null ? string.Empty : "\n\n" + entry.SourceSystem);
                inspectorScroll.verticalNormalizedPosition = 1;
                LayoutInspector();
                if (IsOpen && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(inspectorScroll.viewport.gameObject);
                return true;
            }
            return false;
        }

        /// <summary>Selects an already known place; unknown IDs produce no visual, discovery, save or route mutation.</summary>
        public bool InspectNode(string nodeId)
        {
            if (snapshot == null) return false;
            foreach (WorldMapNodeView node in snapshot.Nodes)
            {
                if (node.NodeId != nodeId) continue;
                ClearFactRows();
                inspectedFactId = null;
                showingObservations = false;
                inspectedNodeId = nodeId;
                inspectedEdgeId = null;
                inspectorTitle.text = "PLACE";
                inspectorBody.text = (node.PlaceKind == null ? FormatToken(node.DiscoveryState.ToString()) + " PLACE" : FormatToken(node.PlaceKind)) +
                    "\n\nKnowledge: " + node.DiscoveryState +
                    (node.AtlasX.HasValue ? "\nPosition: " + node.AtlasX.Value + "." + node.AtlasY.Value : string.Empty) +
                    (node.BiomeFamily == null ? string.Empty : "\nBiome: " + FormatToken(node.BiomeFamily));
                HighlightInspection(nodeButtons, nodeId);
                LayoutInspector();
                return true;
            }
            return false;
        }

        /// <summary>Selects an already known directed road; inspection never selects it for travel or reveals an unknown destination.</summary>
        public bool InspectEdge(string edgeId)
        {
            if (snapshot == null) return false;
            foreach (WorldMapEdgeView edge in snapshot.Edges)
            {
                if (edge.EdgeId != edgeId) continue;
                ClearFactRows();
                inspectedFactId = null;
                showingObservations = false;
                inspectedNodeId = null;
                inspectedEdgeId = edgeId;
                inspectorTitle.text = "ROAD";
                inspectorBody.text = FormatToken(edge.WorldDirection) + "\n" + FormatToken(edge.ClueKey) +
                    "\n\nKnowledge: " + edge.DiscoveryState +
                    "\nFrom: " + KnownPlaceName(edge.FromNodeId) + "\nTo: " + KnownPlaceName(edge.ToNodeId) +
                    "\n\nResource observations: Unrecorded";
                HighlightInspection(edgeButtons, edgeId);
                LayoutInspector();
                return true;
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneBootstrap()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AtlasScreenController existing = FindAnyObjectByType<AtlasScreenController>();
            if (!string.Equals(scene.name, MainSceneName, StringComparison.Ordinal))
            {
                if (existing != null)
                    existing.SetOpen(false);
                return;
            }

            if (existing == null)
            {
                GameObject prefab = Resources.Load<GameObject>(ResourcePath);
                if (prefab == null)
                {
                    Debug.LogError("Atlas screen prefab is missing from Resources/Atlas.");
                    return;
                }

                GameObject instance = Instantiate(prefab);
                instance.name = "AtlasScreen";
                DontDestroyOnLoad(instance);
                if (GameManager.instance != null) instance.transform.SetParent(GameManager.instance.transform, false);
                existing = instance.GetComponent<AtlasScreenController>();
            }

            if (existing != null)
                existing.BindToActiveGameManager();
        }

        private void Awake()
        {
            BuildInterface();
            SetOpen(false);
        }

        private void OnEnable()
        {
            BindToActiveGameManager();
        }

        private void OnDisable()
        {
            UnbindGameManager();
        }

        /// <summary>
        /// Rebuilds atlas visuals from a fresh, knowledge-filtered world map snapshot without changing game state.
        /// </summary>
        public void RefreshAtlas()
        {
            RebuildAtlas(CurrentViewport());
        }

        /// <summary>Rebuilds the same filtered map at explicit desktop or narrow pixel bounds, without scaling text or changing knowledge.</summary>
        public void Resize(Vector2 size)
        {
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x < 320 || size.y < 480)
                throw new ArgumentException("Atlas requires at least 320 by 480 pixels.", nameof(size));
            RebuildAtlas(size);
        }

        private void RebuildAtlas(Vector2 size)
        {
            if (!EnsureWorldMapService())
                return;

            snapshot = worldMapService.GetAtlasSnapshot();
            nodeButtons.Clear();
            edgeButtons.Clear();
            inspectionColors.Clear();
            ClearChildren(edgeLayer, renderedEdges);
            ClearChildren(nodeLayer, renderedNodes);
            ClearChildren(rumorStrip, renderedNodes);
            positionedNodes.Clear();

            ApplyLayout(size);
            Canvas.ForceUpdateCanvases();
            BuildPositionLookup(snapshot.Nodes);
            RenderEdges(snapshot.Edges);
            RenderNodes(snapshot.Nodes);
            UpdateCurrentNodeLabel(snapshot.Nodes);
            if (inspectedFactId != null && ShowFact(inspectedFactId)) return;
            if (showingObservations) { ShowObservations(); return; }
            if (inspectedNodeId != null && InspectNode(inspectedNodeId)) return;
            if (inspectedEdgeId != null && InspectEdge(inspectedEdgeId)) return;
            if (!InspectNode(GetCurrentNodeId()))
            {
                inspectorTitle.text = "ATLAS";
                inspectorBody.text = "No places recorded";
                LayoutInspector();
            }
        }

        private void BindToActiveGameManager()
        {
            GameManager nextManager = GameManager.instance;
            if (ReferenceEquals(gameManager, nextManager) && worldMapService != null)
                return;

            UnbindGameManager();
            gameManager = nextManager;
            if (gameManager == null)
                return;

            gameManager.OnRouteChoicesChanged += HandleRouteChoicesChanged;
            EnsureWorldMapService();
            if (gameManager.IsRouteChoiceActive)
                HandleRouteChoicesChanged(gameManager.RouteChoices);
        }

        private bool EnsureWorldMapService()
        {
            if (gameManager == null || gameManager.Session == null || worldDefinitionJson == null)
                return false;
            if (worldMapService != null && ReferenceEquals(boundSession, gameManager.Session))
                return true;

            try
            {
                boundSession = gameManager.Session;
                worldMapService = new WorldMapService(
                    WorldDefinitionJson.Deserialize(worldDefinitionJson.text),
                    boundSession,
                    new FileSystemSaveStore(GameManager.GetPersistenceRoot()));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("Atlas presentation could not read the world map: " + exception.Message);
                boundSession = null;
                worldMapService = null;
                return false;
            }
        }

        private void UnbindGameManager()
        {
            if (gameManager != null)
                gameManager.OnRouteChoicesChanged -= HandleRouteChoicesChanged;

            gameManager = null;
            boundSession = null;
            worldMapService = null;
        }

        private void HandleRouteChoicesChanged(IReadOnlyList<WorldMapExitOption> choices)
        {
            reading = false;
            closeReader.gameObject.SetActive(false);
            routeBand.gameObject.SetActive(true);
            ClearRouteButtons();
            if (choices == null || gameManager == null || !gameManager.IsRouteChoiceActive)
            {
                SetOpen(false);
                return;
            }

            selectedRouteEdgeId = null;
            revealedRoute = null;
            SetDiscoveryMessage("DISCOVERY: ROUTES OBSERVED");
            RefreshAtlas();
            List<WorldMapExitOption> ordered = OrderRouteChoices(choices);
            for (int index = 0; index < ordered.Count; index++)
                CreateRouteButton(ordered[index], index);

            SetOpen(true);
            Canvas.ForceUpdateCanvases();
            LayoutRouteButtons();
            routeScroll.horizontalNormalizedPosition = 0f;
            EnsureEventSystem();
            if (routeButtons.Count > 0)
            {
                routeButtons[0].Select();
                EventSystem.current.SetSelectedGameObject(routeButtons[0].gameObject);
            }
        }

        private void SubmitRoute(string edgeId)
        {
            if (gameManager == null || string.IsNullOrEmpty(edgeId))
                return;

            selectedRouteEdgeId = edgeId;
            if (!gameManager.ChooseRoute(edgeId))
                return;

            for (int index = 0; index < routeButtons.Count; index++)
                routeButtons[index].interactable = false;
        }

        private void BuildInterface()
        {
            gameObject.name = "AtlasScreen";
            Canvas canvas = GetOrAddComponent<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = GetOrAddComponent<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GetOrAddComponent<GraphicRaycaster>(gameObject);
            canvasGroup = GetOrAddComponent<CanvasGroup>(gameObject);

            RectTransform root = GetOrAddComponent<RectTransform>(gameObject);
            Stretch(root);
            CreateImage("Backdrop", root, Vector2.zero, Vector2.one, BackdropColor);

            header = CreateImage(
                "AtlasHeader",
                root,
                new Vector2(0f, 0.84f),
                Vector2.one,
                BandColor);
            CreateText(
                "AtlasTitle",
                header,
                new Vector2(0.025f, 0.12f),
                new Vector2(0.31f, 0.88f),
                "FIELD ATLAS",
                30,
                TextAnchor.MiddleLeft,
                PaperColor);
            currentNodeText = CreateText(
                "CurrentNode",
                header,
                new Vector2(0.32f, 0.12f),
                new Vector2(0.66f, 0.88f),
                "CURRENT: UNKNOWN",
                18,
                TextAnchor.MiddleLeft,
                PaperColor);
            RectTransform close = CreateImage("Close Atlas", header, Vector2.zero, Vector2.one, BandColor);
            closeReader = close.gameObject.AddComponent<Button>();
            StyleReaderControl(closeReader);
            closeReader.onClick.AddListener(() => gameManager?.FieldNotes?.Close());
            Text closeLabel = CreateText("Close", close, Vector2.zero, Vector2.one, "X", 22, TextAnchor.MiddleCenter, PaperColor);
            RectTransform tooltip = CreateImage("Close atlas tooltip", close, Vector2.zero, Vector2.one, BandColor);
            Place(tooltip, -154, 96, 194, 24);
            CreateText("Tooltip", tooltip, Vector2.zero, Vector2.one, "Close atlas", 14, TextAnchor.MiddleCenter, PaperColor);
            closeTooltip = tooltip.gameObject;
            closeTooltip.SetActive(false);
            var closeHover = close.gameObject.AddComponent<EventTrigger>();
            var closeEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            closeEnter.callback.AddListener(_ => closeTooltip.SetActive(true));
            var closeExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            closeExit.callback.AddListener(_ => closeTooltip.SetActive(false));
            closeHover.triggers.Add(closeEnter);
            closeHover.triggers.Add(closeExit);
            closeReader.gameObject.SetActive(false);
            discoveryText = CreateText(
                "DiscoveryStatus",
                header,
                new Vector2(0.67f, 0.12f),
                new Vector2(0.975f, 0.88f),
                string.Empty,
                16,
                TextAnchor.MiddleRight,
                AmberColor);

            mapArea = CreateImage(
                "AtlasMapArea",
                root,
                new Vector2(0.02f, 0.24f),
                new Vector2(0.75f, 0.82f),
                MapColor);
            mapArea.gameObject.AddComponent<RectMask2D>();
            mapContent = CreateRect("AtlasMapContent", mapArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            mapScroll = mapArea.gameObject.AddComponent<ScrollRect>();
            mapScroll.viewport = mapArea;
            mapScroll.content = mapContent;
            mapScroll.movementType = ScrollRect.MovementType.Clamped;
            edgeLayer = CreateRect("AtlasEdges", mapContent, Vector2.zero, Vector2.one);
            nodeLayer = CreateRect("AtlasNodes", mapContent, Vector2.zero, Vector2.one);
            rumorStrip = CreateImage(
                "AtlasRumors",
                mapContent,
                new Vector2(0.02f, 0.03f),
                new Vector2(0.29f, 0.29f),
                new Color32(48, 53, 49, 238));
            VerticalLayoutGroup rumorLayout = rumorStrip.gameObject.AddComponent<VerticalLayoutGroup>();
            rumorLayout.padding = new RectOffset(12, 12, 10, 10);
            rumorLayout.spacing = 8f;
            rumorLayout.childControlHeight = false;
            rumorLayout.childForceExpandHeight = false;
            rumorLayout.childControlWidth = true;

            inspector = CreateImage(
                "AtlasLegend",
                root,
                new Vector2(0.77f, 0.24f),
                new Vector2(0.98f, 0.82f),
                BandColor);
            inspectorTitle = CreateText(
                "LegendTitle",
                inspector,
                new Vector2(0.08f, 0.88f),
                new Vector2(0.92f, 0.98f),
                "ATLAS",
                22,
                TextAnchor.MiddleLeft,
                PaperColor);
            RectTransform detailViewport = CreateRect("AtlasDetailViewport", inspector, new Vector2(0, 0), Vector2.one);
            detailViewport.gameObject.AddComponent<RectMask2D>();
            detailViewport.gameObject.AddComponent<Image>().color = new Color32(31, 38, 34, 255);
            detailViewport.gameObject.AddComponent<Selectable>();
            RectTransform detailContent = CreateRect("AtlasDetailContent", detailViewport, new Vector2(0, 1), Vector2.one);
            detailContent.pivot = new Vector2(0, 1);
            inspectorScroll = detailViewport.gameObject.AddComponent<ScrollRect>();
            inspectorScroll.viewport = detailViewport;
            inspectorScroll.content = detailContent;
            inspectorScroll.horizontal = false;
            inspectorScroll.movementType = ScrollRect.MovementType.Clamped;
            inspectorBody = CreateText(
                "AtlasDetails",
                detailContent,
                Vector2.zero,
                Vector2.one,
                string.Empty,
                14,
                TextAnchor.UpperLeft,
                PaperColor);
            RectTransform observations = CreateImage("Atlas Observations", inspector, Vector2.zero, Vector2.one, MossColor);
            observationsButton = observations.gameObject.AddComponent<Button>();
            StyleReaderControl(observationsButton);
            observationsButton.onClick.AddListener(ShowObservations);
            CreateText("Observations", observations, Vector2.zero, Vector2.one, "Observations", 14, TextAnchor.MiddleCenter, PaperColor);

            routeBand = CreateImage(
                "AtlasRouteBand",
                root,
                new Vector2(0f, 0f),
                new Vector2(1f, 0.21f),
                BandColor);
            CreateText(
                "RouteTitle",
                routeBand,
                new Vector2(0.025f, 0.14f),
                new Vector2(0.2f, 0.86f),
                "CHOOSE ROUTE",
                20,
                TextAnchor.MiddleLeft,
                PaperColor);
            RectTransform routeViewport = CreateRect(
                "AtlasRouteViewport",
                routeBand,
                new Vector2(0.21f, 0.15f),
                new Vector2(0.975f, 0.85f));
            routeViewport.gameObject.AddComponent<RectMask2D>();
            routeButtonArea = CreateRect("AtlasRouteButtons", routeViewport, Vector2.zero, new Vector2(0f, 1f));
            routeButtonArea.pivot = new Vector2(0f, 0.5f);
            routeScroll = routeViewport.gameObject.AddComponent<ScrollRect>();
            routeScroll.viewport = routeViewport;
            routeScroll.content = routeButtonArea;
            routeScroll.horizontal = true;
            routeScroll.vertical = false;
            routeScroll.movementType = ScrollRect.MovementType.Clamped;
            HorizontalLayoutGroup routeLayout = routeButtonArea.gameObject.AddComponent<HorizontalLayoutGroup>();
            routeLayout.spacing = 16f;
            routeLayout.childAlignment = TextAnchor.MiddleCenter;
            routeLayout.childControlWidth = true;
            routeLayout.childControlHeight = true;
            routeLayout.childForceExpandWidth = true;
            routeLayout.childForceExpandHeight = true;
        }

        private void LateUpdate()
        {
            if (!IsOpen) return;
            if (viewportSize != CurrentViewport()) RefreshAtlas();
            LayoutInspector();
            if (routeButtons.Count == 0) return;
            LayoutRouteButtons();
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null || !routeButtons.Exists(button => button.gameObject == selected)) return;
            Vector2 size = routeScroll.viewport.rect.size;
            if (revealedRoute == selected && revealedViewportSize == size) return;
            revealedRoute = selected;
            revealedViewportSize = size;
            routeScroll.StopMovement();
            keyboardFocus?.RecoverFocus();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                routeScroll.viewport, (RectTransform)selected.transform);
            Rect viewport = routeScroll.viewport.rect;
            float delta = bounds.min.x < viewport.xMin ? viewport.xMin - bounds.min.x :
                bounds.max.x > viewport.xMax ? viewport.xMax - bounds.max.x : 0f;
            routeButtonArea.anchoredPosition += new Vector2(delta, 0f);
        }

        private void LayoutRouteButtons()
        {
            if (routeButtons.Count == 0) return;
            float scale = GetComponent<Canvas>().scaleFactor;
            HorizontalLayoutGroup layout = routeButtonArea.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f / scale;
            int padding = Mathf.CeilToInt(6f / scale);
            int verticalPadding = Mathf.CeilToInt(2f / scale);
            layout.padding = new RectOffset(padding, padding, verticalPadding, verticalPadding);
            float width = Mathf.Max(112f / scale,
                (routeScroll.viewport.rect.width - layout.padding.horizontal - layout.spacing * (Math.Min(2, routeButtons.Count) - 1)) /
                Math.Min(2, routeButtons.Count));
            routeButtonArea.sizeDelta = new Vector2(
                width * routeButtons.Count + layout.spacing * (routeButtons.Count - 1) + layout.padding.horizontal, 0f);
            foreach (Button button in routeButtons)
            {
                Text label = button.GetComponentInChildren<Text>();
                label.fontSize = Mathf.CeilToInt(14f / scale);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(routeButtonArea);
        }

        private void BuildPositionLookup(IReadOnlyList<WorldMapNodeView> nodes)
        {
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;

            for (int index = 0; index < nodes.Count; index++)
            {
                WorldMapNodeView node = nodes[index];
                if (!node.AtlasX.HasValue || !node.AtlasY.HasValue)
                    continue;

                minX = Math.Min(minX, node.AtlasX.Value);
                maxX = Math.Max(maxX, node.AtlasX.Value);
                minY = Math.Min(minY, node.AtlasY.Value);
                maxY = Math.Max(maxY, node.AtlasY.Value);
            }

            if (minX == int.MaxValue)
                return;

            float width = Math.Max(1, maxX - minX);
            float height = Math.Max(1, maxY - minY);
            for (int index = 0; index < nodes.Count; index++)
            {
                WorldMapNodeView node = nodes[index];
                if (!node.AtlasX.HasValue || !node.AtlasY.HasValue)
                    continue;

                float normalizedX = 0.14f + 0.72f * ((node.AtlasX.Value - minX) / width);
                float normalizedY = 0.18f + 0.68f * ((node.AtlasY.Value - minY) / height);
                positionedNodes[node.NodeId] = new Vector2(normalizedX, normalizedY);
            }
        }

        private void RenderNodes(IReadOnlyList<WorldMapNodeView> nodes)
        {
            int rumoredIndex = 0;
            int sightedIndex = 0;
            int visitedIndex = 0;
            string currentNodeId = GetCurrentNodeId();

            for (int index = 0; index < nodes.Count; index++)
            {
                WorldMapNodeView node = nodes[index];
                if (node.DiscoveryState == NodeDiscoveryState.Rumored)
                {
                    RenderRumoredNode(rumoredIndex++);
                    MakeInspectable(renderedNodes[renderedNodes.Count - 1], node.NodeId, true);
                    continue;
                }

                if (!positionedNodes.TryGetValue(node.NodeId, out Vector2 position))
                    continue;

                bool isCurrent = string.Equals(node.NodeId, currentNodeId, StringComparison.Ordinal);
                if (node.DiscoveryState == NodeDiscoveryState.Sighted)
                    RenderPositionedNode(nodeLayer, position, "AtlasNode-Sighted-" + sightedIndex++, "SIGHTED PLACE", SightedColor, true, isCurrent);
                else if (node.DiscoveryState == NodeDiscoveryState.Visited)
                    RenderPositionedNode(nodeLayer, position, "AtlasNode-Visited-" + visitedIndex++, FormatVisitedNode(node), MossColor, false, isCurrent);
                MakeInspectable(renderedNodes[renderedNodes.Count - 1], node.NodeId, true);
            }
        }

        private void RenderRumoredNode(int index)
        {
            RectTransform node = CreateImage(
                "AtlasNode-Rumored-" + index,
                rumorStrip,
                Vector2.zero,
                Vector2.one,
                AmberColor);
            node.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;
            CreateText(
                "RumoredLabel",
                node,
                new Vector2(0.05f, 0.08f),
                new Vector2(0.95f, 0.92f),
                "?  RUMORED PLACE",
                12,
                TextAnchor.MiddleCenter,
                InkColor);
            renderedNodes.Add(node.gameObject);
        }

        private void RenderPositionedNode(
            RectTransform parent,
            Vector2 position,
            string objectName,
            string label,
            Color color,
            bool outlined,
            bool isCurrent)
        {
            GameObject nodeObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform node = nodeObject.GetComponent<RectTransform>();
            node.SetParent(parent, false);
            node.anchorMin = position;
            node.anchorMax = position;
            node.pivot = new Vector2(0.5f, 0.5f);
            node.sizeDelta = new Vector2(138f, 72f);
            nodeObject.GetComponent<Image>().color = outlined ? PaperColor : color;

            Outline outline = nodeObject.AddComponent<Outline>();
            outline.effectColor = isCurrent ? CoralColor : color;
            outline.effectDistance = isCurrent ? new Vector2(5f, -5f) : new Vector2(3f, -3f);

            string visibleLabel = isCurrent ? label + "\nCURRENT" : label;
            CreateText(
                "NodeLabel",
                node,
                new Vector2(0.05f, 0.08f),
                new Vector2(0.95f, 0.92f),
                visibleLabel,
                12,
                TextAnchor.MiddleCenter,
                InkColor);
            renderedNodes.Add(nodeObject);
        }

        private void RenderEdges(IReadOnlyList<WorldMapEdgeView> edges)
        {
            IReadOnlyList<string> currentRoute = GetCurrentRoute();
            int sightedIndex = 0;
            int traversedIndex = 0;

            for (int index = 0; index < edges.Count; index++)
            {
                WorldMapEdgeView edge = edges[index];
                if (!positionedNodes.TryGetValue(edge.FromNodeId, out Vector2 from) ||
                    !positionedNodes.TryGetValue(edge.ToNodeId, out Vector2 to))
                {
                    continue;
                }

                bool isCurrentRoute = edge.DiscoveryState == EdgeDiscoveryState.Traversed &&
                    IsCurrentRouteEdge(edge, currentRoute);
                if (edge.DiscoveryState == EdgeDiscoveryState.Sighted)
                    RenderRoad(from, to, "AtlasEdge-Sighted-" + sightedIndex++, true, false);
                else if (edge.DiscoveryState == EdgeDiscoveryState.Traversed)
                    RenderRoad(from, to, "AtlasEdge-Traversed-" + traversedIndex++, false, isCurrentRoute);
                MakeInspectable(renderedEdges[renderedEdges.Count - 1], edge.EdgeId, false);
            }
        }

        private void RenderRoad(Vector2 from, Vector2 to, string objectName, bool dashed, bool isCurrentRoute)
        {
            Vector2 start = NormalizedToLocal(from);
            Vector2 end = NormalizedToLocal(to);
            Vector2 difference = end - start;
            float length = difference.magnitude;

            RectTransform road = CreateRect(objectName, edgeLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            road.anchoredPosition = (start + end) * 0.5f;
            road.sizeDelta = new Vector2(length, 34f);
            road.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg);

            if (dashed)
            {
                const int segmentCount = 7;
                for (int segment = 0; segment < segmentCount; segment++)
                {
                    float startAnchor = segment / (float)segmentCount;
                    float endAnchor = Math.Min(1f, startAnchor + 0.075f);
                    CreateImage(
                        "Dash-" + segment,
                        road,
                        new Vector2(startAnchor, 0.39f),
                        new Vector2(endAnchor, 0.61f),
                        SightedColor);
                }
            }
            else
            {
                CreateImage(
                    "SolidRoad",
                    road,
                    new Vector2(0f, isCurrentRoute ? 0.33f : 0.39f),
                    new Vector2(1f, isCurrentRoute ? 0.67f : 0.61f),
                    isCurrentRoute ? CoralColor : MossColor);
            }

            Text roadLabel = CreateText(
                "RoadLabel",
                road,
                new Vector2(0.12f, 0.58f),
                new Vector2(0.88f, 1f),
                dashed ? "SIGHTED ROAD / DASHED" : isCurrentRoute ? "CURRENT ROUTE / SOLID" : "TRAVERSED ROAD / SOLID",
                8,
                TextAnchor.MiddleCenter,
                InkColor);
            roadLabel.rectTransform.localRotation = Quaternion.Inverse(road.localRotation);
            renderedEdges.Add(road.gameObject);
        }

        private Vector2 NormalizedToLocal(Vector2 position)
        {
            Rect rect = mapContent.rect;
            return new Vector2(
                (position.x - 0.5f) * rect.width,
                (position.y - 0.5f) * rect.height);
        }

        private void CreateRouteButton(WorldMapExitOption option, int index)
        {
            GameObject buttonObject = new GameObject(
                "AtlasRouteButton-" + index,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            RectTransform buttonTransform = buttonObject.GetComponent<RectTransform>();
            buttonTransform.SetParent(routeButtonArea, false);
            buttonObject.GetComponent<Image>().color = index == 0 ? AmberColor : PaperColor;
            buttonObject.AddComponent<Outline>().effectColor = CoralColor;

            string edgeId = option.EdgeId;
            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() => SubmitRoute(edgeId));
            var trigger = buttonObject.AddComponent<EventTrigger>();
            var select = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            select.callback.AddListener(_ => InspectEdge(edgeId));
            trigger.triggers.Add(select);
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            button.navigation = navigation;

            CreateText(
                "RouteLabel",
                buttonTransform,
                new Vector2(0.05f, 0.02f),
                new Vector2(0.95f, 0.98f),
                FormatRouteLabel(option),
                12,
                TextAnchor.MiddleCenter,
                InkColor);
            routeButtons.Add(button);
        }

        private static void StyleReaderControl(Button button)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.65f, 0.65f, 0.65f, 1);
            colors.selectedColor = colors.highlightedColor = Color.white;
            colors.fadeDuration = 0;
            button.colors = colors;
        }

        private void MakeInspectable(GameObject owner, string id, bool node)
        {
            Image image = owner.GetComponent<Image>();
            if (image == null) image = owner.AddComponent<Image>();
            if (!node) image.color = Color.clear;
            Button button = owner.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            inspectionColors.Add(button, image.color);
            (node ? nodeButtons : edgeButtons).Add(id, button);
            button.onClick.AddListener(() => { if (node) InspectNode(id); else InspectEdge(id); });
            var trigger = owner.AddComponent<EventTrigger>();
            var select = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            select.callback.AddListener(_ => { if (node) InspectNode(id); else InspectEdge(id); });
            trigger.triggers.Add(select);
        }

        private void HighlightInspection(Dictionary<string, Button> buttons, string id)
        {
            foreach (var pair in inspectionColors) if (pair.Key != null) pair.Key.image.color = pair.Value;
            if (!buttons.TryGetValue(id, out Button selected)) return;
            selected.image.color = buttons == nodeButtons ? AmberColor : new Color(0.85f, 0.65f, 0.26f, 0.35f);
            mapScroll.StopMovement();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(mapArea, selected.transform);
            Rect viewport = mapArea.rect;
            mapContent.anchoredPosition += new Vector2(
                bounds.min.x < viewport.xMin ? viewport.xMin - bounds.min.x : bounds.max.x > viewport.xMax ? viewport.xMax - bounds.max.x : 0,
                bounds.min.y < viewport.yMin ? viewport.yMin - bounds.min.y : bounds.max.y > viewport.yMax ? viewport.yMax - bounds.max.y : 0);
        }

        private string KnownPlaceName(string nodeId)
        {
            foreach (WorldMapNodeView node in snapshot.Nodes)
                if (node.NodeId == nodeId) return node.PlaceKind == null ? FormatToken(node.DiscoveryState.ToString()) + " PLACE" : FormatToken(node.PlaceKind);
            return "Unrecorded place";
        }

        private Vector2 CurrentViewport()
        {
            Canvas canvas = GetComponent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;
            return camera != null ? new Vector2(camera.pixelWidth, camera.pixelHeight) : new Vector2(Screen.width, Screen.height);
        }

        private void ApplyLayout(Vector2 size)
        {
            viewportSize = size;
            float width = viewportSize.x;
            float height = viewportSize.y;
            bool narrow = width < 720;
            float headerHeight = narrow ? 96 : 64;
            Place(header, 0, 0, width, headerHeight);
            Place((RectTransform)header.Find("AtlasTitle"), 12, 8, narrow ? width - 76 : 210, narrow ? 40 : 48);
            header.Find("AtlasTitle").GetComponent<Text>().fontSize = 22;
            Place(currentNodeText.rectTransform, narrow ? 12 : 236, narrow ? 50 : 8, narrow ? width - 24 : 280, 40);
            Place((RectTransform)closeReader.transform, width - 52, 12, 40, 40);
            currentNodeText.fontSize = 14;
            discoveryText.gameObject.SetActive(!narrow);
            Place(discoveryText.rectTransform, Mathf.Max(540, width - 330), 8, 316, 48);
            float available = height - headerHeight - (reading ? 28 : 124);
            float mapHeight = narrow ? available * 0.45f : available;
            Place(mapArea, 12, headerHeight + 8, narrow ? width - 24 : width - 344, mapHeight);
            Place(inspector, narrow ? 12 : width - 320, narrow ? headerHeight + 16 + mapHeight : 72,
                narrow ? width - 24 : 308, narrow ? available - mapHeight - 8 : available);
            mapContent.sizeDelta = new Vector2(Mathf.Max(720, mapArea.rect.width), Mathf.Max(480, mapArea.rect.height));
            Place(inspectorTitle.rectTransform, 12, 8, inspector.rect.width - 24, 30);
            inspectorTitle.fontSize = 18;
            Place(inspectorScroll.viewport, 12, 40, inspector.rect.width - 24, inspector.rect.height - 86);
            Place((RectTransform)observationsButton.transform, 12, inspector.rect.height - 38, inspector.rect.width - 24, 30);
            Place(routeBand, 0, height - 100, width, 100);
            Place((RectTransform)routeBand.Find("RouteTitle"), 12, 12, 82, 76);
            routeBand.Find("RouteTitle").GetComponent<Text>().fontSize = 16;
            Place(routeScroll.viewport, 104, 12, width - 116, 76);
            LayoutInspector();
        }

        private void LayoutInspector()
        {
            if (inspectorBody == null) return;
            inspectorScroll.content.sizeDelta = new Vector2(0, Mathf.Max(factsHeight, inspectorBody.preferredHeight + 8));
        }

        private void ClearFactRows()
        {
            foreach (GameObject row in factRows) if (row != null) { row.SetActive(false); Destroy(row); }
            factRows.Clear();
            factsHeight = 0;
        }

        private static void Place(RectTransform rect, float horizontal, float vertical, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(horizontal, -vertical);
            rect.sizeDelta = new Vector2(width, height);
        }

        private void ClearRouteButtons()
        {
            for (int index = 0; index < routeButtons.Count; index++)
            {
                if (routeButtons[index] != null)
                {
                    routeButtons[index].gameObject.SetActive(false);
                    Destroy(routeButtons[index].gameObject);
                }
            }

            routeButtons.Clear();
        }

        private void SetOpen(bool open)
        {
            if (canvasGroup == null)
                return;

            canvasGroup.alpha = open ? 1f : 0f;
            canvasGroup.interactable = open;
            canvasGroup.blocksRaycasts = open;
            if (open)
            {
                if (keyboardFocus == null) keyboardFocus = KeyboardMenuFocus.Attach(gameObject, null, () => { if (reading) gameManager?.FieldNotes?.Close(); });
                keyboardFocus.enabled = true;
                keyboardFocus.RecoverFocus();
            }
            else if (keyboardFocus != null) keyboardFocus.enabled = false;
        }

        private void SetDiscoveryMessage(string message)
        {
            lastDiscoveryMessage = message ?? string.Empty;
            if (discoveryText != null)
                discoveryText.text = lastDiscoveryMessage;
        }

        private void UpdateCurrentNodeLabel(IReadOnlyList<WorldMapNodeView> nodes)
        {
            if (currentNodeText == null)
                return;

            string currentNodeId = GetCurrentNodeId();
            for (int index = 0; index < nodes.Count; index++)
            {
                WorldMapNodeView node = nodes[index];
                if (!string.Equals(node.NodeId, currentNodeId, StringComparison.Ordinal) ||
                    node.DiscoveryState != NodeDiscoveryState.Visited)
                {
                    continue;
                }

                string place = string.IsNullOrEmpty(node.PlaceKind)
                    ? "VISITED PLACE"
                    : FormatToken(node.PlaceKind);
                currentNodeText.text = "CURRENT: " + place;
                return;
            }

            currentNodeText.text = "CURRENT: UNRECORDED";
        }

        private string GetCurrentNodeId()
        {
            return gameManager != null && gameManager.Session != null && gameManager.Session.ActiveRun != null
                ? gameManager.Session.ActiveRun.WorldNodeId
                : null;
        }

        private IReadOnlyList<string> GetCurrentRoute()
        {
            if (gameManager == null || gameManager.Session == null || gameManager.Session.ActiveRun == null)
                return Array.Empty<string>();

            return gameManager.Session.ActiveRun.Route;
        }

        private static bool IsCurrentRouteEdge(WorldMapEdgeView edge, IReadOnlyList<string> route)
        {
            if (route.Count == 0)
                return false;
            if (string.Equals(edge.ToNodeId, route[0], StringComparison.Ordinal))
                return true;

            for (int index = 1; index < route.Count; index++)
            {
                if (string.Equals(edge.FromNodeId, route[index - 1], StringComparison.Ordinal) &&
                    string.Equals(edge.ToNodeId, route[index], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatVisitedNode(WorldMapNodeView node)
        {
            string place = string.IsNullOrEmpty(node.PlaceKind) ? "VISITED PLACE" : node.PlaceKind.ToUpperInvariant();
            string biome = string.IsNullOrEmpty(node.BiomeFamily) ? string.Empty : "\n" + node.BiomeFamily.ToUpperInvariant();
            return place + biome + "\nVISITED / SOLID";
        }

        private static List<WorldMapExitOption> OrderRouteChoices(IReadOnlyList<WorldMapExitOption> choices)
        {
            List<WorldMapExitOption> ordered = new List<WorldMapExitOption>(choices);
            ordered.Sort((left, right) =>
            {
                int direction = RouteDirectionRank(left.WorldDirection).CompareTo(RouteDirectionRank(right.WorldDirection));
                return direction != 0 ? direction : string.CompareOrdinal(left.EdgeId, right.EdgeId);
            });
            return ordered;
        }

        private static int RouteDirectionRank(string direction)
        {
            switch (direction)
            {
                case "west": return 0;
                case "northwest": return 1;
                case "southwest": return 2;
                case "north": return 3;
                case "south": return 4;
                case "northeast": return 5;
                case "east": return 6;
                case "southeast": return 7;
                default: return 8;
            }
        }

        private static string FormatRouteLabel(WorldMapExitOption option)
        {
            return FormatToken(option.WorldDirection) + "\n" + FormatToken(option.ClueKey);
        }

        private static string FormatToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "UNMARKED";

            int separator = token.LastIndexOf('.');
            string visible = separator >= 0 && separator + 1 < token.Length
                ? token.Substring(separator + 1)
                : token;
            return visible.Replace('-', ' ').Replace('_', ' ').ToUpperInvariant();
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            GameObject eventSystemObject = new GameObject(
                "AtlasEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(transform, false);
        }

        private Text CreateText(
            string objectName,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            GameObject textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            RectTransform rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            SetAnchors(rectTransform, anchorMin, anchorMax);

            Text text = textObject.GetComponent<Text>();
            text.font = atlasFont != null
                ? atlasFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateImage(
            string objectName,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color)
        {
            GameObject imageObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            SetAnchors(rectTransform, anchorMin, anchorMax);
            imageObject.GetComponent<Image>().color = color;
            return rectTransform;
        }

        private static RectTransform CreateRect(
            string objectName,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject child = new GameObject(objectName, typeof(RectTransform));
            RectTransform rectTransform = child.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            SetAnchors(rectTransform, anchorMin, anchorMax);
            return rectTransform;
        }

        private static void SetAnchors(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax)
        {
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void ClearChildren(RectTransform parent, List<GameObject> trackedObjects)
        {
            if (parent == null)
                return;

            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                GameObject child = parent.GetChild(index).gameObject;
                trackedObjects.Remove(child);
                child.SetActive(false);
                Destroy(child);
            }
        }
    }
}