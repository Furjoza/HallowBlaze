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
        if (PlayerPrefs.GetString("Music") == "Off")
            musicSource.Stop();

        if (PlayerPrefs.GetString("Sound") == "Off")
            efxSource.mute = true;
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