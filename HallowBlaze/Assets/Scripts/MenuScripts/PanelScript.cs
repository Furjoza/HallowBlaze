using UnityEngine;
using UnityEngine.EventSystems;

public class PanelScript : MonoBehaviour {

    public EventSystem eventSystem;
    public GameObject selectedObject;

    private bool buttonSelected;

    void Update()
    {
        if (buttonSelected || selectedObject == null)
            return;

        EventSystem activeEventSystem = eventSystem != null ? eventSystem : EventSystem.current;
        if (activeEventSystem == null)
            return;

        activeEventSystem.SetSelectedGameObject(selectedObject);
        buttonSelected = true;
    }

    private void OnDisable()
    {
        buttonSelected = false;
    }
}
