#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ControllerInputHandlerCheck
{
    static ControllerInputHandlerCheck()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (assets == null || assets.Length == 0) return;
        var so = new SerializedObject(assets[0]);
        var p = so.FindProperty("activeInputHandler");   // 0 = Old, 1 = New, 2 = Both
        if (p != null && p.intValue == 1)
            Debug.LogWarning("RoboClashController: Active Input Handling is 'Input System Package (New)' only. " +
                             "Input.compass will fail on the phone. Set it to 'Both'.");
    }
}
#endif
