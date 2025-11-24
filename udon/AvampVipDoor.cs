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
    [Tooltip("The raw GitHub Pages URL to your vip.json")]
    public VRCUrl vipListUrl;
    [Tooltip("How often to check for updates (in seconds). Minimum: 60s")]
    public float refreshInterval = 300f;

    [Header("--- Access Control ---")]
    [Tooltip("Object to enable if user is allowed (e.g. the door, or a teleport button)")]
    public GameObject objectToEnable;
    [Tooltip("Object to disable if user is allowed (e.g. a 'Locked' sign)")]
    public GameObject objectToDisable;
    [Tooltip("Optional: Send this Custom Event to this behaviour when access is granted")]
    public string onAccessGrantedEvent;
    
    [Header("--- Debug ---")]
    public bool debugMode = false;

    // --- Internal State ---
    private bool _isAllowed = false;
    private bool _isLoading = false;
    private float _timeSinceLastFetch = 0f;
    private const float MIN_REFRESH_INTERVAL = 60f;

    void Start()
    {
        if (vipListUrl == null || string.IsNullOrEmpty(vipListUrl.Get()))
        {
            Log("URL is missing! VIP Door will not work.");
            return;
        }

        // Initial state
        UpdateAccessState(false);
        
        // Fetch data
        LoadData();
    }

    void Update()
    {
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
        // Manual refresh on interact if not allowed yet
        if (!_isAllowed && !_isLoading)
        {
            LoadData();
        }
    }

    public void LoadData()
    {
        if (_isLoading) return;
        
        _isLoading = true;
        Log($"Syncing VIP list from: {vipListUrl.Get()}");
        
        VRCStringDownloader.LoadUrl(vipListUrl, (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _isLoading = false;
        Log("Data received successfully.");
        CheckAccess(result.Result);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _isLoading = false;
        LogError($"Download FAILED. Error: {result.Error}");
    }

    private void CheckAccess(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data))
        {
            LogError("Invalid JSON Format");
            return;
        }

        if (data.TokenType != TokenType.DataDictionary)
        {
            LogError("Root is not a Dictionary { }");
            return;
        }

        DataDictionary root = data.DataDictionary;

        if (root.ContainsKey("allowed_users"))
        {
            DataList allowedUsers = root["allowed_users"].DataList;
            string localName = Networking.LocalPlayer.displayName;
            bool found = false;

            for (int i = 0; i < allowedUsers.Count; i++)
            {
                if (allowedUsers[i].String == localName)
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                Log($"Access GRANTED for {localName}");
                UpdateAccessState(true);
            }
            else
            {
                Log($"Access DENIED for {localName}");
                UpdateAccessState(false);
            }
        }
        else
        {
            LogError("JSON missing 'allowed_users' key");
        }
    }

    private void UpdateAccessState(bool allowed)
    {
        _isAllowed = allowed;

        if (objectToEnable != null) objectToEnable.SetActive(allowed);
        if (objectToDisable != null) objectToDisable.SetActive(!allowed);

        if (allowed && !string.IsNullOrEmpty(onAccessGrantedEvent))
        {
            SendCustomEvent(onAccessGrantedEvent);
        }
    }

    private void Log(string msg)
    {
        if (debugMode) Debug.Log($"[AVAMP VIP] {msg}");
    }

    private void LogError(string msg)
    {
        Debug.LogError($"[AVAMP VIP] {msg}");
    }
}

