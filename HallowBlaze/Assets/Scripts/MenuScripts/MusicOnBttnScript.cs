using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MusicOnBttnScript : MonoBehaviour {

    public AudioSource MusicSource;
    public Text MusicOnText;
    public Text MusicOffText;

    public void SetMusicOn()
    {
        if (!MusicSource.isPlaying)
        {
            MusicSource.Play();
            MusicOnText.color = new Color32(255, 255, 255, 255);
            MusicOffText.color = new Color32(50, 50, 50, 255);
        }
    }
}
