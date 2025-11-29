using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AvampVipDoor : UdonSharpBehaviour
{
    [Header("--- AVAMP Configuration ---")]
    public VRCUrl vipListUrl;
    public float refreshInterval = 300f;

    [Header("--- Access Control ---")]
    public GameObject objectToEnable;
    public GameObject objectToDisable;
    
    [Tooltip("If true, the door will close itself after X seconds")]
    public bool useAutoClose = true;
    [Tooltip("How long the door stays open")]
    public float closeDelay = 5.0f;

    [Header("--- Debug ---")]
    public bool debugMode = false;

    // --- Internal State ---
    private bool _isLoading = false;
    private float _timeSinceLastFetch = 0f;
    private const float MIN_REFRESH_INTERVAL = 60f;

    void Start()
    {
        if (vipListUrl == null || string.IsNullOrEmpty(vipListUrl.Get())) {
            Log("URL is missing!");
            return;
        }
        // Ensure door is closed at start
        ResetDoorState();
        LoadData();
    }

    void Update()
    {
        _timeSinceLastFetch += Time.deltaTime;
        if (_timeSinceLastFetch >= Mathf.Max(refreshInterval, MIN_REFRESH_INTERVAL) && !_isLoading)
        {
            _timeSinceLastFetch = 0f;
            LoadData();
        }
    }

    public override void Interact()
    {
        // When they click, we check the list immediately
        if (!_isLoading) LoadData();
    }

    public void LoadData()
    {
        if (_isLoading) return;
        _isLoading = true;
        VRCStringDownloader.LoadUrl(vipListUrl, (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _isLoading = false;
        CheckAccess(result.Result);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _isLoading = false;
        LogError($"Download FAILED: {result.Error}");
    }

    private void CheckAccess(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data) || data.TokenType != TokenType.DataDictionary) {
            LogError("Invalid JSON");
            return;
        }

        DataDictionary root = data.DataDictionary;
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        string localPlayerId = localPlayer.playerId;
        string localName = localPlayer.displayName;
        bool found = false;

        // Priority 1: Check VRChat User IDs (most reliable)
        if (root.ContainsKey("allowed_vrc_ids"))
        {
            DataList allowedVrcIds = root["allowed_vrc_ids"].DataList;

            for (int i = 0; i < allowedVrcIds.Count; i++) {
                if (allowedVrcIds[i].String == localPlayerId) {
                    found = true;
                    Log("Access granted via VRChat ID");
                    break;
                }
            }
        }

        // Priority 2: Fallback to display name check (for users without linked VRChat accounts)
        if (!found && root.ContainsKey("allowed_users"))
        {
            DataList allowedUsers = root["allowed_users"].DataList;

            for (int i = 0; i < allowedUsers.Count; i++) {
                if (allowedUsers[i].String == localName) {
                    found = true;
                    Log("Access granted via display name");
                    break;
                }
            }
        }

        if (found) GrantAccess();
        else Log("Access Denied");
    }

    private void GrantAccess()
    {
        Log("Access Granted - Opening Door");
        
        // Open the door
        if (objectToEnable != null) objectToEnable.SetActive(true);
        if (objectToDisable != null) objectToDisable.SetActive(false);

        // Start the timer to close it
        if (useAutoClose)
        {
            SendCustomEventDelayedSeconds(nameof(ResetDoorState), closeDelay);
        }
    }

    public void ResetDoorState()
    {
        // Close the door (Reset to default)
        if (objectToEnable != null) objectToEnable.SetActive(false);
        if (objectToDisable != null) objectToDisable.SetActive(true);
    }

    private void Log(string msg) { if (debugMode) Debug.Log($"[VIP] {msg}"); }
    private void LogError(string msg) { Debug.LogError($"[VIP] {msg}"); }
}