using System;
using System.Threading.Tasks;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Samples complete PC commands and presents authoritative resources/audio; never applies gameplay rules.</summary>
public class PlayerScript : MovingObject, IRunHud, ITurnFeedback
{
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

    /// <summary>Gets the number of actual ordered audio cues played by this view during its lifetime.</summary>
    public int PresentedSoundCount { get; private set; }

    private async void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (!TrySubmitInput(commandSource, out Task<CommandSubmission> submission))
            return;
        try { await submission; }
        catch (Exception exception) { Debug.LogException(exception, this); }
#endif
    }

    private void OnDisable() => commandSource.CancelDraft();

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
        if (foodText != null)
            foodText.text = "Food: " + food;
        if (healthText != null)
            healthText.text = "Health: " + health;
    }

    /// <inheritdoc />
    public void ShowRejection(CommandRejectionCode reason) => Debug.Log("Command rejected: " + reason, this);

    /// <inheritdoc />
    public void ShowEvent(GameEvent gameEvent)
    {
        if (gameEvent is EntityMovedEvent)
            PlaySound(moveSound1, moveSound2);
        else if (gameEvent is FoodRestoredEvent restored)
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