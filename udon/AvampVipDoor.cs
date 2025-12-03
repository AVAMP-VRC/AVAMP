using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AvampVipDoor : UdonSharpBehaviour
{
    [Header("--- CACHE BUSTER SETUP ---")]
    public string sourceUrl;
    public VRCUrl[] vipListUrls;

    [Header("--- AVAMP Configuration ---")]
    public float refreshInterval = 300f;

    [Header("--- Access Control ---")]
    [Tooltip("The actual door object to hide/show")]
    public GameObject objectToEnable;
    public GameObject objectToDisable;
    
    public bool useAutoClose = true;
    public float closeDelay = 5.0f;

    [Header("--- Debug ---")]
    [Tooltip("Check this to see names in the logs")]
    public bool debugMode = false;

    // --- Internal State ---
    private bool _isLoading = false;
    private float _timeSinceLastFetch = 0f;
    private const float MIN_REFRESH_INTERVAL = 60f;
    
    private string _localPlayerName;
    private bool _isVipCached = false;
    private bool _triggeredByInteract = false;

    void Start()
    {
        if (Networking.LocalPlayer != null)
        {
            _localPlayerName = Networking.LocalPlayer.displayName;
        }

        // Close Door & Initial Load
        ResetDoorState();
        
        if (vipListUrls != null && vipListUrls.Length > 0)
        {
            LoadData(false);
        }
        else
        {
            LogError("No URLs found. Did you run the Generator?");
        }
    }

    void Update()
    {
        _timeSinceLastFetch += Time.deltaTime;
        
        if (_timeSinceLastFetch >= Mathf.Max(refreshInterval, MIN_REFRESH_INTERVAL) && !_isLoading)
        {
            _timeSinceLastFetch = 0f;
            LoadData(false);
        }
    }

    public override void Interact()
    {
        // 1. If we are already VIP, open instantly.
        if (_isVipCached)
        {
            OpenDoorSequence();
            return;
        }

        // 2. If not cached, or we want to force check, trigger load.
        // If we are ALREADY loading (background refresh), we "upgrade" it to an interaction.
        LoadData(true);
    }

    public void LoadData(bool didInteract)
    {
        // FIX: If loading, just ensure the flag is set so the door opens when done.
        if (_isLoading) 
        {
            if (didInteract) _triggeredByInteract = true;
            return;
        }

        if (vipListUrls == null || vipListUrls.Length == 0) return;

        _isLoading = true;
        _triggeredByInteract = didInteract;

        int randomIndex = UnityEngine.Random.Range(0, vipListUrls.Length);
        VRCUrl selectedUrl = vipListUrls[randomIndex];
        if (!Utilities.IsValid(selectedUrl)) selectedUrl = vipListUrls[0];

        if (debugMode && didInteract) Log($"Checking Access...");
        
        VRCStringDownloader.LoadUrl(selectedUrl, (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _isLoading = false;
        
        bool accessFound = CheckAccessInJSON(result.Result);
        _isVipCached = accessFound;

        if (_triggeredByInteract)
        {
            if (_isVipCached) OpenDoorSequence();
            else LogError($"Access Denied for '{_localPlayerName}'");
        }
        
        _triggeredByInteract = false;
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _isLoading = false;
        _triggeredByInteract = false;
        if (debugMode) LogError($"Download FAILED: {result.Error}");
        
        // Retry logic
        SendCustomEventDelayedSeconds(nameof(RetryLoad), 10f);
    }

    public void RetryLoad() { LoadData(false); }

    private bool CheckAccessInJSON(string json)
    {
        if (string.IsNullOrEmpty(_localPlayerName)) return false;

        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data) || data.TokenType != TokenType.DataDictionary) {
            LogError("Invalid JSON");
            return false;
        }

        DataDictionary root = data.DataDictionary;
        
        // Helper to check names case-insensitively
        string lowerLocalName = _localPlayerName.ToLower().Trim();

        // 1. Check 'allowed_users'
        if (root.ContainsKey("allowed_users"))
        {
            DataList list = root["allowed_users"].DataList;
            for (int i = 0; i < list.Count; i++) {
                string allowedName = list[i].String.ToLower().Trim();
                if (allowedName == lowerLocalName) return true;
            }
        }

        // 2. Check 'supporters'
        if (root.ContainsKey("supporters"))
        {
            DataList list = root["supporters"].DataList;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].TokenType == TokenType.DataDictionary) {
                    DataDictionary profile = list[i].DataDictionary;
                    if (profile.ContainsKey("name")) {
                        string supporterName = profile["name"].String.ToLower().Trim();
                        if (supporterName == lowerLocalName) return true;
                    }
                }
            }
        }

        return false;
    }

    private void OpenDoorSequence()
    {
        Log("Access Granted - Opening.");
        if (objectToEnable != null) objectToEnable.SetActive(true);
        if (objectToDisable != null) objectToDisable.SetActive(false);

        if (useAutoClose) SendCustomEventDelayedSeconds(nameof(ResetDoorState), closeDelay);
    }

    public void ResetDoorState()
    {
        if (objectToEnable != null) objectToEnable.SetActive(false);
        if (objectToDisable != null) objectToDisable.SetActive(true);
    }

    private void Log(string msg) { if (debugMode) Debug.Log($"[VIP] {msg}"); }
    private void LogError(string msg) { Debug.LogError($"[VIP] {msg}"); }
}