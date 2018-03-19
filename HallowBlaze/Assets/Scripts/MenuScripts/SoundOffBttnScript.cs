using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SoundOffBttnScript : MonoBehaviour {

    public AudioSource SoundSource;
    public Text SoundOnText;
    public Text SoundOffText;

    public void SetSoundOff()
    {
        SoundSource.mute = false;
        SoundOnText.color = new Color32(50, 50, 50, 255);
        SoundOffText.color = new Color32(255, 255, 255, 255);
    }
}
