using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using HallowBlaze.Presentation.Runtime;

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
    private KeyboardControlsView controls;
    private bool controlsInstalled;

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
        if (Input.GetKeyDown(PcInputBindings.Cancel))
            HandleEscape();
    }

    private void HandleEscape()
    {
        if (KeyboardControlsView.ClosedFrame == Time.frameCount) return;
        if (controls != null && controls.IsOpen) { controls.Close(); return; }
        if (currentState == PauseViewState.Settings)
            BackFromSettings();
        else if (currentState == PauseViewState.PauseRoot)
            Resume();
        else
            OpenPause();
    }

    /// <summary>Opens the pause menu with visible keyboard focus and blocks gameplay until an explicit resume.</summary>
    public void OpenPause()
    {
        if (!CanOpenPause())
            return;

        GameManager.instance.SetGameplayInputBlocked(true);
        Time.timeScale = 0f;
        currentState = PauseViewState.PauseRoot;
        SetPanels(true, false);
        if (!controlsInstalled && pauseRoot != null)
        {
            KeyboardControlsView.InstallEntry(pauseRoot, OpenControls);
            controlsInstalled = true;
        }
        Select(resumeButton);
        KeyboardMenuFocus.Attach(pauseRoot, resumeButton);
    }

    /// <summary>Closes pause and its read-only help; gameplay input remains deferred through the closing frame.</summary>
    public void Resume()
    {
        if (isExiting || currentState == PauseViewState.Closed)
            return;
        if (controls != null && controls.IsOpen) controls.Close();

        RestoreRuntimeState();
        currentState = PauseViewState.Closed;
        SetPanels(false, false);
    }

    /// <summary>Opens settings only from the pause root, without stealing focus from an open Controls modal.</summary>
    public void OpenSettings()
    {
        if (isExiting || currentState != PauseViewState.PauseRoot)
            return;
        if (controls != null && controls.IsOpen) return;

        currentState = PauseViewState.Settings;
        SetPanels(false, true);
        Select(settingsBackButton);
        KeyboardMenuFocus.Attach(settingsRoot, settingsBackButton);
    }

    /// <summary>Shows read-only Controls while retaining pause and its gameplay block; closing help never resumes the run.</summary>
    public void OpenControls()
    {
        if (isExiting || currentState != PauseViewState.PauseRoot || !isActiveAndEnabled) return;
        controls = KeyboardControlsView.Open(pauseRoot.transform);
    }

    public void BackFromSettings()
    {
        if (isExiting || currentState != PauseViewState.Settings)
            return;

        currentState = PauseViewState.PauseRoot;
        SetPanels(true, false);
        KeyboardMenuFocus.Attach(pauseRoot, resumeButton).RecoverFocus();
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
        if (controls != null && controls.IsOpen) controls.Close();
        currentState = PauseViewState.Closed;
        SetPanels(false, false);
        RestoreRuntimeState();
    }

    private void OnDestroy()
    {
        OnDisable();
    }
}