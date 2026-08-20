using UnityEngine;
using UnityEngine.SceneManagement;

public class RestartBttnScript : MonoBehaviour {

    public GameObject gameManager;
    
    //Destroy GameManager and re-instantiate it as it is the easiest way of restarting the game.
    public void Restart()
    {
        Destroy(GameManager.instance.gameObject);
        Instantiate(gameManager);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}