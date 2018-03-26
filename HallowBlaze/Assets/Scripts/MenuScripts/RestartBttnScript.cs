using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RestartBttnScript : MonoBehaviour {

    public GameObject gameManager;

    public void Restart()
    {
        Destroy(GameManager.instance.gameObject);
        //SoundManager.instance.musicSource.Play();
        Instantiate(gameManager);
        Application.LoadLevel(Application.loadedLevel);
    }
}