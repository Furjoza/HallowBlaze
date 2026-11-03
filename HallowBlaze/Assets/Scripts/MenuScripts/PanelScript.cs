using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using HallowBlaze.Presentation.Runtime;

public class PanelScript : MonoBehaviour {

    public EventSystem eventSystem;
    public GameObject selectedObject;

    private KeyboardMenuFocus keyboardFocus;

    void Update()
    {
        if (gameObject.scene.name != "Menu")
            return;
        if (keyboardFocus == null)
            keyboardFocus = KeyboardMenuFocus.Attach(gameObject, selectedObject, Back);
        else if (!keyboardFocus.enabled)
            keyboardFocus.enabled = true;
    }

    private void OnDisable()
    {
        if (keyboardFocus != null) keyboardFocus.enabled = false;
    }

    private void Back()
    {
        foreach (Button button in GetComponentsInChildren<Button>())
            if (button.name == "BackBttn" && button.IsInteractable())
            {
                button.onClick.Invoke();
                return;
            }
    }
}
