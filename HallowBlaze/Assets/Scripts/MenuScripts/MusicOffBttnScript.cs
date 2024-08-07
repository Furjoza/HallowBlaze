using UnityEngine;
using UnityEngine.UI;

public class MusicOffBttnScript : MonoBehaviour {

    public AudioSource MusicSource;
    public Text MusicOnText;
    public Text MusicOffText;

    //When the script instance is being loaded check saved music settings and set buttons accordingly.
    public void Start()
    {
        if (PlayerPrefs.GetString("Music") == "Off")
            MusicOffText.color = new Color32(255, 255, 255, 255);
        else
            MusicOffText.color = new Color32(50, 50, 50, 255);
    }

    public void SetMusicOff()
    {
        if (MusicSource.isPlaying)
        {
            MusicSource.Stop();
            MusicOnText.color = new Color32(50, 50, 50, 255);
            MusicOffText.color = new Color32(255, 255, 255, 255);
            PlayerPrefs.SetString("Music", "Off");
        }
    }
}
