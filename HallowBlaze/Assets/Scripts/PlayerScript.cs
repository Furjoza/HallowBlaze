using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerScript : MovingObject {

    public int wallDamage = 1;
    public int pointsPerFood = 10;
    public int pointsPerSoda = 20;
    public int pointsPerAid = 10;
    public float restartLevelDelay = 1f;
    public Text foodText;
    public Text healthText;
    public AudioClip moveSound1;
    public AudioClip moveSound2;
    public AudioClip eatSound1;
    public AudioClip eatSound2;
    public AudioClip drinkSound1;
    public AudioClip drinkSound2;
    public AudioClip gameOverSound;


    private Animator animator;
    private int food;
    private int health;
    private bool onCarrot = false;
    private Vector2 touchOrigin = -Vector2.one;
    private GameObject tmpCarrot;

	// Use this for initialization
	protected override void Start ()
    {
        animator = GetComponent<Animator>();

        food = GameManager.instance.playerFoodPoints;
        health = GameManager.instance.playerHealthPoints;

        foodText.text = "Food: " + food;
        healthText.text = "Health: " + health;
        base.Start();
	}

    private void OnDisable()
    {
        GameManager.instance.playerFoodPoints = food;
        GameManager.instance.playerHealthPoints = health;
    }

    // Update is called once per frame
    void Update () {
        if (!GameManager.instance.playerTurn) return;

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
        food--;
        foodText.text = "Food: " + food;
        healthText.text = "Health: " + health;

        base.AttemptMove<T>(xDir, yDir);

        RaycastHit2D hit;
        if(Move(xDir, yDir, out hit))
        {
            SoundManager.instance.RandomizeSfx(moveSound1, moveSound2);
        }
        CheckIfGameOver();

        GameManager.instance.playerTurn = false;
    }

    protected void AttemptGathering()
    {
        food--;
        foodText.text = "Food: " + food;
        healthText.text = "Health: " + health;

        CheckIfGameOver();

        if (onCarrot)
        {
            food += pointsPerFood;
            foodText.text = "Food: " + food + "+" + pointsPerFood;
            SoundManager.instance.RandomizeSfx(eatSound1, eatSound2);
            tmpCarrot.SetActive(false);
        }

        GameManager.instance.playerTurn = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if(other.tag == "Exit")
        {
            Invoke("Restart", restartLevelDelay);
            enabled = false;
        }
        else if (other.tag == "Food")
        {
            food += pointsPerFood;
            foodText.text = "Food: " + food + "+" + pointsPerFood;
            SoundManager.instance.RandomizeSfx(eatSound1, eatSound2);
            other.gameObject.SetActive(false);
        }
        else if (other.tag == "Soda")
        {
            food += pointsPerSoda;
            foodText.text = "Food: " + food + "+" + pointsPerSoda;
            SoundManager.instance.RandomizeSfx(drinkSound1, drinkSound2);
            other.gameObject.SetActive(false);
        }
        else if (other.tag == "Aid")
        {
            health += pointsPerAid;
            healthText.text = "Health: " + health + "+" + pointsPerAid;
            SoundManager.instance.RandomizeSfx(drinkSound1, drinkSound2); //Zmienić dźwięk!!!
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
        if (other.tag == "Carrot")
            onCarrot = false;
    }
    protected override void OnCantMove<T>(T component)
    {
        Wall hitWall = component as Wall;
        hitWall.DamageWall(wallDamage);
        animator.SetTrigger("playerChop");
    }

    private void Restart()
    {
        Application.LoadLevel(Application.loadedLevel);
    }

    public void LoseHealth (int loss)
    {
        animator.SetTrigger("playerHit");
        health -= loss;
        healthText.text =  "Health: " + health + "-" + loss;
        CheckIfGameOver();
    }

    private void CheckIfGameOver()
    {
        if (food <= 0 || health <= 0)
        {
            SoundManager.instance.RandomizeSfx(gameOverSound);
            //SoundManager.instance.musicSource.Stop();
            GameManager.instance.GameOver(food <= 0 ? true : false);
        }    
    }
}
