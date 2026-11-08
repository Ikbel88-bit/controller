using System.Collections.Generic;
using RoboClashController;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RoboClashControllerGenerator
{
    private const string SceneFolder = "Assets/Scenes";
    private const string ScenePath = SceneFolder + "/RoboClashController.unity";
    private const string GeneratedRootName = "RoboClashControllerGenerated";

    [MenuItem("Tools/RoboClash Controller/Generate Controller UI")]
    public static void GenerateControllerUI()
    {
        EnsureSceneFolder();
        Scene scene;
        var existingScene = SceneManager.GetSceneByPath(ScenePath);
        if (existingScene.IsValid() && existingScene.isLoaded)
            scene = existingScene;
        else if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        else
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var generatedRoots = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == GeneratedRootName)
                generatedRoots.Add(root);
        }

        foreach (var generatedRoot in generatedRoots)
            Undo.DestroyObjectImmediate(generatedRoot);

        var rootObject = new GameObject(GeneratedRootName);
        Undo.RegisterCreatedObjectUndo(rootObject, "Generate RoboClash Controller UI");

        var canvasObject = new GameObject("Controller Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(rootObject.transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var eventSystemExists = false;
        foreach (var eventSystem in Object.FindObjectsByType<EventSystem>())
        {
            if (eventSystem.gameObject.scene == scene)
            {
                eventSystemExists = true;
                break;
            }
        }

        if (!eventSystemExists)
        {
            var eventSystemObject = new GameObject("Controller Event System",
                typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(rootObject.transform, false);
        }

        rootObject.AddComponent<ControllerInputProvider>();
        rootObject.AddComponent<ControllerGyroscope>();
        rootObject.AddComponent<ControllerDemoScreen>();

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError($"Controller UI generation failed: unable to save '{ScenePath}'.");
            return;
        }

        AddSceneToBuildSettings();
        SceneManager.SetActiveScene(scene);
        Selection.activeGameObject = rootObject;
        Debug.Log(
            $"Generated standalone controller scene at '{ScenePath}'. Press Play to enter the PC Wi-Fi IPv4 address " +
            "and test the controller connection over TCP port 17862.",
            rootObject);
    }

    private static void EnsureSceneFolder()
    {
        if (!AssetDatabase.IsValidFolder(SceneFolder))
            AssetDatabase.CreateFolder("Assets", "Scenes");
    }

    private static void AddSceneToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var scene in scenes)
        {
            if (scene.path == ScenePath)
                return;
        }

        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
