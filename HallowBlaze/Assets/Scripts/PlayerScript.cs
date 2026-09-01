using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlayerScript : MovingObject {

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

    private bool onCarrot = false;
    private int food;
    private int health;
    private Animator animator;
    private GameObject tmpCarrot;
    private Vector2 touchOrigin = -Vector2.one;

	// Use this for initialization
	protected override void Start ()
    {
        animator = GetComponent<Animator>();

        if (GameManager.instance != null && GameManager.instance.runState != null)
        {
            food = GameManager.instance.runState.playerFoodPoints;
            health = GameManager.instance.runState.playerHealthPoints;
        }

        if (foodText != null)
            foodText.text = "Food: " + food;
        if (healthText != null)
            healthText.text = "Health: " + health;

        // Subscribe to state changes
        if (GameManager.instance != null && GameManager.instance.runState != null)
        {
            GameManager.instance.runState.OnFoodChanged += OnFoodChanged;
            GameManager.instance.runState.OnHealthChanged += OnHealthChanged;
        }

        base.Start();
	}

    private void OnDisable()
    {
        // Unsubscribe from state changes
        if (GameManager.instance != null && GameManager.instance.runState != null)
        {
            GameManager.instance.runState.OnFoodChanged -= OnFoodChanged;
            GameManager.instance.runState.OnHealthChanged -= OnHealthChanged;
        }
    }

    private void OnFoodChanged(int newFood)
    {
        food = newFood;
        if (foodText != null)
            foodText.text = "Food: " + food;
    }

    private void OnHealthChanged(int newHealth)
    {
        health = newHealth;
        if (healthText != null)
            healthText.text = "Health: " + health;
    }

    // Update is called once per frame
    void Update () {
        // Don't process input when gameplay is blocked or game is not running
        if (GameManager.instance == null || !GameManager.instance.IsGameplayInputEnabled) return;

        int horizontal = 0;
        int vertical = 0;

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBPLAYER

        if (Input.GetKeyDown("space"))
            AttemptGathering();

        horizontal = (int)Input.GetAxisRaw("Horizontal");
        vertical = (int)Input.GetAxisRaw("Vertical");

        if (horizontal != 0)
            vertical = 0;

#else

        if (Input.touchCount > 0)
        {
            Touch myTouch = Input.touches[0];

            if(myTouch.phase == TouchPhase.Began)
            {
                touchOrigin = myTouch.position;
            }
            else if(myTouch.phase == TouchPhase.Ended && touchOrigin.x >= 0)
            {
                Vector2 touchEnd = myTouch.position;
                float x = touchEnd.x - touchOrigin.x;
                float y = touchEnd.y - touchOrigin.y;
                touchOrigin.x = -1;
                if(Mathf.Abs(x) > Mathf.Abs(y))
                {
                    horizontal = x > 0 ? 1 : -1;
                }
                else
                {
                    vertical = y > 0 ? 1 : -1;
                }
            }
        }

#endif

        if (horizontal != 0 || vertical != 0)
            AttemptMove<Wall>(horizontal, vertical);
	}

    protected override void AttemptMove<T>(int xDir, int yDir)
    {
        RaycastHit2D hit;
        bool canMove = Move(xDir, yDir, out hit);
        T hitComponent = hit.transform == null ? null : hit.transform.GetComponent<T>();

        if (!canMove && hitComponent == null)
            return;

        if (GameManager.instance != null && GameManager.instance.runState != null)
        {
            GameManager.instance.runState.DecrementFoodPoints();
            food = GameManager.instance.runState.playerFoodPoints;
            health = GameManager.instance.runState.playerHealthPoints;
        }
        else
        {
            food--;
        }

        if (foodText != null)
            foodText.text = "Food: " + food;
        if (healthText != null)
            healthText.text = "Health: " + health;

        if (!canMove)
            OnCantMove(hitComponent);
        else if (SoundManager.instance != null)
            SoundManager.instance.RandomizeSfx(moveSound1, moveSound2);

        CheckIfGameOver();

        // Set player turn to false through RunState
        if (GameManager.instance != null && GameManager.instance.runState != null)
            GameManager.instance.runState.SetPlayerTurn(false);
    }

    protected void AttemptGathering()
    {
        // Update state through RunState
        if (GameManager.instance != null && GameManager.instance.runState != null)
        {
            GameManager.instance.runState.DecrementFoodPoints();
            
            // Update UI directly since we already have the current values
            if (foodText != null)
                foodText.text = "Food: " + GameManager.instance.runState.playerFoodPoints;
            if (healthText != null)
                healthText.text = "Health: " + GameManager.instance.runState.playerHealthPoints;
        }

        CheckIfGameOver();

        if (onCarrot)
        {
            // Update state through RunState
            if (GameManager.instance != null && GameManager.instance.runState != null)
            {
                GameManager.instance.runState.IncrementFoodPoints(pointsPerFood);
                
                // Update UI directly since we already have the current values
                if (foodText != null)
                    foodText.text = "Food: " + GameManager.instance.runState.playerFoodPoints + "+" + pointsPerFood;
                if (SoundManager.instance != null)
                    SoundManager.instance.RandomizeSfx(eatSound1, eatSound2);
                if (tmpCarrot != null)
                    tmpCarrot.SetActive(false);
            }
        }

        // Set player turn to false through RunState
        if (GameManager.instance != null && GameManager.instance.runState != null)
            GameManager.instance.runState.SetPlayerTurn(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Don't process collisions when gameplay is blocked
        if (GameManager.instance != null && GameManager.instance.IsGameplayInputBlocked) return;

        if(other.tag == "Exit")
        {
            Invoke("Restart", restartLevelDelay);
            enabled = false;
        }
        else if (other.tag == "Food")
        {
            // Update state through RunState
            if (GameManager.instance != null && GameManager.instance.runState != null)
            {
                GameManager.instance.runState.IncrementFoodPoints(pointsPerFood);
                
                // Update UI directly since we already have the current values
                if (foodText != null)
                    foodText.text = "Food: " + GameManager.instance.runState.playerFoodPoints + "+" + pointsPerFood;
                if (SoundManager.instance != null)
                    SoundManager.instance.RandomizeSfx(eatSound1, eatSound2);
                other.gameObject.SetActive(false);
            }
        }
        else if (other.tag == "Soda")
        {
            // Update state through RunState
            if (GameManager.instance != null && GameManager.instance.runState != null)
            {
                GameManager.instance.runState.IncrementFoodPoints(pointsPerSoda);
                
                // Update UI directly since we already have the current values
                if (foodText != null)
                    foodText.text = "Food: " + GameManager.instance.runState.playerFoodPoints + "+" + pointsPerSoda;
                if (SoundManager.instance != null)
                    SoundManager.instance.RandomizeSfx(drinkSound1, drinkSound2);
                other.gameObject.SetActive(false);
            }
        }
        else if (other.tag == "Aid")
        {
            // Update state through RunState
            if (GameManager.instance != null && GameManager.instance.runState != null)
            {
                GameManager.instance.runState.IncrementHealthPoints(pointsPerAid);
                
                // Update UI directly since we already have the current values
                if (healthText != null)
                    healthText.text = "Health: " + GameManager.instance.runState.playerHealthPoints + "+" + pointsPerAid;
                if (SoundManager.instance != null)
                    SoundManager.instance.RandomizeSfx(drinkSound1, drinkSound2); //Zmienić dźwięk!!!
                other.gameObject.SetActive(false);
            }
        }
        if (other.tag == "Carrot")
        {
            onCarrot = true;
            tmpCarrot = other.gameObject;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        // Don't process exit collisions when gameplay is blocked
        if (GameManager.instance != null && GameManager.instance.IsGameplayInputBlocked) return;

        if (other.tag == "Carrot")
            onCarrot = false;
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

    public void LoseHealth (int loss)
    {
        if (GameManager.instance != null && GameManager.instance.IsGameplayInputBlocked)
            return;

        animator.SetTrigger("playerHit");
        
        // Update state through RunState
        if (GameManager.instance != null && GameManager.instance.runState != null)
        {
            GameManager.instance.runState.DecrementHealthPoints(loss);
            
            // Update UI directly since we already have the current values
            if (healthText != null)
                healthText.text =  "Health: " + GameManager.instance.runState.playerHealthPoints + "-" + loss;
        }
        
        CheckIfGameOver();
    }

    private void CheckIfGameOver()
    {
        if (GameManager.instance == null || GameManager.instance.IsGameplayInputBlocked)
            return;

        if (GameManager.instance != null && GameManager.instance.runState != null)
        {
            if (GameManager.instance.runState.playerFoodPoints <= 0 || GameManager.instance.runState.playerHealthPoints <= 0)
            {
                if (SoundManager.instance != null)
                    SoundManager.instance.RandomizeSfx(gameOverSound);
                if (GameManager.instance != null)
                    GameManager.instance.GameOver(GameManager.instance.runState.playerFoodPoints <= 0 ? true : false);
            }    
        }
    }
}