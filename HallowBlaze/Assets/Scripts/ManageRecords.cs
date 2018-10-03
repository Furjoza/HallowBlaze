using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ManageRecords : MonoBehaviour {

    string highScores = string.Empty;
    string playerName = string.Empty;

    public Button uploadRecordButton;
    public InputField mainInputField;
    public Text uploadedRecordText;
    public Text resultsText;
    public Scrollbar scrollbar;
    
    //Dreamlo specific variables
    string webserviceURL = "http://dreamlo.com/lb/";
    public string privateCode = "EWoIz_OEmEKKefw2XQ49kge9Rh8wnyZE-WvSX9kxb0kA";
    public string publicCode = "5ba67a28613a880614fe3ace";

    public struct Score
    {
        public string playerName;
        public int score;
        public int seconds;
        public string shortText;
        public string dateString;
    }

    void Awake()
    {
        resultsText.text = string.Empty;
        LoadScores();

        //Adds a listener that invokes the "LockInput" method when the player finishes editing the main input field.
        //Passes the main input field into the method when "LockInput" is invoked.
        mainInputField.onEndEdit.AddListener(delegate { LockInput(mainInputField); });
    }

    //Checks if there is anything entered into the input field.
    void LockInput(InputField input)
    {
        if (input.text.Length > 3)
        {
            this.playerName = input.text;
            LoadSingleScore();
        }
    }

    public static double DateDiffInSeconds(System.DateTime now, System.DateTime olderdate)
    {
        var difference = now.Subtract(olderdate);
        return difference.TotalSeconds;
    }

    System.DateTime _lastRequest = System.DateTime.Now;
    int _requestTotal = 0;

    bool TooManyRequests()
    {
        var now = System.DateTime.Now;

        if (DateDiffInSeconds(now, _lastRequest) <= 2)
        {
            _lastRequest = now;
            _requestTotal++;
            if (_requestTotal > 3)
            {
                Debug.LogError("DREAMLO Too Many Requests. Am I inside an update loop?");
                return true;
            }
        }
        else
        {
            _lastRequest = now;
            _requestTotal = 0;
        }

        return false;
    }

    public void AddScore()
    {
        if (TooManyRequests()) return;

        StartCoroutine(AddScoreWithPipe(this.playerName, PlayerPrefs.GetInt("HighScore", 0)));
    }

    // This function saves a trip to the server. Adds the score and retrieves results in one trip.
    IEnumerator AddScoreWithPipe(string playerName, int totalScore)
    {
        playerName = Clean(playerName);
        WWW www = new WWW(webserviceURL + this.privateCode + "/add-pipe/" + WWW.EscapeURL(playerName) + "/" + totalScore.ToString());
        yield return www;

        highScores = www.text;
        LoadSingleScore();
        ListScores();
    }

    IEnumerator GetScores()
    {
        WWW www = new WWW(webserviceURL + publicCode + "/pipe");
        yield return www;

        highScores = www.text;
        ListScores();
    }

    public void ListScores()
    {
        resultsText.text = string.Empty;

        List<Score> resultList = ToListHighToLow();
        for (int x = 0; x < resultList.Count; x++)
        {
            resultsText.text += string.Format("{0} {1} {2} \n", (x + 1).ToString().PadRight(5), resultList[x].playerName.PadRight(9), resultList[x].score);
        }
    }

    IEnumerator GetSingleScore()
    {
        WWW www = new WWW(webserviceURL + publicCode + "/pipe-get/" + WWW.EscapeURL(this.playerName));
        yield return www;

        UpdateUploadedRecordText(www);
    }

    public void UpdateUploadedRecordText(WWW www)
    {
        string uploadedRecord = "0";

        if (www.text == string.Empty)
        {
            uploadedRecordText.text = string.Format("Player {0} has no uploaded record.", this.playerName);
        }
        else
        {
            uploadedRecord = www.text.Split('|')[1];
            uploadedRecordText.text = string.Format("Your uploaded record is {0} days.", uploadedRecord);
        }

        if (int.Parse(uploadedRecord) < PlayerPrefs.GetInt("HighScore", 0))
            uploadRecordButton.gameObject.SetActive(true);
    }

    public void LoadScores()
    {
        if (TooManyRequests()) return;
        StartCoroutine(GetScores());
    }

    public void LoadSingleScore()
    {
        if (TooManyRequests()) return;
        StartCoroutine(GetSingleScore());
    }

    public string[] ToStringArray()
    {
        if (this.highScores == null) return null;
        if (this.highScores == string.Empty) return null;

        string[] rows = this.highScores.Split(new char[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        return rows;
    }

    public List<Score> ToListLowToHigh()
    {
        Score[] scoreList = this.ToScoreArray();

        if (scoreList == null) return new List<Score>();

        List<Score> genericList = new List<Score>(scoreList);

        genericList.Sort((x, y) => x.score.CompareTo(y.score));

        return genericList;
    }

    public List<Score> ToListHighToLow()
    {
        Score[] scoreList = this.ToScoreArray();

        if (scoreList == null) return new List<Score>();

        List<Score> genericList = new List<Score>(scoreList);

        genericList.Sort((x, y) => y.score.CompareTo(x.score));

        return genericList;
    }

    public Score[] ToScoreArray()
    {
        string[] rows = ToStringArray();
        if (rows == null) return null;

        int rowcount = rows.Length;

        if (rowcount <= 0) return null;

        Score[] scoreList = new Score[rowcount];

        for (int i = 0; i < rowcount; i++)
        {
            string[] values = rows[i].Split(new char[] { '|' }, System.StringSplitOptions.None);

            Score current = new Score();
            current.playerName = values[0];
            current.score = 0;
            current.seconds = 0;
            current.shortText = string.Empty;
            current.dateString = string.Empty;
            if (values.Length > 1) current.score = CheckInt(values[1]);
            if (values.Length > 2) current.seconds = CheckInt(values[2]);
            if (values.Length > 3) current.shortText = values[3];
            if (values.Length > 4) current.dateString = values[4];
            scoreList[i] = current;
        }

        return scoreList;
    }

    // Keep pipe and slash out of names
    string Clean(string s)
    {
        s = s.Replace("/", string.Empty);
        s = s.Replace("|", string.Empty);

        return s;
    }

    int CheckInt(string s)
    {
        int x = 0;

        int.TryParse(s, out x);

        return x;
    }
}
