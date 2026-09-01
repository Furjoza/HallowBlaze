using UnityEngine;

public class SoundManager : MonoBehaviour {

    public AudioSource efxSource;
    public AudioSource musicSource;

    static public SoundManager instance = null;

    public float lowPitchRange = .95f;
    public float highPitchRange = 1.05f;
	
	void Awake ()
    {
        // Enforce singleton pattern
        if (instance == null)
            instance = this;

        else if (instance != this)
            Destroy(gameObject);

        DontDestroyOnLoad(gameObject);
    }

    // When the script instance is being started check saved sound settings and set audio sources accordingly.
    private void Start ()
    {
        ApplySavedSettings();
    }

    public bool MusicEnabled
    {
        get { return PlayerPrefs.GetString("Music", "On") != "Off"; }
    }

    public bool SoundEnabled
    {
        get { return PlayerPrefs.GetString("Sound", "On") != "Off"; }
    }

    public void SetMusicEnabled(bool enabled)
    {
        PlayerPrefs.SetString("Music", enabled ? "On" : "Off");

        if (musicSource == null)
            return;

        if (enabled)
        {
            if (!musicSource.isPlaying)
                musicSource.Play();
        }
        else
        {
            musicSource.Stop();
        }
    }

    public void SetSoundEnabled(bool enabled)
    {
        PlayerPrefs.SetString("Sound", enabled ? "On" : "Off");

        if (efxSource != null)
            efxSource.mute = !enabled;
    }

    public void ApplySavedSettings()
    {
        SetMusicEnabled(MusicEnabled);
        SetSoundEnabled(SoundEnabled);
    }

    public void PlaySingle (AudioClip clip)
    {
        efxSource.clip = clip;
        efxSource.Play();
    }

    // Takes a list of sounds, randomly chooses one and play it with randomized pitch.
    public void RandomizeSfx (params AudioClip[] clips)
    {
        int randomIndex = Random.Range(0, clips.Length);
        float randomPitch = Random.Range(lowPitchRange, highPitchRange);

        efxSource.pitch = randomPitch;
        efxSource.clip = clips[randomIndex];
        efxSource.Play();
    }
}