using UnityEngine;
using UnityEngine.UI;

public class SoundOffBttnScript : MonoBehaviour {

    public AudioSource SoundSource;
    public Text SoundOnText;
    public Text SoundOffText;

    //When the script instance is being loaded check saved sound settings and set buttons accordingly.
    public void Awake()
    {
        if (PlayerPrefs.GetString("Sound") == "Off")
            SoundOffText.color = new Color32(255, 255, 255, 255);
        else
            SoundOffText.color = new Color32(50, 50, 50, 255);
    }

    public void SetSoundOff()
    {
        SoundSource.mute = true;
        SoundOnText.color = new Color32(50, 50, 50, 255);
        SoundOffText.color = new Color32(255, 255, 255, 255);
        PlayerPrefs.SetString("Sound", "Off");
    }
}
