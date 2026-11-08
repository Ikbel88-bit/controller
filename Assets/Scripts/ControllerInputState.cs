using System;
using UnityEngine;

namespace RoboClashController
{
    [Serializable]
    public struct ControllerInputState
    {
        public float MoveForward;
        public float AimHorizontal;
        public bool Shoot;
        public bool Jump;
        public bool Ability;
        public bool Ultimate;
        public bool Reload;
        public float GyroTurn;
        public float GyroPitch;
        public float GyroYawDelta;
        public float GyroPitchDelta;
        public bool ProximityNear;
        public bool CompassValid;
        public float CompassHeading;
        public bool AccelerometerValid;
        public float AccelerometerX;
        public float AccelerometerY;
        public float AccelerometerZ;
        public bool Recenter;
    }

    public enum ControllerAction
    {
        Shoot,
        Jump,
        Ability,
        Ultimate,
        Reload,
        GyroClutch
    }

    public static class ControllerLocalSettings
    {
        private const string GyroEnabledKey = "RoboClashController.GyroEnabled";
        private const string GyroYawSensitivityKey = "RoboClashController.GyroYawSensitivity";
        private const string GyroPitchSensitivityKey = "RoboClashController.GyroPitchSensitivity";
        private const string GyroDeadZoneKey = "RoboClashController.GyroDeadZone";
        private const string GyroResponseCurveKey = "RoboClashController.GyroResponseCurve";
        private const string GyroSmoothingKey = "RoboClashController.GyroSmoothing";
        private const string GyroInvertYawKey = "RoboClashController.GyroInvertYaw";
        private const string GyroInvertPitchKey = "RoboClashController.GyroInvertPitch";
        private const string CompassAssistKey = "RoboClashController.CompassAssist";
        private const string JoystickDeadZoneKey = "RoboClashController.JoystickDeadZone";
        private const string DiagnosticsVisibleKey = "RoboClashController.DiagnosticsVisible";

        public static bool GyroscopeEnabled
        {
            get => PlayerPrefs.GetInt(GyroEnabledKey, 1) != 0;
            set => SaveBool(GyroEnabledKey, value);
        }

        public static float GyroscopeYawSensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(GyroYawSensitivityKey, 2.5f), 0.1f, 8f);
            set => PlayerPrefs.SetFloat(GyroYawSensitivityKey, Mathf.Clamp(value, 0.1f, 8f));
        }

        public static float GyroscopePitchSensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(GyroPitchSensitivityKey, 1.8f), 0.1f, 8f);
            set => PlayerPrefs.SetFloat(GyroPitchSensitivityKey, Mathf.Clamp(value, 0.1f, 8f));
        }

        public static float GyroscopeDeadZone
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(GyroDeadZoneKey, 1.5f), 0f, 10f);
            set => PlayerPrefs.SetFloat(GyroDeadZoneKey, Mathf.Clamp(value, 0f, 10f));
        }

        public static float GyroscopeResponseCurve
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(GyroResponseCurveKey, 1.5f), 1f, 2.5f);
            set => PlayerPrefs.SetFloat(GyroResponseCurveKey, Mathf.Clamp(value, 1f, 2.5f));
        }

        public static float GyroscopeSmoothing
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(GyroSmoothingKey, 20f), 1f, 30f);
            set => PlayerPrefs.SetFloat(GyroSmoothingKey, Mathf.Clamp(value, 1f, 30f));
        }

        public static bool InvertGyroscopeYaw
        {
            get => PlayerPrefs.GetInt(GyroInvertYawKey, 0) != 0;
            set => SaveBool(GyroInvertYawKey, value);
        }

        public static bool InvertGyroscopePitch
        {
            get => PlayerPrefs.GetInt(GyroInvertPitchKey, 0) != 0;
            set => SaveBool(GyroInvertPitchKey, value);
        }

        public static bool CompassAssist
        {
            get => PlayerPrefs.GetInt(CompassAssistKey, 1) != 0;
            set => SaveBool(CompassAssistKey, value);
        }

        public static float JoystickDeadZone
        {
            get => PlayerPrefs.GetFloat(JoystickDeadZoneKey, 0.12f);
            set => PlayerPrefs.SetFloat(JoystickDeadZoneKey, Mathf.Clamp(value, 0f, 0.5f));
        }

        public static bool DiagnosticsVisible
        {
            get => PlayerPrefs.GetInt(DiagnosticsVisibleKey, 0) != 0;
            set => SaveBool(DiagnosticsVisibleKey, value);
        }

        public static void Save()
        {
            PlayerPrefs.Save();
        }

        private static void SaveBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
