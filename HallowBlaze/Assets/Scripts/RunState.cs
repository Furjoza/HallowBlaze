using System;
using UnityEngine;

/// <summary>
/// Centralized state management for a single game run.
/// This class serves as the single source of truth for all game state during a run.
/// </summary>
public class RunState : MonoBehaviour
{
    [Header("Player State")]
    public int playerFoodPoints = 100;
    public int playerHealthPoints = 100;
    
    [Header("Game State")]
    public int level = 0;
    public bool playerTurn = true;
    public bool enemiesMoving = false;
    public bool doingSetup = false;

    [Header("Game Events")]
    public Action<int> OnFoodChanged;
    public Action<int> OnHealthChanged;
    public Action<int> OnLevelChanged;
    public Action<bool> OnPlayerTurnChanged;
    public Action<bool> OnEnemiesMovingChanged;

    private void Awake()
    {
        // Initialize the state
        ResetState();
    }

    /// <summary>
    /// Resets all game state to initial values
    /// </summary>
    public void ResetState()
    {
        playerFoodPoints = 100;
        playerHealthPoints = 100;
        level = 0;
        playerTurn = true;
        enemiesMoving = false;
        doingSetup = false;
    }

    /// <summary>
    /// Increments the current level
    /// </summary>
    public void IncrementLevel()
    {
        level++;
        OnLevelChanged?.Invoke(level);
    }

    /// <summary>
    /// Sets player food points and notifies subscribers
    /// </summary>
    public void SetFoodPoints(int value)
    {
        playerFoodPoints = value;
        OnFoodChanged?.Invoke(playerFoodPoints);
    }

    /// <summary>
    /// Sets player health points and notifies subscribers
    /// </summary>
    public void SetHealthPoints(int value)
    {
        playerHealthPoints = value;
        OnHealthChanged?.Invoke(playerHealthPoints);
    }

    /// <summary>
    /// Sets player turn state and notifies subscribers
    /// </summary>
    public void SetPlayerTurn(bool value)
    {
        playerTurn = value;
        OnPlayerTurnChanged?.Invoke(playerTurn);
    }

    /// <summary>
    /// Sets enemies moving state and notifies subscribers
    /// </summary>
    public void SetEnemiesMoving(bool value)
    {
        enemiesMoving = value;
        OnEnemiesMovingChanged?.Invoke(enemiesMoving);
    }

    /// <summary>
    /// Decrements food points (used when player moves or performs actions)
    /// </summary>
    public void DecrementFoodPoints(int amount = 1)
    {
        playerFoodPoints = Math.Max(0, playerFoodPoints - amount);
        OnFoodChanged?.Invoke(playerFoodPoints);
    }

    /// <summary>
    /// Decrements health points (used when player takes damage)
    /// </summary>
    public void DecrementHealthPoints(int amount)
    {
        playerHealthPoints = Math.Max(0, playerHealthPoints - amount);
        OnHealthChanged?.Invoke(playerHealthPoints);
    }

    /// <summary>
    /// Increments food points (used when player collects food items)
    /// </summary>
    public void IncrementFoodPoints(int amount)
    {
        playerFoodPoints += amount;
        OnFoodChanged?.Invoke(playerFoodPoints);
    }

    /// <summary>
    /// Increments health points (used when player collects health items)
    /// </summary>
    public void IncrementHealthPoints(int amount)
    {
        playerHealthPoints += amount;
        OnHealthChanged?.Invoke(playerHealthPoints);
    }
}