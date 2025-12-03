using UnityEngine;
using UnityEditor;
using UdonSharpEditor; // Required
using VRC.SDKBase;
using VRC.Udon;

[CustomEditor(typeof(AvampVipDoor))]
public class AvampVipDoorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the normal inspector
        if (DrawDefaultInspector())
        {
            // If user changes anything manually, auto-save
            AvampVipDoor script = (AvampVipDoor)target;
            UdonSharpEditorUtility.CopyProxyToUdon(script);
        }

        EditorGUILayout.Space(20);
        
        AvampVipDoor targetScript = (AvampVipDoor)target;

        GUI.backgroundColor = Color.cyan;
        if (GUILayout.Button("⚡ GENERATE VIP LINKS ⚡", GUILayout.Height(40)))
        {
            GenerateLinks(targetScript);
        }
        GUI.backgroundColor = Color.white;
    }

    private void GenerateLinks(AvampVipDoor script)
    {
        if (string.IsNullOrEmpty(script.sourceUrl))
        {
            Debug.LogError("[AVAMP] Error: Source URL is empty.");
            return;
        }

        // 1. Register Undo so you can Ctrl+Z
        Undo.RecordObject(script, "Generate VIP Links");

        // 2. Prepare the loop
        string cleanUrl = script.sourceUrl.Trim();
        string separator = cleanUrl.Contains("?") ? "&" : "?";
        VRCUrl[] newUrls = new VRCUrl[500];

        // 3. Generate
        for (int i = 0; i < 500; i++)
        {
            // We use new VRCUrl here because we are in the EDITOR, not Udon.
            newUrls[i] = new VRCUrl($"{cleanUrl}{separator}t={i}");
        }

        // 4. Apply to Proxy
        script.vipListUrls = newUrls;

        // 5. FORCE SAVE TO UDON (The Critical Step)
        // This copies the C# array into the actual UdonBehaviour memory
        UdonSharpEditorUtility.CopyProxyToUdon(script);

        // 6. Mark the object as "Dirty" so Unity knows to save the scene file
        UdonBehaviour backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(script);
        if (backingBehaviour != null)
        {
            EditorUtility.SetDirty(backingBehaviour);
        }
        EditorUtility.SetDirty(script);

        Debug.Log($"<color=cyan>[AVAMP VIP]</color> Generated {newUrls.Length} links. Don't forget to save your scene!");
    }
}