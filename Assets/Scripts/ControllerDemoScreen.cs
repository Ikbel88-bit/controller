using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace RoboClashController
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ControllerInputProvider))]
    [RequireComponent(typeof(ControllerGyroscope))]
    public sealed class ControllerDemoScreen : MonoBehaviour
    {
        private static readonly Color Navy = new Color(0.018f, 0.035f, 0.075f);
        private static readonly Color Panel = new Color(0.035f, 0.075f, 0.14f, 0.96f);
        private static readonly Color Cyan = new Color(0.08f, 0.88f, 1f);
        private static readonly Color Purple = new Color(0.57f, 0.28f, 1f);
        private static readonly Color Green = new Color(0.2f, 0.95f, 0.55f);
        private static readonly Color Gold = new Color(1f, 0.68f, 0.12f);
        private static readonly Color Muted = new Color(0.62f, 0.72f, 0.83f);
        private static readonly Color Dark = new Color(0.025f, 0.055f, 0.105f);
        private const float CompassPixelsPerDegree = 7f;
        private const int CompassTickIntervalDegrees = 5;
        private const float SensorUiUpdateInterval = 0.2f;

        private readonly StringBuilder compassDiagnosticsBuilder = new StringBuilder(160);
        private ControllerInputProvider inputProvider;
        private ControllerGyroscope gyroscope;
        private ControllerCompass compass;
        private ControllerProximity proximitySensor;
        private ControllerNetworkClient networkClient;
        private Canvas canvas;
        private RectTransform safeRoot;
        private GameObject compassStrip;
        private RectTransform[] compassTickMarks;
        private int[] compassTickBearings;
        private RectTransform[] compassCardinalLabels;
        private static readonly int[] CompassCardinalBearings = { 0, 90, 180, 270 };
        private ControllerVirtualJoystick joystick;
        private ControllerActionButton shootButton;
        private ControllerActionButton jumpButton;
        private ControllerActionButton abilityButton;
        private ControllerActionButton ultimateButton;
        private ControllerActionButton reloadButton;
        private ControllerActionButton gyroClutchButton;
        private GameObject settingsPanel;
        private GameObject diagnosticsPanel;
        private Text movementStatus;
        private Text gyroStatus;
        private Text gyroWarning;
        private Text rawGyroText;
        private Text compassProximityText;
        private Text guardIndicator;
        private Text turnValueText;
        private Text pitchValueText;
        private Text[] actionStatusTexts;
        private InputBar moveBar;
        private InputBar aimBar;
        private InputBar gyroBar;
        private InputBar diagnosticGyroBar;
        private InputBar diagnosticGyroPitchBar;
        private Slider yawSensitivitySlider;
        private Slider pitchSensitivitySlider;
        private Slider gyroDeadZoneSlider;
        private Slider responseCurveSlider;
        private Slider smoothingSlider;
        private Slider joystickDeadZoneSlider;
        private Toggle gyroToggle;
        private Toggle invertYawToggle;
        private Toggle invertPitchToggle;
        private Toggle compassAssistToggle;
        private Toggle invertCompassToggle;
        private Toggle diagnosticsToggle;
        private Text settingsYawSensitivityValue;
        private Text settingsPitchSensitivityValue;
        private Text settingsDeadZoneValue;
        private Text settingsResponseCurveValue;
        private Text settingsSmoothingValue;
        private Text settingsJoystickDeadZoneValue;
        private Text compassOffsetButtonLabel;
        private Text driftTestStatusText;
        private Text connectionStatusText;
        private Button connectionButton;
        private readonly ControllerIpEntry ipAddressEntry = new ControllerIpEntry();
        private Text ipAddressDisplay;
        private Text connectionPanelStatusText;
        private Button connectButton;
        private GameObject connectionPanel;
        private CanvasGroup controllerCanvasGroup;
        private ControllerTouchDebug touchDebug;
        private bool wasConnected;
        private float nextGyroUiUpdate;
        private float nextSensorUiUpdate;

        private sealed class InputBar
        {
            public RectTransform Marker;
            public Text ValueText;

            public void SetValue(float value)
            {
                value = Mathf.Clamp(value, -1f, 1f);
                var marker = Marker.anchorMin;
                marker.x = 0.5f + value * 0.5f;
                Marker.anchorMin = marker;
                marker = Marker.anchorMax;
                marker.x = 0.5f + value * 0.5f;
                Marker.anchorMax = marker;
                ValueText.text = value.ToString("+0.00;-0.00;0.00");
            }
        }

        private void Awake()
        {
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;

            inputProvider = GetComponent<ControllerInputProvider>();
            gyroscope = GetComponent<ControllerGyroscope>();
            networkClient = GetComponent<ControllerNetworkClient>();
            if (networkClient == null)
                networkClient = gameObject.AddComponent<ControllerNetworkClient>();
            ipAddressEntry.Changed += RefreshIPAddressDisplay;
            ipAddressEntry.Set(PlayerPrefs.GetString("controller.lastIp", string.Empty));
            ControllerEventSystemBootstrap.Ensure();
            BuildInterface();
            BuildSettingsPanel();
            ControllerEventSystemBootstrap.Ensure();
            EnsureCanvasRaycasters();
            BuildTouchDebug();
            ConfigureInput();
            compass = inputProvider != null ? inputProvider.Compass : null;
            proximitySensor = inputProvider != null ? inputProvider.ProximitySensor : null;
            ApplyPersistedSettings();
            RefreshInput(inputProvider.CurrentInput);
            SetDiagnosticsVisible(ControllerLocalSettings.DiagnosticsVisible);
            RefreshGyroscopeStatus();
            UpdateCompassStrip();
        }

        private void OnEnable()
        {
            if (inputProvider != null)
                inputProvider.InputChanged += RefreshInput;
        }

        private void OnDisable()
        {
            if (inputProvider != null)
                inputProvider.InputChanged -= RefreshInput;
        }

        private void Update()
        {
            UpdateCompassStrip();
            if (guardIndicator != null && inputProvider != null)
                guardIndicator.gameObject.SetActive(
                    inputProvider.GetCurrentInputSnapshot().ProximityNear);
            var now = Time.unscaledTime;
            if (now >= nextSensorUiUpdate)
            {
                nextSensorUiUpdate = now + SensorUiUpdateInterval;
                UpdateSensorDiagnostics();
                RefreshDriftTestStatus();
            }
            if (Time.unscaledTime < nextGyroUiUpdate)
                return;
            nextGyroUiUpdate = Time.unscaledTime + 0.1f;
            RefreshGyroscopeStatus();
            RefreshConnectionStatus();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                inputProvider?.ResetAllInputs();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
                inputProvider?.ResetAllInputs();
        }

        private void EnsureCanvasRaycasters()
        {
            foreach (var childCanvas in GetComponentsInChildren<Canvas>(true))
            {
                if (childCanvas.GetComponent<GraphicRaycaster>() == null)
                    childCanvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private void BuildTouchDebug()
        {
            var label = CreateText(canvas.transform, string.Empty, 12, Color.white,
                TextAnchor.UpperLeft, new Vector2(0.01f, 0.91f),
                new Vector2(0.48f, 0.995f), FontStyle.Normal, false);
            label.raycastTarget = false;
            touchDebug = label.gameObject.AddComponent<ControllerTouchDebug>();
            touchDebug.Initialize(label, ControllerLocalSettings.DiagnosticsVisible);
        }

        private void BuildInterface()
        {
            canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null)
            {
                var canvasObject = new GameObject("Controller Canvas",
                    typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(transform, false);
                canvas = canvasObject.GetComponent<Canvas>();
            }

            canvas.transform.localScale = Vector3.one;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();

            var background = CreatePanel("Navy Background", canvas.transform, Navy);
            Stretch(background.GetComponent<RectTransform>());
            var topRule = CreatePanel("Top Cyan Rule", canvas.transform, Cyan);
            Place(topRule.GetComponent<RectTransform>(), new Vector2(0.025f, 0.972f),
                new Vector2(0.975f, 0.979f));
            safeRoot = CreateRect("Safe Area Layout", canvas.transform).GetComponent<RectTransform>();
            UpdateSafeArea();

            BuildHeader();
            BuildConnectionControls();
            BuildJoystick();
            BuildActionButtons();
            BuildCenterStatus();
            BuildFooter();
            BuildFloatingControls();
            BuildCompassStrip();
            controllerCanvasGroup = safeRoot.gameObject.AddComponent<CanvasGroup>();
            controllerCanvasGroup.alpha = 0f;
            controllerCanvasGroup.interactable = false;
            controllerCanvasGroup.blocksRaycasts = false;
            BuildConnectionScreen();
        }

        private void BuildCompassStrip()
        {
            compassStrip = CreatePanel("Compass Strip", safeRoot,
                new Color(Navy.r, Navy.g, Navy.b, 0.78f));
            var stripRect = compassStrip.GetComponent<RectTransform>();
            stripRect.anchorMin = stripRect.anchorMax = new Vector2(0.5f, 1f);
            stripRect.pivot = new Vector2(0.5f, 1f);
            stripRect.anchoredPosition = new Vector2(0f, -180f);
            stripRect.sizeDelta = new Vector2(1152f, 28f);
            compassStrip.GetComponent<Image>().raycastTarget = false;
            compassStrip.AddComponent<RectMask2D>();
            AddOutline(compassStrip, new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f), 1f);

            compassTickMarks = new RectTransform[360 / CompassTickIntervalDegrees];
            compassTickBearings = new int[compassTickMarks.Length];
            for (var i = 0; i < compassTickMarks.Length; i++)
            {
                var bearing = i * CompassTickIntervalDegrees;
                var major = bearing % 30 == 0;
                var tick = CreatePanel("Compass Tick " + bearing, compassStrip.transform,
                    major ? new Color(0.78f, 0.9f, 1f, 0.9f) : new Color(0.48f, 0.62f, 0.76f, 0.72f));
                tick.GetComponent<Image>().raycastTarget = false;
                var tickRect = tick.GetComponent<RectTransform>();
                tickRect.anchorMin = tickRect.anchorMax = new Vector2(0.5f, 0.5f);
                tickRect.pivot = new Vector2(0.5f, 0.5f);
                tickRect.sizeDelta = new Vector2(major ? 2f : 1f, major ? 13f : 7f);
                compassTickMarks[i] = tickRect;
                compassTickBearings[i] = bearing;
            }

            compassCardinalLabels = new RectTransform[CompassCardinalBearings.Length];
            for (var i = 0; i < compassCardinalLabels.Length; i++)
            {
                var cardinal = CreateText(compassStrip.transform,
                    new[] { "N", "E", "S", "W" }[i], 13,
                    i == 0 ? Cyan : Color.white, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), FontStyle.Bold);
                cardinal.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                cardinal.rectTransform.sizeDelta = new Vector2(24f, 22f);
                cardinal.raycastTarget = false;
                compassCardinalLabels[i] = cardinal.rectTransform;
            }

            var centerMarker = CreatePanel("Compass Center Marker", compassStrip.transform, Cyan);
            centerMarker.GetComponent<Image>().raycastTarget = false;
            var markerRect = centerMarker.GetComponent<RectTransform>();
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.sizeDelta = new Vector2(2f, 20f);
        }

        private void UpdateCompassStrip()
        {
            if (compassStrip == null)
                return;

            var available = compass != null && compass.Available;
            if (compassStrip.activeSelf != available)
                compassStrip.SetActive(available);
            if (!available)
                return;

            var heading = compass.DisplayHeading;
            for (var i = 0; i < compassTickMarks.Length; i++)
            {
                var position = compassTickMarks[i].anchoredPosition;
                position.x = Mathf.DeltaAngle(heading, compassTickBearings[i]) *
                             CompassPixelsPerDegree;
                compassTickMarks[i].anchoredPosition = position;
            }

            for (var i = 0; i < compassCardinalLabels.Length; i++)
            {
                var position = compassCardinalLabels[i].anchoredPosition;
                position.x = Mathf.DeltaAngle(heading, CompassCardinalBearings[i]) *
                             CompassPixelsPerDegree;
                compassCardinalLabels[i].anchoredPosition = position;
            }
        }

        private void BuildHeader()
        {
            CreateText(safeRoot, "ROBOCLASH", 30, Color.white, TextAnchor.MiddleLeft,
                new Vector2(0.035f, 0.895f), new Vector2(0.26f, 0.97f), FontStyle.Bold);
            CreateText(safeRoot, "CONTROLLER", 14, Cyan, TextAnchor.MiddleLeft,
                new Vector2(0.18f, 0.895f), new Vector2(0.34f, 0.97f), FontStyle.Bold);
            connectionStatusText = CreateText(safeRoot, "NOT CONNECTED", 15, Muted,
                TextAnchor.MiddleCenter,
                new Vector2(0.40f, 0.905f), new Vector2(0.60f, 0.96f), FontStyle.Bold);

            CreateButton(safeRoot, "SETTINGS", new Vector2(0.81f, 0.90f),
                new Vector2(0.90f, 0.965f), Panel, Cyan, 15, () =>
                {
                    settingsPanel.SetActive(!settingsPanel.activeSelf);
                    if (settingsPanel.activeSelf)
                        diagnosticsPanel.SetActive(false);
                });
            CreateButton(safeRoot, "DEBUG", new Vector2(0.91f, 0.90f),
                new Vector2(0.975f, 0.965f), Panel, Purple, 14, () =>
            {
                SetDiagnosticsVisible(!diagnosticsPanel.activeSelf);
            });
        }

        private void BuildConnectionControls()
        {
            connectionButton = CreateButton(safeRoot, "DISCONNECT",
                new Vector2(0.42f, 0.845f), new Vector2(0.58f, 0.89f),
                Panel, Cyan, 15, ToggleConnection);
        }

        private void BuildConnectionScreen()
        {
            connectionPanel = CreatePanel("Wi-Fi Connection Screen", canvas.transform, Navy);
            Stretch(connectionPanel.GetComponent<RectTransform>());

            var card = CreatePanel("Connection Card", connectionPanel.transform, Panel);
            SetAnchoredSize(card.GetComponent<RectTransform>(), new Vector2(0.5f, 0.52f),
                new Vector2(900f, 920f));
            AddOutline(card, Cyan, 3f);

            CreateText(card.transform, "ROBOCLASH", 38, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.91f), new Vector2(0.92f, 0.98f), FontStyle.Bold);
            CreateText(card.transform, "PHONE CONTROLLER  //  WI-FI LINK",
                17, Cyan, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.86f),
                new Vector2(0.92f, 0.91f), FontStyle.Bold);
            CreateText(card.transform, "Enter the PC's Wi-Fi IPv4 address shown in RoboClash.",
                17, Muted, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.80f),
                new Vector2(0.92f, 0.86f));
            CreateText(card.transform, "Same Wi-Fi  //  Each phone connects independently  //  TCP 17862",
                13, Muted, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.75f),
                new Vector2(0.92f, 0.80f));

            var inputDisplay = CreatePanel("PC IPv4 Address Display", card.transform, Dark);
            Place(inputDisplay.GetComponent<RectTransform>(), new Vector2(0.10f, 0.67f),
                new Vector2(0.90f, 0.74f));
            AddOutline(inputDisplay, new Color(0.25f, 0.42f, 0.58f), 1f);
            ipAddressDisplay = CreateText(inputDisplay.transform, string.Empty, 27, Color.white,
                TextAnchor.MiddleCenter, new Vector2(0.035f, 0.05f),
                new Vector2(0.965f, 0.95f), FontStyle.Bold, false);
            RefreshIPAddressDisplay();

            AddIpKey(card.transform, "1", '1', 0.10f, 0.36f, 0.58f, 0.66f);
            AddIpKey(card.transform, "2", '2', 0.37f, 0.63f, 0.58f, 0.66f);
            AddIpKey(card.transform, "3", '3', 0.64f, 0.90f, 0.58f, 0.66f);
            AddIpKey(card.transform, "4", '4', 0.10f, 0.36f, 0.49f, 0.57f);
            AddIpKey(card.transform, "5", '5', 0.37f, 0.63f, 0.49f, 0.57f);
            AddIpKey(card.transform, "6", '6', 0.64f, 0.90f, 0.49f, 0.57f);
            AddIpKey(card.transform, "7", '7', 0.10f, 0.36f, 0.40f, 0.48f);
            AddIpKey(card.transform, "8", '8', 0.37f, 0.63f, 0.40f, 0.48f);
            AddIpKey(card.transform, "9", '9', 0.64f, 0.90f, 0.40f, 0.48f);
            AddIpKey(card.transform, ".", '.', 0.10f, 0.36f, 0.31f, 0.39f);
            AddIpKey(card.transform, "0", '0', 0.37f, 0.63f, 0.31f, 0.39f);
            CreateButton(card.transform, "BACKSPACE", new Vector2(0.64f, 0.31f),
                new Vector2(0.90f, 0.39f), Dark, Cyan, 17, ipAddressEntry.Backspace);
            CreateButton(card.transform, "CLEAR", new Vector2(0.10f, 0.20f),
                new Vector2(0.38f, 0.28f), Dark, Gold, 17, ipAddressEntry.Clear);
            connectButton = CreateButton(card.transform, "CONNECT",
                new Vector2(0.42f, 0.20f), new Vector2(0.90f, 0.28f),
                new Color(0.025f, 0.24f, 0.31f), Cyan, 20, ConnectToHost);
            connectionPanelStatusText = CreateText(card.transform, "NOT CONNECTED",
                15, Muted, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.09f),
                new Vector2(0.92f, 0.16f), FontStyle.Bold, false);
        }

        private void AddIpKey(Transform parent, string label, char character,
            float minX, float maxX, float minY, float maxY)
        {
            CreateButton(parent, label, new Vector2(minX, minY), new Vector2(maxX, maxY),
                Dark, Cyan, 22, () => ipAddressEntry.Append(character));
        }

        private void RefreshIPAddressDisplay()
        {
            if (ipAddressDisplay == null)
                return;

            var hasAddress = !string.IsNullOrEmpty(ipAddressEntry.Value);
            ipAddressDisplay.text = hasAddress ? ipAddressEntry.Value : "192.168.1.x";
            ipAddressDisplay.color = hasAddress ? Color.white : Muted;
        }

        private void BuildJoystick()
        {
            var baseObject = CreatePanel("Movement and Aim Joystick", safeRoot,
                new Color(0.035f, 0.13f, 0.21f, 0.96f));
            var baseRect = baseObject.GetComponent<RectTransform>();
            SetAnchoredSize(baseRect, new Vector2(0.16f, 0.285f), new Vector2(276f, 276f));
            baseObject.GetComponent<Image>().raycastTarget = true;
            AddOutline(baseObject, Cyan, 3f);

            var ring = CreatePanel("Inner Joystick Ring", baseObject.transform,
                new Color(0.02f, 0.06f, 0.12f, 0.75f));
            SetAnchoredSize(ring.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(186f, 186f));
            ring.GetComponent<Image>().raycastTarget = false;
            AddOutline(ring, new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f), 2f);

            var knob = CreatePanel("Joystick Knob", baseObject.transform,
                new Color(0.1f, 0.78f, 0.98f, 0.95f));
            SetAnchoredSize(knob.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(92f, 92f));
            knob.GetComponent<Image>().raycastTarget = false;
            AddOutline(knob, Color.white, 2f);

            joystick = baseObject.AddComponent<ControllerVirtualJoystick>();
            joystick.Configure(baseRect, knob.GetComponent<RectTransform>(),
                ControllerLocalSettings.JoystickDeadZone);

            CreateText(baseObject.transform, "▲", 24, Cyan, TextAnchor.MiddleCenter,
                new Vector2(0.42f, 0.79f), new Vector2(0.58f, 0.93f), FontStyle.Bold, false);
            CreateText(baseObject.transform, "▼", 24, Cyan, TextAnchor.MiddleCenter,
                new Vector2(0.42f, 0.07f), new Vector2(0.58f, 0.21f), FontStyle.Bold, false);
            CreateText(baseObject.transform, "◀", 24, Purple, TextAnchor.MiddleCenter,
                new Vector2(0.07f, 0.42f), new Vector2(0.21f, 0.58f), FontStyle.Bold, false);
            CreateText(baseObject.transform, "▶", 24, Purple, TextAnchor.MiddleCenter,
                new Vector2(0.79f, 0.42f), new Vector2(0.93f, 0.58f), FontStyle.Bold, false);

            movementStatus = CreateText(safeRoot, "MOVE  0.00     AIM  0.00",
                14, Color.white, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.055f),
                new Vector2(0.28f, 0.105f), FontStyle.Bold);
            CreateText(safeRoot, "UP / DOWN = MOVE     LEFT / RIGHT = STRAFE",
                11, Muted, TextAnchor.MiddleCenter, new Vector2(0.025f, 0.015f),
                new Vector2(0.30f, 0.052f));
        }

        private void BuildActionButtons()
        {
            jumpButton = CreateActionButton("JUMP", "↑", ControllerAction.Jump, false,
                new Vector2(0.705f, 0.36f), new Vector2(136f, 136f), Green);
            shootButton = CreateActionButton("SHOOT", "✦", ControllerAction.Shoot, true,
                new Vector2(0.875f, 0.29f), new Vector2(174f, 174f), Purple);
            gyroClutchButton = CreateActionButton("CLUTCH", "C", ControllerAction.GyroClutch, true,
                new Vector2(0.75f, 0.29f), new Vector2(82f, 82f), Cyan);
            abilityButton = CreateActionButton("ABILITY", "A", ControllerAction.Ability, false,
                new Vector2(0.69f, 0.13f), new Vector2(124f, 124f), Gold);
            ultimateButton = CreateActionButton("ULTIMATE", "U", ControllerAction.Ultimate, false,
                new Vector2(0.92f, 0.57f), new Vector2(134f, 134f), Gold);
            reloadButton = CreateActionButton("RELOAD", "R", ControllerAction.Reload, false,
                new Vector2(0.81f, 0.115f), new Vector2(102f, 102f), Cyan);
        }

        private void BuildCenterStatus()
        {
            CreateText(safeRoot, "ARCADE CONTROL DECK", 24, Cyan, TextAnchor.MiddleCenter,
                new Vector2(0.33f, 0.66f), new Vector2(0.67f, 0.73f), FontStyle.Bold);
            CreateText(safeRoot, "Tilt left / right to turn  //  tilt forward / back to aim up / down",
                14, Muted, TextAnchor.MiddleCenter, new Vector2(0.32f, 0.61f),
                new Vector2(0.68f, 0.66f));
            gyroStatus = CreateText(safeRoot, "GYROSCOPE  //  CHECKING",
                15, Green, TextAnchor.MiddleCenter, new Vector2(0.34f, 0.47f),
                new Vector2(0.66f, 0.53f), FontStyle.Bold);
            gyroWarning = CreateText(safeRoot, string.Empty, 13, Gold, TextAnchor.MiddleCenter,
                new Vector2(0.34f, 0.40f), new Vector2(0.66f, 0.47f));
            turnValueText = CreateText(safeRoot, "TURN  0.00",
                15, Color.white, TextAnchor.MiddleCenter, new Vector2(0.39f, 0.335f),
                new Vector2(0.61f, 0.38f), FontStyle.Bold);
            pitchValueText = CreateText(safeRoot, "PITCH  0.00",
                15, Color.white, TextAnchor.MiddleCenter, new Vector2(0.39f, 0.285f),
                new Vector2(0.61f, 0.33f), FontStyle.Bold);
            gyroBar = CreateInputBar(safeRoot, "Gyro Activity",
                new Vector2(0.375f, 0.245f), new Vector2(0.625f, 0.275f), Gold);
        }

        private void BuildFooter()
        {
            CreateText(safeRoot, "HOLD SHOOT  •  TAP ACTIONS  •  TILT LEFT / RIGHT TO TURN, FORWARD / BACK TO AIM",
                12, Muted, TextAnchor.MiddleCenter, new Vector2(0.34f, 0.015f),
                new Vector2(0.69f, 0.055f));
            CreateText(safeRoot, "TCP LINK  //  PORT 17862",
                10, new Color(0.42f, 0.57f, 0.71f), TextAnchor.MiddleRight,
                new Vector2(0.70f, 0.015f), new Vector2(0.975f, 0.05f));
        }

        private void BuildFloatingControls()
        {
            diagnosticsPanel = CreatePanel("Input Diagnostics", safeRoot,
                new Color(0.01f, 0.025f, 0.06f, 0.98f));
            SetAnchoredSize(diagnosticsPanel.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.81f), new Vector2(570f, 295f));
            AddOutline(diagnosticsPanel, Cyan, 2f);
            CreateText(diagnosticsPanel.transform, "LIVE INPUT DIAGNOSTICS",
                18, Cyan, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.84f),
                new Vector2(0.78f, 0.98f), FontStyle.Bold);
            CreateButton(diagnosticsPanel.transform, "×", new Vector2(0.86f, 0.86f),
                new Vector2(0.97f, 0.98f), Dark, Color.white, 18, () => SetDiagnosticsVisible(false));

            moveBar = CreateInputBar(diagnosticsPanel.transform, "MoveForward",
                new Vector2(0.05f, 0.68f), new Vector2(0.69f, 0.77f), Green);
            aimBar = CreateInputBar(diagnosticsPanel.transform, "AimHorizontal",
                new Vector2(0.05f, 0.54f), new Vector2(0.69f, 0.63f), Purple);
            diagnosticGyroBar = CreateInputBar(diagnosticsPanel.transform, "GyroTurn (left / right)",
                new Vector2(0.05f, 0.43f), new Vector2(0.69f, 0.51f), Gold);
            diagnosticGyroPitchBar = CreateInputBar(diagnosticsPanel.transform, "GyroPitch (up / down)",
                new Vector2(0.05f, 0.28f), new Vector2(0.69f, 0.36f), Cyan);
            rawGyroText = CreateText(diagnosticsPanel.transform, "GYRO RATE  TURN 0.0°/s  PITCH 0.0°/s",
                11, Muted, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.14f),
                new Vector2(0.69f, 0.24f));
            compassProximityText = CreateText(diagnosticsPanel.transform, string.Empty,
                11, Muted, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.055f),
                new Vector2(0.97f, 0.13f));

            actionStatusTexts = new Text[5];
            var actions = new[] { "SHOOT", "JUMP", "ABILITY", "ULTIMATE", "RELOAD" };
            for (var i = 0; i < actions.Length; i++)
            {
                var y = 0.79f - i * 0.145f;
                actionStatusTexts[i] = CreateText(diagnosticsPanel.transform, actions[i] + "  RELEASED",
                    12, Muted, TextAnchor.MiddleLeft, new Vector2(0.73f, y - 0.07f),
                    new Vector2(0.97f, y + 0.02f), FontStyle.Bold);
            }
            guardIndicator = CreateText(safeRoot, "GUARD", 14, Green,
                TextAnchor.MiddleCenter, new Vector2(0.76f, 0.76f),
                new Vector2(0.88f, 0.81f), FontStyle.Bold);
            guardIndicator.raycastTarget = false;
            guardIndicator.gameObject.SetActive(false);
            diagnosticsPanel.SetActive(false);
        }

        private void BuildSettingsPanel()
        {
            settingsPanel = CreatePanel("Controller Settings", safeRoot,
                new Color(0.012f, 0.028f, 0.068f, 0.99f));
            SetAnchoredSize(settingsPanel.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(900f, 820f));
            AddOutline(settingsPanel, Cyan, 3f);
            CreateText(settingsPanel.transform, "CONTROLLER SETTINGS", 26, Color.white, TextAnchor.MiddleLeft,
                new Vector2(0.06f, 0.91f), new Vector2(0.78f, 0.99f), FontStyle.Bold);
            CreateText(settingsPanel.transform, "LOCAL DEVICE PREFERENCES",
                12, Muted, TextAnchor.MiddleLeft, new Vector2(0.065f, 0.86f),
                new Vector2(0.75f, 0.91f));
            CreateButton(settingsPanel.transform, "CLOSE", new Vector2(0.82f, 0.91f),
                new Vector2(0.96f, 0.98f), Dark, Cyan, 14, () => settingsPanel.SetActive(false));

            var gyroSettingsColumn = CreateRect("Gyroscope Settings Column", settingsPanel.transform);
            Place(gyroSettingsColumn.GetComponent<RectTransform>(),
                new Vector2(0.035f, 0.10f), new Vector2(0.525f, 0.86f));
            var otherSettingsColumn = CreateRect("Other Settings Column", settingsPanel.transform);
            Place(otherSettingsColumn.GetComponent<RectTransform>(),
                new Vector2(0.545f, 0.10f), new Vector2(0.965f, 0.86f));

            gyroToggle = CreateToggle(otherSettingsColumn.transform, "GYRO AIMING (PRIMARY)",
                new Vector2(0.02f, 0.86f), new Vector2(0.98f, 0.98f), Green,
                ControllerLocalSettings.GyroscopeEnabled, value =>
                {
                    ControllerLocalSettings.GyroscopeEnabled = value;
                    gyroscope.SetEnabled(value);
                    RefreshGyroscopeStatus();
                });

            yawSensitivitySlider = CreateSliderRow(gyroSettingsColumn.transform, "YAW SENSITIVITY",
                0.76f, 0.1f, 8f, ControllerLocalSettings.GyroscopeYawSensitivity, Purple,
                value =>
                {
                    ControllerLocalSettings.GyroscopeYawSensitivity = value;
                    UpdateSettingValue(settingsYawSensitivityValue, $"{value:0.00}×");
                    ApplyGyroscopeSettings();
                }, out settingsYawSensitivityValue, "2.50×");
            pitchSensitivitySlider = CreateSliderRow(gyroSettingsColumn.transform, "PITCH SENSITIVITY",
                0.64f, 0.1f, 8f, ControllerLocalSettings.GyroscopePitchSensitivity, Purple,
                value =>
                {
                    ControllerLocalSettings.GyroscopePitchSensitivity = value;
                    UpdateSettingValue(settingsPitchSensitivityValue, $"{value:0.00}×");
                    ApplyGyroscopeSettings();
                }, out settingsPitchSensitivityValue, "1.80×");
            gyroDeadZoneSlider = CreateSliderRow(gyroSettingsColumn.transform, "ANGULAR DEAD ZONE",
                0.52f, 0f, 10f, ControllerLocalSettings.GyroscopeDeadZone, Gold,
                value =>
                {
                    ControllerLocalSettings.GyroscopeDeadZone = value;
                    UpdateSettingValue(settingsDeadZoneValue, $"{value:0.0}°/s");
                    ApplyGyroscopeSettings();
                }, out settingsDeadZoneValue, "1.5°/s");
            responseCurveSlider = CreateSliderRow(gyroSettingsColumn.transform, "RESPONSE CURVE",
                0.40f, 1f, 2.5f, ControllerLocalSettings.GyroscopeResponseCurve, Gold,
                value =>
                {
                    ControllerLocalSettings.GyroscopeResponseCurve = value;
                    UpdateSettingValue(settingsResponseCurveValue, $"{value:0.00}");
                    ApplyGyroscopeSettings();
                }, out settingsResponseCurveValue, "1.50");
            smoothingSlider = CreateSliderRow(gyroSettingsColumn.transform, "SMOOTHING",
                0.28f, 1f, 30f, ControllerLocalSettings.GyroscopeSmoothing, Cyan,
                value =>
                {
                    ControllerLocalSettings.GyroscopeSmoothing = value;
                    UpdateSettingValue(settingsSmoothingValue, $"{value:0.0}");
                    ApplyGyroscopeSettings();
                }, out settingsSmoothingValue, "20.0");

            invertYawToggle = CreateToggle(otherSettingsColumn.transform, "INVERT YAW",
                new Vector2(0.02f, 0.77f), new Vector2(0.98f, 0.85f), Gold,
                ControllerLocalSettings.InvertGyroscopeYaw, value =>
                {
                    ControllerLocalSettings.InvertGyroscopeYaw = value;
                    ApplyGyroscopeSettings();
                });
            invertPitchToggle = CreateToggle(otherSettingsColumn.transform, "INVERT PITCH",
                new Vector2(0.02f, 0.68f), new Vector2(0.98f, 0.76f), Gold,
                ControllerLocalSettings.InvertGyroscopePitch, value =>
                {
                    ControllerLocalSettings.InvertGyroscopePitch = value;
                    ApplyGyroscopeSettings();
                });
            compassAssistToggle = CreateToggle(otherSettingsColumn.transform, "COMPASS ASSIST",
                new Vector2(0.02f, 0.59f), new Vector2(0.98f, 0.67f), Cyan,
                ControllerLocalSettings.CompassAssist, value =>
                {
                    ControllerLocalSettings.CompassAssist = value;
                    if (gyroscope != null)
                        gyroscope.CompassAssist = value;
                    ControllerLocalSettings.Save();
                });
            invertCompassToggle = CreateToggle(otherSettingsColumn.transform,
                "INVERT COMPASS DIRECTION",
                new Vector2(0.02f, 0.50f), new Vector2(0.98f, 0.58f), Cyan,
                gyroscope.InvertCompassSign, value =>
                {
                    gyroscope.InvertCompassSign = value;
                });

            joystickDeadZoneSlider = CreateSliderRow(gyroSettingsColumn.transform, "JOYSTICK DEAD ZONE",
                0.16f, 0f, 0.5f, ControllerLocalSettings.JoystickDeadZone, Green,
                value =>
                {
                    ControllerLocalSettings.JoystickDeadZone = value;
                    joystick.SetDeadZone(value);
                    UpdateSettingValue(settingsJoystickDeadZoneValue, $"{value:0.00}");
                }, out settingsJoystickDeadZoneValue, "0.12");

            diagnosticsToggle = CreateToggle(otherSettingsColumn.transform, "SHOW INPUT DIAGNOSTICS",
                new Vector2(0.02f, 0.41f), new Vector2(0.98f, 0.49f), Purple,
                ControllerLocalSettings.DiagnosticsVisible, value =>
                {
                    ControllerLocalSettings.DiagnosticsVisible = value;
                    SetDiagnosticsVisible(value);
                });
            var compassOffsetButton = CreateButton(otherSettingsColumn.transform,
                string.Empty, new Vector2(0.02f, 0.32f), new Vector2(0.98f, 0.40f),
                Dark, Cyan, 13, AdvanceCompassOffset);
            compassOffsetButtonLabel = compassOffsetButton.GetComponentInChildren<Text>();
            RefreshCompassOffsetLabel();
            CreateButton(otherSettingsColumn.transform, "DRIFT TEST (2 MIN)",
                new Vector2(0.02f, 0.23f), new Vector2(0.98f, 0.31f),
                new Color(0.18f, 0.12f, 0.03f), Gold, 13,
                () => gyroscope?.StartDriftTest());
            driftTestStatusText = CreateText(otherSettingsColumn.transform, "Drift test: not run",
                11, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.14f),
                new Vector2(0.97f, 0.22f));
            CreateButton(gyroSettingsColumn.transform, "RECENTER GYRO",
                new Vector2(0.02f, 0.025f), new Vector2(0.46f, 0.12f),
                new Color(0.18f, 0.12f, 0.03f), Gold, 13, RecenterGyroscope);
            CreateText(gyroSettingsColumn.transform,
                "Hold still during calibration. Drift test: lay the phone still, press RECENTER GYRO and wait 1 s before each run; test Compass assist OFF and ON.",
                10, Muted, TextAnchor.MiddleLeft, new Vector2(0.50f, 0.025f),
                new Vector2(0.98f, 0.12f));
            settingsPanel.SetActive(false);
        }

        private void ConfigureInput()
        {
            inputProvider.Configure(joystick, gyroscope, shootButton, jumpButton,
                abilityButton, ultimateButton, reloadButton, gyroClutchButton);
        }

        private void RecenterGyroscope()
        {
            gyroscope?.Recenter();
            inputProvider?.RequestRecenter();
        }

        private void ToggleConnection()
        {
            if (networkClient == null)
                return;

            if (networkClient.IsConnected)
                networkClient.Disconnect();

            RefreshConnectionStatus();
        }

        private void ConnectToHost()
        {
            if (networkClient == null)
                return;

            networkClient.Connect(ipAddressEntry.Value);
            RefreshConnectionStatus();
        }

        private void RefreshConnectionStatus()
        {
            if (networkClient == null)
                return;

            var connected = networkClient.IsConnected;
            var connecting = networkClient.Status.StartsWith("CONNECTING", StringComparison.Ordinal);
            var statusColor = connected
                ? Green
                : networkClient.Status.StartsWith("CONNECTION FAILED", StringComparison.Ordinal) ||
                  networkClient.Status == "CONNECTION LOST" ||
                  networkClient.Status == "ENTER A VALID IPV4 ADDRESS"
                    ? Gold
                    : Muted;

            if (connectionStatusText != null)
            {
                connectionStatusText.text = connected ? "CONNECTED // SLOT SET BY GAME" : networkClient.Status;
                connectionStatusText.color = statusColor;
            }
            if (connectionPanelStatusText != null)
            {
                connectionPanelStatusText.text = connected
                    ? "CONNECTED // SLOT SET BY GAME"
                    : networkClient.Status;
                connectionPanelStatusText.color = statusColor;
            }
            if (controllerCanvasGroup != null)
            {
                controllerCanvasGroup.alpha = connected ? 1f : 0f;
                controllerCanvasGroup.interactable = connected;
                controllerCanvasGroup.blocksRaycasts = connected;
            }
            if (connectionPanel != null)
                connectionPanel.SetActive(!connected);
            if (connectButton != null)
                connectButton.interactable = !connecting;

            var buttonText = connectionButton != null
                ? connectionButton.GetComponentInChildren<Text>()
                : null;
            if (buttonText != null)
                buttonText.text = "DISCONNECT";

            if (wasConnected && !connected)
                inputProvider?.ResetAllInputs();
            else if (!wasConnected && connected)
            {
                PlayerPrefs.SetString("controller.lastIp", ipAddressEntry.Value);
                PlayerPrefs.Save();
            }
            wasConnected = connected;
        }

        private void ApplyPersistedSettings()
        {
            gyroToggle.SetIsOnWithoutNotify(ControllerLocalSettings.GyroscopeEnabled);
            invertYawToggle.SetIsOnWithoutNotify(ControllerLocalSettings.InvertGyroscopeYaw);
            invertPitchToggle.SetIsOnWithoutNotify(ControllerLocalSettings.InvertGyroscopePitch);
            compassAssistToggle.SetIsOnWithoutNotify(ControllerLocalSettings.CompassAssist);
            invertCompassToggle.SetIsOnWithoutNotify(gyroscope.InvertCompassSign);
            diagnosticsToggle.SetIsOnWithoutNotify(ControllerLocalSettings.DiagnosticsVisible);
            yawSensitivitySlider.SetValueWithoutNotify(ControllerLocalSettings.GyroscopeYawSensitivity);
            pitchSensitivitySlider.SetValueWithoutNotify(ControllerLocalSettings.GyroscopePitchSensitivity);
            gyroDeadZoneSlider.SetValueWithoutNotify(ControllerLocalSettings.GyroscopeDeadZone);
            responseCurveSlider.SetValueWithoutNotify(ControllerLocalSettings.GyroscopeResponseCurve);
            smoothingSlider.SetValueWithoutNotify(ControllerLocalSettings.GyroscopeSmoothing);
            joystickDeadZoneSlider.SetValueWithoutNotify(ControllerLocalSettings.JoystickDeadZone);
            UpdateSettingValue(settingsYawSensitivityValue, $"{yawSensitivitySlider.value:0.00}×");
            UpdateSettingValue(settingsPitchSensitivityValue, $"{pitchSensitivitySlider.value:0.00}×");
            UpdateSettingValue(settingsDeadZoneValue, $"{gyroDeadZoneSlider.value:0.0}°/s");
            UpdateSettingValue(settingsResponseCurveValue, $"{responseCurveSlider.value:0.00}");
            UpdateSettingValue(settingsSmoothingValue, $"{smoothingSlider.value:0.0}");
            UpdateSettingValue(settingsJoystickDeadZoneValue, $"{joystickDeadZoneSlider.value:0.00}");
            joystick.SetDeadZone(ControllerLocalSettings.JoystickDeadZone);
            ApplyGyroscopeSettings();
        }

        private void ApplyGyroscopeSettings()
        {
            gyroscope.Configure(ControllerLocalSettings.GyroscopeEnabled,
                ControllerLocalSettings.GyroscopeYawSensitivity,
                ControllerLocalSettings.GyroscopePitchSensitivity,
                ControllerLocalSettings.GyroscopeDeadZone,
                ControllerLocalSettings.GyroscopeResponseCurve,
                ControllerLocalSettings.GyroscopeSmoothing,
                ControllerLocalSettings.InvertGyroscopeYaw,
                ControllerLocalSettings.InvertGyroscopePitch);
            gyroscope.CompassAssist = ControllerLocalSettings.CompassAssist;
            ControllerLocalSettings.Save();
            RefreshGyroscopeStatus();
        }

        private void UpdateSensorDiagnostics()
        {
            if (compassProximityText == null)
                return;

            compassDiagnosticsBuilder.Clear();
            if (compass != null && compass.LegacyInputFailed)
                compassDiagnosticsBuilder.Append("Compass: unavailable (legacy input disabled)");
            else if (compass != null && compass.Available)
                compassDiagnosticsBuilder.Append("Compass: ")
                    .Append(compass.RelativeHeading.ToString("0"))
                    .Append("°");
            else
                compassDiagnosticsBuilder.Append("Compass: n/a");

            var compassAvailable = compass != null && compass.Available;
            var proximityAvailable = proximitySensor != null && proximitySensor.Available;
            var driftError = compassAvailable && gyroscope != null && gyroscope.IsAvailable
                ? gyroscope.DriftErrorDeg.ToString("0.0") + "°"
                : "n/a";
            var proximity = proximityAvailable
                ? proximitySensor.Near ? "NEAR" : "FAR"
                : "n/a";
            var inputSnapshot = inputProvider != null
                ? inputProvider.GetCurrentInputSnapshot()
                : default;
            var accelerometer = inputSnapshot.AccelerometerValid
                ? "active"
                : "n/a";
            var assist = gyroscope != null && gyroscope.CompassAssist ? "ON" : "OFF";
            compassDiagnosticsBuilder.Append(" | drift err: ").Append(driftError)
                .Append(" | assist ").Append(assist)
                .Append(" | Accelerometer: ").Append(accelerometer)
                .Append(" | Proximity: ").Append(proximity);
            if (gyroscope != null)
                compassDiagnosticsBuilder.Append(" | sign: ").Append(gyroscope.CompassSignCheck);
            compassProximityText.text = compassDiagnosticsBuilder.ToString();
        }

        private void AdvanceCompassOffset()
        {
            ControllerCompass.OffsetDegrees += 90;
            RefreshCompassOffsetLabel();
            UpdateCompassStrip();
        }

        private void RefreshCompassOffsetLabel()
        {
            if (compassOffsetButtonLabel != null)
                compassOffsetButtonLabel.text =
                    "Compass offset: " + ControllerCompass.OffsetDegrees + "° (tap +90°)";
        }

        private void RefreshDriftTestStatus()
        {
            if (driftTestStatusText == null || gyroscope == null)
                return;

            if (gyroscope.DriftTestRunning)
                driftTestStatusText.text =
                    "Drift test: " + gyroscope.DriftTestRemaining.ToString("0") + "s";
            else
                driftTestStatusText.text = string.IsNullOrEmpty(gyroscope.DriftTestResult)
                    ? "Drift test: not run"
                    : gyroscope.DriftTestResult;
        }

        private void RefreshInput(ControllerInputState state)
        {
            if (movementStatus != null)
                movementStatus.text = $"MOVE  {state.MoveForward:+0.00;-0.00;0.00}     " +
                                      $"AIM  {state.AimHorizontal:+0.00;-0.00;0.00}";
            moveBar?.SetValue(state.MoveForward);
            aimBar?.SetValue(state.AimHorizontal);
            gyroBar?.SetValue(state.GyroTurn);
            diagnosticGyroBar?.SetValue(state.GyroTurn);
            diagnosticGyroPitchBar?.SetValue(state.GyroPitch);
            if (turnValueText != null)
                turnValueText.text = $"TURN  {state.GyroTurn:+0.00;-0.00;0.00}";
            if (pitchValueText != null)
                pitchValueText.text = $"PITCH  {state.GyroPitch:+0.00;-0.00;0.00}";

            if (actionStatusTexts == null)
                return;
            var pressed = new[] { state.Shoot, state.Jump, state.Ability, state.Ultimate, state.Reload };
            for (var i = 0; i < actionStatusTexts.Length; i++)
            {
                actionStatusTexts[i].text =
                    $"{new[] { "SHOOT", "JUMP", "ABILITY", "ULTIMATE", "RELOAD" }[i]}  " +
                    (pressed[i] ? "PRESSED" : "RELEASED");
                actionStatusTexts[i].color = pressed[i] ? Green : Muted;
            }
        }

        private void RefreshGyroscopeStatus()
        {
            if (gyroscope == null || gyroStatus == null)
                return;

            if (!gyroscope.IsAvailable)
            {
                gyroStatus.text = "GYROSCOPE  //  UNAVAILABLE";
                gyroStatus.color = Gold;
                gyroWarning.text = "Motion turning is unavailable on this device; touch controls remain active.";
            }
            else if (!ControllerLocalSettings.GyroscopeEnabled)
            {
                gyroStatus.text = "GYROSCOPE  //  DISABLED";
                gyroStatus.color = Muted;
                gyroWarning.text = "Enable gyroscope turning in Settings.";
            }
            else if (gyroscope.IsCalibrating)
            {
                gyroStatus.text = "GYROSCOPE  //  CALIBRATING";
                gyroStatus.color = Gold;
                gyroWarning.text = "Keep the phone still.";
            }
            else
            {
                gyroStatus.text = "GYROSCOPE  //  ACTIVE";
                gyroStatus.color = Green;
                gyroWarning.text = string.Empty;
            }

            if (rawGyroText != null)
                rawGyroText.text =
                    $"GYRO RATE  TURN {gyroscope.RawRateDegreesPerSecond:+0.0;-0.0;0.0}°/s  " +
                    $"PITCH {gyroscope.RawPitchRateDegreesPerSecond:+0.0;-0.0;0.0}°/s";
        }

        private void SetDiagnosticsVisible(bool visible)
        {
            if (diagnosticsPanel != null)
                diagnosticsPanel.SetActive(visible);
            touchDebug?.SetDiagnosticsEnabled(visible);
            ControllerLocalSettings.DiagnosticsVisible = visible;
            if (diagnosticsToggle != null)
                diagnosticsToggle.SetIsOnWithoutNotify(visible);
        }

        private ControllerActionButton CreateActionButton(string label, string icon,
            ControllerAction action, bool hold, Vector2 anchor, Vector2 size, Color color)
        {
            var root = CreatePanel(label + " Action", safeRoot,
                new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 0.96f));
            var rect = root.GetComponent<RectTransform>();
            SetAnchoredSize(rect, anchor, size);
            AddOutline(root, color, label == "SHOOT" ? 4f : 3f);

            var iconText = CreateText(root.transform, icon, label == "SHOOT" ? 42 : 36,
                color, TextAnchor.MiddleCenter, new Vector2(0.12f, 0.38f),
                new Vector2(0.88f, 0.90f), FontStyle.Bold, false);
            CreateText(root.transform, label, label.Length > 7 ? 11 : 13, Color.white,
                TextAnchor.MiddleCenter, new Vector2(0.05f, 0.08f),
                new Vector2(0.95f, 0.36f), FontStyle.Bold, false);
            iconText.raycastTarget = false;

            var image = root.GetComponent<Image>();
            image.raycastTarget = true;
            var actionButton = root.AddComponent<ControllerActionButton>();
            actionButton.Configure(action, hold, image, image.color,
                new Color(Mathf.Min(1f, color.r * 1.4f), Mathf.Min(1f, color.g * 1.4f),
                    Mathf.Min(1f, color.b * 1.4f), 1f));
            return actionButton;
        }

        private InputBar CreateInputBar(Transform parent, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            CreateText(parent, label, 12, Muted, TextAnchor.MiddleLeft,
                new Vector2(anchorMin.x, anchorMin.y - 0.11f),
                new Vector2(Mathf.Min(anchorMin.x + 0.24f, anchorMax.x), anchorMax.y));
            var track = CreatePanel(label + " Track", parent, new Color(0.15f, 0.21f, 0.29f));
            var trackRect = track.GetComponent<RectTransform>();
            Place(trackRect, anchorMin, anchorMax);
            AddOutline(track, new Color(0.25f, 0.37f, 0.48f), 1f);

            var center = CreatePanel("Neutral Center", track.transform, Muted);
            Place(center.GetComponent<RectTransform>(), new Vector2(0.495f, 0.05f),
                new Vector2(0.505f, 0.95f));
            var marker = CreatePanel("Input Marker", track.transform, color);
            var markerRect = marker.GetComponent<RectTransform>();
            markerRect.anchorMin = new Vector2(0.495f, 0.05f);
            markerRect.anchorMax = new Vector2(0.505f, 0.95f);
            markerRect.offsetMin = Vector2.zero;
            markerRect.offsetMax = Vector2.zero;
            var valueText = CreateText(parent, "0.00", 12, Color.white, TextAnchor.MiddleRight,
                new Vector2(anchorMax.x + 0.01f, anchorMin.y - 0.025f),
                new Vector2(Mathf.Min(anchorMax.x + 0.16f, 0.99f), anchorMax.y + 0.025f),
                FontStyle.Bold);
            return new InputBar { Marker = markerRect, ValueText = valueText };
        }

        private Slider CreateSliderRow(Transform parent, string label, float y,
            float minimum, float maximum, float initialValue, Color accent,
            Action<float> valueChanged, out Text valueText, string initialLabel)
        {
            CreateText(parent, label, 13, Color.white, TextAnchor.MiddleLeft,
                new Vector2(0.06f, y + 0.025f), new Vector2(0.65f, y + 0.085f), FontStyle.Bold);
            valueText = CreateText(parent, initialLabel, 13, accent, TextAnchor.MiddleRight,
                new Vector2(0.75f, y + 0.025f), new Vector2(0.94f, y + 0.085f), FontStyle.Bold);

            var sliderObject = CreateRect(label + " Slider", parent);
            Place(sliderObject.GetComponent<RectTransform>(),
                new Vector2(0.07f, y - 0.035f), new Vector2(0.93f, y + 0.015f));
            var background = CreatePanel("Track", sliderObject.transform, new Color(0.13f, 0.2f, 0.29f));
            Stretch(background.GetComponent<RectTransform>());
            background.GetComponent<Image>().raycastTarget = true;

            var fillArea = CreateRect("Fill Area", sliderObject.transform);
            Place(fillArea.GetComponent<RectTransform>(), new Vector2(0f, 0.25f), new Vector2(1f, 0.75f),
                new Vector2(8f, 0f), new Vector2(-8f, 0f));
            var fill = CreatePanel("Fill", fillArea.transform, accent);
            Stretch(fill.GetComponent<RectTransform>());

            var handleArea = CreateRect("Handle Slide Area", sliderObject.transform);
            Stretch(handleArea.GetComponent<RectTransform>(), new Vector2(0f, 0.25f),
                new Vector2(1f, 0.75f));
            var handle = CreatePanel("Handle", handleArea.transform, Color.white);
            handle.GetComponent<Image>().raycastTarget = true;
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(22f, 34f);
            var slider = sliderObject.AddComponent<Slider>();
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(Mathf.Clamp(initialValue, minimum, maximum));
            slider.onValueChanged.AddListener(value => valueChanged(value));
            return slider;
        }

        private Toggle CreateToggle(Transform parent, string label, Vector2 anchorMin,
            Vector2 anchorMax, Color accent, bool initialValue, Action<bool> changed)
        {
            var row = CreateRect(label + " Toggle Row", parent);
            Place(row.GetComponent<RectTransform>(), anchorMin, anchorMax);
            var toggleObject = CreatePanel("Toggle", row.transform, Dark);
            var toggleRect = toggleObject.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 0.15f);
            toggleRect.anchorMax = new Vector2(1f, 0.85f);
            toggleRect.pivot = new Vector2(1f, 0.5f);
            toggleRect.sizeDelta = new Vector2(72f, 0f);
            toggleRect.anchoredPosition = Vector2.zero;
            toggleObject.GetComponent<Image>().raycastTarget = true;
            AddOutline(toggleObject, accent, 2f);

            var check = CreatePanel("Check", toggleObject.transform, accent);
            Stretch(check.GetComponent<RectTransform>(), new Vector2(0.56f, 0.12f),
                new Vector2(0.88f, 0.88f));
            var toggle = toggleObject.AddComponent<Toggle>();
            toggle.targetGraphic = toggleObject.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            toggle.isOn = initialValue;
            toggle.onValueChanged.AddListener(value => changed?.Invoke(value));

            CreateText(row.transform, label, 13, Color.white, TextAnchor.MiddleLeft,
                new Vector2(0.02f, 0.05f), new Vector2(0.82f, 0.95f), FontStyle.Bold);
            return toggle;
        }

        private Button CreateButton(Transform parent, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color background, Color foreground,
            int fontSize, Action pressed)
        {
            var root = CreatePanel(label + " Button", parent, background);
            Place(root.GetComponent<RectTransform>(), anchorMin, anchorMax);
            root.GetComponent<Image>().raycastTarget = true;
            AddOutline(root, foreground, 2f);
            var button = root.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.8f);
            colors.pressedColor = new Color(0.7f, 0.85f, 1f, 0.9f);
            button.colors = colors;
            var text = CreateText(root.transform, label, fontSize, foreground,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, FontStyle.Bold, false);
            text.raycastTarget = false;
            button.onClick.AddListener(() => pressed?.Invoke());
            return button;
        }

        private void UpdateSafeArea()
        {
            if (safeRoot == null)
                return;
            var safe = Screen.safeArea;
            var width = Mathf.Max(1f, Screen.width);
            var height = Mathf.Max(1f, Screen.height);
            safeRoot.anchorMin = new Vector2(safe.xMin / width, safe.yMin / height);
            safeRoot.anchorMax = new Vector2(safe.xMax / width, safe.yMax / height);
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        private void LateUpdate()
        {
            UpdateSafeArea();
        }

        private static void UpdateSettingValue(Text target, string value)
        {
            if (target != null)
                target.text = value;
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
            var image = panel.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return panel;
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static Text CreateText(Transform parent, string value, int size, Color color,
            TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax,
            FontStyle style = FontStyle.Normal, bool raycastTarget = false)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            Place(rect, anchorMin, anchorMax);
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = raycastTarget;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max,
            Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin ?? Vector2.zero;
            rect.offsetMax = offsetMax ?? Vector2.zero;
        }

        private static void Stretch(RectTransform rect, Vector2? min = null, Vector2? max = null)
        {
            Place(rect, min ?? Vector2.zero, max ?? Vector2.one);
        }

        private static void SetAnchoredSize(RectTransform rect, Vector2 anchor, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        private static void AddOutline(GameObject target, Color color, float distance)
        {
            var outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
            outline.useGraphicAlpha = false;
        }
    }
}
