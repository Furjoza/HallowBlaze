using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HelpBttnScript : MonoBehaviour {

    public GameObject mainMenuPanel;
    public GameObject helpMenuPanel;

    public void Help()
    {
        mainMenuPanel.SetActive(false);
        helpMenuPanel.SetActive(true);
    }
}
