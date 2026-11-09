using HallowBlaze.Presentation.Runtime;
using UnityEngine;

/// <summary>Replays resolved enemy swings and hit audio without choosing targets or applying damage.</summary>
public class Enemy : MovingObject, IEnemyAttackView {

    /// <summary>Retained serialized tuning; authoritative damage belongs to the turn resolver.</summary>
    public int playerDamage;

    /// <summary>Optional authored hit cues; misses never play these impact sounds.</summary>
    public AudioClip enemyAttack1;
    /// <summary>Optional second hit cue.</summary>
    public AudioClip enemyAttack2;
    /// <summary>Optional third hit cue.</summary>
    public AudioClip enemyAttack3;

    private Animator attackAnimator;
    private AudioSource attackSource;

    /// <summary>Gets the number of actual hit-audio playback requests during this view's lifetime.</summary>
    public int PresentedAttackSoundCount { get; private set; }

    /// <summary>Starts one resolved swing and optional hit cue, returning its unscaled animation duration.</summary>
    /// <param name="animate">Whether to play the authored swing; audio settings remain independent.</param>
    /// <param name="hit">Recorded hit outcome; false suppresses hit-only audio.</param>
    /// <returns>The authored swing duration, or zero when animation is disabled or absent.</returns>
    public float PresentAttack(bool animate, bool hit)
    {
        StopAttack();
        float duration = 0f;
        attackAnimator = GetComponent<Animator>();
        if (animate && attackAnimator != null && attackAnimator.runtimeAnimatorController != null)
        {
            attackAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            attackAnimator.SetTrigger("enemyAttack");
            attackAnimator.Update(0f);
            duration = attackAnimator.GetCurrentAnimatorStateInfo(0).length;
        }
        SoundManager sounds = SoundManager.instance;
        AudioClip clip = enemyAttack1 != null ? enemyAttack1 : enemyAttack2 != null ? enemyAttack2 : enemyAttack3;
        if (hit && clip != null && sounds != null && sounds.efxSource != null)
        {
            if (attackSource == null)
            {
                attackSource = gameObject.AddComponent<AudioSource>();
                attackSource.playOnAwake = false;
            }
            attackSource.loop = false;
            attackSource.spatialBlend = 0f;
            attackSource.volume = sounds.efxSource.volume;
            attackSource.outputAudioMixerGroup = sounds.efxSource.outputAudioMixerGroup;
            attackSource.mute = !sounds.SoundEnabled || sounds.efxSource.mute;
            attackSource.clip = clip;
            attackSource.Play();
            PresentedAttackSoundCount++;
        }
        return duration;
    }

    /// <summary>Stops only this view's attack cue and resets artwork without replaying a gameplay event.</summary>
    public void StopAttack()
    {
        if (attackSource != null)
            attackSource.Stop();
        if (attackAnimator != null && attackAnimator.isActiveAndEnabled && attackAnimator.runtimeAnimatorController != null)
        {
            attackAnimator.Rebind();
            attackAnimator.Update(0f);
        }
    }

    private void Update()
    {
        if (attackSource != null)
        {
            SoundManager sounds = SoundManager.instance;
            attackSource.mute = sounds == null || sounds.efxSource == null || !sounds.SoundEnabled || sounds.efxSource.mute;
        }
    }

    private void OnDisable() => StopAttack();
}