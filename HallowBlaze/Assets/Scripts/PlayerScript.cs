using System;
using System.Threading.Tasks;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Samples complete PC commands and presents authoritative resources/audio; never applies gameplay rules.</summary>
public class PlayerScript : MovingObject, IRunHud, ITurnFeedback, IPlayerStepAudio, ITurnResourceGains
{
    /// <summary>Maximum player-step playback in seconds, additionally bounded by the movement presentation.</summary>
    public const float MaximumStepSeconds = 0.1f;
    public float restartLevelDelay = 1f;
    public int wallDamage = 1;
    public int pointsPerFood = 10;
    public int pointsPerSoda = 20;
    public int pointsPerAid = 10;
    public AudioClip moveSound1;
    public AudioClip moveSound2;
    public AudioClip eatSound1;
    public AudioClip eatSound2;
    public AudioClip drinkSound1;
    public AudioClip drinkSound2;
    public AudioClip gameOverSound;
    public Text foodText;
    public Text healthText;

    /// <summary>Controls visual interpolation only; disabling it does not change rules or event order.</summary>
    public bool animationsEnabled = true;

    private readonly PcCommandSource commandSource = new PcCommandSource();
    private AudioSource stepSource;
    private int displayedHealth;
    private int displayedFood;
    private long healthGain;
    private long foodGain;

    /// <summary>Gets the number of actual ordered audio cues played by this view during its lifetime.</summary>
    public int PresentedSoundCount { get; private set; }

    private async void Update()
    {
        if (stepSource != null && SoundManager.instance != null && SoundManager.instance.efxSource != null)
            stepSource.mute = !SoundManager.instance.SoundEnabled || SoundManager.instance.efxSource.mute;
#if UNITY_EDITOR || UNITY_STANDALONE
        if (!TrySubmitInput(commandSource, out Task<CommandSubmission> submission))
            return;
        try { await submission; }
        catch (Exception exception) { Debug.LogException(exception, this); }
#endif
    }

    private void OnDisable()
    {
        commandSource.CancelDraft();
        StopStep();
    }

    /// <inheritdoc />
    public void BeginStep(float duration)
    {
        StopStep();
        SoundManager sounds = SoundManager.instance;
        if (duration <= 0f || sounds == null || sounds.efxSource == null ||
            moveSound1 == null && moveSound2 == null)
            return;
        if (stepSource == null)
        {
            stepSource = gameObject.AddComponent<AudioSource>();
            stepSource.playOnAwake = false;
        }
        stepSource.loop = false;
        stepSource.spatialBlend = 0f;
        stepSource.volume = sounds.efxSource.volume;
        stepSource.outputAudioMixerGroup = sounds.efxSource.outputAudioMixerGroup;
        stepSource.mute = !sounds.SoundEnabled || sounds.efxSource.mute;
        stepSource.pitch = UnityEngine.Random.Range(sounds.lowPitchRange, sounds.highPitchRange);
        stepSource.clip = moveSound1 != null && moveSound2 != null
            ? (UnityEngine.Random.Range(0, 2) == 0 ? moveSound1 : moveSound2)
            : (moveSound1 != null ? moveSound1 : moveSound2);
        stepSource.Play();
        stepSource.SetScheduledEndTime(AudioSettings.dspTime + Mathf.Min(duration, MaximumStepSeconds));
        PresentedSoundCount++;
    }

    /// <inheritdoc />
    public void StopStep()
    {
        if (stepSource != null)
            stepSource.Stop();
    }

    /// <summary>Forwards a complete intent through the same gate as production PC polling.</summary>
    /// <param name="command">The complete command; this view never resolves it.</param>
    /// <param name="submission">The admitted operation, or null when blocked.</param>
    /// <returns>Whether this enabled view and the current lifecycle admitted submission.</returns>
    public bool TrySubmitCommand(PlayerCommand command, out Task<CommandSubmission> submission)
    {
        submission = null;
        return OwnsActivePlayerView() &&
            GameManager.instance.TrySubmitCommand(command, out submission);
    }

    /// <summary>Forwards one source sample without sampling or buffering blocked input.</summary>
    /// <param name="source">A complete-command source, normally the PC key-down adapter.</param>
    /// <param name="submission">The admitted operation, or null when blocked.</param>
    /// <returns>Whether the production gate admitted sampling.</returns>
    public bool TrySubmitInput(ICommandSource source, out Task<CommandSubmission> submission)
    {
        submission = null;
        return OwnsActivePlayerView() &&
            GameManager.instance.TrySubmitInput(source, out submission);
    }

    private bool OwnsActivePlayerView()
    {
        BoardRuntime runtime = GameManager.instance == null ? null : GameManager.instance.ActiveBoardRuntime;
        return isActiveAndEnabled && runtime != null && !runtime.IsDisposed &&
            runtime.Views.TryGetValue(runtime.PlayerId, out GameObject player) && player == gameObject;
    }

    /// <inheritdoc />
    public void Refresh(int health, int food)
    {
        displayedHealth = health;
        displayedFood = food;
        if (foodText != null)
            foodText.text = "Food: " + food + (foodGain > 0 ? " +" + foodGain : "");
        if (healthText != null)
            healthText.text = "Health: " + health + (healthGain > 0 ? " +" + healthGain : "");
    }

    /// <inheritdoc />
    public void AddResourceGains(int health, int food)
    {
        if (health < 0) throw new ArgumentOutOfRangeException(nameof(health));
        if (food < 0) throw new ArgumentOutOfRangeException(nameof(food));
        healthGain = checked(healthGain + health);
        foodGain = checked(foodGain + food);
        Refresh(displayedHealth, displayedFood);
    }

    /// <inheritdoc />
    public void ClearResourceGains()
    {
        healthGain = 0;
        foodGain = 0;
        Refresh(displayedHealth, displayedFood);
    }

    /// <summary>Hides the current resource HUD during route selection and the full-screen day intro.</summary>
    /// <param name="visible">Whether gameplay resource labels may be shown.</param>
    public void SetHudVisible(bool visible)
    {
        if (foodText != null)
            foodText.gameObject.SetActive(visible);
        if (healthText != null)
            healthText.gameObject.SetActive(visible);
    }

    /// <inheritdoc />
    public void ShowRejection(CommandRejectionCode reason) => Debug.Log("Command rejected: " + reason, this);

    /// <inheritdoc />
    public void ShowEvent(GameEvent gameEvent)
    {
        if (gameEvent is FoodRestoredEvent restored)
        {
            BoardRuntime runtime = GameManager.instance == null ? null : GameManager.instance.ActiveBoardRuntime;
            bool soda = runtime != null && !runtime.IsDisposed &&
                runtime.Views.TryGetValue(restored.SourceItemId, out GameObject item) && item != null && item.CompareTag("Soda");
            if (soda)
                PlaySound(drinkSound1, drinkSound2);
            else
                PlaySound(eatSound1, eatSound2);
        }
        else if (gameEvent is PlayerStarvedEvent || gameEvent is PlayerDiedEvent)
            PlaySound(gameOverSound, null);
    }

    private void PlaySound(AudioClip first, AudioClip second)
    {
        if (SoundManager.instance == null || (first == null && second == null))
            return;
        if (first != null && second != null)
            SoundManager.instance.RandomizeSfx(first, second);
        else
            SoundManager.instance.RandomizeSfx(first != null ? first : second);
        PresentedSoundCount++;
    }
}