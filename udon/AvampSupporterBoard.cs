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
    [Header("--- CACHE BUSTER SETUP ---")]
    [Tooltip("PASTE YOUR URL HERE. Then look for the 'GENERATE LINKS' button at the bottom of this component.")]
    public string sourceUrl = "https://your-name.github.io/supporters.json";
    
    [Tooltip("The list of cache-busting URLs. Auto-filled by the Generator.")]
    public VRCUrl[] dataUrls;

    [Header("--- AVAMP Configuration ---")]
    [Tooltip("How often to check for updates (in seconds). Minimum: 60s")]
    public float refreshInterval = 300f;

    [Header("--- Visual Customization ---")]
    public string boardTitle = "Our Supporters";
    public Color headerColor = new Color(0.5f, 0f, 0.5f, 1f); // Purple default
    public Color textColor = Color.white;
    [Range(0f, 1f)]
    public float backgroundOpacity = 0.8f;
    
    [Header("--- SMART PAGING CALCULATOR ---")]
    [Tooltip("CRITICAL: Set this number to match the actual 'Font Size' of your ContentText object.\n\nSince Udon cannot read the font size automatically, this number is required to calculate how many names fit on a page.")]
    public float contentFontSize = 36f;

    [Header("--- Layout Settings ---")]
    [Tooltip("If true, ignores 'Names Per Page' and calculates it automatically based on the ContentText box height and the Content Font Size above.")]
    public bool smartPageSizing = true;
    
    [Tooltip("Layout Mode: 0=List (Group by Tier), 1=Grid (Group by Tier)")]
    public int layoutMode = 0;
    
    [Tooltip("How many columns to use in Grid Mode")]
    public int gridColumns = 2;
    
    [Tooltip("Horizontal spacing between columns (percentage, 25-40 recommended)")]
    [Range(15, 50)]
    public int gridColumnSpacing = 30;
    
    [Tooltip("Manual Override: How many lines to show (Only used if Smart Page Sizing is OFF)")]
    public int namesPerPage = 20;
    
    [Tooltip("How many seconds to stay on a page before scrolling")]
    public float pageDisplayTime = 10f;
    
    [Tooltip("Max characters per column in grid mode (for alignment)")]
    public int gridColumnWidth = 20;
    
    [Header("--- UI References ---")]
    public TextMeshProUGUI contentText;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI headerText;
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
    private DataDictionary _tierGroups; 
    private DataDictionary _tierColors; 
    private DataDictionary _tierNameColors; 
    private string[] _tierNames; 

    void Start()
    {
        ValidateConfiguration();
        ApplyVisuals();
        
        if (dataUrls == null || dataUrls.Length == 0)
        {
            Debug.LogError("[AVAMP] CONFIG ERROR: URL list is empty. Did you run the Generator?");
            SetStatus("Config Error: Run Generator");
        }
        else
        {
            LoadData();
        }
    }

    void Update()
    {
        // 1. Auto-Scroll Logic
        if (_hasData && _totalPages > 1)
        {
            _timeSinceLastScroll += Time.deltaTime;
            if (_timeSinceLastScroll >= pageDisplayTime)
            {
                NextPage();
            }
        }

        // 2. Auto-Refresh Logic
        _timeSinceLastFetch += Time.deltaTime;
        float safeInterval = Mathf.Max(refreshInterval, MIN_REFRESH_INTERVAL);
        
        if (_timeSinceLastFetch >= safeInterval && !_isLoading)
        {
            _timeSinceLastFetch = 0f;
            Debug.Log("[AVAMP] Auto-Refresh triggering...");
            LoadData();
        }
    }

    public override void Interact()
    {
        if (!_isLoading) LoadData();
        else if (_hasData && _totalPages > 1) NextPage();
    }

    public void LoadData()
    {
        if (_isLoading) return;
        if (dataUrls == null || dataUrls.Length == 0) return;

        // --- THE REVOLVER TRICK ---
        int randomIndex = UnityEngine.Random.Range(0, dataUrls.Length);
        VRCUrl selectedUrl = dataUrls[randomIndex];

        if (!Utilities.IsValid(selectedUrl)) selectedUrl = dataUrls[0]; 

        _isLoading = true;
        SetStatus($"Syncing...");
        Debug.Log($"[AVAMP] Requesting Source #{randomIndex}: {selectedUrl.Get()}");
        
        VRCStringDownloader.LoadUrl(selectedUrl, (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _isLoading = false;
        _timeSinceLastFetch = 0f; 
        Debug.Log("[AVAMP] Success! Data received.");
        ParseAndOptimizeData(result.Result);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _isLoading = false;
        Debug.LogError($"[AVAMP] Download FAILED. Error: {result.Error}");
        if (_hasData) SetStatus($"Sync Failed (Retrying...)");
        else SetStatus($"Connection Error: {result.Error}");
        
        SendCustomEventDelayedSeconds(nameof(LoadData), 10f);
    }

    private void ParseAndOptimizeData(string json)
    {
        if (string.IsNullOrEmpty(json)) { HandleParseError("Empty Response"); return; }
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data)) { HandleParseError("Invalid JSON Format"); return; }
        DataDictionary root = data.DataDictionary;

        // 1. Apply Settings
        if (root.ContainsKey("board_settings")) ApplyBoardSettingsFromJSON(root["board_settings"]);

        // 2. Parse Tier Definitions
        if (root.ContainsKey("tiers")) ParseTierDefinitions(root["tiers"].DataList);
        else { _tierColors = new DataDictionary(); _tierNameColors = new DataDictionary(); }

        // 3. Group Supporters
        if (root.ContainsKey("supporters")) {
            GroupSupportersByTier(root["supporters"].DataList);
            BuildPages();
        } else {
            HandleParseError("Missing 'supporters' key in JSON");
        }
    }

    private void CalculateSmartPageLimit()
    {
        if (!smartPageSizing || contentText == null) return;

        RectTransform rt = contentText.GetComponent<RectTransform>();
        if (rt == null) return;

        float containerHeight = rt.rect.height;
        
        // Use the explicit variable from the inspector
        float fontSize = contentFontSize; 

        // 1.25 is a standard multiplier for line spacing in TMP
        float estimatedLineHeight = fontSize * 1.25f;

        int calculatedLines = Mathf.FloorToInt(containerHeight / estimatedLineHeight);

        if (calculatedLines < 5) calculatedLines = 5;

        namesPerPage = calculatedLines;
    }

    private void ParseTierDefinitions(DataList tierDefs)
    {
        _tierColors = new DataDictionary();
        _tierNameColors = new DataDictionary();
        for(int i=0; i<tierDefs.Count; i++) {
            if(tierDefs[i].TokenType == TokenType.DataDictionary) {
                DataDictionary t = tierDefs[i].DataDictionary;
                if(t.ContainsKey("name")) {
                    string tName = t["name"].String;
                    if(t.ContainsKey("color_header")) _tierColors.SetValue(tName, t["color_header"]);
                    if(t.ContainsKey("color_supporters")) _tierNameColors.SetValue(tName, t["color_supporters"]);
                }
            }
        }
    }

    private void GroupSupportersByTier(DataList supporters)
    {
        _tierGroups = new DataDictionary();
        _tierNames = new string[0]; 
        for (int i = 0; i < supporters.Count; i++) {
            DataToken item = supporters[i];
            string tier = "Supporter";
            if (item.TokenType == TokenType.DataDictionary) {
                DataDictionary d = item.DataDictionary;
                if (d.ContainsKey("tier")) tier = d["tier"].String;
            }
            if (!_tierGroups.ContainsKey(tier)) {
                _tierGroups.SetValue(tier, new DataList());
                string[] newKeys = new string[_tierNames.Length + 1];
                for(int k=0; k<_tierNames.Length; k++) newKeys[k] = _tierNames[k];
                newKeys[_tierNames.Length] = tier;
                _tierNames = newKeys;
            }
            _tierGroups[tier].DataList.Add(item); 
        }
    }

    private string GetTierHeaderColor(string tierName, DataList members)
    {
        if(_tierColors.ContainsKey(tierName)) return _tierColors[tierName].String;
        if (members.Count > 0 && members[0].TokenType == TokenType.DataDictionary) {
            DataDictionary fd = members[0].DataDictionary;
            if (fd.ContainsKey("tier_color")) return fd["tier_color"].String;
        }
        return ColorToHex(headerColor);
    }

    private string GetMemberNameColor(string tierName, DataToken memberToken)
    {
        if (memberToken.TokenType == TokenType.DataDictionary) {
            DataDictionary d = memberToken.DataDictionary;
            if (d.ContainsKey("name_color")) return d["name_color"].String;
        }
        if(_tierNameColors.ContainsKey(tierName)) return _tierNameColors[tierName].String;
        return ColorToHex(textColor);
    }

    private void BuildPages()
    {
        CalculateSmartPageLimit();
        if (layoutMode == 1 && gridColumns > 1) BuildGridPages();
        else BuildListPages();
    }

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
            string hColor = GetTierHeaderColor(tierName, members);
            string headerLine = $"<size=120%><b><color={hColor}>{tierName}</color></b></size>\n";

            if (linesInCurrentPage + 2 >= namesPerPage) {
                tempPages[pageCount] = currentPageContent;
                pageCount++;
                currentPageContent = "";
                linesInCurrentPage = 0;
            }
            currentPageContent += headerLine;
            linesInCurrentPage++;

            for (int m = 0; m < members.Count; m++) {
                if (linesInCurrentPage >= namesPerPage) {
                    tempPages[pageCount] = currentPageContent;
                    pageCount++;
                    currentPageContent = "";
                    linesInCurrentPage = 0;
                }
                string memberName = GetNameFromToken(members[m]);
                string memberColor = GetMemberNameColor(tierName, members[m]);
                currentPageContent += $"<color={memberColor}>{memberName}</color>\n";
                linesInCurrentPage++;
            }
            currentPageContent += "\n";
            linesInCurrentPage++;
        }
        if (!string.IsNullOrEmpty(currentPageContent)) { tempPages[pageCount] = currentPageContent; pageCount++; }
        FinalizePages(tempPages, pageCount);
    }

    private void BuildGridPages()
    {
        string[] tempPages = new string[50];
        int pageCount = 0;
        string currentPageContent = "";
        int linesInCurrentPage = 0;
        int tiersToShow = Mathf.Min(gridColumns, _tierNames.Length);
        int tierBatchCount = Mathf.CeilToInt((float)_tierNames.Length / tiersToShow);

        for (int batch = 0; batch < tierBatchCount; batch++)
        {
            int startTier = batch * tiersToShow;
            int endTier = Mathf.Min(startTier + tiersToShow, _tierNames.Length);
            int actualColumns = endTier - startTier;
            DataList[] tierMembers = new DataList[actualColumns];
            string[] tierColors = new string[actualColumns];
            int maxMembers = 0;

            for (int col = 0; col < actualColumns; col++) {
                int tierIndex = startTier + col;
                string tName = _tierNames[tierIndex];
                tierMembers[col] = _tierGroups[tName].DataList;
                tierColors[col] = GetTierHeaderColor(tName, tierMembers[col]);
                if (tierMembers[col].Count > maxMembers) maxMembers = tierMembers[col].Count;
            }

            if (linesInCurrentPage + maxMembers + 2 >= namesPerPage) {
                if (!string.IsNullOrEmpty(currentPageContent)) {
                    tempPages[pageCount] = currentPageContent;
                    pageCount++;
                    currentPageContent = "";
                    linesInCurrentPage = 0;
                }
            }

            string headerRow = "";
            for (int col = 0; col < actualColumns; col++) {
                int tierIndex = startTier + col;
                string tierName = TruncateText(_tierNames[tierIndex], gridColumnWidth);
                headerRow += GetColumnPosition(col, actualColumns);
                headerRow += $"<b><color={tierColors[col]}>{tierName}</color></b>";
            }
            currentPageContent += headerRow + "\n";
            linesInCurrentPage++;

            for (int row = 0; row < maxMembers; row++) {
                if (linesInCurrentPage >= namesPerPage) {
                    tempPages[pageCount] = currentPageContent;
                    pageCount++;
                    currentPageContent = "";
                    linesInCurrentPage = 0;
                }
                string memberRow = "";
                for (int col = 0; col < actualColumns; col++) {
                    memberRow += GetColumnPosition(col, actualColumns);
                    if (row < tierMembers[col].Count) {
                        string memberName = TruncateText(GetNameFromToken(tierMembers[col][row]), gridColumnWidth);
                        string memberColor = GetMemberNameColor(_tierNames[startTier+col], tierMembers[col][row]);
                        memberRow += $"<color={memberColor}>{memberName}</color>";
                    }
                }
                currentPageContent += memberRow + "\n";
                linesInCurrentPage++;
            }
            currentPageContent += "\n";
            linesInCurrentPage++;
        }
        if (!string.IsNullOrEmpty(currentPageContent)) { tempPages[pageCount] = currentPageContent; pageCount++; }
        FinalizePages(tempPages, pageCount);
    }

    // --- Helpers ---
    private void ApplyBoardSettingsFromJSON(DataToken settingsToken)
    {
        if (settingsToken.TokenType != TokenType.DataDictionary) return;
        DataDictionary settings = settingsToken.DataDictionary;
        
        if (settings.ContainsKey("smart_paging")) smartPageSizing = settings["smart_paging"].Boolean;
        if (settings.ContainsKey("board_title")) boardTitle = settings["board_title"].String;
        if (settings.ContainsKey("layout_mode")) layoutMode = (int)settings["layout_mode"].Number;
        if (settings.ContainsKey("grid_columns")) gridColumns = (int)settings["grid_columns"].Number;
        if (settings.ContainsKey("names_per_page")) namesPerPage = (int)settings["names_per_page"].Number;
        if (settings.ContainsKey("page_display_time")) pageDisplayTime = (float)settings["page_display_time"].Number;
        if (settings.ContainsKey("grid_column_spacing")) gridColumnSpacing = (int)settings["grid_column_spacing"].Number;
        if (settings.ContainsKey("header_color")) headerColor = HexToColor(settings["header_color"].String);
        if (settings.ContainsKey("text_color")) textColor = HexToColor(settings["text_color"].String);
        if (settings.ContainsKey("bg_opacity")) backgroundOpacity = (float)settings["bg_opacity"].Number;
        if (settings.ContainsKey("content_font_size")) contentFontSize = (float)settings["content_font_size"].Number;
        
        ApplyVisuals();
    }

    private void FinalizePages(string[] tempPages, int pageCount) { _totalPages = pageCount; _formattedPages = new string[_totalPages]; for (int i = 0; i < _totalPages; i++) _formattedPages[i] = tempPages[i]; _hasData = true; _currentPage = 0; UpdateDisplay(); }
    private void NextPage() { _timeSinceLastScroll = 0f; _currentPage++; if (_currentPage >= _totalPages) _currentPage = 0; UpdateDisplay(); }
    private void UpdateDisplay() {
        if (!_hasData || _formattedPages == null || _formattedPages.Length == 0) return;
        if (contentText != null) {
            string content = _formattedPages[_currentPage];
            if (layoutMode == 1) content = "<align=left>" + content;
            contentText.text = content;
        }
        SetStatus(_totalPages > 1 ? $"Page {_currentPage + 1} / {_totalPages}" : "Powered by AVAMP");
    }
    private string GetNameFromToken(DataToken token) { if (token.TokenType == TokenType.String) return token.String; if (token.TokenType == TokenType.DataDictionary && token.DataDictionary.ContainsKey("name")) return token.DataDictionary["name"].String; return "Unknown"; }
    private string TruncateText(string text, int maxWidth) { return text.Length > maxWidth ? text.Substring(0, maxWidth - 1) + "…" : text; }
    private string GetColumnPosition(int columnIndex, int totalColumns) { return columnIndex == 0 ? "" : $"<pos={columnIndex * gridColumnSpacing:F0}%>"; }
    private string ColorToHex(Color color) { return $"#{Mathf.RoundToInt(color.r * 255f):X2}{Mathf.RoundToInt(color.g * 255f):X2}{Mathf.RoundToInt(color.b * 255f):X2}"; }
    private Color HexToColor(string hex) { if (hex.StartsWith("#")) hex = hex.Substring(1); if (hex.Length != 6) return Color.white; int r = HexCharToInt(hex[0]) * 16 + HexCharToInt(hex[1]), g = HexCharToInt(hex[2]) * 16 + HexCharToInt(hex[3]), b = HexCharToInt(hex[4]) * 16 + HexCharToInt(hex[5]); return (r < 0 || g < 0 || b < 0) ? Color.white : new Color(r / 255f, g / 255f, b / 255f, 1f); }
    private int HexCharToInt(char c) { if (c >= '0' && c <= '9') return c - '0'; if (c >= 'a' && c <= 'f') return 10 + (c - 'a'); if (c >= 'A' && c <= 'F') return 10 + (c - 'A'); return -1; }
    private void ValidateConfiguration() { if (namesPerPage < 1) namesPerPage = 20; if (pageDisplayTime < 1f) pageDisplayTime = 5f; if (statusText == null) statusText = GetComponentInChildren<TextMeshProUGUI>(); }
    
    private void ApplyVisuals() {
        if (headerText != null) { 
            headerText.color = headerColor; 
            headerText.text = boardTitle; 
        }
        if (contentText != null) {
            contentText.color = textColor;
        }
        if (backgroundImage != null) { Color bg = backgroundImage.color; bg.a = backgroundOpacity; backgroundImage.color = bg; }
    }
    
    private void SetStatus(string msg) { if (statusText != null) statusText.text = msg; }
    private void HandleParseError(string reason) { Debug.LogError($"[AVAMP] Parse Error: {reason}"); if (!_hasData) SetStatus("Data Error"); }
}