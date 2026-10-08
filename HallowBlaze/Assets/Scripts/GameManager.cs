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

/// <summary>Composes the active board's single command/presentation path and guarded session lifecycle.</summary>
public class GameManager : MonoBehaviour, IBoardOutcomeSink
{
    private const int InitialHealth = 100;
    private const int InitialFood = 100;
    private const string InitialWorldNodeId = "forest.start";
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
    private PlayerScript activePlayerView;
    private bool handlingBoardOutcome;
    private bool boardOutcomeHandled;
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

    /// <summary>Gets the legal options exposed by the current board exit.</summary>
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
                && worldMap.ChoiceState == RouteChoiceState.AwaitingChoice;
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
                || IsRouteChoiceActive || boardOutcomeHandled || terminalActionRequested; }
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

    /// <summary>Gets whether a nonterminal board may open pause without interrupting a terminal outcome.</summary>
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
    /// Observes the active node's exits and blocks gameplay until one legal edge is chosen.
    /// </summary>
    /// <returns><c>true</c> when at least one legal route is ready for presentation.</returns>
    public bool BeginRouteChoice()
    {
        if (IsRouteChoiceActive ||
            session == null ||
            session.ActiveRun == null ||
            session.ActiveRun.Status != RunStatus.Active ||
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

        if (choicesResult.Exits.Count == 0)
        {
            Debug.LogWarning("The current world node has no outgoing route choices.");
            return false;
        }

        SetRouteChoices(choicesResult.Exits);
        return true;
    }

    /// <summary>
    /// Submits one displayed edge and loads the next board only after its run checkpoint is saved.
    /// </summary>
    /// <param name="edgeId">A stable edge ID from <see cref="RouteChoices"/>.</param>
    /// <returns><c>true</c> only when this call commits a new route and requests the next board.</returns>
    public bool ChooseRoute(string edgeId)
    {
        if (!IsRouteChoiceActive || !TryEnsureWorldMap())
            return false;

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

        SetRouteChoices(NoRouteChoices);
        sceneLoader(SceneManager.GetActiveScene().name);
        return true;
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
        UpdatePresentationBlocks();
    }

    private void OnDestroy()
    {
        DisposeActiveBoardRuntime();
        if (instance == this)
            instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.buildIndex != 1)
        {
            DisposeActiveBoardRuntime();
            return;
        }

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
        if (!TryEnterCurrentWorldNode())
            return;

        InitGame();
    }

    private void BlockBoardStartup()
    {
        boardStartupInProgress = true;
        doingSetup = true;
        DisposeActiveBoardRuntime();
        boardOutcomeHandled = false;
        SetRouteChoices(NoRouteChoices);
        SetGameplayInputBlocked(true);
    }

    private void InitGame()
    {
        doingSetup = true;
        terminalActionRequested = false;
        SetRouteChoices(NoRouteChoices);
        SetGameplayInputBlocked(true);

        levelImage = GameObject.Find("LevelImage");
        if (levelImage != null)
        {
            levelImage.transform.SetAsFirstSibling();
            levelImage.SetActive(true);
        }

        GameObject levelTextObject = GameObject.Find("LevelText");
        if (levelTextObject != null)
            levelText = levelTextObject.GetComponent<Text>();
        if (levelText != null)
            levelText.text = "Day: " + (session.GetCurrentRun().CurrentDay + 1);

        restartButton = GameObject.Find("RestartBttn");
        if (restartButton != null)
            restartButton.SetActive(false);

        menuButton = GameObject.Find("MenuBttn");
        if (menuButton != null)
            menuButton.SetActive(false);

        GameObject scoreTextObject = GameObject.Find("ScoreText");
        if (scoreTextObject != null)
            scoreText = scoreTextObject.GetComponent<Text>();
        if (scoreText != null)
            scoreText.text = string.Empty;

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
            BoardRequest request = BoardRequest.CreateFromRun(run, worldDefinition);
            GameObject playerView = GameObject.FindGameObjectWithTag("Player");
            BoardRuntime runtime = boardScript.SetupScene(request, run, playerView);

            PublishBoardRuntime(runtime);
            boardStartupInProgress = false;
            SetGameplayInputBlocked(false);
            CancelInvoke(nameof(HideLevelImage));
            Invoke(nameof(HideLevelImage), levelStartDelay);
        }
        catch (Exception exception)
        {
            DisposeActiveBoardRuntime();
            CancelInvoke(nameof(HideLevelImage));
            SetGameplayInputBlocked(true);
            Debug.LogError("Board startup failed: " + exception.Message);
        }
    }

    private void PublishBoardRuntime(BoardRuntime runtime)
    {
        activeBoardRuntime = runtime;
        activeBoardRequest = runtime.Request;
        activePlayerView = runtime.Views[runtime.PlayerId].GetComponent<PlayerScript>();
        if (activePlayerView == null)
            throw new InvalidOperationException("The registered player has no production input/view adapter.");
        activeBoardPresenter = new BoardEventPresenter(runtime, activePlayerView, activePlayerView,
            this, ReportPresentationDiagnostic, activePlayerView.moveTime);
        activeBoardPresenter.AnimationsEnabled = activePlayerView.animationsEnabled;
        activeBoardPresenter.SynchronizeFromState();
        UpdatePresentationBlocks();
    }

    private void ReportPresentationDiagnostic(PresentationDiagnostic diagnostic)
    {
        if (diagnostic.Code != PresentationDiagnosticCode.Canceled)
            Debug.LogError("Board presentation failed: " + diagnostic.Code + " at event " +
                diagnostic.EventIndex + " (" + diagnostic.EventType + "): " + diagnostic.Error);
    }

    private void UpdatePresentationBlocks()
    {
        if (activeBoardPresenter == null)
            return;
        PresentationGate gate = activeBoardPresenter.Coordinator.Gate;
        gate.SetBlocked(PresentationInputBlock.Setup, doingSetup || boardStartupInProgress);
        gate.SetBlocked(PresentationInputBlock.Modal, gameplayInputBlocked || IsRouteChoiceActive);
        gate.SetBlocked(PresentationInputBlock.Disabled, !enabled || boardOutcomeHandled ||
            terminalActionRequested || Time.frameCount <= gameplayInputResumeFrame);
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
        if (outcome is ExitReachedEvent)
            boardOutcome = BoardOutcome.ExitReached(request);
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

    /// <summary>
    /// Applies one result emitted by the currently active local board.
    /// </summary>
    /// <param name="outcome">The board outcome to handle.</param>
    /// <returns><c>true</c> when the matching outcome was accepted; otherwise <c>false</c>.</returns>
    public bool HandleBoardOutcome(BoardOutcome outcome)
    {
        if (outcome == null
            || activeBoardRequest == null
            || boardOutcomeHandled
            || !activeBoardRequest.HasSameIdentity(outcome.Request))
        {
            return false;
        }

        switch (outcome.Type)
        {
            case BoardOutcomeType.ExitReached:
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

        boardOutcomeHandled = true;
        UpdatePresentationBlocks();
        return true;
    }

    private void HideLevelImage()
    {
        if (levelImage != null)
            levelImage.SetActive(false);

        if (boardStartupInProgress || activeBoardRequest == null)
            return;

        doingSetup = false;
        UpdatePresentationBlocks();
    }

    public void GameOver(bool isStarved)
    {
        if (session == null || session.ActiveRun == null)
            return;

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
                levelText.text = "After " + run.CurrentDay + " days, you've starved.";
            else
                levelText.text = "After " + run.CurrentDay + " days, your brain has been eaten.";
        }

        int score = ManageScore(run.CurrentDay);

        if (levelImage != null)
            levelImage.SetActive(true);

        GameObject scoreTextObject = GameObject.Find("ScoreText");
        if (scoreTextObject != null)
            scoreText = scoreTextObject.GetComponent<Text>();
        if (scoreText != null)
            scoreText.text = "Your current local record is " + score + " days.";

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
        DisposeActiveBoardRuntime();
        worldMap = null;
        boardOutcomeHandled = false;
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
            InitialWorldNodeId);
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
            InitialWorldNodeId);
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
