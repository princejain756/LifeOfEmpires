using UnityEngine;

namespace EmpireRush
{
    /// <summary>
    /// True isometric camera: orthographic projection at a fixed 45° pitch and 45° yaw. The rig
    /// object sits on the ground and is the point the camera looks at, so "pan" is just moving the
    /// rig and "focus" is placing it — no trigonometry at the call site.
    ///
    /// Panning is clamped to the battlefield and zoom is clamped between
    /// <see cref="minOrthoSize"/> and <see cref="maxOrthoSize"/>, so the player can never lose the
    /// battlefield off-screen or zoom past the point where units are legible.
    /// </summary>
    public class IsometricCameraRig : MonoBehaviour
    {
        [Header("Orientation")]
        [Tooltip("Downward tilt. 45 is true isometric.")]
        [Range(20f, 80f)] public float pitch = 45f;

        [Tooltip("Rotation about the world up axis. 45 is true isometric.")]
        public float yaw = 45f;

        [Header("Zoom")]
        [Tooltip("Closest zoom, as an orthographic half-height in metres.")]
        [Min(1f)] public float minOrthoSize = 7f;

        [Tooltip("Furthest zoom, as an orthographic half-height in metres.")]
        [Min(1f)] public float maxOrthoSize = 26f;

        [Tooltip("Orthographic half-height applied at start.")]
        [Min(1f)] public float startOrthoSize = 14f;

        [Tooltip("Metres of ortho size change per pixel of pinch.")]
        [Min(0.001f)] public float pinchSensitivity = 0.045f;

        [Header("Panning")]
        [Tooltip("Extra metres of slack around the battlefield bounds, so edge buildings stay reachable.")]
        [Min(0f)] public float panMargin = 4f;

        [Header("Battlefield")]
        public Bounds battlefieldBounds = new Bounds(Vector3.zero, new Vector3(64f, 0f, 64f));

        [Header("Camera")]
        [Tooltip("Distance from the rig pivot back along the view axis. Ortho size drives framing; this only needs to keep geometry inside the clip planes.")]
        [Min(1f)] public float cameraDistance = 60f;

        private Camera _camera;
        private float _orthoSize;

        /// <summary>True while a multi-touch gesture owns the input. Tap detection is suppressed while set.</summary>
        public bool IsGesturing { get; private set; }

        public float OrthoSize => _orthoSize;

        public Camera Camera
        {
            get
            {
                if (_camera == null)
                {
                    _camera = GetComponentInChildren<Camera>();
                    if (_camera == null) _camera = Camera.main;
                }

                return _camera;
            }
        }

        /// <summary>The ground point the camera is centred on.</summary>
        public Vector3 FocusPoint => transform.position;

        private void Awake()
        {
            ApplyConfiguration();
        }

        /// <summary>
        /// Apply orientation, projection and clamping from the serialised fields. Called on Awake and
        /// by the editor bootstrap, so scene-authored and runtime setup cannot drift apart.
        /// </summary>
        public void ApplyConfiguration()
        {
            _orthoSize = Mathf.Clamp(_orthoSize <= 0f ? startOrthoSize : _orthoSize, minOrthoSize, maxOrthoSize);
            ApplyRotation();
            ApplyOrthoSize();
            ClampToBounds();
        }

        private void ApplyRotation()
        {
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            Camera camera = Camera;
            if (camera == null) return;

            camera.orthographic = true;
            camera.transform.localPosition = new Vector3(0f, 0f, -cameraDistance);
            camera.transform.localRotation = Quaternion.identity;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = cameraDistance * 4f;
        }

        private void ApplyOrthoSize()
        {
            Camera camera = Camera;
            if (camera != null) camera.orthographicSize = _orthoSize;
        }

        /// <summary>Change the zoom by an orthographic-size delta, clamped to the configured range.</summary>
        public void ZoomBy(float delta)
        {
            SetOrthoSize(_orthoSize + delta);
        }

        /// <summary>Apply a pinch, where a positive <paramref name="pinchDeltaPixels"/> is fingers spreading apart.</summary>
        public void ZoomByPinch(float pinchDeltaPixels)
        {
            ZoomBy(-pinchDeltaPixels * pinchSensitivity);
        }

        public void SetOrthoSize(float size)
        {
            _orthoSize = Mathf.Clamp(size, minOrthoSize, maxOrthoSize);
            ApplyOrthoSize();
        }

        /// <summary>
        /// Pan so the battlefield moves with the finger. <paramref name="screenDelta"/> is in pixels.
        /// </summary>
        public void PanByScreenDelta(Vector2 screenDelta)
        {
            if (screenDelta.sqrMagnitude <= 0f) return;

            Camera camera = Camera;
            float viewHeight = camera != null && camera.orthographic ? camera.orthographicSize * 2f : _orthoSize * 2f;
            float worldPerPixel = viewHeight / Mathf.Max(1f, Screen.height);

            Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
            Vector3 right = flat * Vector3.right;
            Vector3 forward = flat * Vector3.forward;

            // Dragging right/down should drag the world with the finger, so the rig moves the other way.
            Vector3 move = ((-right * screenDelta.x) - (forward * screenDelta.y)) * worldPerPixel;

            transform.position += move;
            ClampToBounds();
        }

        /// <summary>Centre the camera on a world point, clamped to the battlefield.</summary>
        public void FocusOn(Vector3 worldPoint)
        {
            transform.position = new Vector3(worldPoint.x, transform.position.y, worldPoint.z);
            ClampToBounds();
        }

        public void SetBattlefieldBounds(Bounds bounds)
        {
            battlefieldBounds = bounds;
            ClampToBounds();
        }

        /// <summary>Keep the focus point inside the battlefield, with <see cref="panMargin"/> of slack.</summary>
        public void ClampToBounds()
        {
            Vector3 min = battlefieldBounds.min;
            Vector3 max = battlefieldBounds.max;
            Vector3 position = transform.position;

            position.x = Mathf.Clamp(position.x, min.x - panMargin, max.x + panMargin);
            position.z = Mathf.Clamp(position.z, min.z - panMargin, max.z + panMargin);
            transform.position = position;
        }

        /// <summary>
        /// Mark a gesture as in progress. <see cref="BattleInputController"/> consults this before it
        /// treats a release as a tap, which is what stops a pinch from ordering the army into a
        /// corner of the map.
        /// </summary>
        public void BeginGesture() => IsGesturing = true;

        public void EndGesture() => IsGesturing = false;

        /// <summary>Screen point projected onto the ground plane. Returns false when the ray points at the sky.</summary>
        public bool TryScreenToGround(Vector2 screenPoint, out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;

            Camera camera = Camera;
            if (camera == null) return false;

            var ground = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
            Ray ray = camera.ScreenPointToRay(screenPoint);
            if (!ground.Raycast(ray, out float distance)) return false;

            worldPoint = ray.GetPoint(distance);
            return true;
        }
    }
}
