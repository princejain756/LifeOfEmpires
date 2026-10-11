using UnityEngine;

namespace EmpireRush
{
    /// <summary>
    /// World-space billboard health bar. Created lazily and shown only when it carries information —
    /// damaged, selected, or being built — so an untouched battlefield stays clean. Two quads, no
    /// canvas, no per-frame allocation.
    /// </summary>
    public class HealthBar : MonoBehaviour
    {
        [Header("Geometry")]
        [Min(0.01f)] public float width = 0.9f;
        [Min(0.01f)] public float height = 0.12f;

        [Tooltip("Metres above the owner's pivot where the bar floats.")]
        [Min(0f)] public float verticalOffset = 1.6f;

        [Header("Behaviour")]
        [Tooltip("Seconds the bar stays visible after the last hit.")]
        [Min(0f)] public float lingerSeconds = 3f;

        [Tooltip("Always show, even at full health. For the currently selected unit.")]
        public bool forceVisible;

        [Header("Colours")]
        public Color fillColor = new Color(0.35f, 0.85f, 0.35f);
        public Color damagedColor = new Color(0.95f, 0.75f, 0.2f);
        public Color criticalColor = new Color(0.9f, 0.25f, 0.2f);
        public Color backgroundColor = new Color(0.05f, 0.05f, 0.06f, 0.85f);

        private Transform _fill;
        private Transform _root;
        private Material _fillMaterial;
        private readonly System.Collections.Generic.List<Material> _materials = new System.Collections.Generic.List<Material>(4);
        private Camera _camera;
        private float _visibleUntil;
        private int _lastHealth = -1;
        private int _maxHealth = 1;

        /// <summary>Attach a bar to an owner. Safe to call once per unit from its initialiser.</summary>
        public void Bind(Transform follow, int maxHealth)
        {
            _maxHealth = Mathf.Max(1, maxHealth);
            _lastHealth = _maxHealth;
            _camera = Camera.main;

            // The bar shares its owner's GameObject, so the vertical offset must be applied to the
            // bar's own child root: writing it here would move the unit itself to the world origin.
            if (follow != null && follow != transform) transform.SetParent(follow, false);

            BuildGeometry();
            SetVisible(false);
        }

        /// <summary>Feed a health reading. The bar appears when health drops and hides again after the linger.</summary>
        public void Report(int health)
        {
            health = Mathf.Max(0, health);
            if (health != _lastHealth)
            {
                _lastHealth = health;
                _visibleUntil = Time.time + lingerSeconds;
                UpdateFill();
            }

            SetVisible(forceVisible || health < _maxHealth || Time.time < _visibleUntil);
        }

        public void ShowUntil(float seconds) => _visibleUntil = Mathf.Max(_visibleUntil, Time.time + seconds);

        /// <summary>
        /// Hide the bar for good — used when the owner dies. Deliberately does not deactivate this
        /// component's GameObject: the bar shares its owner's object, so deactivating it would take
        /// the unit with it.
        /// </summary>
        public void Hide()
        {
            SetVisible(false);
            enabled = false;
        }

        private void BuildGeometry()
        {
            _root = new GameObject("HealthBarRoot").transform;
            _root.SetParent(transform, false);
            _root.localPosition = new Vector3(0f, verticalOffset, 0f);

            _fillMaterial = CreateMaterial(fillColor);

            Transform background = CreateQuad("Background", _root, backgroundColor, 0.95f);
            background.localScale = new Vector3(width, height, 1f);

            // The fill quad must own the very material UpdateFill mutates, or the bar would never
            // change colour as health drains.
            _fill = CreateQuad("Fill", _root, fillColor, 1.0f, _fillMaterial);
            _fill.localScale = new Vector3(width, height, 1f);
        }

        private Transform CreateQuad(string name, Transform parent, Color color, float depth, Material shared = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                // Destroy is illegal outside play mode, and geometry is built at construction time,
                // which tests and the bootstrap also do.
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            Material material = shared != null ? shared : CreateMaterial(color);
            if (shared == null && material != null) _materials.Add(material);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, depth * 0.01f);
            return go.transform;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var material = new Material(shader) { name = "HealthBarRuntime" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            return material;
        }

        private void UpdateFill()
        {
            if (_fill == null) return;

            float fraction = _maxHealth <= 0 ? 0f : Mathf.Clamp01(_lastHealth / (float)_maxHealth);
            _fill.localScale = new Vector3(width * fraction, height, 1f);

            // Scale from the left edge so the bar drains toward the anchor rather than the centre.
            _fill.localPosition = new Vector3((fraction - 1f) * width * 0.5f, 0f, 0.01f);

            Color color = fraction > 0.6f ? fillColor : fraction > 0.3f ? damagedColor : criticalColor;
            if (_fillMaterial != null)
            {
                if (_fillMaterial.HasProperty("_BaseColor")) _fillMaterial.SetColor("_BaseColor", color);
                else if (_fillMaterial.HasProperty("_Color")) _fillMaterial.SetColor("_Color", color);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        }

        private void LateUpdate()
        {
            // Face the camera so the bar is legible from any isometric yaw.
            if (_root == null || !_root.gameObject.activeSelf) return;

            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            _root.rotation = _camera.transform.rotation;
        }

        private void OnDestroy()
        {
            if (_fillMaterial != null)
            {
                if (Application.isPlaying) Destroy(_fillMaterial);
                else DestroyImmediate(_fillMaterial);
            }

            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i] == null) continue;
                if (Application.isPlaying) Destroy(_materials[i]);
                else DestroyImmediate(_materials[i]);
            }

            _materials.Clear();
        }
    }
}
