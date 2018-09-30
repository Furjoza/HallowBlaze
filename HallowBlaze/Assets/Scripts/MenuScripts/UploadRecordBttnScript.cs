using UnityEngine;
using UnityEngine.UI;

public class UploadRecordBttnScript : MonoBehaviour {

    public Text localRecordText;

    public void SetLocalResult()
    {
        int score = PlayerPrefs.GetInt("HighScore", 0);
        localRecordText.text = string.Format("Your local record is {0} days.", score);
    }
}
