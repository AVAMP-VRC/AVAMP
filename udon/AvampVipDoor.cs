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

    [Header("--- DOOR SETUP ---")]
    [Tooltip("The Solid Door/Blocker. This object will DISAPPEAR when access is granted.")]
    public GameObject lockedDoorObject;

    [Tooltip("Optional: A Green Light or 'Open' sound. This object will APPEAR when access is granted.")]
    public GameObject unlockVisuals;

    [Tooltip("Optional: If set, VIPs will be teleported here immediately.")]
    public Transform teleportTarget;
    
    [Header("--- Timers ---")]
    public bool useAutoClose = true;
    [Tooltip("How long (in seconds) the door stays open.")]
    public float closeDelay = 5.0f;

    [Header("--- Debug ---")]
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

        // Ensure door is closed on load
        ResetDoorState();
        
        if (vipListUrls != null && vipListUrls.Length > 0)
        {
            LoadData(false);
        }
        else
        {
            LogError("No URLs found.");
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

    // Called by the Relay Script on the buttons
    public override void Interact()
    {
        if (_isVipCached)
        {
            ExecuteAccessGranted();
            return;
        }
        LoadData(true);
    }

    public void LoadData(bool didInteract)
    {
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
        
        _isVipCached = CheckAccessInJSON(result.Result);

        if (_triggeredByInteract)
        {
            if (_isVipCached) ExecuteAccessGranted();
            else LogError($"Access Denied for '{_localPlayerName}'");
        }
        
        _triggeredByInteract = false;
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _isLoading = false;
        _triggeredByInteract = false;
        if (debugMode) LogError($"Download FAILED: {result.Error}");
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
        string lowerLocalName = _localPlayerName.ToLower().Trim();

        // 1. Check 'allowed_users'
        if (root.ContainsKey("allowed_users"))
        {
            DataList list = root["allowed_users"].DataList;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].String.ToLower().Trim() == lowerLocalName) return true;
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
                        if (profile["name"].String.ToLower().Trim() == lowerLocalName) return true;
                    }
                }
            }
        }
        return false;
    }

    private void ExecuteAccessGranted()
    {
        Log("Access Granted.");

        // Visuals Logic: Hide the blocker, Show the green light
        if (lockedDoorObject != null) lockedDoorObject.SetActive(false);
        if (unlockVisuals != null) unlockVisuals.SetActive(true);

        // Teleport Logic
        if (teleportTarget != null)
        {
            Networking.LocalPlayer.TeleportTo(teleportTarget.position, teleportTarget.rotation);
        }

        if (useAutoClose) SendCustomEventDelayedSeconds(nameof(ResetDoorState), closeDelay);
    }

    public void ResetDoorState()
    {
        // Reset Visuals: Show the blocker, Hide the green light
        if (lockedDoorObject != null) lockedDoorObject.SetActive(true);
        if (unlockVisuals != null) unlockVisuals.SetActive(false);
    }

    private void Log(string msg) { if (debugMode) Debug.Log($"[VIP] {msg}"); }
    private void LogError(string msg) { Debug.LogError($"[VIP] {msg}"); }
}