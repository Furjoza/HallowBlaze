using UnityEngine;

/// <summary>Retains serialized movement presentation settings; it performs no physics or gameplay adjudication.</summary>
public abstract class MovingObject : MonoBehaviour
{
    /// <summary>Movement interpolation duration in unscaled seconds, read by the board presenter.</summary>
    public float moveTime = 0.1f;

    /// <summary>Legacy serialized compatibility only; colliders no longer decide gameplay.</summary>
    public LayerMask blockingLayer;
}