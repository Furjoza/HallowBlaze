using UnityEngine;

public class RestartBttnScript : MonoBehaviour
{
    public void Restart()
    {
        if (GameManager.instance != null)
            GameManager.instance.RestartGame();
    }

    public void ReturnToMenu()
    {
        if (GameManager.instance != null)
            GameManager.instance.ReturnToMenuAfterGameOver();
    }
}