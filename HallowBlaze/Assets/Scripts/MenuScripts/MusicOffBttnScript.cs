using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MusicOffBttnScript : MonoBehaviour {

    public AudioSource MusicSource;
    public Text MusicOnText;
    public Text MusicOffText;

    public void SetMusicOff()
    {
        if (MusicSource.isPlaying)
        {
            MusicSource.Stop();
            MusicOnText.color = new Color32(50, 50, 50, 255);
            MusicOffText.color = new Color32(255, 255, 255, 255);
        }
    }
}
