using System;
using System.Collections.Generic;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
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
        private RectTransform edgeLayer;
        private RectTransform nodeLayer;
        private RectTransform rumorStrip;
        private RectTransform routeButtonArea;
        private Text currentNodeText;
        private Text discoveryText;
        private GameManager gameManager;
        private GameSession boundSession;
        private WorldMapService worldMapService;
        private string selectedRouteEdgeId;
        private string lastDiscoveryMessage = string.Empty;

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
            if (!EnsureWorldMapService())
                return;

            WorldMapSnapshot snapshot = worldMapService.GetAtlasSnapshot();
            ClearChildren(edgeLayer, renderedEdges);
            ClearChildren(nodeLayer, renderedNodes);
            ClearChildren(rumorStrip, renderedNodes);
            positionedNodes.Clear();

            Canvas.ForceUpdateCanvases();
            BuildPositionLookup(snapshot.Nodes);
            RenderEdges(snapshot.Edges);
            RenderNodes(snapshot.Nodes);
            UpdateCurrentNodeLabel(snapshot.Nodes);
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
            if (gameManager.IsRouteChoiceActive && gameManager.RouteChoices.Count > 0)
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
            ClearRouteButtons();
            if (choices == null || choices.Count == 0 || gameManager == null || !gameManager.IsRouteChoiceActive)
            {
                SetOpen(false);
                return;
            }

            selectedRouteEdgeId = null;
            SetDiscoveryMessage("DISCOVERY: ROUTES OBSERVED");
            RefreshAtlas();
            for (int index = 0; index < choices.Count; index++)
                CreateRouteButton(choices[index], index);

            SetOpen(true);
            Canvas.ForceUpdateCanvases();
            EnsureEventSystem();
            routeButtons[0].Select();
            EventSystem.current.SetSelectedGameObject(routeButtons[0].gameObject);
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
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GetOrAddComponent<GraphicRaycaster>(gameObject);
            canvasGroup = GetOrAddComponent<CanvasGroup>(gameObject);

            RectTransform root = GetOrAddComponent<RectTransform>(gameObject);
            Stretch(root);
            CreateImage("Backdrop", root, Vector2.zero, Vector2.one, BackdropColor);

            RectTransform header = CreateImage(
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
            edgeLayer = CreateRect("AtlasEdges", mapArea, Vector2.zero, Vector2.one);
            nodeLayer = CreateRect("AtlasNodes", mapArea, Vector2.zero, Vector2.one);
            rumorStrip = CreateImage(
                "AtlasRumors",
                mapArea,
                new Vector2(0.02f, 0.03f),
                new Vector2(0.29f, 0.29f),
                new Color32(48, 53, 49, 238));
            VerticalLayoutGroup rumorLayout = rumorStrip.gameObject.AddComponent<VerticalLayoutGroup>();
            rumorLayout.padding = new RectOffset(12, 12, 10, 10);
            rumorLayout.spacing = 8f;
            rumorLayout.childControlHeight = false;
            rumorLayout.childForceExpandHeight = false;
            rumorLayout.childControlWidth = true;

            RectTransform legend = CreateImage(
                "AtlasLegend",
                root,
                new Vector2(0.77f, 0.24f),
                new Vector2(0.98f, 0.82f),
                BandColor);
            CreateText(
                "LegendTitle",
                legend,
                new Vector2(0.08f, 0.88f),
                new Vector2(0.92f, 0.98f),
                "MAP KEY",
                22,
                TextAnchor.MiddleLeft,
                PaperColor);
            CreateText(
                "LegendBody",
                legend,
                new Vector2(0.08f, 0.07f),
                new Vector2(0.92f, 0.87f),
                "PLACES\n\nUNKNOWN  HIDDEN\nRUMORED  ? MARK\nSIGHTED  OUTLINE\nVISITED  SOLID\n\nROADS\n\nUNKNOWN  HIDDEN\nSIGHTED  DASHED\nTRAVERSED  SOLID\n\nCURRENT  DOUBLE BORDER\nCURRENT ROUTE  WIDE MARK",
                14,
                TextAnchor.UpperLeft,
                PaperColor);

            RectTransform routeBand = CreateImage(
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
            routeButtonArea = CreateRect(
                "AtlasRouteButtons",
                routeBand,
                new Vector2(0.21f, 0.15f),
                new Vector2(0.975f, 0.85f));
            HorizontalLayoutGroup routeLayout = routeButtonArea.gameObject.AddComponent<HorizontalLayoutGroup>();
            routeLayout.spacing = 16f;
            routeLayout.childAlignment = TextAnchor.MiddleCenter;
            routeLayout.childControlWidth = true;
            routeLayout.childControlHeight = true;
            routeLayout.childForceExpandWidth = true;
            routeLayout.childForceExpandHeight = true;
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
                    continue;
                }

                if (!positionedNodes.TryGetValue(node.NodeId, out Vector2 position))
                    continue;

                bool isCurrent = string.Equals(node.NodeId, currentNodeId, StringComparison.Ordinal);
                if (node.DiscoveryState == NodeDiscoveryState.Sighted)
                    RenderPositionedNode(nodeLayer, position, "AtlasNode-Sighted-" + sightedIndex++, "SIGHTED PLACE", SightedColor, true, isCurrent);
                else if (node.DiscoveryState == NodeDiscoveryState.Visited)
                    RenderPositionedNode(nodeLayer, position, "AtlasNode-Visited-" + visitedIndex++, FormatVisitedNode(node), MossColor, false, isCurrent);
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
            node.sizeDelta = new Vector2(190f, 92f);
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
            Rect rect = mapArea.rect;
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
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            button.navigation = navigation;

            CreateText(
                "RouteLabel",
                buttonTransform,
                new Vector2(0.05f, 0.08f),
                new Vector2(0.95f, 0.92f),
                FormatRouteLabel(option),
                12,
                TextAnchor.MiddleCenter,
                InkColor);
            routeButtons.Add(button);
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