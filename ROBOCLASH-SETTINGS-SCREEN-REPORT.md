# RoboClash Settings Screen — Technical Report

## Scope

This report explains the settings screen currently implemented in the RoboClash Unity project, including how it is built at runtime, which settings it exposes, where values are stored, and the code paths used to apply them.

The settings screen uses Unity's built-in UGUI controls (`Text`, `Slider`, `Toggle`, and `Dropdown`). It does not use TextMeshPro and does not require a separate settings scene.

## Runtime creation and navigation

The gameplay HUD creates the settings component and parents its generated interface under the gameplay Canvas:

```csharp
private void BuildPauseSettings()
{
    settingsPanel = gameObject.AddComponent<RoboClashSettingsPanel>();
    settingsPanel.Initialize(canvas.transform, font, ClosePauseSettings);
    settingsPanel.Root.SetActive(false);
}
```

The pause menu's Settings button opens it. The Back button closes the settings panel and returns to the pause menu:

```csharp
private void OpenPauseSettings()
{
    redesignedHud?.SetSettingsVisible(true);
    settingsPanel.Show();
}

private void ClosePauseSettings()
{
    settingsPanel.Hide();
    redesignedHud?.SetSettingsVisible(false);
}
```

The pause menu is entered through the existing gameplay HUD's Escape-key handling and the match manager's pause behavior. While paused, the match manager stops simulation and unlocks/shows the cursor so the settings controls can be clicked. The redesigned gameplay HUD is hidden while paused/settings are open to prevent its higher-sorting Canvas from covering dropdowns:

```csharp
public void SetMatchState(RoboClashMatchState state)
{
    var isFinished = state == RoboClashMatchState.Victory ||
                     state == RoboClashMatchState.Defeat ||
                     state == RoboClashMatchState.Draw;
    canvasRoot.gameObject.SetActive(!settingsVisible &&
        state != RoboClashMatchState.Initializing &&
        state != RoboClashMatchState.Paused && !isFinished);
}
```

## Settings available

| Setting | UI control | Runtime effect / persistence |
| --- | --- | --- |
| Master volume | Slider | Changes `AudioListener.volume`; saved in the settings JSON. |
| Music volume | Slider | Updates the music setting and emits `MusicVolumeChanged`; saved in JSON. |
| SFX volume | Slider | Updates RoboClash sound-effect volume; saved in JSON. |
| Fullscreen | Toggle | Changes `Screen.fullScreen`; saved in JSON. |
| Resolution | Dropdown | Chooses a supported `Screen.resolutions` entry; asks the player to confirm it, or automatically restores the prior mode after 10 seconds. |
| Graphics quality | Dropdown | Chooses one of `QualitySettings.names`; saved in JSON. |
| V-Sync | Toggle | Changes `QualitySettings.vSyncCount` and affects the frame-rate cap; saved in JSON. |
| Frame-rate limit | Dropdown | 30, 60, 120, 144 FPS, or unlimited; saved in JSON. |
| Camera mode | Dropdown | First person or third person; stored as a `PlayerPrefs` session preference. |
| Field of view | Dropdown | 70, 80, 90, 100, or 110 degrees; stored in `PlayerPrefs`. |
| Mouse sensitivity | Dropdown | Very low, low, medium, high, or very high; stored in `PlayerPrefs`. |
| Reset settings | Button | Resets audio/display settings and resets camera mode, FOV, and sensitivity. |

The screen also includes a keyboard-control reminder. The music slider is present and enabled, even though the heading currently contains a legacy notice about music tracks.

## Building the controls

`RoboClashSettingsPanel.BuildControls()` constructs the panel, labels, and controls at runtime. For example, the camera and resolution dropdowns are wired to their respective handlers:

