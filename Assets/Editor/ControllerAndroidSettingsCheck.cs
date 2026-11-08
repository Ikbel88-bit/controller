#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ControllerAndroidSettingsCheck
{
    static ControllerAndroidSettingsCheck()
    {
        EditorApplication.delayCall += Apply;
    }

    private static void Apply()
    {
        if (!PlayerSettings.Android.forceInternetPermission)
        {
            PlayerSettings.Android.forceInternetPermission = true;
            Debug.Log("RoboClashController: Android Internet Access set to Require.");
        }
    }
}
#endif
