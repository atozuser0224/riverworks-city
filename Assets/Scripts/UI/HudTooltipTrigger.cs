using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Riverworks
{
    /// <summary>Shows a build-item tooltip on mouse hover or a 0.45 second touch hold.</summary>
    public sealed class HudTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public const float LongPressSeconds = .45f;

        public bool ConsumeClick { get; private set; }
        public string TooltipText { get; set; }
        public Action<RectTransform, string> Show { get; set; }
        public Action Hide { get; set; }

        bool pointerDown;
        bool longPressShown;
        float pressedAt;

        void Update()
        {
            if (!pointerDown || longPressShown || Time.unscaledTime - pressedAt < LongPressSeconds) return;
            longPressShown = true;
            ConsumeClick = true;
            ShowTooltip();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (eventData != null && eventData.pointerId < 0) ShowTooltip();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!pointerDown) Hide?.Invoke();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            pointerDown = true;
            longPressShown = false;
            ConsumeClick = false;
            pressedAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pointerDown = false;
            if (longPressShown) Hide?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (ConsumeClick) eventData?.Use();
            ConsumeClick = false;
            longPressShown = false;
        }

        void OnDisable()
        {
            pointerDown = false;
            longPressShown = false;
            ConsumeClick = false;
            Hide?.Invoke();
        }

        void ShowTooltip()
        {
            if (!string.IsNullOrEmpty(TooltipText)) Show?.Invoke(transform as RectTransform, TooltipText);
        }
    }
}
