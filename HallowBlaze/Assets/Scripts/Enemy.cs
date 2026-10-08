using UnityEngine;

/// <summary>Inert legacy enemy artwork; movement, attacks and cadence await M3.7/M3.8 domain rules.</summary>
public class Enemy : MovingObject {

    public int playerDamage;

    // Sounds of getting hit by enemy
    public AudioClip enemyAttack1;
    public AudioClip enemyAttack2;
    public AudioClip enemyAttack3;

}