using UnityEngine;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime.Interaction;

/// <summary>Legacy obstacle view; the board resolver owns current clearing progress and removal.</summary>
public class Wall : MonoBehaviour, IObstacleWorkFeedback {

    public int hp = 4;
    public Sprite dmgSprite;
    public AudioClip chopSound1;
    public AudioClip chopSound2;

    private SpriteRenderer spriteRenderer;
	
	void Awake () {
        spriteRenderer = GetComponent<SpriteRenderer>();
	}
	
    /// <summary>Replays clearing feedback without applying damage, consuming a charge or removing a board entity.</summary>
    public void PresentWork(ObstacleWorkedEvent work)
    {
        if (spriteRenderer != null && dmgSprite != null)
            spriteRenderer.sprite = dmgSprite;
        if (SoundManager.instance != null && (chopSound1 != null || chopSound2 != null))
            SoundManager.instance.RandomizeSfx(chopSound1 != null ? chopSound1 : chopSound2);
    }

	public void DamageWall (int loss)
    {
        SoundManager.instance.RandomizeSfx(chopSound1, chopSound2);
        spriteRenderer.sprite = dmgSprite;
        hp -= loss;

        if (hp <= 0)
            gameObject.SetActive(false);
	}
}