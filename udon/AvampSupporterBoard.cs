using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
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
    [Tooltip("Header text component")]
    public TextMeshProUGUI headerText;
    [Tooltip("Background Image component")]
    public Image backgroundImage;

    // --- Internal Optimization State ---
    // We pre-format pages into strings so we don't generate garbage during Update/Scroll
    private string[] _formattedPages; 
    private int _totalPages = 0;
    private int _currentPage = 0;
    
    // Timers
    private float _timeSinceLastScroll = 0f;
    private float _timeSinceLastFetch = 0f;
    
    // State flags
    private bool _hasData = false;
    private bool _isLoading = false;
    private const float MIN_REFRESH_INTERVAL = 60f;

    void Start()
    {
        // 1. Robust Initialization & Auto-Discovery
        ValidateConfiguration();
        ApplyVisuals();
        
        // 2. Initial Load
        if (Utilities.IsValid(dataUrl) && !string.IsNullOrEmpty(dataUrl.Url))
        {
            LoadData();
        }
        else
        {
            SetStatus("Configuration Error: No URL provided");
        }
    }

    void Update()
    {
        if (!_hasData) return;

        // Handle Auto-Scrolling
        if (_totalPages > 1)
        {
            _timeSinceLastScroll += Time.deltaTime;
            if (_timeSinceLastScroll >= pageDisplayTime)
            {
                NextPage();
            }
        }

        // Handle Auto-Refresh (Polling)
        _timeSinceLastFetch += Time.deltaTime;
        // Clamp refresh interval to prevent API spam
        float safeInterval = Mathf.Max(refreshInterval, MIN_REFRESH_INTERVAL);
        
        if (_timeSinceLastFetch >= safeInterval && !_isLoading)
        {
            _timeSinceLastFetch = 0f;
            LoadData();
        }
    }

    // Support manual interaction (clicking the board)
    public override void Interact()
    {
        if (_hasData && _totalPages > 1)
        {
            NextPage();
        }
        else if (!_hasData && !_isLoading)
        {
            // specific interaction behavior if empty: try forcing a reload
            LoadData();
        }
    }

    // --- Visuals ---
    public void ApplyVisuals()
    {
        if (headerText != null)
        {
            headerText.color = headerColor;
            // Only set title if we haven't loaded data yet (which appends count)
            if (!_hasData) headerText.text = boardTitle;
        }

        if (contentText != null)
        {
            contentText.color = textColor;
        }

        if (backgroundImage != null)
        {
            Color bg = backgroundImage.color;
            bg.a = backgroundOpacity;
            backgroundImage.color = bg;
        }
    }

    // --- Core Logic: Fetching ---
    public void LoadData()
    {
        if (_isLoading) return;
        
        _isLoading = true;
        SetStatus("Syncing...");
        
        // Udon's VRCStringDownloader is the bridge to the outside world
        VRCStringDownloader.LoadUrl(dataUrl, (UdonBehaviour)this);
    }

    // --- VRChat Events ---
    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _isLoading = false;
        Debug.Log("[AVAMP] Data received successfully.");
        ParseAndOptimizeData(result.Result);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _isLoading = false;
        Debug.LogError($"[AVAMP] Download Error: {result.Error}");
        
        // If we already have data shown, don't clear it. Just warn in logs/footer.
        if (_hasData)
        {
            SetStatus($"Sync Failed (Retrying in {refreshInterval}s)");
        }
        else
        {
            SetStatus("Connection Failed. Click to Retry.");
        }
    }

    // --- Core Logic: Parsing & Optimization ---
    private void ParseAndOptimizeData(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data))
        {
            HandleParseError("Invalid JSON");
            return;
        }

        // Expected Structure: { "supporters": [ { "name": "X", "tier": "Y" }, ... ], "total_supporters": 100 }
        if (data.TokenType != TokenType.DataDictionary)
        {
            HandleParseError("Root not Dictionary");
            return;
        }

        DataDictionary root = data.DataDictionary;
        
        // 1. Update Header Stats if available
        if (headerText != null && root.ContainsKey("total_supporters"))
        {
            double count = root["total_supporters"].Number; // VRCJson numbers are doubles
            headerText.text = $"{boardTitle} ({count})";
        }

        // 2. Parse Supporters List
        if (root.ContainsKey("supporters"))
        {
            DataList supporters = root["supporters"].DataList;
            ProcessSupportersList(supporters);
        }
        else
        {
            HandleParseError("No 'supporters' key");
        }
    }

    private void ProcessSupportersList(DataList supporters)
    {
        if (supporters.Count == 0)
        {
            SetStatus("No supporters yet!");
            if (contentText != null) contentText.text = "";
            return;
        }

        // Calculate pages needed
        int count = supporters.Count;
        _totalPages = Mathf.CeilToInt((float)count / namesPerPage);
        _formattedPages = new string[_totalPages];

        // Build pages efficiently
        // We do this ONCE per fetch, so scrolling is zero-allocation later
        int currentSupporterIndex = 0;

        for (int pageIndex = 0; pageIndex < _totalPages; pageIndex++)
        {
            // Udon doesn't have StringBuilder, so we use string concat.
            // Since this happens only on load (every 5 mins), it's acceptable.
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

        // Success!
        _hasData = true;
        _currentPage = 0;
        UpdateDisplay();
    }

    private string FormatSupporterEntry(DataToken token)
    {
        // Handle both simple string array and object array
        if (token.TokenType == TokenType.String)
        {
            return token.String;
        }
        else if (token.TokenType == TokenType.DataDictionary)
        {
            DataDictionary dict = token.DataDictionary;
            string name = dict.ContainsKey("name") ? dict["name"].String : "Unknown";
            string tier = dict.ContainsKey("tier") ? dict["tier"].String : "";
            
            // Use the format string
            if (!string.IsNullOrEmpty(tier))
            {
                // Simplistic string.Format replacement for Udon
                string formatted = entryFormat.Replace("{0}", name).Replace("{1}", tier);
                return formatted;
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
        if (!_hasData) SetStatus("Data Error");
    }

    // --- Display Logic ---
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

        // 1. Update Content
        if (contentText != null)
        {
            contentText.text = _formattedPages[_currentPage];
        }

        // 2. Update Footer
        SetStatus(_totalPages > 1 
            ? $"Page {_currentPage + 1} / {_totalPages}" 
            : "Powered by AVAMP");
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    // --- Helpers ---
    private void ValidateConfiguration()
    {
        // Try to find components if not assigned (Quality of Life)
        if (contentText == null) contentText = GetComponentInChildren<TextMeshProUGUI>();
        if (headerText == null && transform.Find("Header") != null) headerText = transform.Find("Header").GetComponent<TextMeshProUGUI>();
        if (backgroundImage == null) backgroundImage = GetComponentInChildren<Image>();
        
        // If still null, we can't do much for content, but we can warn
        if (contentText == null) Debug.LogError("[AVAMP] Content Text (TMP) is missing!");
        
        // Ensure reasonable defaults
        if (namesPerPage < 1) namesPerPage = 20;
        if (pageDisplayTime < 1f) pageDisplayTime = 5f;
    }
}
