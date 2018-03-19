using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuScript : MonoBehaviour {

    public GameObject gameManager;

    public void LoadByName(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void Restart()
    {
        Destroy(GameManager.instance.gameObject);
        SoundManager.instance.musicSource.Play();
        Instantiate(gameManager);
        Application.LoadLevel(Application.loadedLevel);
    }
}