```csharp
CreateSettingLabel(panel.transform, "RESOLUTION", 4);
resolutionDropdown = CreateSettingDropdown(panel.transform, 4);
resolutionDropdown.onValueChanged.AddListener(ApplyResolution);

CreateSettingLabel(panel.transform, "CAMERA MODE", 8);
cameraModeDropdown = CreateSettingDropdown(panel.transform, 8);
cameraModeDropdown.AddOptions(new List<string>
{
    "FIRST PERSON",
    "THIRD PERSON"
});
cameraModeDropdown.onValueChanged.AddListener(SetCameraMode);
```

Volume sliders pass normalized values to the save service:

```csharp
masterSlider = AddSliderRow(panel.transform, "MASTER VOLUME", RowY(0),
    RoboClashSaveService.Current.settings.masterVolume,
    RoboClashSaveService.SetMasterVolume);
musicSlider = AddSliderRow(panel.transform, "MUSIC VOLUME", RowY(1),
    RoboClashSaveService.Current.settings.musicVolume,
    RoboClashSaveService.SetMusicVolume);
sfxSlider = AddSliderRow(panel.transform, "SFX VOLUME", RowY(2),
    RoboClashSaveService.Current.settings.sfxVolume,
    RoboClashSaveService.SetSfxVolume);
```

Each dropdown is a normal `UnityEngine.UI.Dropdown` with a generated caption, template, masked viewport, content layout, toggle group, and scroll view:

```csharp
var dropdown = dropdownObject.AddComponent<Dropdown>();
dropdown.captionText = caption;
dropdown.template = templateRect;
dropdown.itemText = itemText;
dropdown.itemImage = item.GetComponent<Image>();

var scroll = template.AddComponent<ScrollRect>();
scroll.viewport = viewport.GetComponent<RectTransform>();
scroll.content = content.GetComponent<RectTransform>();
scroll.horizontal = false;
scroll.vertical = true;
```

## Resolution change safety

Resolution options are populated from the active platform's `Screen.resolutions`. Duplicate width/height entries are skipped, and the current screen resolution is added if Unity did not include it in that list.

Changing resolution records the previous width, height, and fullscreen state, then temporarily applies the selected resolution and opens a confirmation overlay:

```csharp
previousResolutionWidth = Screen.width;
previousResolutionHeight = Screen.height;
previousFullscreen = Screen.fullScreen;
RoboClashSaveService.SetResolution(resolution.x, resolution.y);
resolutionChangePending = true;
resolutionConfirmationSeconds = 10f;
ShowConfirmation(
    $"KEEP {resolution.x} x {resolution.y}? REVERTING IN 10 SECONDS.",
    ConfirmResolution, false);
```

If the player confirms, the pending flag is cleared and settings are saved. If the countdown expires, the player cancels, or the settings panel closes before confirmation, the old fullscreen state and resolution are restored. The countdown uses unscaled time, so it continues while gameplay time is paused.

The save service checks a requested resolution before applying it:

```csharp
public static void SetResolution(int width, int height)
{
    EnsureLoaded();
    if (width <= 0 || height <= 0 || !IsSupportedResolution(width, height))
    {
        Debug.LogWarning(
            $"The requested RoboClash resolution {width} x {height} is not supported.");
        return;
    }

    current.settings.resolutionWidth = width;
    current.settings.resolutionHeight = height;
    Screen.SetResolution(width, height, current.settings.fullscreen);
}
```

## Camera, field-of-view, and sensitivity preferences

These preferences are handled by `RoboClashSession`, which persists them to `PlayerPrefs`. Camera mode is saved as an enum:

```csharp
private static void SetCameraMode(int index)
{
    RoboClashSession.SaveCameraMode(
        index == (int)RoboClashCameraMode.ThirdPerson
            ? RoboClashCameraMode.ThirdPerson
            : RoboClashCameraMode.FirstPerson);
}
```

`RoboClashRobotMovement` checks the session preference during its camera update and applies the selected mode:

```csharp
if (cameraMode != RoboClashSession.CameraMode)
    SetCameraMode(RoboClashSession.CameraMode);
```

