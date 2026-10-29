using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Composes the active board's single command/presentation path and guarded session lifecycle.
/// Borrows camera framing for intent UI when needed and restores it when the UI lifetime ends.
/// Prepares intents during setup but reveals their markers and legend only after the day transition ends.
/// </summary>
public class GameManager : MonoBehaviour, IBoardOutcomeSink
{
    private const int InitialHealth = 100;
    private const int InitialFood = 100;
    private const string OpeningWarmUpOriginNodeId = "shelter.warm-up";
    private const string OpeningWarmUpEdgeId = "road.shelter-warm-up";
    private const string MenuSceneName = "Menu";
    private static readonly IReadOnlyList<WorldMapExitOption> NoRouteChoices =
        Array.AsReadOnly(new WorldMapExitOption[0]);

    /// <summary>Identifies how entering the gameplay scene obtains its profile and run.</summary>
    public enum RunLaunchMode
    {
        /// <summary>Starts another run while preserving the current profile.</summary>
        NewRun,

        /// <summary>Replaces the current profile before starting its first run.</summary>
        NewGame,

        /// <summary>Restores the persisted profile and active run.</summary>
        Continue
    }

    private static RunLaunchMode requestedLaunchMode = RunLaunchMode.NewRun;
    internal static string PersistenceRootOverride;

    public float levelStartDelay = 2f;
    public static GameManager instance = null;
    public BoardManager boardScript;
    [SerializeField] private TextAsset worldDefinitionJson;

    private Text levelText;
    private Text scoreText;
    private GameObject levelImage;
    private GameObject restartButton;
    private GameObject menuButton;
    private bool doingSetup;
    private bool gameplayInputBlocked;
    private bool boardStartupInProgress;
    private int gameplayInputResumeFrame = -1;
    private int nextRunSequence;
    private GameSession session;
    private RunLifecycleService lifecycle;
    private ISaveStore saveStore;
    private WorldDefinition worldDefinition;
    private WorldMapService worldMap;
    private BoardRequest activeBoardRequest;
    private BoardRuntime activeBoardRuntime;
    private BoardEventPresenter activeBoardPresenter;
    private GameObject activeIntentOwner;
    private EnemyIntentPresenter activeIntents;
    private ToolHudPresenter activeToolHud;
    private EnemyIntentLegend activeIntentLegend;
    private FieldNotesPresenter fieldNotes;
    private GameObject fieldNotesOwner;
    private ProfileState fieldNotesProfile;
    private FactKnowledge factKnowledge;
    private FactDiscoveryRecorder factRecorder;
    private bool fieldNotesInputBlocked;
    private bool fieldNotesAfterDeath;
    private bool fieldNotesGameplayScene;
    private Vector2 intentViewport;
    private Camera intentLayoutCamera;
    private Rect originalIntentCameraRect;
    private float originalIntentCameraSize;
    private Camera boardVisibilityCamera;
    private Camera viewportClearCamera;
    private int gameplayCullingMask;
    private PlayerScript activePlayerView;
    private bool handlingBoardOutcome;
    private bool boardOutcomeHandled;
    private BoardOutcome pendingBoardOutcome;
    private bool arrivalCheckpointCommitted;
    private bool arrivalDiscoveryCommitted;
    private string pendingChosenEdgeId;
    private IReadOnlyList<WorldMapExitOption> routeChoices = NoRouteChoices;
    private bool terminalActionRequested;
    private Action<string> sceneLoader = SceneManager.LoadScene;

    public GameSession Session
    {
        get { return session; }
    }

    /// <summary>Gets the immutable request that identifies the active local board.</summary>
    public BoardRequest ActiveBoardRequest
    {
        get { return activeBoardRequest; }
    }

    /// <summary>Gets the complete authoritative runtime for the active local board.</summary>
    public BoardRuntime ActiveBoardRuntime
    {
        get { return activeBoardRuntime; }
    }

    /// <summary>Gets the sole presenter for the active board, including its transient gate and diagnostics.</summary>
    public BoardEventPresenter ActiveBoardPresenter => activeBoardPresenter;

    /// <summary>Gets the read-only equipment display for the current board lifetime, or null before setup.</summary>
    public ToolHudPresenter ActiveToolHud => activeToolHud;

    /// <summary>Gets the profile-owned reader and committed discovery feedback, retained across death and new runs.</summary>
    public FieldNotesPresenter FieldNotes => fieldNotes;

    /// <summary>Gets the legal options at the reached checkpoint, never an intermediate segment.</summary>
    public IReadOnlyList<WorldMapExitOption> RouteChoices
    {
        get { return routeChoices; }
    }

    /// <summary>Gets whether gameplay is waiting for an explicit route selection.</summary>
    public bool IsRouteChoiceActive
    {
        get
        {
            return worldMap != null
                && (worldMap.ChoiceState == RouteChoiceState.AwaitingChoice ||
                    worldMap.ChoiceState == RouteChoiceState.RunSavePending);
        }
    }

    /// <summary>Raised when the placeholder route-choice presentation should refresh.</summary>
    public event Action<IReadOnlyList<WorldMapExitOption>> OnRouteChoicesChanged;

    /// <summary>Gets whether lifecycle and presentation ownership admit a complete board command.</summary>
    public bool IsGameplayInputEnabled
    {
        get
        {
            return !IsGameplayInputBlocked
                && Time.frameCount > gameplayInputResumeFrame
                && session != null
                && session.ActiveRun != null
                && session.ActiveRun.Status == RunStatus.Active
                && activeBoardPresenter != null
                && activeBoardPresenter.Coordinator.CanSubmit;
        }
    }

    /// <summary>Gets lifecycle blocking independently of transient presentation ownership.</summary>
    public bool IsGameplayInputBlocked
    {
        get { return !enabled || doingSetup || boardStartupInProgress || gameplayInputBlocked
            || fieldNotesInputBlocked || IsRouteChoiceActive || boardOutcomeHandled || terminalActionRequested; }
    }

    /// <summary>Requests another run in the current profile.</summary>
    public static void RequestNewRun()
    {
        requestedLaunchMode = RunLaunchMode.NewRun;
    }

    /// <summary>Requests a new game that replaces the saved profile before its first run starts.</summary>
    public static void RequestNewGame()
    {
        requestedLaunchMode = RunLaunchMode.NewGame;
    }

