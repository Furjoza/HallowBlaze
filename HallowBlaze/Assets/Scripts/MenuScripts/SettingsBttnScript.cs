using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SettingsBttnScript : MonoBehaviour {

    public GameObject mainMenuPanel;
    public GameObject settingsMenuPanel;

    public void Settings()
    {
        mainMenuPanel.SetActive(false);
        settingsMenuPanel.SetActive(true);
    }
}
