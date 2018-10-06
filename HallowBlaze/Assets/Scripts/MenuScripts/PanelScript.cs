using UnityEngine;
using UnityEngine.EventSystems;

public class PanelScript : MonoBehaviour {

    public EventSystem eventSystem;
    public GameObject selectedObject;

    private bool buttonSelected;

    void Update()
    {
        //if (Input.GetAxisRaw("Vertical") != 0 && buttonSelected == false)
        if (buttonSelected == false)
        {
            eventSystem.SetSelectedGameObject(selectedObject);
            buttonSelected = true;
        }
    }

    private void OnDisable()
    {
        buttonSelected = false;
    }
}
