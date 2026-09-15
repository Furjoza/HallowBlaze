using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using HallowBlaze.Core.Persistence;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;

public class StartBttnScript : MonoBehaviour
{
    [SerializeField] private bool continueButton;

    private void OnEnable()
    {
        if (continueButton)
            RefreshContinueAvailability();
    }

    public void LoadByName(string sceneName)
    {
        GameManager.RequestNewRun();
        SceneManager.LoadScene(sceneName);
    }

    public void ContinueByName(string sceneName)
    {
        SaveStoreResult<RunState> result =
            RunLifecycleService.InspectContinue(CreateSaveStore());
        if (!result.IsSuccess)
            return;

        GameManager.RequestContinue();
        SceneManager.LoadScene(sceneName);
    }

    public void RefreshContinueAvailability()
    {
        Button button = GetComponent<Button>();
        if (button == null)
            return;

        SaveStoreResult<RunState> result =
            RunLifecycleService.InspectContinue(CreateSaveStore());
        button.interactable = result.IsSuccess;

        Text label = GetComponentInChildren<Text>(true);
        if (label == null)
            return;

        if (result.Type == SaveStoreResultType.Corrupt)
            label.text = "Continue (save damaged)";
        else if (result.Type == SaveStoreResultType.UnsupportedFutureSchema)
            label.text = "Continue (incompatible save)";
        else if (result.Type == SaveStoreResultType.IoError)
            label.text = "Continue (save unavailable)";
        else
            label.text = "Continue";
    }

    private static ISaveStore CreateSaveStore()
    {
        return new FileSystemSaveStore(GameManager.GetPersistenceRoot());
    }
}