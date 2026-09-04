using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlayerScript : MovingObject
{
    public float restartLevelDelay = 1f;
    public int wallDamage = 1;
    public int pointsPerFood = 10;
    public int pointsPerSoda = 20;
    public int pointsPerAid = 10;
    public AudioClip moveSound1;
    public AudioClip moveSound2;
    public AudioClip eatSound1;
    public AudioClip eatSound2;
    public AudioClip drinkSound1;
    public AudioClip drinkSound2;
    public AudioClip gameOverSound;
    public Text foodText;
    public Text healthText;

    private bool onCarrot;
    private Animator animator;
    private GameObject tmpCarrot;
    private Vector2 touchOrigin = -Vector2.one;
    private GameSession subscribedSession;

    private void OnEnable()
    {
        BindSession();
        RefreshResourceText();
    }

    protected override void Start()
    {
        animator = GetComponent<Animator>();
        BindSession();
        RefreshResourceText();
        base.Start();
    }

    private void OnDisable()
    {
        UnbindSession();
    }

    private void Update()
    {
        if (GameManager.instance == null || !GameManager.instance.IsGameplayInputEnabled)
            return;

        int horizontal = 0;
        int vertical = 0;

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBPLAYER

        if (Input.GetKeyDown("space"))
        {
            AttemptGathering();
            return;
        }

        horizontal = (int)Input.GetAxisRaw("Horizontal");
        vertical = (int)Input.GetAxisRaw("Vertical");

        if (horizontal != 0)
            vertical = 0;

#else

        if (Input.touchCount > 0)
        {
            Touch myTouch = Input.touches[0];

            if (myTouch.phase == TouchPhase.Began)
            {
                touchOrigin = myTouch.position;
            }
            else if (myTouch.phase == TouchPhase.Ended && touchOrigin.x >= 0)
            {
                Vector2 touchEnd = myTouch.position;
                float x = touchEnd.x - touchOrigin.x;
                float y = touchEnd.y - touchOrigin.y;
                touchOrigin.x = -1;
                if (Mathf.Abs(x) > Mathf.Abs(y))
                    horizontal = x > 0 ? 1 : -1;
                else
                    vertical = y > 0 ? 1 : -1;
            }
        }

#endif

        if (horizontal != 0 || vertical != 0)
            AttemptMove<Wall>(horizontal, vertical);
    }

    protected override void AttemptMove<T>(int xDir, int yDir)
    {
        if (!TryGetActiveSession(out GameSession activeSession))
            return;

        RaycastHit2D hit;
        bool canMove = Move(xDir, yDir, out hit);
        T hitComponent = hit.transform == null ? null : hit.transform.GetComponent<T>();

        if (!canMove && hitComponent == null)
            return;

        activeSession.ConsumeFood();

        if (!canMove)
            OnCantMove(hitComponent);
        else if (SoundManager.instance != null)
            SoundManager.instance.RandomizeSfx(moveSound1, moveSound2);

        CheckIfGameOver();

        if (GameManager.instance != null)
            GameManager.instance.EndPlayerTurn();
    }

    protected void AttemptGathering()
    {
        if (!TryGetActiveSession(out GameSession activeSession))
            return;
        if (!onCarrot || tmpCarrot == null || !tmpCarrot.activeInHierarchy)
            return;

        GameObject gatheredCarrot = tmpCarrot;
        onCarrot = false;
        tmpCarrot = null;

        activeSession.RestoreFood(pointsPerFood);
        if (SoundManager.instance != null)
            SoundManager.instance.RandomizeSfx(eatSound1, eatSound2);
        gatheredCarrot.SetActive(false);

        activeSession.ConsumeFood();
        CheckIfGameOver();

        if (activeSession.ActiveRun.Status == RunStatus.Active)
        {
            ShowFoodGain(pointsPerFood);

            if (GameManager.instance != null)
                GameManager.instance.EndPlayerTurn();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (GameManager.instance != null && GameManager.instance.IsGameplayInputBlocked)
            return;

        if (other.tag == "Exit")
        {
            Invoke(nameof(Restart), restartLevelDelay);
            enabled = false;
        }
        else if (other.tag == "Food")
        {
            if (!TryGetActiveSession(out GameSession activeSession))
                return;

            activeSession.RestoreFood(pointsPerFood);
            ShowFoodGain(pointsPerFood);
            if (SoundManager.instance != null)
                SoundManager.instance.RandomizeSfx(eatSound1, eatSound2);
            other.gameObject.SetActive(false);
        }
        else if (other.tag == "Soda")
        {
            if (!TryGetActiveSession(out GameSession activeSession))
                return;

            activeSession.RestoreFood(pointsPerSoda);
            ShowFoodGain(pointsPerSoda);
            if (SoundManager.instance != null)
                SoundManager.instance.RandomizeSfx(drinkSound1, drinkSound2);
            other.gameObject.SetActive(false);
        }
        else if (other.tag == "Aid")
        {
            if (!TryGetActiveSession(out GameSession activeSession))
                return;

            activeSession.RestoreHealth(pointsPerAid);
            ShowHealthGain(pointsPerAid);
            if (SoundManager.instance != null)
                SoundManager.instance.RandomizeSfx(drinkSound1, drinkSound2);
            other.gameObject.SetActive(false);
        }

        if (other.tag == "Carrot")
        {
            onCarrot = true;
            tmpCarrot = other.gameObject;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (GameManager.instance != null && GameManager.instance.IsGameplayInputBlocked)
            return;

        if (other.tag == "Carrot")
        {
            onCarrot = false;
            if (tmpCarrot == other.gameObject)
                tmpCarrot = null;
        }
    }

    protected override void OnCantMove<T>(T component)
    {
        Wall hitWall = component as Wall;
        if (hitWall != null)
            hitWall.DamageWall(wallDamage);
        if (animator != null)
            animator.SetTrigger("playerChop");
    }

    private void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void LoseHealth(int loss)
    {
        if (GameManager.instance != null && GameManager.instance.IsGameplayInputBlocked)
            return;
        if (!TryGetActiveSession(out GameSession activeSession))
            return;

        if (animator != null)
            animator.SetTrigger("playerHit");

        activeSession.TakeDamage(loss);
        ShowHealthLoss(loss);
        CheckIfGameOver();
    }

    private void CheckIfGameOver()
    {
        if (GameManager.instance == null || GameManager.instance.IsGameplayInputBlocked)
            return;
        if (!TryGetActiveSession(out GameSession activeSession))
            return;

        RunState run = activeSession.ActiveRun;
        if (run.Food > 0 && run.Health > 0)
            return;

        if (SoundManager.instance != null)
            SoundManager.instance.RandomizeSfx(gameOverSound);

        GameManager.instance.GameOver(run.Food <= 0);
    }

    private void BindSession()
    {
        GameSession nextSession = GameManager.instance == null
            ? null
            : GameManager.instance.Session;
        if (ReferenceEquals(nextSession, subscribedSession))
            return;

        UnbindSession();
        subscribedSession = nextSession;
        if (subscribedSession == null)
            return;

        subscribedSession.OnRunStarted += OnRunStateChanged;
        subscribedSession.OnRunChanged += OnRunStateChanged;
        subscribedSession.OnRunAbandoned += OnRunStateChanged;
    }

    private void UnbindSession()
    {
        if (subscribedSession == null)
            return;

        subscribedSession.OnRunStarted -= OnRunStateChanged;
        subscribedSession.OnRunChanged -= OnRunStateChanged;
        subscribedSession.OnRunAbandoned -= OnRunStateChanged;
        subscribedSession = null;
    }

    private bool TryGetActiveSession(out GameSession activeSession)
    {
        BindSession();
        activeSession = subscribedSession;
        return activeSession != null
            && activeSession.ActiveRun != null
            && activeSession.ActiveRun.Status == RunStatus.Active;
    }

    private void OnRunStateChanged(RunState runState)
    {
        RefreshResourceText();
    }

    private void RefreshResourceText()
    {
        RunState run = subscribedSession == null ? null : subscribedSession.ActiveRun;
        if (foodText != null)
            foodText.text = run == null ? "Food: -" : "Food: " + run.Food;
        if (healthText != null)
            healthText.text = run == null ? "Health: -" : "Health: " + run.Health;
    }

    private void ShowFoodGain(int amount)
    {
        if (foodText != null && subscribedSession != null && subscribedSession.ActiveRun != null)
            foodText.text = "Food: " + subscribedSession.ActiveRun.Food + "+" + amount;
    }

    private void ShowHealthGain(int amount)
    {
        if (healthText != null && subscribedSession != null && subscribedSession.ActiveRun != null)
            healthText.text = "Health: " + subscribedSession.ActiveRun.Health + "+" + amount;
    }

    private void ShowHealthLoss(int amount)
    {
        if (healthText != null && subscribedSession != null && subscribedSession.ActiveRun != null)
            healthText.text = "Health: " + subscribedSession.ActiveRun.Health + "-" + amount;
    }
}