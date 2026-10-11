using System;
using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// Shared uGUI construction for the battle HUD: one font, one palette, one set of anchored
    /// rectangles. Every control the HUD builds goes through here so the whole interface stays
    /// consistent and no panel grows its own ad-hoc layout maths.
    /// </summary>
    internal static class UiFactory
    {
        /// <summary>Portrait reference resolution the canvas scales against.</summary>
        public const int ReferenceWidth = 1080;
        public const int ReferenceHeight = 1920;

        public static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.10f, 0.62f);
        public static readonly Color ButtonColor = new Color(0.20f, 0.26f, 0.34f, 0.95f);
        public static readonly Color ButtonDisabledColor = new Color(0.20f, 0.20f, 0.22f, 0.45f);
        public static readonly Color TextColor = new Color(0.96f, 0.95f, 0.90f);
        public static readonly Color AccentColor = new Color(0.91f, 0.76f, 0.36f);
        public static readonly Color DangerColor = new Color(0.86f, 0.38f, 0.34f);

        private static Font _font;
        private static bool _fontReportedMissing;

        /// <summary>
        /// The built-in runtime font. Unity 6 no longer ships "Arial.ttf" — asking for it returns null
        /// and every label renders as nothing — so "LegacyRuntime.ttf" is the only supported builtin
        /// name. A missing font is reported once rather than silently producing an empty HUD.
        /// </summary>
        public static Font DefaultFont
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_font == null && !_fontReportedMissing)
                    {
                        _fontReportedMissing = true;
                        Debug.LogError("UiFactory: builtin font 'LegacyRuntime.ttf' was not found; HUD labels will not render.");
                    }
                }

                return _font;
            }
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>A flat colour block. Never a raycast target: only buttons should eat taps.</summary>
        public static Image Panel(string name, Transform parent, Color color)
        {
            RectTransform rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(string name, Transform parent, string text, int fontSize, TextAnchor anchor, Color color)
        {
            RectTransform rect = Rect(name, parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = DefaultFont;
            label.fontSize = fontSize;
            label.alignment = anchor;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            // Overflow rather than wrap: a squeezed label must never silently clip a resource count.
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public static Button Button(string name, Transform parent, string label, int fontSize, Action onClick)
        {
            RectTransform rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            // The interactable look is owned entirely by SetInteractable's colour swap, so the
            // built-in tint must not darken it a second time.
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            button.colors = colors;

            if (onClick != null) button.onClick.AddListener(() => onClick());

            Text child = Label("Label", rect, label, fontSize, TextAnchor.MiddleCenter, TextColor);
            Stretch(child.rectTransform);
            return button;
        }

        public static void SetInteractable(Button button, bool interactable)
        {
            if (button == null) return;

            button.interactable = interactable;
            if (button.targetGraphic is Image image)
            {
                image.color = interactable ? ButtonColor : ButtonDisabledColor;
            }
        }

        /// <summary>
        /// A settings row: a caption on the left and one button on the right whose label carries the
        /// current value. The row itself supplies the layout, so the caller places it exactly like any
        /// other menu row and this stays consistent with the upgrade rows above it.
        /// </summary>
        public static Button SettingRow(Transform parent, string name, string label, string initialValue, Action onClick)
        {
            Text caption = Label($"{name}Label", parent, label, 28, TextAnchor.MiddleLeft, TextColor);
            Place(caption.rectTransform,
                Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(16f, 16f), new Vector2(440f, 90f));

            Button button = Button($"{name}Button", parent, initialValue, 28, onClick);
            Place(button.GetComponent<RectTransform>(),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-16f, 0f), new Vector2(320f, 90f));
            return button;
        }

        /// <summary>Fill the parent, optionally inset. The default for full-bleed strips.</summary>
        public static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Place a fixed-size element against an anchor pair. Used by every HUD panel.</summary>
        public static RectTransform Place(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
