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
    [Tooltip("Layout Mode: 0=List (Group by Tier), 1=Grid (Group by Tier)")]
    public int layoutMode = 0;
    [Tooltip("How many columns to use in Grid Mode")]
    public int gridColumns = 2;
    [Tooltip("Horizontal spacing between columns (percentage, 25-40 recommended)")]
    [Range(15, 50)]
    public int gridColumnSpacing = 30;
    [Tooltip("How many names to show before making a new page")]
    public int namesPerPage = 20;
    [Tooltip("How many seconds to stay on a page before scrolling")]
    public float pageDisplayTime = 10f;
    [Tooltip("Max characters per column in grid mode (for alignment)")]
    public int gridColumnWidth = 20;
    [Tooltip("Format: {0} is Name. Tier is shown in Header.")]
    public string entryFormat = "{0}";
    
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

    // Data Cache
    private DataDictionary _tierGroups; // Maps TierName -> DataList of Names
    private string[] _tierNames; // Ordered list of tiers

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
        Debug.LogError($"[AVAMP] Download FAILED. Error: {result.Error}");
        
        if (_hasData) SetStatus($"Sync Failed (Retrying...)");
        else SetStatus($"Connection Error: {result.Error}");
    }

    private void ParseAndOptimizeData(string json)
    {
        if (string.IsNullOrEmpty(json)) { HandleParseError("Empty Response"); return; }

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

        // Apply board settings from JSON if available
        if (root.ContainsKey("board_settings"))
        {
            ApplyBoardSettingsFromJSON(root["board_settings"]);
        }

        if (root.ContainsKey("supporters"))
        {
            DataList supporters = root["supporters"].DataList;
            GroupSupportersByTier(supporters);
            BuildPages();
        }
        else
        {
            HandleParseError("Missing 'supporters' key in JSON");
        }
    }

    // Apply board settings from JSON (overrides Unity Inspector values)
    private void ApplyBoardSettingsFromJSON(DataToken settingsToken)
    {
        if (settingsToken.TokenType != TokenType.DataDictionary) return;

        DataDictionary settings = settingsToken.DataDictionary;

        // Update board title
        if (settings.ContainsKey("board_title"))
        {
            boardTitle = settings["board_title"].String;
        }

        // Update layout mode
        if (settings.ContainsKey("layout_mode"))
        {
            layoutMode = (int)settings["layout_mode"].Number;
        }

        // Update grid columns
        if (settings.ContainsKey("grid_columns"))
        {
            gridColumns = (int)settings["grid_columns"].Number;
        }

        // Update names per page
        if (settings.ContainsKey("names_per_page"))
        {
            namesPerPage = (int)settings["names_per_page"].Number;
        }

        // Update page display time
        if (settings.ContainsKey("page_display_time"))
        {
            pageDisplayTime = (float)settings["page_display_time"].Number;
        }

        // Update header color
        if (settings.ContainsKey("header_color"))
        {
            string hexColor = settings["header_color"].String;
            headerColor = HexToColor(hexColor);
        }

        // Update text color
        if (settings.ContainsKey("text_color"))
        {
            string hexColor = settings["text_color"].String;
            textColor = HexToColor(hexColor);
        }

        // Update background opacity
        if (settings.ContainsKey("bg_opacity"))
        {
            backgroundOpacity = (float)settings["bg_opacity"].Number;
        }

        // Update grid column spacing
        if (settings.ContainsKey("grid_column_spacing"))
        {
            gridColumnSpacing = (int)settings["grid_column_spacing"].Number;
        }

        // Re-apply visuals with updated settings
        ApplyVisuals();

        Debug.Log("[AVAMP] Board settings applied from JSON");
    }

    // 1. Group Logic: Convert flat list into Tier Groups
    private void GroupSupportersByTier(DataList supporters)
    {
        _tierGroups = new DataDictionary();
        _tierNames = new string[0]; // Reset

        for (int i = 0; i < supporters.Count; i++)
        {
            DataToken item = supporters[i];
            // We store the whole DataToken (dictionary) instead of just the string name
            // so we can access colors later during page build
            
            string tier = "Supporter";
            
            if (item.TokenType == TokenType.DataDictionary)
            {
                DataDictionary d = item.DataDictionary;
                if (d.ContainsKey("tier")) tier = d["tier"].String;
            }

            // Add to dictionary
            if (!_tierGroups.ContainsKey(tier))
            {
                _tierGroups[tier] = new DataList();
                
                string[] newKeys = new string[_tierNames.Length + 1];
                for(int k=0; k<_tierNames.Length; k++) newKeys[k] = _tierNames[k];
                newKeys[_tierNames.Length] = tier;
                _tierNames = newKeys;
            }

            DataList groupList = _tierGroups[tier].DataList;
            groupList.Add(item); // Store the full object/token
        }
    }

    private string GetNameFromToken(DataToken token)
    {
        if (token.TokenType == TokenType.String) return token.String;
        if (token.TokenType == TokenType.DataDictionary)
        {
            if (token.DataDictionary.ContainsKey("name")) return token.DataDictionary["name"].String;
        }
        return "Unknown";
    }

    private string ColorToHex(Color color)
    {
        int r = Mathf.RoundToInt(color.r * 255f);
        int g = Mathf.RoundToInt(color.g * 255f);
        int b = Mathf.RoundToInt(color.b * 255f);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private Color HexToColor(string hex)
    {
        // Remove # if present
        if (hex.StartsWith("#")) hex = hex.Substring(1);

        // Default to white if invalid
        if (hex.Length != 6) return Color.white;

        // Manually parse hex without try/catch (Udon doesn't support exceptions)
        int r = HexCharToInt(hex[0]) * 16 + HexCharToInt(hex[1]);
        int g = HexCharToInt(hex[2]) * 16 + HexCharToInt(hex[3]);
        int b = HexCharToInt(hex[4]) * 16 + HexCharToInt(hex[5]);

        // If any invalid characters, return white
        if (r < 0 || g < 0 || b < 0) return Color.white;

        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    private int HexCharToInt(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
        if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
        return -1; // Invalid character
    }

    // Truncate text to max width for grid mode
    private string TruncateText(string text, int maxWidth)
    {
        if (text.Length > maxWidth)
        {
            return text.Substring(0, maxWidth - 1) + "…";
        }
        return text;
    }

    // Get horizontal position tag for a column (uses percentage of width)
    private string GetColumnPosition(int columnIndex, int totalColumns)
    {
        if (columnIndex == 0) return ""; // First column starts at natural position

        // Use gridColumnSpacing to control distance between columns
        float percentage = columnIndex * gridColumnSpacing;
        return $"<pos={percentage:F0}%>";
    }

    private string GetColorFromToken(DataToken token)
    {
        // Default global text color
        string def = ColorToHex(textColor);

        if (token.TokenType == TokenType.DataDictionary)
        {
            if (token.DataDictionary.ContainsKey("name_color")) return token.DataDictionary["name_color"].String;
        }
        return def;
    }

    // 2. Page Building Logic: Construct string pages based on Layout Mode
    private void BuildPages()
    {
        if (layoutMode == 1 && gridColumns > 1)
        {
            BuildGridPages();
        }
        else
        {
            BuildListPages();
        }
    }

    // LIST MODE: Vertical stacking by tier
    private void BuildListPages()
    {
        string[] tempPages = new string[50];
        int pageCount = 0;
        string currentPageContent = "";
        int linesInCurrentPage = 0;

        for (int t = 0; t < _tierNames.Length; t++)
        {
            string tierName = _tierNames[t];
            DataList members = _tierGroups[tierName].DataList;

            // Get tier color from first member
            string hColor = ColorToHex(headerColor);
            if (members.Count > 0)
            {
                DataToken first = members[0];
                if (first.TokenType == TokenType.DataDictionary)
                {
                    DataDictionary fd = first.DataDictionary;
                    if (fd.ContainsKey("tier_color")) hColor = fd["tier_color"].String;
                }
            }

            string headerLine = $"<size=120%><b><color={hColor}>{tierName}</color></b></size>\n";

            // Start new page if header doesn't fit
            if (linesInCurrentPage + 2 >= namesPerPage)
            {
                tempPages[pageCount] = currentPageContent;
                pageCount++;
                currentPageContent = "";
                linesInCurrentPage = 0;
            }

            currentPageContent += headerLine;
            linesInCurrentPage++;

            // Add members
            for (int m = 0; m < members.Count; m++)
            {
                if (linesInCurrentPage >= namesPerPage)
                {
                    tempPages[pageCount] = currentPageContent;
                    pageCount++;
                    currentPageContent = "";
                    linesInCurrentPage = 0;
                }

                string memberName = GetNameFromToken(members[m]);
                string memberColor = GetColorFromToken(members[m]);
                currentPageContent += $"<color={memberColor}>{memberName}</color>\n";
                linesInCurrentPage++;
            }

            // Add spacing between tiers
            currentPageContent += "\n";
            linesInCurrentPage++;
        }

        // Add final page
        if (!string.IsNullOrEmpty(currentPageContent))
        {
            tempPages[pageCount] = currentPageContent;
            pageCount++;
        }

        FinalizePages(tempPages, pageCount);
    }

    // GRID MODE: Interleaved row construction
    private void BuildGridPages()
    {
        string[] tempPages = new string[50];
        int pageCount = 0;
        string currentPageContent = "";
        int linesInCurrentPage = 0;

        // Process tiers in batches based on gridColumns
        int tiersToShow = Mathf.Min(gridColumns, _tierNames.Length);
        int tierBatchCount = Mathf.CeilToInt((float)_tierNames.Length / tiersToShow);

        for (int batch = 0; batch < tierBatchCount; batch++)
        {
            int startTier = batch * tiersToShow;
            int endTier = Mathf.Min(startTier + tiersToShow, _tierNames.Length);
            int actualColumns = endTier - startTier;

            // Collect tier data for this batch
            DataList[] tierMembers = new DataList[actualColumns];
            string[] tierColors = new string[actualColumns];
            int maxMembers = 0;

            for (int col = 0; col < actualColumns; col++)
            {
                int tierIndex = startTier + col;
                tierMembers[col] = _tierGroups[_tierNames[tierIndex]].DataList;

                // Get tier color
                tierColors[col] = ColorToHex(headerColor);
                if (tierMembers[col].Count > 0)
                {
                    DataToken first = tierMembers[col][0];
                    if (first.TokenType == TokenType.DataDictionary)
                    {
                        DataDictionary fd = first.DataDictionary;
                        if (fd.ContainsKey("tier_color")) tierColors[col] = fd["tier_color"].String;
                    }
                }

                if (tierMembers[col].Count > maxMembers)
                {
                    maxMembers = tierMembers[col].Count;
                }
            }

            // Check if we need a new page
            if (linesInCurrentPage + maxMembers + 2 >= namesPerPage)
            {
                if (!string.IsNullOrEmpty(currentPageContent))
                {
                    tempPages[pageCount] = currentPageContent;
                    pageCount++;
                    currentPageContent = "";
                    linesInCurrentPage = 0;
                }
            }

            // ROW 1: Tier headers (side by side) using <pos> tags for alignment
            string headerRow = "";
            for (int col = 0; col < actualColumns; col++)
            {
                int tierIndex = startTier + col;
                string tierName = TruncateText(_tierNames[tierIndex], gridColumnWidth);

                headerRow += GetColumnPosition(col, actualColumns);
                headerRow += $"<b><color={tierColors[col]}>{tierName}</color></b>";
            }
            currentPageContent += headerRow + "\n";
            linesInCurrentPage++;

            // ROW 2+: Member rows (interleaved)
            for (int row = 0; row < maxMembers; row++)
            {
                if (linesInCurrentPage >= namesPerPage)
                {
                    tempPages[pageCount] = currentPageContent;
                    pageCount++;
                    currentPageContent = "";
                    linesInCurrentPage = 0;
                }

                string memberRow = "";
                for (int col = 0; col < actualColumns; col++)
                {
                    memberRow += GetColumnPosition(col, actualColumns);

                    if (row < tierMembers[col].Count)
                    {
                        string memberName = TruncateText(GetNameFromToken(tierMembers[col][row]), gridColumnWidth);
                        string memberColor = GetColorFromToken(tierMembers[col][row]);
                        memberRow += $"<color={memberColor}>{memberName}</color>";
                    }
                    // No else needed - empty cells just skip to next column position
                }
                currentPageContent += memberRow + "\n";
                linesInCurrentPage++;
            }

            // Add spacing between tier batches
            currentPageContent += "\n";
            linesInCurrentPage++;
        }

        // Add final page
        if (!string.IsNullOrEmpty(currentPageContent))
        {
            tempPages[pageCount] = currentPageContent;
            pageCount++;
        }

        FinalizePages(tempPages, pageCount);
    }

    private void FinalizePages(string[] tempPages, int pageCount)
    {
        _totalPages = pageCount;
        _formattedPages = new string[_totalPages];
        for (int i = 0; i < _totalPages; i++)
        {
            _formattedPages[i] = tempPages[i];
        }

        _hasData = true;
        _currentPage = 0;
        UpdateDisplay();
        Debug.Log($"[AVAMP] Built {_totalPages} pages from {_tierNames.Length} tiers.");
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

        if (contentText != null)
        {
            // For grid mode, prepend <align=left> tag to force left alignment (required for <pos> tags)
            // For list mode, don't add alignment tag (uses Unity Inspector setting)
            string content = _formattedPages[_currentPage];
            if (layoutMode == 1)
            {
                content = "<align=left>" + content;
            }
            contentText.text = content;
        }

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
}