The FOV and sensitivity dropdowns map their indices to configured values and call `SaveFieldOfView` and `SaveMouseSensitivity` respectively. The camera setting is saved immediately; changes to camera presentation are applied by the controlled player's movement/camera logic.

## Persistence and reset

Audio and display preferences are held in `RoboClashSaveData.settings`, validated by `RoboClashSaveService`, and stored in `roboclash-save.json` under `Application.persistentDataPath`. The service applies saved display settings, quality, V-Sync, and frame rate when loading:

```csharp
public static void ApplySettings()
{
    EnsureLoaded();
    Validate(current);
    ApplyAudioSettings();
    MusicVolumeChanged?.Invoke(current.settings.musicVolume);

    var settings = current.settings;
    if (settings.resolutionWidth > 0 && settings.resolutionHeight > 0 &&
        IsSupportedResolution(settings.resolutionWidth, settings.resolutionHeight))
    {
        Screen.SetResolution(
            settings.resolutionWidth, settings.resolutionHeight, settings.fullscreen);
    }
    else
    {
        Screen.fullScreen = settings.fullscreen;
    }

    if (QualitySettings.names.Length > 0)
        QualitySettings.SetQualityLevel(settings.qualityLevel, true);
    QualitySettings.vSyncCount = settings.vSync ? 1 : 0;
    ApplyFrameRate();
}
```

On leaving settings, pending resolution changes are rolled back before saving:

```csharp
public void Hide()
{
    if (root == null)
        return;

    if (resolutionChangePending)
        RestorePreviousResolution();
    RoboClashSaveService.Save();
    confirmationAction = null;
    confirmationRoot?.SetActive(false);
    contentRoot.SetActive(true);
    root.SetActive(false);
}
```

Resetting invokes `RoboClashSaveService.ResetSettings()` and separately resets the session preferences to first person, FOV 80, and sensitivity 0.12:

```csharp
RoboClashSaveService.ResetSettings();
RoboClashSession.SaveCameraMode(RoboClashCameraMode.FirstPerson);
RoboClashSession.SaveFieldOfView(80);
RoboClashSession.SaveMouseSensitivity(0.12f);
Refresh();
```

## Input System and troubleshooting

The gameplay UI uses a `GraphicRaycaster` on its Canvas. The shared `RoboClashUIEventSystem.EnsureForScene()` helper in `RoboClashMenuController.cs` selects or creates the scene's EventSystem and configures an `InputSystemUIInputModule`. It repairs missing default pointer actions and disables duplicate scene EventSystems/modules.

If settings are visible but dropdowns do not open or options cannot be clicked, check:

1. The game is paused so the cursor is unlocked and visible.
2. There is an active EventSystem in the gameplay scene with an enabled `InputSystemUIInputModule`.
3. Its Point and Left Click actions are assigned and enabled.
4. No other active Canvas or overlay is intercepting pointer input.
5. No Console error occurred while settings were being created.

The redesigned HUD's separate higher-sorting Canvas is hidden during pause/settings to avoid visually covering dropdowns. Hiding it addresses a rendering overlap; it does not substitute for a functioning EventSystem or pointer module.

## Source files

- [`RoboClashSettingsPanel.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashSettingsPanel.cs) — runtime settings UI and callbacks.
- [`RoboClashHUD.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashHUD.cs) — pause menu, settings creation, opening/closing flow.
- [`RoboClashHudV2.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashHudV2.cs) — redesigned HUD visibility during pause/settings.
- [`RoboClashSaveService.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashSaveService.cs) — persistent audio/display settings and application.
- [`RoboClashSession.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashSession.cs) — camera, FOV, sensitivity, and match session preferences.
- [`RoboClashRobotMovement.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotMovement.cs) — applies the selected camera mode.
- [`RoboClashMenuController.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashMenuController.cs) — shared Input System UI EventSystem setup.

This report describes the source implementation. The settings controls, display mode confirmation, and camera changes still require manual verification in Unity Play Mode and in a standalone build.
