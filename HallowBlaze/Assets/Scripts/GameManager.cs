using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;       //Allows us to use Lists. 

public class GameManager : MonoBehaviour
{
    public float levelStartDelay = 2f;
    public float turnDelay = .1f;
    public static GameManager instance = null;              //Static instance of GameManager which allows it to be accessed by any other script.
    public BoardManager boardScript;                       //Store a reference to our BoardManager which will set up the level.
    public RunState runState;                              //Centralized state management for the current game run

    private Text levelText;
    private Text scoreText;
    private GameObject levelImage;
    private GameObject restartButton;
    private List<Enemy> enemies;
    private bool doingSetup;

    //Awake is always called before any Start functions
    void Awake()
    {
        //Check if instance already exists
        if (instance == null)
            //if not, set instance to this
            instance = this;

        //If instance already exists and it's not this:
        else if (instance != this)
            //Then destroy this. This enforces our singleton pattern, meaning there can only ever be one instance of a GameManager.
            Destroy(gameObject);

        //Sets this to not be destroyed when reloading scene
        DontDestroyOnLoad(gameObject);

        //
        enemies = new List<Enemy>();

        //Get a component reference to the attached BoardManager script
        boardScript = GetComponent<BoardManager>();
        
        // Get or create RunState component
        runState = FindObjectOfType<RunState>();
        if (runState == null)
        {
            // Create a new RunState GameObject if one doesn't exist
            GameObject runStateGO = new GameObject("RunState");
            runState = runStateGO.AddComponent<RunState>();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.buildIndex == 1)
        {
            runState.IncrementLevel();
            InitGame();
        }
    }

    //Initializes the game for each level.
    void InitGame()
    {
        doingSetup = true;

        levelImage = GameObject.Find("LevelImage");
        if (levelImage != null)
            levelImage.SetActive(true);

        GameObject levelTextObject = GameObject.Find("LevelText");
        if (levelTextObject != null)
            levelText = levelTextObject.GetComponent<Text>();
        if (levelText != null)
            levelText.text = "Day: " + runState.level;

        restartButton = GameObject.Find("RestartBttn");
        if (restartButton != null)
            restartButton.SetActive(false);

        GameObject scoreTextObject = GameObject.Find("ScoreText");
        if (scoreTextObject != null)
            scoreText = scoreTextObject.GetComponent<Text>();
        if (scoreText != null)
            scoreText.text = string.Empty;

        Invoke(nameof(HideLevelImage), levelStartDelay);
        enemies.Clear();
        
        //Call the SetupScene function of the BoardManager script, pass it current level number.
        if (boardScript != null)
            boardScript.SetupScene(runState.level);

    }

    private void HideLevelImage()
    {
        levelImage.SetActive(false);
        doingSetup = false;
    }

    public void GameOver(bool isStarved)
    {
        if (levelText != null)
        {
            if (isStarved)
                levelText.text = "After " + level + " days, you've starved.";
            else
                levelText.text = "After " + level + " days, your brain has been eaten.";
        }

        int score = ManageScore(level);

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

        score = PlayerPrefs.GetInt("HighScore", 0);

        return score;
    }

    //Update is called every frame.
    void Update()
    {
        if (runState == null || runState.playerTurn || runState.enemiesMoving || runState.doingSetup)
            return;

        StartCoroutine(MoveEnemies());

    }

    public void AddEnemytoList(Enemy script)
    {
        enemies.Add(script);
    }

    IEnumerator MoveEnemies()
    {
        runState.enemiesMoving = true;
        yield return new WaitForSeconds(turnDelay);
        if (enemies.Count == 0)
            yield return new WaitForSeconds(turnDelay);

        for (int i = 0; i < enemies.Count; i++)
        {
            enemies[i].MoveEnemy();
            yield return new WaitForSeconds(enemies[i].moveTime);
        }

        runState.playerTurn = true;
        runState.enemiesMoving = false;
    }
}