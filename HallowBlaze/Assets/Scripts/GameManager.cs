using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;       //Allows us to use Lists. 

public class GameManager : MonoBehaviour
{
    public float levelStartDelay = 2f;
    public float turnDelay = .1f;
    public static GameManager instance = null;              //Static instance of GameManager which allows it to be accessed by any other script.
    public BoardManager boardScript;                       //Store a reference to our BoardManager which will set up the level.
    public int playerFoodPoints = 100;
    public int playerHealthPoints = 100;
    [HideInInspector] public bool playerTurn = true;

    private Text levelText;
    private Text scoreText;
    private GameObject levelImage;
    private GameObject restartButton;
    private List<Enemy> enemies;
    private int level = 0;                                  //Current level number, expressed in game as "Day 1".
    private bool enemiesMoving;
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
    }

    private void OnLevelWasLoaded(int index)
    {
        if (index == 1)
        {
            level++;
            InitGame();
        } 
    }

    //Initializes the game for each level.
    void InitGame()
    {
        doingSetup = true;

        levelImage = GameObject.Find("LevelImage");
        levelImage.SetActive(true);

        levelText = GameObject.Find("LevelText").GetComponent<Text>();
        levelText.text = "Day: " + level;

        restartButton = GameObject.Find("RestartBttn");
        restartButton.SetActive(false);

        scoreText = GameObject.Find("ScoreText").GetComponent<Text>();
        scoreText.text = string.Empty;

        Invoke(nameof(HideLevelImage), levelStartDelay);
        enemies.Clear();
        
        //Call the SetupScene function of the BoardManager script, pass it current level number.
        boardScript.SetupScene(level);

    }

    private void HideLevelImage()
    {
        levelImage.SetActive(false);
        doingSetup = false;
    }

    public void GameOver(bool isStarved)
    {
        if (isStarved)
            levelText.text = "After " + level + " days, you've starved.";
        else
            levelText.text = "After " + level + " days, your brain has been eaten.";

        int score = ManageScore(level);

        levelImage.SetActive(true);
        scoreText = GameObject.Find("ScoreText").GetComponent<Text>();
        scoreText.text = "Your current local record is " + score + " days.";
        enabled = false;
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
        if (playerTurn || enemiesMoving || doingSetup)
            return;

        StartCoroutine(MoveEnemies());

    }

    public void AddEnemytoList(Enemy script)
    {
        enemies.Add(script);
    }

    IEnumerator MoveEnemies()
    {
        enemiesMoving = true;
        yield return new WaitForSeconds(turnDelay);
        if (enemies.Count == 0)
            yield return new WaitForSeconds(turnDelay);

        for (int i = 0; i < enemies.Count; i++)
        {
            enemies[i].MoveEnemy();
            yield return new WaitForSeconds(enemies[i].moveTime);
        }

        playerTurn = true;
        enemiesMoving = false;
    }
}