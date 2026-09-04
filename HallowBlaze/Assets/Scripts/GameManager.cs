using System.Collections;
using System.Collections.Generic;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    private const int InitialHealth = 100;
    private const int InitialFood = 100;
    private const string InitialWorldNodeId = "forest.start";

    public float levelStartDelay = 2f;
    public float turnDelay = .1f;
    public static GameManager instance = null;
    public BoardManager boardScript;

    private Text levelText;
    private Text scoreText;
    private GameObject levelImage;
    private GameObject restartButton;
    private List<Enemy> enemies;
    private bool playerTurn = true;
    private bool enemiesMoving;
    private bool doingSetup;
    private bool gameplayInputBlocked;
    private int gameplayInputResumeFrame = -1;
    private int nextRunSequence;
    private GameSession session;

    public GameSession Session
    {
        get { return session; }
    }

    public bool IsGameplayInputEnabled
    {
        get
        {
            return enabled
                && !doingSetup
                && !gameplayInputBlocked
                && Time.frameCount > gameplayInputResumeFrame
                && playerTurn
                && session != null
                && session.ActiveRun != null
                && session.ActiveRun.Status == RunStatus.Active;
        }
    }

    public bool IsGameplayInputBlocked
    {
        get { return gameplayInputBlocked; }
    }

    public bool CanPauseGameplay
    {
        get
        {
            return enabled
                && !doingSetup
                && !gameplayInputBlocked
                && session != null
                && session.ActiveRun != null
                && session.ActiveRun.Status == RunStatus.Active;
        }
    }

    public void SetGameplayInputBlocked(bool blocked)
    {
        gameplayInputBlocked = blocked;

        if (!blocked)
            gameplayInputResumeFrame = Time.frameCount;
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
        enemies = new List<Enemy>();
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
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (session != null)
        {
            session.OnRunStarted -= OnRunStarted;
            session.OnRunAbandoned -= OnRunAbandoned;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.buildIndex != 1)
            return;

        if (!session.IsRunActive() || session.ActiveRun.Status != RunStatus.Active)
            StartNewRun();

        session.AdvanceDay();
        InitGame();
    }

    private void InitGame()
    {
        doingSetup = true;

        levelImage = GameObject.Find("LevelImage");
        if (levelImage != null)
            levelImage.SetActive(true);

        GameObject levelTextObject = GameObject.Find("LevelText");
        if (levelTextObject != null)
            levelText = levelTextObject.GetComponent<Text>();
        if (levelText != null)
            levelText.text = "Day: " + session.GetCurrentRun().CurrentDay;

        restartButton = GameObject.Find("RestartBttn");
        if (restartButton != null)
            restartButton.SetActive(false);

        GameObject scoreTextObject = GameObject.Find("ScoreText");
        if (scoreTextObject != null)
            scoreText = scoreTextObject.GetComponent<Text>();
        if (scoreText != null)
            scoreText.text = string.Empty;

        CancelInvoke(nameof(HideLevelImage));
        Invoke(nameof(HideLevelImage), levelStartDelay);
        enemies.Clear();

        if (boardScript != null)
            boardScript.SetupScene(session.GetCurrentRun().CurrentDay);
    }

    private void HideLevelImage()
    {
        if (levelImage != null)
            levelImage.SetActive(false);

        doingSetup = false;
    }

    public void GameOver(bool isStarved)
    {
        if (session == null || session.ActiveRun == null)
            return;

        RunState run = session.ActiveRun;
        if (run.Status == RunStatus.Active)
            session.MarkDead();

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
    }

    private int ManageScore(int score)
    {
        if (score > PlayerPrefs.GetInt("HighScore", 0))
            PlayerPrefs.SetInt("HighScore", score);

        return PlayerPrefs.GetInt("HighScore", 0);
    }

    private void Update()
    {
        if (gameplayInputBlocked
            || doingSetup
            || session == null
            || session.ActiveRun == null
            || session.ActiveRun.Status != RunStatus.Active
            || playerTurn
            || enemiesMoving)
            return;

        StartCoroutine(MoveEnemies());
    }

    public void AddEnemytoList(Enemy script)
    {
        enemies.Add(script);
    }

    public void EndPlayerTurn()
    {
        if (!IsGameplayInputEnabled)
            return;

        playerTurn = false;
    }

    private IEnumerator MoveEnemies()
    {
        enemiesMoving = true;
        yield return new WaitForSeconds(turnDelay);
        yield return WaitWhileGameplayBlocked();

        if (enemies.Count == 0)
        {
            yield return new WaitForSeconds(turnDelay);
            yield return WaitWhileGameplayBlocked();
        }

        for (int index = 0; index < enemies.Count; index++)
        {
            yield return WaitWhileGameplayBlocked();
            enemies[index].MoveEnemy();
            yield return new WaitForSeconds(enemies[index].moveTime);
        }

        yield return WaitWhileGameplayBlocked();
        playerTurn = true;
        enemiesMoving = false;
    }

    private IEnumerator WaitWhileGameplayBlocked()
    {
        while (gameplayInputBlocked)
            yield return null;
    }

    private void OnRunStarted(RunState runState)
    {
        StopAllCoroutines();
        playerTurn = true;
        enemiesMoving = false;
        doingSetup = false;
        gameplayInputBlocked = false;
        gameplayInputResumeFrame = Time.frameCount;
    }

    private void OnRunAbandoned(RunState runState)
    {
        StopAllCoroutines();
        playerTurn = false;
        enemiesMoving = false;
        doingSetup = false;
        gameplayInputBlocked = true;
    }

    public void StartNewRun()
    {
        if (!enabled)
            enabled = true;

        nextRunSequence = checked(nextRunSequence + 1);
        RunStateConfiguration configuration = new RunStateConfiguration(
            InitialHealth,
            InitialFood,
            0,
            InitialWorldNodeId);
        session.StartNewRun(
            session.Profile.ProfileId + "-run-" + nextRunSequence,
            12345 + nextRunSequence,
            configuration);
    }

    public void AbandonRun()
    {
        if (session != null)
            session.AbandonRun();
    }

    public void RestartGame()
    {
        StartNewRun();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}