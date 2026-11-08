using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoboClashController
{
    public sealed class ControllerInputProvider : MonoBehaviour
    {
        [SerializeField] private ControllerVirtualJoystick joystick;
        [SerializeField] private ControllerGyroscope gyroscope;
        [SerializeField] private ControllerCompass compass;
        [SerializeField] private ControllerProximity proximitySensor;
        [SerializeField] private ControllerActionButton shootButton;
        [SerializeField] private ControllerActionButton jumpButton;
        [SerializeField] private ControllerActionButton abilityButton;
        [SerializeField] private ControllerActionButton ultimateButton;
        [SerializeField] private ControllerActionButton reloadButton;
        [SerializeField] private ControllerActionButton gyroClutchButton;

        private readonly object inputStateLock = new object();
        private readonly List<ControllerActionButton> buttons = new List<ControllerActionButton>();
        private bool recenterPending;

        public ControllerInputState CurrentInput { get; private set; }
        public ControllerGyroscope Gyroscope => gyroscope;
        public ControllerVirtualJoystick Joystick => joystick;
        public ControllerCompass Compass => compass;
        public ControllerProximity ProximitySensor => proximitySensor;
        public event Action<ControllerInputState> InputChanged;
        public event Action<ControllerAction> ActionPressed;
        public event Action<ControllerAction> ActionReleased;

        private void Awake()
        {
            EnsureSensors();
        }

        public void Configure(ControllerVirtualJoystick virtualJoystick, ControllerGyroscope gyro,
            ControllerActionButton shoot, ControllerActionButton jump,
            ControllerActionButton ability, ControllerActionButton ultimate,
            ControllerActionButton reload, ControllerActionButton gyroClutch)
        {
            EnsureSensors();
            Unsubscribe();
            joystick = virtualJoystick;
            gyroscope = gyro;
            shootButton = shoot;
            jumpButton = jump;
            abilityButton = ability;
            ultimateButton = ultimate;
            reloadButton = reload;
            gyroClutchButton = gyroClutch;
            gyroscope?.SetClutched(gyroClutchButton != null && gyroClutchButton.IsPressed);
            Subscribe();
            PublishCurrentInput();
        }

        private void EnsureSensors()
        {
            if (compass == null)
                compass = GetComponent<ControllerCompass>();
            if (compass == null)
                compass = gameObject.AddComponent<ControllerCompass>();

            if (proximitySensor == null)
                proximitySensor = GetComponent<ControllerProximity>();
            if (proximitySensor == null)
                proximitySensor = gameObject.AddComponent<ControllerProximity>();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            ResetAllInputs();
            Unsubscribe();
        }

        private void Update()
        {
            PublishCurrentInput();
        }

        public void ResetAllInputs()
        {
            joystick?.CancelPointer();
            foreach (var button in buttons)
                button?.CancelPress();

            lock (inputStateLock)
            {
                recenterPending = false;
                CurrentInput = default;
            }
            InputChanged?.Invoke(CurrentInput);
        }

        private void PublishCurrentInput()
        {
            ControllerInputState state;
            bool changed;
            lock (inputStateLock)
            {
                var gyroYawDelta = 0f;
                var gyroPitchDelta = 0f;
                var acceleration = SystemInfo.supportsAccelerometer
                    ? Input.acceleration
                    : Vector3.zero;
                var accelerationMagnitude = acceleration.magnitude;
                var accelerometerValid = SystemInfo.supportsAccelerometer &&
                                         accelerationMagnitude >= 0.5f &&
                                         accelerationMagnitude <= 20f;
                gyroscope?.SetClutched(gyroClutchButton != null && gyroClutchButton.IsPressed);
                gyroscope?.PeekAimDelta(out gyroYawDelta, out gyroPitchDelta);
                state = new ControllerInputState
                {
                    MoveForward = joystick != null ? joystick.MoveForward : 0f,
                    AimHorizontal = joystick != null ? joystick.AimHorizontal : 0f,
                    Shoot = shootButton != null && shootButton.IsPressed,
                    Jump = jumpButton != null && jumpButton.IsPressed,
                    Ability = abilityButton != null && abilityButton.IsPressed,
                    Ultimate = ultimateButton != null && ultimateButton.IsPressed,
                    Reload = reloadButton != null && reloadButton.IsPressed,
                    GyroTurn = gyroscope != null ? gyroscope.TurnInput : 0f,
                    GyroPitch = gyroscope != null ? gyroscope.PitchInput : 0f,
                    GyroYawDelta = gyroYawDelta,
                    GyroPitchDelta = gyroPitchDelta,
                    ProximityNear = proximitySensor != null &&
                                    proximitySensor.Available && proximitySensor.Near,
                    CompassValid = compass != null && compass.Available,
                    CompassHeading = compass != null && compass.Available
                        ? compass.RelativeHeading
                        : 0f,
                    AccelerometerValid = accelerometerValid,
                    AccelerometerX = acceleration.x,
                    AccelerometerY = acceleration.y,
                    AccelerometerZ = acceleration.z,
                    Recenter = recenterPending
                };
                changed = !ApproximatelyEqual(CurrentInput, state);
                CurrentInput = state;
            }
            if (changed)
                InputChanged?.Invoke(state);
        }

        public ControllerInputState GetCurrentInputSnapshot()
        {
            lock (inputStateLock)
                return CurrentInput;
        }

        public void AcknowledgeGyroAimDelta(float yawDeg, float pitchDeg)
        {
            lock (inputStateLock)
            {
                gyroscope?.ConsumeAimDelta(yawDeg, pitchDeg);
                var state = CurrentInput;
                state.GyroYawDelta -= yawDeg;
                state.GyroPitchDelta -= pitchDeg;
                if (state.Recenter)
                {
                    recenterPending = false;
                    state.Recenter = false;
                }
                CurrentInput = state;
            }
        }

        public void RequestRecenter()
        {
            lock (inputStateLock)
                recenterPending = true;
            PublishCurrentInput();
        }

        private void Subscribe()
        {
            if (buttons.Count > 0)
                return;

            AddButton(shootButton);
            AddButton(jumpButton);
            AddButton(abilityButton);
            AddButton(ultimateButton);
            AddButton(reloadButton);
            AddButton(gyroClutchButton);
        }

        private void AddButton(ControllerActionButton button)
        {
            if (button == null)
                return;

            button.Pressed += OnButtonPressed;
            button.Released += OnButtonReleased;
            buttons.Add(button);
        }

        private void Unsubscribe()
        {
            foreach (var button in buttons)
            {
                if (button == null)
                    continue;
                button.Pressed -= OnButtonPressed;
                button.Released -= OnButtonReleased;
            }
            buttons.Clear();
        }

        private void OnButtonPressed(ControllerAction action)
        {
            ActionPressed?.Invoke(action);
            PublishCurrentInput();
        }

        private void OnButtonReleased(ControllerAction action)
        {
            ActionReleased?.Invoke(action);
            PublishCurrentInput();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                ResetAllInputs();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
                ResetAllInputs();
        }

        private static bool ApproximatelyEqual(ControllerInputState left, ControllerInputState right)
        {
            return Mathf.Abs(left.MoveForward - right.MoveForward) < 0.001f &&
                   Mathf.Abs(left.AimHorizontal - right.AimHorizontal) < 0.001f &&
                   Mathf.Abs(left.GyroTurn - right.GyroTurn) < 0.001f &&
                   Mathf.Abs(left.GyroPitch - right.GyroPitch) < 0.001f &&
                   Mathf.Abs(left.GyroYawDelta - right.GyroYawDelta) < 0.001f &&
                   Mathf.Abs(left.GyroPitchDelta - right.GyroPitchDelta) < 0.001f &&
                   Mathf.Abs(left.CompassHeading - right.CompassHeading) < 0.001f &&
                   Mathf.Abs(left.AccelerometerX - right.AccelerometerX) < 0.001f &&
                   Mathf.Abs(left.AccelerometerY - right.AccelerometerY) < 0.001f &&
                   Mathf.Abs(left.AccelerometerZ - right.AccelerometerZ) < 0.001f &&
                   left.Shoot == right.Shoot &&
                   left.Jump == right.Jump &&
                   left.Ability == right.Ability &&
                   left.Ultimate == right.Ultimate &&
                   left.Reload == right.Reload &&
                   left.ProximityNear == right.ProximityNear &&
                   left.CompassValid == right.CompassValid &&
                   left.AccelerometerValid == right.AccelerometerValid &&
                   left.Recenter == right.Recenter;
        }
    }
}