    /// <summary>Requests restoration of the persisted profile and active run.</summary>
    public static void RequestContinue()
    {
        requestedLaunchMode = RunLaunchMode.Continue;
    }

    /// <summary>Gets whether a nonterminal board may open pause without stealing a target draft or its cancel key frame.</summary>
    public bool CanPauseGameplay
    {
        get
        {
            return enabled
                && !doingSetup
                && !gameplayInputBlocked
                && session != null
                && session.ActiveRun != null
                && session.ActiveRun.Status == RunStatus.Active
                && !IsRouteChoiceActive
                && !boardOutcomeHandled
                && activeBoardRuntime != null
                && !activeBoardRuntime.IsDisposed
                && activeBoardPresenter != null
                && (activeBoardPresenter.Coordinator.Gate.Blocks & (PresentationInputBlock.Interaction | PresentationInputBlock.FieldNotes)) == PresentationInputBlock.None
                && !activeBoardRuntime.Controller.IsTerminal;
        }
    }

    /// <summary>Sets the pause owner's block without releasing setup, route, terminal, or presentation ownership.</summary>
    public void SetGameplayInputBlocked(bool blocked)
    {
        gameplayInputBlocked = blocked;

        if (!blocked)
            gameplayInputResumeFrame = Time.frameCount;
        UpdatePresentationBlocks();
    }

    /// <summary>Admits one complete command through the current board's coordinator; blocked calls never resolve.</summary>
    /// <param name="command">A complete intent; null is a free no-command sample.</param>
    /// <param name="submission">The admitted operation, or null when lifecycle ownership blocks admission.</param>
    /// <returns>Whether the current coordinator was called, independently of domain acceptance.</returns>
    public bool TrySubmitCommand(PlayerCommand command, out Task<CommandSubmission> submission)
    {
        UpdatePresentationBlocks();
        submission = null;
        if (!IsGameplayInputEnabled)
            return false;
        submission = activeBoardPresenter.Coordinator.SubmitAsync(command);
        return true;
    }

    /// <summary>Samples at most one complete intent only when every production input owner admits it.</summary>
    /// <param name="source">The PC binding or another complete-command source; never sampled while blocked.</param>
    /// <param name="submission">The admitted operation, or null when blocked.</param>
    /// <returns>Whether source submission was admitted, not whether an intent was available or accepted.</returns>
    public bool TrySubmitInput(ICommandSource source, out Task<CommandSubmission> submission)
    {
        UpdatePresentationBlocks();
        submission = null;
        if (!IsGameplayInputEnabled)
            return false;
        submission = activeBoardPresenter.Coordinator.SubmitFromAsync(source);
        return true;
    }

    /// <summary>
    /// Opens route choice at an arrived checkpoint, never during the mandatory opening leg.
    /// </summary>
    /// <returns>Whether route choice is ready, including a terminal node with no outgoing road.</returns>
    public bool BeginRouteChoice()
    {
        RunState currentRun = session?.ActiveRun;
        if (session == null ||
            currentRun == null ||
            currentRun.Status != RunStatus.Active ||
            (currentRun.ActiveLeg == null &&
                string.Equals(currentRun.WorldNodeId, OpeningWarmUpOriginNodeId, StringComparison.Ordinal)) ||
            !TryEnsureWorldMap())
        {
            return false;
        }

        WorldMapCommandResult beginResult = worldMap.BeginRouteChoice();
        if (!beginResult.IsSuccess)
        {
            Debug.LogError("Route choice could not begin: " + beginResult.ErrorMessage);
            return false;
        }

        WorldMapExitQueryResult choicesResult = worldMap.GetRouteChoices();
        if (!choicesResult.IsSuccess)
        {
            Debug.LogError("Route choices could not be queried: " + choicesResult.ErrorMessage);
            return false;
        }

        SetRouteChoices(choicesResult.Exits);
        return true;
    }

    /// <summary>
    /// Starts one displayed route leg and loads its first segment only after the Pending checkpoint succeeds.
    /// </summary>
    /// <param name="edgeId">A stable edge ID from <see cref="RouteChoices"/>.</param>
    /// <returns><c>true</c> only when this call commits a new route and requests the next board.</returns>
    public bool ChooseRoute(string edgeId)
    {
        if (!IsRouteChoiceActive || !TryEnsureWorldMap())
            return false;

        if (worldMap.ChoiceState != RouteChoiceState.RunSavePending)
            pendingChosenEdgeId = edgeId;
        RouteChoiceCommandResult result = worldMap.ChooseRoute(edgeId);
        if (result.Status == RouteChoiceCommandStatus.PersistenceFailed)
        {
            Debug.LogError(
                "Route choice save failed for " + result.PersistenceTarget +
                ": " + result.PersistenceResultType);
            return false;
        }

        if (result.Status == RouteChoiceCommandStatus.Rejected)
        {
            Debug.LogWarning("Route choice rejected: " + result.ErrorMessage);
            return false;
        }

        if (result.Status != RouteChoiceCommandStatus.Applied)
            return false;

        pendingChosenEdgeId = null;
        SetGameplayInputBlocked(true);
        SetRouteChoices(NoRouteChoices);
        sceneLoader(SceneManager.GetActiveScene().name);
        return true;
    }

