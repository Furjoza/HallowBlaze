using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class PauseMenuController : MonoBehaviour
{
    public enum PauseViewState
    {
        Closed,
        PauseRoot,
        Settings
    }

    [SerializeField] private GameObject pauseRoot;
    [SerializeField] private GameObject settingsRoot;
    [SerializeField] private GameObject resumeButton;
    [SerializeField] private GameObject settingsBackButton;
    [SerializeField] private string gameplaySceneName = "Main";
    [SerializeField] private string menuSceneName = "Menu";

    private PauseViewState currentState = PauseViewState.Closed;
    private bool isExiting;
    private Action<string> sceneLoader = SceneManager.LoadScene;

    public PauseViewState CurrentState
    {
        get { return currentState; }
    }

    private void Awake()
    {
        SetPanels(false, false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            HandleEscape();
    }

    private void HandleEscape()
    {
        if (currentState == PauseViewState.Settings)
            BackFromSettings();
        else if (currentState == PauseViewState.PauseRoot)
            Resume();
        else
            OpenPause();
    }

    public void OpenPause()
    {
        if (!CanOpenPause())
            return;

        GameManager.instance.SetGameplayInputBlocked(true);
        Time.timeScale = 0f;
        currentState = PauseViewState.PauseRoot;
        SetPanels(true, false);
        Select(resumeButton);
    }

    public void Resume()
    {
        if (isExiting || currentState == PauseViewState.Closed)
            return;

        RestoreRuntimeState();
        currentState = PauseViewState.Closed;
        SetPanels(false, false);
    }

    public void OpenSettings()
    {
        if (isExiting || currentState != PauseViewState.PauseRoot)
            return;

        currentState = PauseViewState.Settings;
        SetPanels(false, true);
        Select(settingsBackButton);
    }

    public void BackFromSettings()
    {
        if (isExiting || currentState != PauseViewState.Settings)
            return;

        currentState = PauseViewState.PauseRoot;
        SetPanels(true, false);
        Select(resumeButton);
    }

    public void ExitToMenu()
    {
        if (isExiting)
            return;

        isExiting = true;

        if (GameManager.instance != null && !GameManager.instance.ExitToMenu())
        {
            isExiting = false;
            return;
        }

        RestoreRuntimeState();
        sceneLoader(menuSceneName);
    }

    private bool CanOpenPause()
    {
        return !isExiting
            && SceneManager.GetActiveScene().name == gameplaySceneName
            && GameManager.instance != null
            && GameManager.instance.CanPauseGameplay;
    }

    private void RestoreRuntimeState()
    {
        Time.timeScale = 1f;

        if (GameManager.instance != null)
            GameManager.instance.SetGameplayInputBlocked(false);
    }

    private void SetPanels(bool showPauseRoot, bool showSettings)
    {
        if (pauseRoot != null)
            pauseRoot.SetActive(showPauseRoot);
        if (settingsRoot != null)
            settingsRoot.SetActive(showSettings);
    }

    private static void Select(GameObject target)
    {
        if (EventSystem.current == null)
            return;

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(target);
    }

    private void OnDisable()
    {
        RestoreRuntimeState();
    }

    private void OnDestroy()
    {
        RestoreRuntimeState();
    }
}