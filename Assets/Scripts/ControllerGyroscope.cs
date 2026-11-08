using System;
using System.Globalization;
using UnityEngine;

namespace RoboClashController
{
    public sealed class ControllerGyroscope : MonoBehaviour
    {
        private const float FullRateDegreesPerSecond = 180f;
        private const float CalibrationDuration = 0.75f;
        private const string InvertCompassKey = "controller.compass.invertSign";

        private readonly object aimDeltaLock = new object();
        [SerializeField, Range(0.1f, 8f)] private float yawSensitivity = 2.5f;
        [SerializeField, Range(0.1f, 8f)] private float pitchSensitivity = 1.8f;
        [SerializeField, Range(0f, 10f)] private float angularDeadZone = 1.5f;
        [SerializeField, Range(1f, 2.5f)] private float responseCurveExponent = 1.5f;
        [SerializeField, Range(1f, 30f)] private float smoothingSpeed = 20f;
        [SerializeField] private bool invertYaw;
        [SerializeField] private bool invertPitch;
        [SerializeField] private bool compassAssist = true;
        [SerializeField] private bool inputEnabled = true;
        private ControllerCompass compass;
        private bool outputClutched;
        private bool wasClutched;

        private float calibratedBias;
        private float calibratedPitchBias;
        private float rawYawDeg;
        private float driftErrorDeg;
        private float prevCompassRel;
        private bool hasPrevCompass;
        private float signCorrelation;
        private float calibrationTotal;
        private float calibrationPitchTotal;
        private int calibrationSamples;
        private float calibrationEndsAt;
        private bool applicationPaused;
        private float accumulatedYawDeltaDegrees;
        private float accumulatedPitchDeltaDegrees;
        private float driftStartRawYaw;
        private float driftMaxAbs;
        private float driftAimTotalYaw;
        private bool driftStartAssist;

