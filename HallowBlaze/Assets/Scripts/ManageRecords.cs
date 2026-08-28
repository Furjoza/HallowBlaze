using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ManageRecords : MonoBehaviour {
    
    public Button uploadRecordButton;
    public InputField mainInputField;
    public Text uploadedRecordText;
    public Text resultsText;
    public Scrollbar scrollbar;

    void Awake()
    {
        // Ensure components are not null
        if (resultsText != null)
        {
            resultsText.text = string.Empty;
        }
        
        // Load local scores or set default message
        LoadScores();
    }

    void OnEnable()
    {
        // Register input field listener - only once
        if (mainInputField != null)
        {
            mainInputField.onEndEdit.AddListener(HandlePlayerNameEntry);
        }
    }

    void OnDisable()
    {
        // Remove input field listener
        if (mainInputField != null)
        {
            mainInputField.onEndEdit.RemoveListener(HandlePlayerNameEntry);
        }
    }

    void HandlePlayerNameEntry(string playerName)
    {
        // Safely handle null input
        if (string.IsNullOrEmpty(playerName))
        {
            playerName = "Anonymous";
        }

        // Clean the player name - remove / and | characters
        string cleanName = Clean(playerName);

        // Show local high score (from PlayerPrefs)
        int highScore = PlayerPrefs.GetInt("HighScore", 0);
        if (uploadedRecordText != null)
        {
            uploadedRecordText.text = string.Format("Player {0}: your local record is {1}.", cleanName, highScore);
        }
        
        // Show offline message with cleaned name
        if (resultsText != null)
        {
            resultsText.text = $"Leaderboard unavailable in offline mode. Your clean name: {cleanName}.";
        }
    }

    public bool IsUploadAvailable()
    {
        return false; // Always return false since we're in offline mode
    }

    public void AddScore()
    {
        // No-op for offline mode - no upload possible
    }

    public void LoadScores()
    {
        // Show offline message
        if (resultsText != null)
        {
            resultsText.text = "Leaderboard unavailable in offline mode.";
        }
        
        // Disable upload button
        if (uploadRecordButton != null)
        {
            uploadRecordButton.gameObject.SetActive(false);
        }
    }

    public void LoadSingleScore()
    {
        // Show local high score
        int highScore = PlayerPrefs.GetInt("HighScore", 0);
        if (uploadedRecordText != null)
        {
            uploadedRecordText.text = string.Format("Your local record is {0}.", highScore);
        }
    }

    // Keep pipe and slash out of names
    string Clean(string s)
    {
        s = s.Replace("/", string.Empty);
        s = s.Replace("|", string.Empty);

        // If result is empty or whitespace, use "Anonymous"
        if (string.IsNullOrWhiteSpace(s))
        {
            return "Anonymous";
        }

        return s;
    }
}