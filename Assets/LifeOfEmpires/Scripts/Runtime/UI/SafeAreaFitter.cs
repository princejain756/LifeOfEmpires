using UnityEngine;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// Keeps a RectTransform inside <see cref="Screen.safeArea"/> so no control lands under the notch,
    /// the Dynamic Island, the rounded corners or the home indicator — Section 4 of the spec forbids
    /// controls in unsafe regions, and a phone's safe area changes with the device and the rotation.
    ///
    /// Applied as anchors rather than pixel offsets, so a child that stretches to this rect follows
    /// the safe area at any resolution. Re-applied only when the area actually moves.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _applied;
        private bool _hasApplied;

        /// <summary>The safe area last applied, in pixels. Exposed so tests can assert the mapping.</summary>
        public Rect AppliedSafeArea => _applied;

        public bool HasApplied => _hasApplied;

        private void OnEnable()
        {
            _rect = GetComponent<RectTransform>();
            Apply();
        }

        private void Update() => Apply();

        /// <summary>
        /// Re-anchor to the current safe area. Idempotent and allocation-free; a no-op when the area
        /// has not changed, and when the display reports nothing (batchmode, headless test runs).
        /// </summary>
        public void Apply()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_rect == null) return;

            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            if (screenWidth <= 0 || screenHeight <= 0) return;

            Rect safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f) return;

            if (_hasApplied && safe == _applied) return;

            _applied = safe;
            _hasApplied = true;

            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= screenWidth;
            min.y /= screenHeight;
            max.x /= screenWidth;
            max.y /= screenHeight;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
