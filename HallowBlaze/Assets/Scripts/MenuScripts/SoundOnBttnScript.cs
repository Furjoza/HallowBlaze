using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SoundOnBttnScript : MonoBehaviour {

    public AudioSource SoundSource;
    public Text SoundOnText;
    public Text SoundOffText;

    //When the script instance is being loaded check saved sound settings and set buttons accordingly.
    public void Awake()
    {
        if (PlayerPrefs.GetString("Sound") == "On")
            SoundOnText.color = new Color32(255, 255, 255, 255);
        else
            SoundOnText.color = new Color32(50, 50, 50, 255);
    }

    public void SetSoundOn()
    {
        SoundSource.mute = false;
        SoundOnText.color = new Color32(255, 255, 255, 255);
        SoundOffText.color = new Color32(50, 50, 50, 255);
        PlayerPrefs.SetString("Sound", "On");
    }
}
