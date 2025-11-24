using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces; 
using TMPro;
using UnityEngine.UI;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AvampSupporterBoard : UdonSharpBehaviour
{
    [Header("--- AVAMP Configuration ---")]
    [Tooltip("The raw GitHub Pages URL to your supporters.json")]
    public VRCUrl dataUrl;
    [Tooltip("How often to check for updates (in seconds). Minimum: 60s")]
    public float refreshInterval = 300f;

    [Header("--- Visual Customization ---")]
    public string boardTitle = "Our Supporters";
    public Color headerColor = new Color(0.5f, 0f, 0.5f, 1f); // Purple default
    public Color textColor = Color.white;
    [Range(0f, 1f)]
    public float backgroundOpacity = 0.8f;

    [Header("--- Layout Settings ---")]
    [Tooltip("How many names to show before making a new page")]
    public int namesPerPage = 20;
    [Tooltip("How many seconds to stay on a page before scrolling")]
    public float pageDisplayTime = 10f;
    [Tooltip("Format: {0} is Name, {1} is Tier. Example: '{0} <color=#FFD700>[{1}]</color>'")]
    public string entryFormat = "{0} <color=#888888><size=80%>{1}</size></color>";
    
    [Header("--- UI References (Auto-Assigned if empty) ---")]
    [Tooltip("Main text area for the list")]
    public TextMeshProUGUI contentText;
    [Tooltip("Small footer for status/page numbers")]
    public TextMeshProUGUI statusText;
    [Tooltip("Header text component (Optional)")]
    public TextMeshProUGUI headerText;
    [Tooltip("Background Image component (Optional)")]
    public Image backgroundImage;

    // --- Internal State ---
    private string[] _formattedPages; 
    private int _totalPages = 0;
    private int _currentPage = 0;
    private float _timeSinceLastScroll = 0f;
    private float _timeSinceLastFetch = 0f;
    private bool _hasData = false;
    private bool _isLoading = false;
    private const float MIN_REFRESH_INTERVAL = 60f;

    void Start()
    {
        // 1. DEBUG: Tell us exactly what the URL is seeing
        if (dataUrl != null)
        {
            Debug.Log($"[AVAMP] Start() - URL Object exists. String value: '{dataUrl.Get()}'");
        }
        else
        {
            Debug.LogError("[AVAMP] Start() - URL Object is NULL!");
        }

        ValidateConfiguration();
        ApplyVisuals();
        
        // 2. Validate URL before trying to load
        string urlStr = dataUrl.Get();
        if (!Utilities.IsValid(dataUrl) || string.IsNullOrEmpty(urlStr) || urlStr == "''")
        {
            Debug.LogError("[AVAMP] CONFIG ERROR: URL is empty or invalid.");
            SetStatus("Config Error: Check URL");
        }
        else
        {
            LoadData();
        }
    }

    void Update()
    {
        if (!_hasData) return;

        // Auto-Scroll Logic
        if (_totalPages > 1)
        {
            _timeSinceLastScroll += Time.deltaTime;
            if (_timeSinceLastScroll >= pageDisplayTime)
            {
                NextPage();
            }
        }

        // Auto-Refresh Logic
        _timeSinceLastFetch += Time.deltaTime;
        float safeInterval = Mathf.Max(refreshInterval, MIN_REFRESH_INTERVAL);
        
        if (_timeSinceLastFetch >= safeInterval && !_isLoading)
        {
            _timeSinceLastFetch = 0f;
            LoadData();
        }
    }

    public override void Interact()
    {
        // Click to force update or change page
        if (_hasData && _totalPages > 1)
        {
            NextPage();
        }
        else if (!_hasData && !_isLoading)
        {
            LoadData();
        }
    }

    public void LoadData()
    {
        if (_isLoading) return;
        
        _isLoading = true;
        SetStatus("Syncing...");
        Debug.Log($"[AVAMP] Attempting download from: {dataUrl.Get()}");
        
        // Explicit Cast to IUdonEventReceiver
        VRCStringDownloader.LoadUrl(dataUrl, (IUdonEventReceiver)this);
    }

    // --- VRChat Event: Success ---
    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _isLoading = false;
        Debug.Log("[AVAMP] Success! Data received.");
        ParseAndOptimizeData(result.Result);
    }

    // --- VRChat Event: Error ---
    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _isLoading = false;
        // FIX: Removed invalid 'ErrorMessage' property
        Debug.LogError($"[AVAMP] Download FAILED. Error: {result.Error}");
        
        if (_hasData) SetStatus($"Sync Failed (Retrying...)");
        else SetStatus($"Connection Error: {result.Error}");
    }

    private void ParseAndOptimizeData(string json)
    {
        // Safety check for empty JSON
        if (string.IsNullOrEmpty(json)) {
             HandleParseError("Empty Response"); 
             return; 
        }

        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data))
        {
            HandleParseError("Invalid JSON Format");
            return;
        }

        if (data.TokenType != TokenType.DataDictionary)
        {
            HandleParseError("Root is not a Dictionary { }");
            return;
        }

        DataDictionary root = data.DataDictionary;
        
        if (root.ContainsKey("supporters"))
        {
            DataList supporters = root["supporters"].DataList;
            ProcessSupportersList(supporters);
        }
        else
        {
            HandleParseError("Missing 'supporters' key in JSON");
        }
    }

    private void ProcessSupportersList(DataList supporters)
    {
        if (supporters.Count == 0)
        {
            SetStatus("No supporters yet!");
            if (contentText != null) contentText.text = "Be the first supporter!";
            return;
        }

        int count = supporters.Count;
        _totalPages = Mathf.CeilToInt((float)count / namesPerPage);
        _formattedPages = new string[_totalPages];

        int currentSupporterIndex = 0;

        for (int pageIndex = 0; pageIndex < _totalPages; pageIndex++)
        {
            string pageContent = "";

            for (int i = 0; i < namesPerPage; i++)
            {
                if (currentSupporterIndex >= count) break;

                DataToken item = supporters[currentSupporterIndex];
                string entry = FormatSupporterEntry(item);
                
                pageContent += entry + "\n";
                currentSupporterIndex++;
            }

            _formattedPages[pageIndex] = pageContent;
        }

        _hasData = true;
        _currentPage = 0;
        UpdateDisplay();
        Debug.Log($"[AVAMP] Processed {count} supporters into {_totalPages} pages.");
    }

    private string FormatSupporterEntry(DataToken token)
    {
        // Handle simple list ["Name", "Name"]
        if (token.TokenType == TokenType.String)
        {
            return token.String;
        }
        // Handle complex list [{"name": "Name", "tier": "Gold"}]
        else if (token.TokenType == TokenType.DataDictionary)
        {
            DataDictionary dict = token.DataDictionary;
            string name = dict.ContainsKey("name") ? dict["name"].String : "Unknown";
            string tier = dict.ContainsKey("tier") ? dict["tier"].String : "";
            
            if (!string.IsNullOrEmpty(tier))
            {
                string formatted = entryFormat;
                // Simple manual replace for speed
                string result = formatted.Replace("{0}", name);
                result = result.Replace("{1}", tier);
                return result;
            }
            else
            {
                return name;
            }
        }
        return "Invalid Entry";
    }

    private void HandleParseError(string reason)
    {
        Debug.LogError($"[AVAMP] Parse Error: {reason}");
        if (!_hasData) SetStatus("Data Error (Check Console)");
    }

    private void NextPage()
    {
        _timeSinceLastScroll = 0f;
        _currentPage++;
        if (_currentPage >= _totalPages) _currentPage = 0;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (!_hasData || _formattedPages == null || _formattedPages.Length == 0) return;

        if (contentText != null) contentText.text = _formattedPages[_currentPage];

        SetStatus(_totalPages > 1 
            ? $"Page {_currentPage + 1} / {_totalPages}" 
            : "Powered by AVAMP");
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    private void ValidateConfiguration()
    {
        // Try to find components if they weren't dragged in
        if (contentText == null) contentText = GetComponentInChildren<TextMeshProUGUI>();
        if (headerText == null && transform.Find("Header") != null) headerText = transform.Find("Header").GetComponent<TextMeshProUGUI>();
        if (backgroundImage == null) backgroundImage = GetComponentInChildren<Image>();
        
        if (namesPerPage < 1) namesPerPage = 20;
        if (pageDisplayTime < 1f) pageDisplayTime = 5f;
    }

    private void ApplyVisuals()
    {
        if (headerText != null)
        {
            headerText.color = headerColor;
            headerText.text = boardTitle;
        }
        if (contentText != null) contentText.color = textColor;
        if (backgroundImage != null)
        {
            Color bg = backgroundImage.color;
            bg.a = backgroundOpacity;
            backgroundImage.color = bg;
        }
    }
}