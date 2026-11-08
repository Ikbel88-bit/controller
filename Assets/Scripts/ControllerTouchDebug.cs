using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RoboClashController
{
    public sealed class ControllerTouchDebug : MonoBehaviour
    {
        public Text label;

        private readonly List<RaycastResult> hits = new List<RaycastResult>(8);
        private float nextUpdate;
        private float visibleUntil;
        private bool diagnosticsEnabled;

        public void Initialize(Text target, bool showDiagnostics)
        {
            label = target;
            diagnosticsEnabled = showDiagnostics;
            visibleUntil = Time.unscaledTime + 10f;
            RefreshVisibility();
        }

        public void SetDiagnosticsEnabled(bool enabled)
        {
            diagnosticsEnabled = enabled;
            RefreshVisibility();
        }

        private void Update()
        {
            if (label == null)
                return;

            RefreshVisibility();
            if (!label.gameObject.activeSelf || Time.unscaledTime < nextUpdate)
                return;

            nextUpdate = Time.unscaledTime + 0.2f;
            var eventSystem = EventSystem.current;
            var module = eventSystem != null && eventSystem.currentInputModule != null
                ? eventSystem.currentInputModule.GetType().Name
                : "NONE";

            var pressed = TryGetPointer(out var position);
            var topHit = "-";
            if (pressed && eventSystem != null)
            {
                hits.Clear();
                eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = position }, hits);
                if (hits.Count > 0 && hits[0].gameObject != null)
                    topHit = hits[0].gameObject.name;
            }

            label.text = "EventSystem: " + (eventSystem != null ? "yes" : "NO") +
                         " | module: " + module +
                         "\ntouch: " + (pressed ? position.ToString("0") : "none") +
                         " | top hit: " + topHit;
        }

        private void RefreshVisibility()
        {
            if (label == null)
                return;

            var visible = diagnosticsEnabled || Time.unscaledTime < visibleUntil;
            if (label.gameObject.activeSelf != visible)
                label.gameObject.SetActive(visible);
        }

        private static bool TryGetPointer(out Vector2 position)
        {
            position = default;
#if ENABLE_INPUT_SYSTEM
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                position = touchscreen.primaryTouch.position.ReadValue();
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                position = Input.GetTouch(0).position;
                return true;
            }
#endif
            return false;
        }
    }
}
