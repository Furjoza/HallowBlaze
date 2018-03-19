using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class StartBttnScript : MonoBehaviour
{

    public void LoadByName(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}