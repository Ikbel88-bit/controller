using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RoboClashController
{
    public sealed class ControllerVirtualJoystick : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform baseRect;
        [SerializeField] private RectTransform knobRect;
        [SerializeField, Range(0f, 0.5f)] private float deadZone = 0.12f;

        private int activePointerId = int.MinValue;
        private Vector2 normalizedValue;

        public Vector2 Value => normalizedValue;
        public float MoveForward => normalizedValue.y;
        public float AimHorizontal => normalizedValue.x;
        public float DeadZone => deadZone;
        public event Action<Vector2> ValueChanged;

        public void Configure(RectTransform joystickBase, RectTransform knob, float initialDeadZone)
        {
            baseRect = joystickBase;
            knobRect = knob;
            SetDeadZone(initialDeadZone);
            ResetInput();
        }

        public void SetDeadZone(float value)
        {
            deadZone = Mathf.Clamp(value, 0f, 0.5f);
            ApplyDeadZone();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (activePointerId != int.MinValue)
                return;
            activePointerId = eventData.pointerId;
            UpdatePosition(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == activePointerId)
                UpdatePosition(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == activePointerId)
                ResetInput();
        }

        public void CancelPointer()
        {
            ResetInput();
        }

        private void UpdatePosition(PointerEventData eventData)
        {
            if (baseRect == null || knobRect == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    baseRect, eventData.position, eventData.pressEventCamera, out var localPoint))
                return;

            var radius = Mathf.Max(1f, Mathf.Min(baseRect.rect.width, baseRect.rect.height) * 0.5f);
            var offset = Vector2.ClampMagnitude(localPoint / radius, 1f);
            SetValue(offset);
            knobRect.anchoredPosition = offset * radius * 0.72f;
        }

        private void SetValue(Vector2 value)
        {
            value = Vector2.ClampMagnitude(value, 1f);
            if (value.magnitude < deadZone)
                value = Vector2.zero;
            else if (deadZone > 0f)
                value = value.normalized * Mathf.InverseLerp(deadZone, 1f, value.magnitude);

            if ((normalizedValue - value).sqrMagnitude < 0.0001f)
                return;

            normalizedValue = value;
            ValueChanged?.Invoke(normalizedValue);
        }

        private void ApplyDeadZone()
        {
            if (normalizedValue.magnitude < deadZone)
                SetValue(Vector2.zero);
        }

        private void ResetInput()
        {
            activePointerId = int.MinValue;
            var changed = normalizedValue.sqrMagnitude > 0.0001f;
            normalizedValue = Vector2.zero;
            if (knobRect != null)
                knobRect.anchoredPosition = Vector2.zero;
            if (changed)
                ValueChanged?.Invoke(normalizedValue);
        }

        private void OnDisable()
        {
            ResetInput();
        }
    }
}
