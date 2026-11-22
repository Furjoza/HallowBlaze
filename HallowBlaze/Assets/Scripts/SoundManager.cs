using UnityEngine;
#if UNITY_EDITOR
using HallowBlaze.Presentation.Runtime;
#endif

public class SoundManager : MonoBehaviour {

    public AudioSource efxSource;
    public AudioSource musicSource;

    static public SoundManager instance = null;

    public float lowPitchRange = .95f;
    public float highPitchRange = 1.05f;
#if UNITY_EDITOR
    private bool authoringPreferences;
    private bool authoringMusicEnabled = true;
    private bool authoringSoundEnabled = true;
    internal System.Func<string, string, string> ReadPreference = PlayerPrefs.GetString;
    internal System.Action<string, string> WritePreference = PlayerPrefs.SetString;

    private bool UsesAuthoringPreferences
    {
        get
        {
            authoringPreferences |= BoardAuthoringTestContext.IsRequested ||
                GameManager.instance != null && GameManager.instance.IsAuthoringSession;
            return authoringPreferences;
        }
    }
#endif
	
	void Awake ()
    {
#if UNITY_EDITOR
        authoringPreferences = UsesAuthoringPreferences;
#endif
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

    /// <summary>Reads the current music choice; an authoring lifetime uses only its temporary preference.</summary>
    public bool MusicEnabled
    {
        get {
#if UNITY_EDITOR
            if (UsesAuthoringPreferences) return authoringMusicEnabled;
            return ReadPreference("Music", "On") != "Off";
#else
            return PlayerPrefs.GetString("Music", "On") != "Off";
#endif
        }
    }

    /// <summary>Reads the current effect choice without consulting player preferences during authoring.</summary>
    public bool SoundEnabled
    {
        get {
    #if UNITY_EDITOR
            if (UsesAuthoringPreferences) return authoringSoundEnabled;
                return ReadPreference("Sound", "On") != "Off";
            #else
                return PlayerPrefs.GetString("Sound", "On") != "Off";
    #endif
        }
    }

    /// <summary>Applies music immediately; authoring changes never write the player's saved preference.</summary>
    /// <param name="enabled">Whether music should play for this session.</param>
    public void SetMusicEnabled(bool enabled)
    {
    #if UNITY_EDITOR
        if (UsesAuthoringPreferences) authoringMusicEnabled = enabled;
        else WritePreference("Music", enabled ? "On" : "Off");
    #else
        PlayerPrefs.SetString("Music", enabled ? "On" : "Off");
    #endif

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

    /// <summary>Applies effect muting; authoring changes remain temporary even after its terminal board.</summary>
    /// <param name="enabled">Whether effects should be audible for this session.</param>
    public void SetSoundEnabled(bool enabled)
    {
    #if UNITY_EDITOR
        if (UsesAuthoringPreferences) authoringSoundEnabled = enabled;
        else WritePreference("Sound", enabled ? "On" : "Off");
    #else
        PlayerPrefs.SetString("Sound", enabled ? "On" : "Off");
    #endif

        if (efxSource != null)
            efxSource.mute = !enabled;
    }

    public void ApplySavedSettings()
    {
        SetMusicEnabled(MusicEnabled);
        SetSoundEnabled(SoundEnabled);
    }

    internal void ReleaseAuthoringPreferences()
    {
#if UNITY_EDITOR
        if (!authoringPreferences || BoardAuthoringTestContext.IsRequested) return;
        authoringPreferences = false;
        if (efxSource != null) efxSource.mute = !SoundEnabled;
        if (musicSource == null) return;
        if (!MusicEnabled) musicSource.Stop();
        else if (!musicSource.isPlaying) musicSource.Play();
#endif
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