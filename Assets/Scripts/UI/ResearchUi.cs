using System;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Pixel-aligned building blocks shared by the technology map and its detail panel.</summary>
    public static class ResearchUi
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = rect.sizeDelta = Vector2.zero;
            return rect;
        }

        public static RectTransform Panel(string name, Transform parent, Color color)
        {
            RectTransform rect = Rect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = HudAssets.Panel;
            image.type = Image.Type.Sliced;
            image.color = color;
            return rect;
        }

        public static Text Label(string name, Transform parent, string text, int size = 11,
            Color? color = null, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            RectTransform rect = Rect(name, parent);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = GameFont.Load();
            label.fontSize = size;
            label.fontStyle = FontStyle.Normal;
            label.text = text;
            label.color = color ?? HudStyle.Text;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.resizeTextForBestFit = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        public static Button Button(string name, Transform parent, string text, Action click, Color? color = null)
        {
            Color background = color ?? HudStyle.SurfaceRaised;
            RectTransform rect = Panel(name, parent, background);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => click?.Invoke());
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1);
            colors.pressedColor = new Color(.82f, .82f, .82f, 1);
            colors.disabledColor = new Color(.65f, .65f, .65f, 1);
            button.colors = colors;
            Text label = Label("Label", rect, text, HudStyle.BodySize, HudStyle.Foreground(background), TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 6, 2, 6, 2);
            FeelUiFeedback.AttachButton(button);
            return button;
        }

        public static void Place(RectTransform rect, float x, float top, float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(top));
            rect.sizeDelta = new Vector2(Mathf.Max(0, Mathf.Round(width)), Mathf.Max(0, Mathf.Round(height)));
        }

        public static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static void SetButtonColor(Button button, Color background)
        {
            if (button == null) return;
            button.targetGraphic.color = background;
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.color = HudStyle.Foreground(background);
        }
    }
}
