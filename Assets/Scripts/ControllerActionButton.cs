using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboClashController
{
    public sealed class ControllerActionButton : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private ControllerAction action;
        [SerializeField] private bool isHoldAction;
        [SerializeField] private Image background;
        [SerializeField] private Color idleColor = Color.white;
        [SerializeField] private Color pressedColor = Color.white;

        private int activePointerId = int.MinValue;

        public ControllerAction Action => action;
        public bool IsPressed { get; private set; }
        public event Action<ControllerAction> Pressed;
        public event Action<ControllerAction> Released;

        public void Configure(ControllerAction buttonAction, bool holdAction,
            Image targetImage, Color idle, Color pressed)
        {
            action = buttonAction;
            isHoldAction = holdAction;
            background = targetImage;
            idleColor = idle;
            pressedColor = pressed;
            RefreshVisual();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (activePointerId != int.MinValue)
                return;

            activePointerId = eventData.pointerId;
            IsPressed = true;
            RefreshVisual();
            Pressed?.Invoke(action);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == activePointerId)
                ResetButton();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData.pointerId == activePointerId)
                ResetButton();
        }

        public void CancelPress()
        {
            ResetButton();
        }

        private void ResetButton()
        {
            if (activePointerId == int.MinValue)
                return;

            activePointerId = int.MinValue;
            IsPressed = false;
            RefreshVisual();
            Released?.Invoke(action);
        }

        private void RefreshVisual()
        {
            if (background != null)
                background.color = IsPressed ? pressedColor : idleColor;
            transform.localScale = IsPressed ? Vector3.one * 0.96f : Vector3.one;
        }

        private void OnDisable()
        {
            ResetButton();
        }
    }
}