    /// <summary>Retries a blocked travel checkpoint without replaying a turn or committed arrival.</summary>
    /// <returns>Whether the pending boundary is ready to continue.</returns>
    public bool RetryTravelCheckpoint()
    {
        if (pendingBoardOutcome != null)
            return HandleBoardOutcome(pendingBoardOutcome);
        if (worldMap?.ChoiceState == RouteChoiceState.RunSavePending)
            return ChooseRoute(pendingChosenEdgeId);
        if (session?.ActiveRun != null && activeBoardRuntime == null &&
            session.ActiveRun.ActiveLeg == null &&
            string.Equals(session.ActiveRun.WorldNodeId, OpeningWarmUpOriginNodeId, StringComparison.Ordinal))
        {
            if (!TryEnterCurrentWorldNode() || !TryStartOpeningWarmUp())
                return false;
            InitGame();
            return activeBoardRuntime != null;
        }
        if (session?.ActiveRun?.ActiveLeg != null && activeBoardRuntime == null)
        {
            InitGame();
            return activeBoardRuntime != null;
        }
        if (session?.ActiveRun != null && activeBoardRuntime == null && TryEnterCurrentWorldNode())
            return BeginRouteChoice();
        return false;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        boardScript = GetComponent<BoardManager>();
        session = new GameSession(new ProfileState("default-profile", "world-1", 1));
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (session != null)
        {
            session.OnRunStarted += OnRunStarted;
            session.OnRunAbandoned += OnRunAbandoned;
        }
        if (activeIntentOwner != null)
        {
            try { activeBoardPresenter.SynchronizeFromState(); }
            catch (Exception exception)
            {
                gameplayInputBlocked = true;
                DisposeIntentUi();
                Debug.LogError("Intent UI resume failed: " + exception.Message);
            }
        }
        UpdatePresentationBlocks();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (session != null)
        {
            session.OnRunStarted -= OnRunStarted;
            session.OnRunAbandoned -= OnRunAbandoned;
        }
        if (activeIntents != null && !activeIntents.IsDisposed)
            activeIntents.Clear();
        if (activeIntentLegend != null)
            activeIntentLegend.Hide();
        if (activeIntentOwner != null)
            activeIntentOwner.SetActive(false);
        UpdatePresentationBlocks();
    }

    private void OnDestroy()
    {
        DisposeFieldNotes();
        DisposeActiveBoardRuntime();
        ReleaseBoardCamera();
        if (instance == this)
            instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.buildIndex != 1)
        {
            fieldNotesGameplayScene = false;
            fieldNotes?.SetAvailable(false);
            DisposeActiveBoardRuntime();
            ReleaseBoardCamera();
            return;
        }

        fieldNotesGameplayScene = true;
        BlockBoardStartup();
        EnsurePersistence();
        if (requestedLaunchMode == RunLaunchMode.NewGame)
        {
            if (!TryStartNewGame())
                return;
        }
        else if (!session.IsRunActive() || session.ActiveRun.Status != RunStatus.Active)
        {
            if (requestedLaunchMode == RunLaunchMode.Continue)
            {
                SaveStoreResult<RunState> continueResult = lifecycle.ContinueRun();
                if (continueResult.IsFailure)
                {
                    Debug.LogError("Continue failed: " + continueResult.Type);
                    return;
                }

            }
            else
            {
                if (!TryStartNewRun())
                    return;
            }
        }

        requestedLaunchMode = RunLaunchMode.NewRun;
        if (worldMap?.ChoiceState == RouteChoiceState.RunSavePending)
        {
            ChooseRoute(pendingChosenEdgeId);
            return;
        }
        if (!TryEnterCurrentWorldNode())
            return;

        if (session.ActiveRun.ActiveLeg == null &&
            string.Equals(session.ActiveRun.WorldNodeId, OpeningWarmUpOriginNodeId, StringComparison.Ordinal) &&
            !TryStartOpeningWarmUp())
        {
            return;
        }

