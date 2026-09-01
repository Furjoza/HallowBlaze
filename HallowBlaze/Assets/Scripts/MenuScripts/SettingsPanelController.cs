using UnityEngine;
using UnityEngine.UI;

public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private Text musicOnText;
    [SerializeField] private Text musicOffText;
    [SerializeField] private Text soundOnText;
    [SerializeField] private Text soundOffText;

    private static readonly Color32 ActiveColor = new Color32(255, 255, 255, 255);
    private static readonly Color32 InactiveColor = new Color32(50, 50, 50, 255);

    private void OnEnable()
    {
        Refresh();
    }

    public void SetMusicOn()
    {
        SetMusic(true);
    }

    public void SetMusicOff()
    {
        SetMusic(false);
    }

    public void SetSoundOn()
    {
        SetSound(true);
    }

    public void SetSoundOff()
    {
        SetSound(false);
    }

    public void Refresh()
    {
        bool musicEnabled = SoundManager.instance != null
            ? SoundManager.instance.MusicEnabled
            : PlayerPrefs.GetString("Music", "On") != "Off";
        bool soundEnabled = SoundManager.instance != null
            ? SoundManager.instance.SoundEnabled
            : PlayerPrefs.GetString("Sound", "On") != "Off";

        SetColors(musicOnText, musicOffText, musicEnabled);
        SetColors(soundOnText, soundOffText, soundEnabled);
    }

    private void SetMusic(bool enabled)
    {
        if (SoundManager.instance != null)
            SoundManager.instance.SetMusicEnabled(enabled);
        else
            PlayerPrefs.SetString("Music", enabled ? "On" : "Off");

        Refresh();
    }

    private void SetSound(bool enabled)
    {
        if (SoundManager.instance != null)
            SoundManager.instance.SetSoundEnabled(enabled);
        else
            PlayerPrefs.SetString("Sound", enabled ? "On" : "Off");

        Refresh();
    }

    private static void SetColors(Text onText, Text offText, bool enabled)
    {
        if (onText != null)
            onText.color = enabled ? ActiveColor : InactiveColor;
        if (offText != null)
            offText.color = enabled ? InactiveColor : ActiveColor;
    }
}