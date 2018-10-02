using UnityEngine;

public class RestartBttnScript : MonoBehaviour {

    public GameObject gameManager;
    
    //Destroy GameManager and re-instantiate it as it is the easiest way of restarting the game.
    public void Restart()
    {
        Destroy(GameManager.instance.gameObject);
        Instantiate(gameManager);
        Application.LoadLevel(Application.loadedLevel);
    }
}