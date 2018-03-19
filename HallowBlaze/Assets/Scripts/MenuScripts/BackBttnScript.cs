using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BackBttnScript : MonoBehaviour {

    public GameObject mainMenuPanel;
    public GameObject settingsMenuPanel;
    public GameObject helpMenuPanel;

    public void Back()
    {
        helpMenuPanel.SetActive(false);
        settingsMenuPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }
}
