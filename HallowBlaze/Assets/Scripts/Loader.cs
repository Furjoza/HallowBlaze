using UnityEngine;

public class Loader : MonoBehaviour {

    public GameObject gameManager;

	void Awake () {
        if (GameManager.instance == null)
        {
            if (gameManager != null)
                Instantiate(gameManager);
            else
                Debug.LogError("Loader is missing the GameManager prefab reference.");
        }
	}
}