        public float TurnInput { get; private set; }
        public float PitchInput { get; private set; }
        public float RawRateDegreesPerSecond { get; private set; }
        public float RawPitchRateDegreesPerSecond { get; private set; }
        public bool IsAvailable => SystemInfo.supportsGyroscope;
        public bool IsCalibrating => calibrationEndsAt > Time.unscaledTime;
        public bool InputEnabled => inputEnabled;
        public bool CompassAssist
        {
            get => compassAssist;
            set => compassAssist = value;
        }
        public bool InvertCompassSign
        {
            get => PlayerPrefs.GetInt(InvertCompassKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(InvertCompassKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
        public float DriftErrorDeg => driftErrorDeg;
        public string CompassSignCheck
        {
            get
            {
                if (Mathf.Abs(signCorrelation) < 200f)
                    return "rotate the phone ~90 deg to test";
                return signCorrelation > 0f
                    ? "OK (same direction)"
                    : "OPPOSITE: enable 'Invert compass direction'";
            }
        }
        public bool DriftTestRunning { get; private set; }
        public float DriftTestRemaining { get; private set; }
        public string DriftTestResult { get; private set; } = "";
        public event Action CalibrationCompleted;

        private void OnEnable()
        {
            EnsureCompass();
            UpdateSensorState();
        }

        private void OnDisable()
        {
            if (SystemInfo.supportsGyroscope)
                Input.gyro.enabled = false;
            TurnInput = 0f;
            PitchInput = 0f;
            ResetAimDelta();
        }

        private void Update()
        {
            if (!IsAvailable || !inputEnabled || !Input.gyro.enabled || applicationPaused || outputClutched)
            {
                SmoothToward(0f);
                SmoothPitchToward(0f);
                ResetAimDelta();
                return;
            }

            var rotationRate = Input.gyro.rotationRateUnbiased;
            var rawRate = GetLandscapeYawRate(rotationRate) * Mathf.Rad2Deg;
            var rawPitchRate = GetLandscapePitchRate(rotationRate) * Mathf.Rad2Deg;
            RawRateDegreesPerSecond = rawRate;
            RawPitchRateDegreesPerSecond = rawPitchRate;
            if (IsCalibrating)
            {
                calibrationTotal += rawRate;
                calibrationPitchTotal += rawPitchRate;
                calibrationSamples++;
                SmoothToward(0f);
                SmoothPitchToward(0f);
                ResetAimDelta();
                return;
            }

            var deltaTime = Time.unscaledDeltaTime;
            var yawRateDeg = rawRate - calibratedBias;
            var pitchRateDeg = rawPitchRate - calibratedPitchBias;
            rawYawDeg += yawRateDeg * deltaTime;

            if (compass != null && compass.Available)
            {
                if (hasPrevCompass)
                {
                    var dComp = Mathf.DeltaAngle(prevCompassRel, compass.RelativeHeading);
                    if (Mathf.Abs(yawRateDeg) > 30f)
                        signCorrelation += (yawRateDeg * deltaTime) * dComp;
                }
                prevCompassRel = compass.RelativeHeading;
                hasPrevCompass = true;
            }
            else
                hasPrevCompass = false;

            var signedCompass = compass != null && compass.Available
                ? InvertCompassSign ? -compass.RelativeHeading : compass.RelativeHeading
                : 0f;
            var still = Mathf.Abs(yawRateDeg) < 8f && Mathf.Abs(pitchRateDeg) < 8f;
            if (compassAssist && compass != null && compass.Available &&
                !outputClutched && still)
            {
                var err = Mathf.DeltaAngle(rawYawDeg, signedCompass);
                err = Mathf.Clamp(err, -10f, 10f);
                driftErrorDeg = err;
                rawYawDeg += err * 0.5f * deltaTime;
                calibratedBias -= err * 0.02f * deltaTime;
                yawRateDeg = rawRate - calibratedBias;
            }

            var processedYawRateDegPerSec =
                ProcessRate(yawRateDeg, yawSensitivity, invertYaw) *
                FullRateDegreesPerSecond;
            var processedPitchRateDegPerSec =
                ProcessRate(pitchRateDeg, pitchSensitivity, invertPitch) *
                FullRateDegreesPerSecond;
            SmoothToward(Mathf.Clamp(
                processedYawRateDegPerSec / FullRateDegreesPerSecond, -1f, 1f));
            SmoothPitchToward(Mathf.Clamp(
                processedPitchRateDegPerSec / FullRateDegreesPerSecond, -1f, 1f));

            if (DriftTestRunning)
            {
                driftAimTotalYaw += processedYawRateDegPerSec * deltaTime;
                driftMaxAbs = Mathf.Max(driftMaxAbs, Mathf.Abs(rawYawDeg - driftStartRawYaw));
                DriftTestRemaining -= deltaTime;
                if (DriftTestRemaining <= 0f)
                {
                    DriftTestRunning = false;
                    DriftTestResult = string.Format(CultureInfo.InvariantCulture,
                        "Assist {0}: internal yaw drift {1:0.0} deg (max {2:0.0}), aim output drift {3:0.0} deg",
                        driftStartAssist ? "ON" : "OFF", rawYawDeg - driftStartRawYaw,
                        driftMaxAbs, driftAimTotalYaw);
                    Debug.Log("[DriftTest] " + DriftTestResult);
                }
            }

            lock (aimDeltaLock)
            {
                accumulatedYawDeltaDegrees += processedYawRateDegPerSec * deltaTime;
                accumulatedPitchDeltaDegrees += processedPitchRateDegPerSec * deltaTime;
            }
        }

        public void Configure(bool enabled, float newYawSensitivity, float newPitchSensitivity,
            float deadZone, float curveExponent, float smoothing, bool newInvertYaw, bool newInvertPitch)
        {
            yawSensitivity = Mathf.Clamp(newYawSensitivity, 0.1f, 8f);
            pitchSensitivity = Mathf.Clamp(newPitchSensitivity, 0.1f, 8f);
            angularDeadZone = Mathf.Clamp(deadZone, 0f, 10f);
            responseCurveExponent = Mathf.Clamp(curveExponent, 1f, 2.5f);
            smoothingSpeed = Mathf.Clamp(smoothing, 1f, 30f);
            invertYaw = newInvertYaw;
            invertPitch = newInvertPitch;
            inputEnabled = enabled;
            UpdateSensorState();
            if (!enabled || !IsAvailable)
            {
                TurnInput = 0f;
                PitchInput = 0f;
                ResetAimDelta();
            }
        }

        public void SetEnabled(bool enabled)
        {
            inputEnabled = enabled;
            UpdateSensorState();
            if (!enabled || !IsAvailable)
            {
                TurnInput = 0f;
                PitchInput = 0f;
                ResetAimDelta();
            }
        }

        public void SetClutched(bool clutched)
        {
            var clutchReleased = wasClutched && !clutched;
            outputClutched = clutched;
            wasClutched = clutched;
            if (clutched)
            {
                TurnInput = 0f;
                PitchInput = 0f;
                ResetAimDelta();
            }
            else if (clutchReleased)
            {
                rawYawDeg = 0f;
                driftErrorDeg = 0f;
                hasPrevCompass = false;
                signCorrelation = 0f;
                compass?.Recenter();
            }
        }

        public void Recenter()
        {
            calibratedBias = 0f;
            calibratedPitchBias = 0f;
            rawYawDeg = 0f;
            driftErrorDeg = 0f;
            hasPrevCompass = false;
            signCorrelation = 0f;
            compass?.Recenter();
            calibrationTotal = 0f;
            calibrationPitchTotal = 0f;
            calibrationSamples = 0;
            ResetAimDelta();
            if (!IsAvailable || !inputEnabled)
            {
                TurnInput = 0f;
                PitchInput = 0f;
                CalibrationCompleted?.Invoke();
                return;
            }

            calibrationEndsAt = Time.unscaledTime + CalibrationDuration;
            TurnInput = 0f;
            PitchInput = 0f;
        }

        public void StartDriftTest(float seconds = 120f)
        {
            DriftTestRunning = true;
            DriftTestRemaining = seconds;
            driftStartRawYaw = rawYawDeg;
            driftMaxAbs = 0f;
            driftAimTotalYaw = 0f;
            driftStartAssist = compassAssist;
            DriftTestResult = "";
        }

        private void UpdateSensorState()
        {
            if (SystemInfo.supportsGyroscope)
                Input.gyro.enabled = inputEnabled && isActiveAndEnabled;
        }

        private void EnsureCompass()
        {
            if (compass == null)
                compass = GetComponent<ControllerCompass>();
            if (compass == null)
                compass = gameObject.AddComponent<ControllerCompass>();
        }

        private void SmoothToward(float target)
        {
            var blend = 1f - Mathf.Exp(-smoothingSpeed * Time.unscaledDeltaTime);
            TurnInput = Mathf.Lerp(TurnInput, target, blend);
            if (Mathf.Abs(TurnInput) < 0.002f)
                TurnInput = 0f;
        }

        private void SmoothPitchToward(float target)
        {
            var blend = 1f - Mathf.Exp(-smoothingSpeed * Time.unscaledDeltaTime);
            PitchInput = Mathf.Lerp(PitchInput, target, blend);
            if (Mathf.Abs(PitchInput) < 0.002f)
                PitchInput = 0f;
        }

        private float ProcessRate(float adjustedRate, float axisSensitivity, bool invert)
        {
            var magnitude = Mathf.Abs(adjustedRate);
            var deadZoneRescaledMagnitude = magnitude <= angularDeadZone
                ? 0f
                : Mathf.Clamp01((magnitude - angularDeadZone) /
                  (FullRateDegreesPerSecond - angularDeadZone));
            var curvedMagnitude = Mathf.Pow(deadZoneRescaledMagnitude, responseCurveExponent);
            var target = curvedMagnitude * Mathf.Sign(adjustedRate) * axisSensitivity;
            return invert ? -target : target;
        }

        public void ConsumeAimDelta(out float yawDeg, out float pitchDeg)
        {
            lock (aimDeltaLock)
            {
                yawDeg = accumulatedYawDeltaDegrees;
                pitchDeg = accumulatedPitchDeltaDegrees;
                accumulatedYawDeltaDegrees = 0f;
                accumulatedPitchDeltaDegrees = 0f;
            }
        }

        public void PeekAimDelta(out float yawDeg, out float pitchDeg)
        {
            lock (aimDeltaLock)
            {
                yawDeg = accumulatedYawDeltaDegrees;
                pitchDeg = accumulatedPitchDeltaDegrees;
            }
        }

        public void ConsumeAimDelta(float yawDeg, float pitchDeg)
        {
            lock (aimDeltaLock)
            {
                accumulatedYawDeltaDegrees -= yawDeg;
                accumulatedPitchDeltaDegrees -= pitchDeg;
            }
        }

        private void ResetAimDelta()
        {
            lock (aimDeltaLock)
            {
                accumulatedYawDeltaDegrees = 0f;
                accumulatedPitchDeltaDegrees = 0f;
            }
        }

        private void LateUpdate()
        {
            if (calibrationEndsAt <= 0f || Time.unscaledTime < calibrationEndsAt)
                return;

            if (calibrationSamples > 0)
            {
                calibratedBias = calibrationTotal / calibrationSamples;
                calibratedPitchBias = calibrationPitchTotal / calibrationSamples;
            }
            calibrationEndsAt = 0f;
            calibrationTotal = 0f;
            calibrationPitchTotal = 0f;
            calibrationSamples = 0;
            CalibrationCompleted?.Invoke();
        }

        private static float GetLandscapeYawRate(Vector3 rate)
        {
            switch (Screen.orientation)
            {
                case ScreenOrientation.LandscapeRight:
                    return -rate.y;
                case ScreenOrientation.LandscapeLeft:
                case ScreenOrientation.AutoRotation:
                default:
                    return rate.y;
            }
        }

        private static float GetLandscapePitchRate(Vector3 rate)
        {
            switch (Screen.orientation)
            {
                case ScreenOrientation.LandscapeRight:
                    return -rate.x;
                case ScreenOrientation.LandscapeLeft:
                case ScreenOrientation.AutoRotation:
                default:
                    return rate.x;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                TurnInput = 0f;
                PitchInput = 0f;
                ResetAimDelta();
            }
            else
                UpdateSensorState();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            applicationPaused = pauseStatus;
            if (pauseStatus)
            {
                TurnInput = 0f;
                PitchInput = 0f;
                ResetAimDelta();
            }
            else
                UpdateSensorState();
        }
    }
}