        if (session.ActiveRun.ActiveLeg != null)
            InitGame();
        else
        {
            PrepareSceneUi();
            boardStartupInProgress = false;
            doingSetup = false;
            BeginRouteChoice();
        }
    }

    private void BlockBoardStartup()
    {
        CancelInvoke(nameof(HideLevelImage));
        boardStartupInProgress = true;
        doingSetup = true;
        DisposeActiveBoardRuntime();
        boardOutcomeHandled = false;
        pendingBoardOutcome = null;
        arrivalCheckpointCommitted = false;
        arrivalDiscoveryCommitted = false;
        SetRouteChoices(NoRouteChoices);
        SetGameplayInputBlocked(true);
    }

    private void InitGame()
    {
        doingSetup = true;
        terminalActionRequested = false;
        SetRouteChoices(NoRouteChoices);
        SetGameplayInputBlocked(true);
        CancelInvoke(nameof(HideLevelImage));
        PrepareSceneUi();

        activeBoardRequest = null;
        activeBoardRuntime = null;
        boardOutcomeHandled = false;
        if (boardScript == null || worldDefinition == null)
        {
            SetGameplayInputBlocked(true);
            Debug.LogError("Board startup failed because its adapter dependencies are unavailable.");
            return;
        }

        try
        {
            RunState run = session.GetCurrentRun();
            bool showIntro = run.ActiveLeg == null;
            if (run.ActiveLeg != null)
            {
                if (!TryEnsureWorldMap())
                    return;
                SaveStoreResult introResult = worldMap.Travel.ConsumeIntro(out showIntro);
                if (introResult.IsFailure)
                {
                    Debug.LogError("Day intro checkpoint failed: " + introResult.Type);
                    return;
                }
            }
            BoardRequest request = BoardRequest.CreateFromRun(run, worldDefinition);
            if (request.Address?.GenerationConfigurationId == RadioTowerLandmark.ConfigurationId)
            {
                var placement = RadioTowerLandmark.Load().CreatePlacement(worldDefinition);
                var selected = run.ActiveLandmark ?? placement.SelectVariant(request.Address, run.RunSeed);
                SaveStoreResult chosen = worldMap.Travel.SelectLandmark(request, selected);
                if (chosen.IsFailure)
                {
                    Debug.LogError("Landmark selection checkpoint failed: " + chosen.Type);
                    return;
                }
            }
            GameObject playerView = GameObject.FindGameObjectWithTag("Player");
            if (request.Address != null && playerView != null)
                playerView.transform.position = new Vector3(0, 0, playerView.transform.position.z);
            BoardRuntime runtime = boardScript.SetupScene(request, run, playerView);

            PublishBoardRuntime(runtime);
            boardStartupInProgress = false;
            SetGameplayInputBlocked(false);
            if (showIntro)
            {
                if (levelText != null)
                    levelText.text = "Day: " + Math.Max(1, run.CurrentDay) +
                        (run.ActiveLandmark == null ? string.Empty : "\nRadio tower");
                if (levelImage != null)
                    levelImage.SetActive(true);
                activePlayerView.SetHudVisible(false);
                Invoke(nameof(HideLevelImage), levelStartDelay);
            }
            else
                HideLevelImage();
        }
        catch (Exception exception)
        {
            DisposeActiveBoardRuntime();
            CancelInvoke(nameof(HideLevelImage));
            SetGameplayInputBlocked(true);
            Debug.LogError("Board startup failed: " + exception.Message);
        }
    }

    private void PrepareSceneUi()
    {
        levelImage = FindSceneObject("LevelImage");
        levelText = FindSceneObject("LevelText")?.GetComponent<Text>();
        restartButton = FindSceneObject("RestartBttn");
        menuButton = FindSceneObject("MenuBttn");
        scoreText = FindSceneObject("ScoreText")?.GetComponent<Text>();
        if (levelImage != null)
        {
            levelImage.transform.SetAsFirstSibling();
            levelImage.SetActive(false);
        }
        if (restartButton != null)
            restartButton.SetActive(false);
        if (menuButton != null)
            menuButton.SetActive(false);
        if (scoreText != null)
            scoreText.text = string.Empty;
        GameObject.FindGameObjectWithTag("Player")?.GetComponent<PlayerScript>()?.SetHudVisible(false);
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name == objectName)
                    return candidate.gameObject;
        return null;
    }

    private void PublishBoardRuntime(BoardRuntime runtime)
    {
        fieldNotesGameplayScene = true;
        EnsureFieldNotes();
        if (activeBoardPresenter != null)
            activeBoardPresenter.Dispose();
        DisposeIntentUi();
        if (activeBoardRuntime != null && !ReferenceEquals(activeBoardRuntime, runtime))
            activeBoardRuntime.Dispose();
        activeBoardRuntime = runtime;
        activeBoardRequest = runtime.Request;
        activePlayerView = runtime.Views[runtime.PlayerId].GetComponent<PlayerScript>();
        if (activePlayerView == null)
            throw new InvalidOperationException("The registered player has no production input/view adapter.");
        activeIntentOwner = new GameObject("Board intent UI") { hideFlags = HideFlags.DontSave };
        activeIntentOwner.SetActive(false);
        activeIntentOwner.transform.SetParent(transform, false);
        activeIntents = activeIntentOwner.AddComponent<EnemyIntentPresenter>();
        activeBoardPresenter = new BoardEventPresenter(runtime, activePlayerView, activePlayerView,
            this, ReportPresentationDiagnostic, activePlayerView.moveTime, activeIntents,
            () => !IsGameplayInputBlocked && Time.frameCount > gameplayInputResumeFrame &&
                activePlayerView != null && activePlayerView.isActiveAndEnabled,
            observeObservation: ObservePresentedObservation);
        activeBoardPresenter.AnimationsEnabled = activePlayerView.animationsEnabled;
        activeBoardPresenter.SynchronizeFromState();
        intentViewport = new Vector2(Screen.width, Screen.height);
        activeToolHud = new ToolHudPresenter(runtime.RunState, activeIntentOwner.transform, intentViewport);
        if (Camera.main != null && Camera.main.orthographic)
        {
            BorrowIntentLayoutCamera();
            ReserveIntentCameraMargin(intentViewport);
        }
        activeIntentLegend = CreateIntentLegend();
        UpdatePresentationBlocks();
    }

    private EnemyIntentLegend CreateIntentLegend()
    {
        try
        {
            return new EnemyIntentLegend(activeIntentOwner.transform, intentViewport, InspectedBoardArea());
        }
        catch (ArgumentException exception) when (exception.ParamName == "inspectedArea" &&
            Camera.main != null && Camera.main.orthographic && intentLayoutCamera == null)
        {
            BorrowIntentLayoutCamera();
            ReserveIntentCameraMargin(intentViewport);
            return new EnemyIntentLegend(activeIntentOwner.transform, intentViewport, InspectedBoardArea());
        }
    }

    private void BorrowIntentLayoutCamera()
    {
        intentLayoutCamera = Camera.main;
        originalIntentCameraRect = intentLayoutCamera.rect;
        originalIntentCameraSize = intentLayoutCamera.orthographicSize;
    }

    private void ReserveIntentCameraMargin(Vector2 size)
    {
        if (intentLayoutCamera == null)
            return;
        intentLayoutCamera.rect = size.x >= 800
            ? new Rect(0, 0, (size.x - 224) / size.x, (size.y - ToolHudPresenter.ReservedHeight) / size.y)
            : new Rect(0, 160 / size.y, 1, (size.y - 160 - ToolHudPresenter.ReservedHeight) / size.y);
        var bounds = activeBoardRuntime.BoardState.Bounds;
        float width = (float)bounds.MaxX - bounds.MinX + 1;
        float height = (float)bounds.MaxY - bounds.MinY + 1;
        intentLayoutCamera.orthographicSize = Mathf.Max(originalIntentCameraSize,
            height / 2, width / (2 * intentLayoutCamera.aspect));
    }

    private Rect InspectedBoardArea()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return default;
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var entity in activeBoardRuntime.BoardState.GetEntities())
        {
            foreach (Vector2 offset in new[] { new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, 0.5f),
                new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f) })
            {
                Vector3 point = camera.WorldToScreenPoint(new Vector3(entity.Position.X + offset.x,
                    entity.Position.Y + offset.y, 0));
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
        }
        return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
    }

    private void LateUpdate()
    {
        UpdateBoardVisibility();
        activeToolHud?.Refresh();
        if (activeIntentLegend == null || activeBoardRuntime == null || activeBoardRuntime.IsDisposed)
            return;
        Vector2 size = new Vector2(Screen.width, Screen.height);
        if (size == intentViewport)
            return;
        try
        {
            activeToolHud?.Resize(size);
            ReserveIntentCameraMargin(size);
            try
            {
                activeIntentLegend.Resize(size, InspectedBoardArea());
            }
            catch (ArgumentException exception) when (exception.ParamName == "inspectedArea" &&
                Camera.main != null && Camera.main.orthographic && intentLayoutCamera == null)
            {
                BorrowIntentLayoutCamera();
                ReserveIntentCameraMargin(size);
                activeIntentLegend.Resize(size, InspectedBoardArea());
            }
            intentViewport = size;
        }
        catch (Exception exception)
        {
            SetGameplayInputBlocked(true);
            DisposeIntentUi();
            Debug.LogError("Intent UI layout failed: " + exception.Message);
        }
    }

    private void DisposeIntentUi()
    {
        if (activeIntents != null)
            activeIntents.Dispose();
        activeIntents = null;
        activeIntentLegend?.Dispose();
        activeIntentLegend = null;
        activeToolHud?.Dispose();
        activeToolHud = null;
        if (intentLayoutCamera != null)
        {
            intentLayoutCamera.rect = originalIntentCameraRect;
            intentLayoutCamera.orthographicSize = originalIntentCameraSize;
        }
        intentLayoutCamera = null;
        if (activeIntentOwner != null)
        {
            activeIntentOwner.SetActive(false);
            Destroy(activeIntentOwner);
        }
        activeIntentOwner = null;
    }

    private void EnsureFieldNotes()
    {
        if (fieldNotes != null && ReferenceEquals(fieldNotesProfile, session.Profile)) return;
        DisposeFieldNotes();
        if (factKnowledge == null) factKnowledge = FactContentCatalog.Load();
        fieldNotesProfile = session.Profile;
        factRecorder = new FactDiscoveryRecorder(fieldNotesProfile, factKnowledge, current =>
        {
            if (saveStore == null) throw new InvalidOperationException("Fact discovery requires the profile save store.");
            return saveStore.SaveProfile(current);
        });
        fieldNotesOwner = new GameObject("Profile Field Notes") { hideFlags = HideFlags.DontSave };
        fieldNotesOwner.transform.SetParent(transform, false);
        fieldNotes = fieldNotesOwner.AddComponent<FieldNotesPresenter>();
        fieldNotes.Bind(fieldNotesProfile, factKnowledge, MayOpenFieldNotes, SetFieldNotesInputBlocked,
            new Vector2(Screen.width, Screen.height));
    }

    private bool MayOpenFieldNotes()
    {
        if ((!enabled && !fieldNotesAfterDeath) || !fieldNotesGameplayScene || doingSetup ||
            boardStartupInProgress || terminalActionRequested || gameplayInputBlocked && !fieldNotesAfterDeath)
            return false;
        if (activeBoardPresenter == null) return true;
        PresentationGate gate = activeBoardPresenter.Coordinator.Gate;
        return gate.State == PresentationGateState.Idle &&
            (gate.Blocks & PresentationInputBlock.Interaction) == PresentationInputBlock.None;
    }

    private void SetFieldNotesInputBlocked(bool blocked)
    {
        fieldNotesInputBlocked = blocked;
        if (blocked)
            activeIntentLegend?.Hide();
        if (!blocked) gameplayInputResumeFrame = Time.frameCount;
        UpdatePresentationBlocks();
    }

    private void DisposeFieldNotes()
    {
        fieldNotes?.Dispose();
        fieldNotes = null;
        fieldNotesProfile = null;
        factRecorder = null;
        if (fieldNotesOwner != null) Destroy(fieldNotesOwner);
        fieldNotesOwner = null;
    }

    private void ObservePresentedObservation(FactObservation observation)
    {
        EnsureFieldNotes();
        foreach (FactDiscovery discovery in factRecorder.Observe(observation)) fieldNotes.ShowDiscovery(discovery);
    }

    private void ReportPresentationDiagnostic(PresentationDiagnostic diagnostic)
    {
        if (diagnostic.Code != PresentationDiagnosticCode.Canceled)
            Debug.LogError("Board presentation failed: " + diagnostic.Code + " at event " +
                diagnostic.EventIndex + " (" + diagnostic.EventType + "): " + diagnostic.Error);
    }

    private void UpdatePresentationBlocks()
    {
        UpdateBoardVisibility();
        fieldNotes?.SetAvailable((enabled || fieldNotesAfterDeath) && fieldNotesGameplayScene &&
            !doingSetup && !boardStartupInProgress && !terminalActionRequested &&
            (fieldNotesAfterDeath || !IsRouteChoiceActive && !boardOutcomeHandled));
        if (activeIntentOwner != null)
            activeIntentOwner.SetActive(enabled && !doingSetup && !boardStartupInProgress &&
                !IsRouteChoiceActive && !boardOutcomeHandled && !terminalActionRequested &&
                activeBoardRuntime != null && !activeBoardRuntime.IsDisposed);
        if (activeIntentLegend != null)
            activeIntentLegend.Root.SetActive(!fieldNotesInputBlocked);
        if (activeToolHud != null)
            activeToolHud.Root.SetActive(!fieldNotesInputBlocked);
        if (IsRouteChoiceActive || boardOutcomeHandled)
            activePlayerView?.SetHudVisible(false);
        if (activeBoardPresenter == null)
            return;
        PresentationGate gate = activeBoardPresenter.Coordinator.Gate;
        gate.SetBlocked(PresentationInputBlock.Setup, doingSetup || boardStartupInProgress);
        gate.SetBlocked(PresentationInputBlock.Modal, gameplayInputBlocked || IsRouteChoiceActive);
        gate.SetBlocked(PresentationInputBlock.FieldNotes, fieldNotesInputBlocked);
        gate.SetBlocked(PresentationInputBlock.Disabled, !enabled || boardOutcomeHandled ||
            terminalActionRequested || Time.frameCount <= gameplayInputResumeFrame);
    }

    private void UpdateBoardVisibility()
    {
        if (!fieldNotesGameplayScene)
        {
            ReleaseBoardCamera();
            return;
        }
        Camera camera = Camera.main;
        if (camera == null)
            return;
        if (boardVisibilityCamera != camera)
        {
            ReleaseBoardCamera();
            boardVisibilityCamera = camera;
            gameplayCullingMask = camera.cullingMask;
            var clearOwner = new GameObject("Board viewport clear") { hideFlags = HideFlags.DontSave };
            clearOwner.transform.SetParent(camera.transform, false);
            viewportClearCamera = clearOwner.AddComponent<Camera>();
            viewportClearCamera.cullingMask = 0;
            viewportClearCamera.clearFlags = CameraClearFlags.SolidColor;
            viewportClearCamera.useOcclusionCulling = false;
        }
        viewportClearCamera.rect = new Rect(0, 0, 1, 1);
        viewportClearCamera.depth = camera.depth - 1;
        viewportClearCamera.backgroundColor = camera.backgroundColor;
        viewportClearCamera.targetDisplay = camera.targetDisplay;
        viewportClearCamera.targetTexture = camera.targetTexture;
        viewportClearCamera.enabled = camera.isActiveAndEnabled;
        bool visible = enabled && !doingSetup && !boardStartupInProgress && !IsRouteChoiceActive &&
            !boardOutcomeHandled && !terminalActionRequested &&
            activeBoardRuntime != null && !activeBoardRuntime.IsDisposed;
        camera.cullingMask = visible ? gameplayCullingMask : 0;
    }

    private void ReleaseBoardCamera()
    {
        if (boardVisibilityCamera != null)
            boardVisibilityCamera.cullingMask = gameplayCullingMask;
        boardVisibilityCamera = null;
        if (viewportClearCamera != null)
        {
            viewportClearCamera.enabled = false;
            Destroy(viewportClearCamera.gameObject);
        }
        viewportClearCamera = null;
    }

    /// <summary>Maps an ordered terminal event from the exact active runtime to its guarded lifecycle outcome.</summary>
    /// <param name="request">The originating runtime's request instance; replaced-board instances are stale.</param>
    /// <param name="outcome">An already-resolved exit, starvation, or death event.</param>
    /// <returns>Whether this active board accepted the outcome once.</returns>
    public bool TryNotify(BoardRequest request, GameEvent outcome)
    {
        if (!ReferenceEquals(request, activeBoardRequest) || boardOutcomeHandled ||
            activeBoardRuntime == null || activeBoardRuntime.IsDisposed)
            return false;
        BoardOutcome boardOutcome;
        if (outcome is ExitReachedEvent reached)
        {
            LandmarkRecord landmark = ResolveLandmarkExit(reached);
            if (session.ActiveRun.ActiveLandmark != null && landmark == null) return false;
            boardOutcome = BoardOutcome.ExitReached(request, landmark);
        }
        else if (outcome is PlayerStarvedEvent)
            boardOutcome = BoardOutcome.PlayerDied(request, DeathReason.Starvation);
        else if (outcome is PlayerDiedEvent)
            boardOutcome = BoardOutcome.PlayerDied(request, DeathReason.HealthDepleted);
        else
            return false;
        if (!HandleBoardOutcome(boardOutcome))
            return false;
        if (activePlayerView != null)
            activePlayerView.ShowEvent(outcome);
        return true;
    }

    private LandmarkRecord ResolveLandmarkExit(ExitReachedEvent reached)
    {
        var authored = boardScript?.ActiveLandmark;
        if (authored == null || activeBoardRuntime == null || !activeBoardRuntime.Controller.IsTerminal ||
            !reached.PlayerId.Equals(activeBoardRuntime.PlayerId) ||
            !activeBoardRuntime.BoardState.TryGetEntity(reached.ExitId, out var exitEntity) ||
            !activeBoardRuntime.BoardState.TryGetEntity(reached.PlayerId, out var playerEntity) ||
            !exitEntity.Definition.Traits.IsExit || !exitEntity.Position.Equals(playerEntity.Position)) return null;
        foreach (var goal in authored.Outcomes)
            if (goal.ExitEntityId.Equals(reached.ExitId)) return new LandmarkRecord(activeBoardRequest.Address,
                authored.TemplateId, authored.TemplateVersion, authored.VariantId, goal.Id);
        return null;
    }

    /// <summary>
    /// Applies one result emitted by the currently active local board.
    /// Intermediate exits checkpoint the next segment; final arrival reconciles discovery before the atlas.
    /// Failed boundaries stay blocked and may retry without another turn or a second arrival save.
    /// </summary>
    /// <param name="outcome">The board outcome to handle.</param>
    /// <returns><c>true</c> when the matching outcome was accepted; otherwise <c>false</c>.</returns>
    public bool HandleBoardOutcome(BoardOutcome outcome)
    {
        if (outcome == null
            || activeBoardRequest == null
            || boardOutcomeHandled
            || handlingBoardOutcome
            || (arrivalCheckpointCommitted
                ? !ReferenceEquals(outcome, pendingBoardOutcome)
                : !BoardOutcomeIdentityGuard.ValidateOutcome(outcome, activeBoardRequest, session?.ActiveRun, out _)))
        {
            return false;
        }

        if (outcome.Type == BoardOutcomeType.ExitReached && session.ActiveRun.ActiveLandmark != null &&
            (outcome.Landmark == null || activeBoardRuntime == null || !activeBoardRuntime.Controller.IsTerminal ||
            !activeBoardRuntime.BoardState.TryGetEntity(activeBoardRuntime.PlayerId, out var currentPlayer) ||
            !activeBoardRuntime.BoardState.TryGetEntity(HallowBlaze.Core.Board.State.BoardLayer.Terrain,
                currentPlayer.Position, out var currentExit) ||
            ResolveLandmarkExit(new ExitReachedEvent(activeBoardRuntime.PlayerId, currentExit.Id))?.OutcomeId != outcome.Landmark.OutcomeId)) return false;

        if (outcome.Type == BoardOutcomeType.ExitReached || outcome.Type == BoardOutcomeType.PlayerDied)
            DisposeIntentUi();
        switch (outcome.Type)
        {
            case BoardOutcomeType.ExitReached:
                if (!TryEnsureWorldMap())
                    return false;
                pendingBoardOutcome = outcome;
                SetGameplayInputBlocked(true);
                if (outcome.Request.Address != null && !arrivalCheckpointCommitted)
                {
                    if (!worldMap.Travel.TryCompleteSegment(outcome.Request, out SaveStoreResult checkpoint, out bool arrived, outcome.Landmark))
                        return false;
                    if (checkpoint.IsFailure)
                    {
                        Debug.LogError("Segment checkpoint failed: " + checkpoint.Type);
                        return false;
                    }
                    if (!arrived)
                    {
                        boardOutcomeHandled = true;
                        pendingBoardOutcome = null;
                        UpdatePresentationBlocks();
                        sceneLoader(SceneManager.GetActiveScene().name);
                        return true;
                    }
                    arrivalCheckpointCommitted = true;
                }
                if (!arrivalDiscoveryCommitted)
                {
                    WorldMapCommandResult discovery = worldMap.ReconcileCompletedTravel();
                    if (!discovery.IsSuccess)
                    {
                        Debug.LogError("Arrival discovery checkpoint failed: " + discovery.ErrorMessage);
                        return false;
                    }
                    arrivalDiscoveryCommitted = true;
                }
                if (!BeginRouteChoice())
                    return false;
                break;

            case BoardOutcomeType.PlayerDied:
                if (!outcome.DeathReason.HasValue)
                    return false;
                handlingBoardOutcome = true;
                try { GameOver(outcome.DeathReason.Value == DeathReason.Starvation); }
                finally { handlingBoardOutcome = false; }
                break;

            default:
                return false;
        }

        pendingBoardOutcome = null;
        boardOutcomeHandled = true;
        UpdatePresentationBlocks();
        return true;
    }

    private void HideLevelImage()
    {
        if (boardStartupInProgress || activeBoardRequest == null)
            return;
        if (session?.ActiveRun?.ActiveLeg?.IntroState == DayIntroState.Pending)
            return;
        if (levelImage != null)
            levelImage.SetActive(false);

        doingSetup = false;
        activePlayerView?.SetHudVisible(true);
        UpdatePresentationBlocks();
    }

    /// <summary>Closes the run and shows completed days separately from death during the active day.</summary>
    /// <param name="isStarved">Whether the accepted terminal outcome was starvation.</param>
    public void GameOver(bool isStarved)
    {
        if (session == null || session.ActiveRun == null)
            return;

        CancelInvoke(nameof(HideLevelImage));
        RunState run = session.ActiveRun;
        if (run.Status == RunStatus.Active)
        {
            EnsurePersistence();
            SaveStoreResult result = lifecycle.MarkDead();
            if (result.IsFailure)
                Debug.LogError("Run completion save failed: " + result.Type);
        }

        if (levelText != null)
        {
            if (isStarved)
                levelText.text = "You've starved on Day " + Math.Max(1, run.CurrentDay) + ".\nCompleted days: " + run.CompletedDayCount;
            else
                levelText.text = "Your brain has been eaten on Day " + Math.Max(1, run.CurrentDay) + ".\nCompleted days: " + run.CompletedDayCount;
        }

        int score = ManageScore(run.CompletedDayCount);
        activePlayerView?.SetHudVisible(false);

        if (levelImage != null)
            levelImage.SetActive(true);

        GameObject scoreTextObject = GameObject.Find("ScoreText");
        if (scoreTextObject != null)
            scoreText = scoreTextObject.GetComponent<Text>();
        if (scoreText != null)
            scoreText.text = "Your current local record is " + score + " days.";

        fieldNotesAfterDeath = true;
        enabled = false;
        if (restartButton != null)
            restartButton.SetActive(true);
        if (menuButton != null)
            menuButton.SetActive(true);
    }

    private int ManageScore(int score)
    {
        if (score > PlayerPrefs.GetInt("HighScore", 0))
            PlayerPrefs.SetInt("HighScore", score);

        return PlayerPrefs.GetInt("HighScore", 0);
    }

    private void OnRunStarted(RunState runState)
    {
        fieldNotesAfterDeath = false;
        fieldNotes?.Close();
        DisposeActiveBoardRuntime();
        worldMap = null;
        boardOutcomeHandled = false;
        pendingBoardOutcome = null;
        arrivalCheckpointCommitted = false;
        arrivalDiscoveryCommitted = false;
        pendingChosenEdgeId = null;
        if (!boardStartupInProgress)
        {
            doingSetup = false;
            gameplayInputBlocked = false;
            gameplayInputResumeFrame = Time.frameCount;
        }
        SetRouteChoices(NoRouteChoices);
    }

    private void OnRunAbandoned(RunState runState)
    {
        if (!handlingBoardOutcome)
            DisposeActiveBoardRuntime();
        if (!boardStartupInProgress)
            doingSetup = false;
        gameplayInputBlocked = true;
        SetRouteChoices(NoRouteChoices);
    }

    public void StartNewRun()
    {
        TryStartNewRun();
    }

    private bool TryStartNewRun()
    {
        if (!enabled)
            enabled = true;

        nextRunSequence = checked(nextRunSequence + 1);
        RunStateConfiguration configuration = new RunStateConfiguration(
            InitialHealth,
            InitialFood,
            0,
            OpeningWarmUpOriginNodeId);
        string runId = session.Profile.ProfileId + "-run-" +
            System.Guid.NewGuid().ToString("N");

        if (lifecycle == null)
        {
            session.StartNewRun(runId, 12345 + nextRunSequence, configuration);
            return true;
        }

        SaveStoreResult result = lifecycle.StartNewRun(
            runId,
            12345 + nextRunSequence,
            configuration);
        if (result.IsFailure)
        {
            Debug.LogError("New run save failed: " + result.Type);
            return false;
        }

        return true;
    }

    public void AbandonRun()
    {
        EnsurePersistence();
        SaveStoreResult result = lifecycle.DeleteRunAndAbandon();
        if (result.IsFailure)
            Debug.LogError("Run abandon failed: " + result.Type);
    }

    public bool ExitToMenu()
    {
        EnsurePersistence();
        SaveStoreResult result = lifecycle.ExitToMenu();
        if (result.IsFailure)
            Debug.LogError("Exit save failed: " + result.Type);

        return result.IsSuccess;
    }

    public void WinGame()
    {
        EnsurePersistence();
        SaveStoreResult result = lifecycle.MarkWon();
        if (result.IsFailure)
            Debug.LogError("Run completion save failed: " + result.Type);
    }

    public void RestartGame()
    {
        if (!TryBeginTerminalAction())
            return;

        RestoreRuntimeState();
        RequestNewRun();
        StartNewRun();
        sceneLoader(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMenuAfterGameOver()
    {
        if (session == null || session.ActiveRun != null ||
            !TryBeginTerminalAction())
            return;

        RestoreRuntimeState();
        if (!enabled)
            enabled = true;

        sceneLoader(MenuSceneName);
    }

    private bool TryBeginTerminalAction()
    {
        if (terminalActionRequested)
            return false;

        terminalActionRequested = true;
        SetButtonInteractable(restartButton, false);
        SetButtonInteractable(menuButton, false);
        return true;
    }

    private void RestoreRuntimeState()
    {
        Time.timeScale = 1f;
        SetGameplayInputBlocked(false);
    }

    private void DisposeActiveBoardRuntime()
    {
        if (activeBoardPresenter != null)
            activeBoardPresenter.Dispose();
        activeBoardPresenter = null;
        DisposeIntentUi();
        activePlayerView = null;
        if (activeBoardRuntime != null)
            activeBoardRuntime.Dispose();

        activeBoardRuntime = null;
        activeBoardRequest = null;
        if (boardScript != null)
            boardScript.ClearActiveRequest();
    }

    private static void SetButtonInteractable(GameObject target, bool interactable)
    {
        if (target == null)
            return;

        Button button = target.GetComponent<Button>();
        if (button != null)
            button.interactable = interactable;
    }

    private void EnsurePersistence()
    {
        if (lifecycle != null)
            return;

        saveStore = new FileSystemSaveStore(GetPersistenceRoot());
        SaveStoreResult<ProfileState> profileResult = saveStore.LoadProfile();
        ProfileState profile = profileResult.IsSuccess
            ? profileResult.Data
            : session.Profile;

        if (profileResult.Type != SaveStoreResultType.Missing && profileResult.IsFailure)
            Debug.LogError("Profile load failed: " + profileResult.Type);

        if (!ReferenceEquals(profile, session.Profile))
        {
            session.OnRunStarted -= OnRunStarted;
            session.OnRunAbandoned -= OnRunAbandoned;
            session = new GameSession(profile);
            session.OnRunStarted += OnRunStarted;
            session.OnRunAbandoned += OnRunAbandoned;
        }

        lifecycle = new RunLifecycleService(session, saveStore);
        EnsureFieldNotes();
        worldDefinition = null;
        worldMap = null;
    }

    private bool TryStartNewGame()
    {
        ProfileState currentProfile = session.Profile;
        ProfileState freshProfile = new ProfileState(
            currentProfile.ProfileId,
            currentProfile.WorldDefinitionId,
            currentProfile.WorldDefinitionVersion);
        nextRunSequence = checked(nextRunSequence + 1);
        RunStateConfiguration configuration = new RunStateConfiguration(
            InitialHealth,
            InitialFood,
            0,
            OpeningWarmUpOriginNodeId);
        RunState freshRun = new RunState(
            freshProfile.ProfileId + "-run-" + System.Guid.NewGuid().ToString("N"),
            12345 + nextRunSequence,
            configuration);

        SaveStoreResult resetResult = saveStore.ResetGame(freshProfile, freshRun);
        if (resetResult.IsFailure)
        {
            Debug.LogError("New game reset failed: " + resetResult.Type);
            return false;
        }

        session.OnRunStarted -= OnRunStarted;
        session.OnRunAbandoned -= OnRunAbandoned;
        session = new GameSession(freshProfile);
        session.OnRunStarted += OnRunStarted;
        session.OnRunAbandoned += OnRunAbandoned;
        lifecycle = new RunLifecycleService(session, saveStore);
        EnsureFieldNotes();
        worldDefinition = null;
        worldMap = null;
        session.ContinueRun(freshRun);
        return true;
    }

    private bool TryEnsureWorldMap()
    {
        EnsurePersistence();
        if (worldMap != null)
            return true;

        if (worldDefinitionJson == null)
        {
            Debug.LogError("World definition JSON is not assigned on GameManager.");
            return false;
        }

        try
        {
            worldDefinition = WorldDefinitionJson.Deserialize(worldDefinitionJson.text);
            worldMap = new WorldMapService(
                worldDefinition,
                session,
                saveStore);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("World definition could not be initialized: " + exception.Message);
            return false;
        }
    }

    private bool TryEnterCurrentWorldNode()
    {
        if (!TryEnsureWorldMap())
            return false;

        WorldMapCommandResult pendingDiscovery = worldMap.RetryPendingDiscovery();
        if (!pendingDiscovery.IsSuccess)
        {
            Debug.LogError("Discovery recovery failed: " + pendingDiscovery.ErrorMessage);
            return false;
        }

        WorldMapCommandResult arrival = worldMap.ReconcileCompletedTravel();
        if (!arrival.IsSuccess)
        {
            Debug.LogError("Arrival recovery failed: " + arrival.ErrorMessage);
            return false;
        }

        WorldMapCommandResult result = worldMap.EnterCurrentNode();
        if (result.IsSuccess)
            return true;

        Debug.LogError(
            "Current world node entry failed: " + result.Status +
            (string.IsNullOrEmpty(result.ErrorMessage)
                ? string.Empty
                : " (" + result.ErrorMessage + ")"));
        return false;
    }

    private bool TryStartOpeningWarmUp()
    {
        if (!TryEnsureWorldMap())
            return false;

        RunState run = session.ActiveRun;
        if (run == null || run.Status != RunStatus.Active ||
            run.ActiveLeg != null ||
            !string.Equals(run.WorldNodeId, OpeningWarmUpOriginNodeId, StringComparison.Ordinal))
        {
            Debug.LogError("Opening warm-up cannot start from the current run checkpoint.");
            return false;
        }

        if (!worldDefinition.TryGetEdge(OpeningWarmUpEdgeId, out WorldEdgeDefinition openingEdge) ||
            !string.Equals(openingEdge.FromNodeId, OpeningWarmUpOriginNodeId, StringComparison.Ordinal))
        {
            Debug.LogError("Opening warm-up route is missing or has an invalid origin.");
            return false;
        }

        SaveStoreResult result = worldMap.Travel.StartRoute(OpeningWarmUpEdgeId);
        if (result.IsFailure)
        {
            Debug.LogError("Opening warm-up checkpoint failed: " + result.Type);
            return false;
        }

        return true;
    }

    private void SetRouteChoices(IReadOnlyList<WorldMapExitOption> choices)
    {
        routeChoices = choices ?? NoRouteChoices;
        UpdatePresentationBlocks();
        OnRouteChoicesChanged?.Invoke(routeChoices);
    }

    internal static string GetPersistenceRoot()
    {
        return string.IsNullOrEmpty(PersistenceRootOverride)
            ? PersistencePathProvider.GetSaveRoot()
            : PersistenceRootOverride;
    }
}
