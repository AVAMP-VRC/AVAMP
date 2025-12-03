using UnityEngine;
using UnityEditor;
using UdonSharpEditor;
using VRC.SDKBase;

[CustomEditor(typeof(AvampSupporterBoard))]
public class AvampBoardEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(20);
        
        AvampSupporterBoard script = (AvampSupporterBoard)target;

        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("⚡ GENERATE 500 LINKS ⚡", GUILayout.Height(40)))
        {
            GenerateLinks(script);
        }
        GUI.backgroundColor = Color.white;
    }

    private void GenerateLinks(AvampSupporterBoard script)
    {
        if (string.IsNullOrEmpty(script.sourceUrl))
        {
            Debug.LogError("[AVAMP] Please enter a Source URL first!");
            return;
        }

        Undo.RecordObject(script, "Generate Links");

        string cleanUrl = script.sourceUrl.Trim();
        string separator = cleanUrl.Contains("?") ? "&" : "?";

        // Generate 500 variations
        VRCUrl[] newUrls = new VRCUrl[500];
        for (int i = 0; i < 500; i++)
        {
            string urlWithBuster = $"{cleanUrl}{separator}t={i}";
            newUrls[i] = new VRCUrl(urlWithBuster);
        }

        script.dataUrls = newUrls;

        UdonSharpEditorUtility.CopyProxyToUdon(script);
        EditorUtility.SetDirty(script);

        Debug.Log($"<color=green>[AVAMP]</color> Successfully generated 500 cache-busting links!");
    }
}