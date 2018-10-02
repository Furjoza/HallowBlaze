using UnityEngine;
using UnityEngine.UI;

public class MusicOnBttnScript : MonoBehaviour {

    public AudioSource MusicSource;
    public Text MusicOnText;
    public Text MusicOffText;

    //When the script instance is being loaded check saved music settings and set buttons accordingly.
    public void Awake()
    {
        if (PlayerPrefs.GetString("Music") == "On")
            MusicOnText.color = new Color32(255, 255, 255, 255);
        else
            MusicOnText.color = new Color32(50, 50, 50, 255);
    }

    public void SetMusicOn()
    {
        if (!MusicSource.isPlaying)
        {
            MusicSource.Play();
            MusicOnText.color = new Color32(255, 255, 255, 255);
            MusicOffText.color = new Color32(50, 50, 50, 255);
            PlayerPrefs.SetString("Music", "On");
        }
    }
}
