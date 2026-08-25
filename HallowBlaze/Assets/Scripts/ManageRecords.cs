// =====================================================================
// SECURITY NOTICE: Dreamlo integration has been secured
// The private code was removed from source to prevent exposure
// In production, this should be loaded from external configuration
// This implementation maintains leaderboard viewing functionality while
// disabling upload capabilities when no private code is configured.
// =====================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ManageRecords : MonoBehaviour {
    
    // Security: Method to check if upload functionality is available
    public bool IsUploadAvailable()
    {
        return !string.IsNullOrEmpty(privateCode);
    }

    string highScores = string.Empty;
    string playerName = string.Empty;

    public Button uploadRecordButton;
    public InputField mainInputField;
    public Text uploadedRecordText;
    public Text resultsText;
    public Scrollbar scrollbar;

    //Dreamlo specific variables
    readonly string webserviceURL = "https://dreamlo.com/lb/";
    public string privateCode = "";
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
        
        // Security: If privateCode is not set, provide warning but still allow viewing
        if (string.IsNullOrEmpty(privateCode))
        {
            Debug.LogWarning("Dreamlo integration disabled - no private code configured. Leaderboard viewing only.");
        }
        
        LoadScores();
    }

    //Checks if there is anything entered into the input field.
    void LockInput(InputField input)
    {
        if (input.text.Length > 3)
        {
            this.playerName = input.text;
            
            // Security: If privateCode is not set, only load public scores for viewing
            if (!string.IsNullOrEmpty(privateCode))
            {
                LoadSingleScore();
            }
            else
            {
                Debug.LogWarning("Dreamlo upload disabled - no private code configured");
                // Show that there's no uploaded record (since we can't check)
                uploadedRecordText.text = string.Format("Player {0} has no uploaded record.", this.playerName);
            }
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
        // Security: If privateCode is not set, disable upload functionality
        if (string.IsNullOrEmpty(privateCode) || TooManyRequests()) return;

        StartCoroutine(AddScoreWithPipe(this.playerName, PlayerPrefs.GetInt("HighScore", 0)));
    }

    void OnEnable()
    {
        // Security: If privateCode is not set, only load scores without upload functionality
        if (!string.IsNullOrEmpty(privateCode))
        {
            StartCoroutine(LoadScoresSafe());
        }
        else
        {
            Debug.LogWarning("Dreamlo upload disabled - no private code configured");
            // Still attempt to load public scores for viewing
            LoadScores();
        }
    }

    IEnumerator LoadScoresSafe()
    {
        yield return null;
        if (resultsText != null)
            resultsText.text = string.Empty;
        LoadScores();
    }

    // This function saves a trip to the server. Adds the score and retrieves results in one trip.
    IEnumerator AddScoreWithPipe(string playerName, int totalScore)
    {
        // Security: If privateCode is not set, disable upload functionality
        if (string.IsNullOrEmpty(privateCode))
        {
            Debug.LogWarning("Dreamlo upload disabled - no private code configured");
            yield break;
        }
        
        playerName = Clean(playerName);
        string url = webserviceURL + this.privateCode + "/add-pipe/" + UnityWebRequest.EscapeURL(playerName) + "/" + totalScore.ToString();
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Leaderboard request failed: " + request.error);
                yield break;
            }

            highScores = request.downloadHandler.text;
            LoadSingleScore();
            ListScores();
        }
    }

    IEnumerator GetScores()
    {
        using (UnityWebRequest request = UnityWebRequest.Get(webserviceURL + publicCode + "/pipe"))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Leaderboard request failed: " + request.error);
                yield break;
            }

            highScores = request.downloadHandler.text;
            ListScores();
        }
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
        string url = webserviceURL + publicCode + "/pipe-get/" + UnityWebRequest.EscapeURL(this.playerName);
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Leaderboard request failed: " + request.error);
                yield break;
            }

            UpdateUploadedRecordText(request.downloadHandler.text);
        }
    }

    public void UpdateUploadedRecordText(string responseText)
    {
        string uploadedRecord = "0";

        if (responseText == string.Empty)
        {
            uploadedRecordText.text = string.Format("Player {0} has no uploaded record.", this.playerName);
        }
        else
        {
            uploadedRecord = responseText.Split('|')[1];
            uploadedRecordText.text = string.Format("Your uploaded record is {0} days.", uploadedRecord);
        }

        // Security: Disable upload button if privateCode is not set
        if (string.IsNullOrEmpty(privateCode))
        {
            uploadRecordButton.gameObject.SetActive(false);
        }
        else if (int.Parse(uploadedRecord) < PlayerPrefs.GetInt("HighScore", 0))
        {
            uploadRecordButton.gameObject.SetActive(true);
        }
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
        int.TryParse(s, out int x);

        return x;
    }
}
        if (string.IsNullOrEmpty(privateCode))
        {
            Debug.LogWarning("Dreamlo integration disabled - no private code configured. Leaderboard viewing only.");
        }
        
        LoadScores();

    //Checks if there is anything entered into the input field.
    void LockInput(InputField input)
    {
        if (input.text.Length > 3)
        {
            this.playerName = input.text;
            
            // Security: If privateCode is not set, only load public scores for viewing
            if (!string.IsNullOrEmpty(privateCode))
            {
                LoadSingleScore();
            }
            else
            {
                Debug.LogWarning("Dreamlo upload disabled - no private code configured");
                // Show that there's no uploaded record (since we can't check)
                uploadedRecordText.text = string.Format("Player {0} has no uploaded record.", this.playerName);
            }
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
        // Security: If privateCode is not set, disable upload functionality
        if (string.IsNullOrEmpty(privateCode) || TooManyRequests()) return;

        StartCoroutine(AddScoreWithPipe(this.playerName, PlayerPrefs.GetInt("HighScore", 0)));
    }

    void OnEnable()
    {
        // Security: If privateCode is not set, only load scores without upload functionality
        if (!string.IsNullOrEmpty(privateCode))
        {
            StartCoroutine(LoadScoresSafe());
        }
        else
        {
            Debug.LogWarning("Dreamlo upload disabled - no private code configured");
            // Still attempt to load public scores for viewing
            LoadScores();
        }
    }

    IEnumerator LoadScoresSafe()
    {
        yield return null;
        if (resultsText != null)
            resultsText.text = string.Empty;
        LoadScores();
    }

    // This function saves a trip to the server. Adds the score and retrieves results in one trip.
    IEnumerator AddScoreWithPipe(string playerName, int totalScore)
    {
        // Security: If privateCode is not set, disable upload functionality
        if (string.IsNullOrEmpty(privateCode))
        {
            Debug.LogWarning("Dreamlo upload disabled - no private code configured");
            yield break;
        }
        
        playerName = Clean(playerName);
        string url = webserviceURL + this.privateCode + "/add-pipe/" + UnityWebRequest.EscapeURL(playerName) + "/" + totalScore.ToString();
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Leaderboard request failed: " + request.error);
                yield break;
            }

            highScores = request.downloadHandler.text;
            LoadSingleScore();
            ListScores();
        }
    }

    IEnumerator GetScores()
    {
        using (UnityWebRequest request = UnityWebRequest.Get(webserviceURL + publicCode + "/pipe"))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Leaderboard request failed: " + request.error);
                yield break;
            }

            highScores = request.downloadHandler.text;
            ListScores();
        }
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
        string url = webserviceURL + publicCode + "/pipe-get/" + UnityWebRequest.EscapeURL(this.playerName);
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Leaderboard request failed: " + request.error);
                yield break;
            }

            UpdateUploadedRecordText(request.downloadHandler.text);
        }
    }

    public void UpdateUploadedRecordText(string responseText)
    {
        string uploadedRecord = "0";

        if (responseText == string.Empty)
        {
            uploadedRecordText.text = string.Format("Player {0} has no uploaded record.", this.playerName);
        }
        else
        {
            uploadedRecord = responseText.Split('|')[1];
            uploadedRecordText.text = string.Format("Your uploaded record is {0} days.", uploadedRecord);
        }

        // Security: Disable upload button if privateCode is not set
        if (string.IsNullOrEmpty(privateCode))
        {
            uploadRecordButton.gameObject.SetActive(false);
        }
        else if (int.Parse(uploadedRecord) < PlayerPrefs.GetInt("HighScore", 0))
        {
            uploadRecordButton.gameObject.SetActive(true);
        }
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
        int.TryParse(s, out int x);

        return x;
    }
}
