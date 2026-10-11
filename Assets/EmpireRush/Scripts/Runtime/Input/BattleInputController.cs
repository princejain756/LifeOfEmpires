using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EmpireRush
{
    /// <summary>What the current pointer interaction is doing.</summary>
    public enum InputGestureState
    {
        None = 0,
        Pressed = 1,
        Dragging = 2,
        Gesturing = 3
    }

    /// <summary>
    /// Routes touches to the selection manager and the camera rig, and nothing else.
    ///
    /// The critical rule is that a gesture can never become a troop command. Movement past
    /// <see cref="tapMovementThresholdPixels"/> promotes a press to a drag, which selects instead of
    /// ordering; two fingers promote it to a gesture, which pans and zooms; and once a gesture has
    /// begun, <c>_suppressUntilRelease</c> holds until every finger has left the screen, so the finger
    /// left behind by a pinch cannot fire a tap.
    ///
    /// Compiles against either input backend: the Input System branch is compiled under
    /// <c>ENABLE_INPUT_SYSTEM</c> and the legacy <c>Input.*</c> branch under
    /// <c>ENABLE_LEGACY_INPUT_MANAGER</c>, so the project builds whichever handler is selected in
    /// Player Settings.
    /// </summary>
    public class BattleInputController : MonoBehaviour
    {
        [Header("Wiring")]
        public UnitSelectionManager selection;
        public IsometricCameraRig cameraRig;
        public BuildingPlacementSystem placement;

        [Header("Tap detection")]
        [Tooltip("Pixel movement that turns a press into a drag. Below this, a release is a tap.")]
        [Min(1f)] public float tapMovementThresholdPixels = 22f;

        [Tooltip("Smallest drag box that counts as a rectangle selection rather than a sloppy tap.")]
        [Min(1f)] public float dragBoxMinSizePixels = 28f;

        [Header("Backends")]
        [Tooltip("Accept mouse input as a single pointer (Editor and desktop).")]
        public bool enableMouseInput = true;

        private readonly List<Vector2> _touches = new List<Vector2>(4);

        private InputGestureState _state = InputGestureState.None;
        private Vector2 _pressPosition;
        private float _pressTime;
        private Vector2 _dragCurrent;
        private Vector2 _lastPinchMidpoint;
        private float _lastPinchDistance;
        private bool _hadTwoTouchesLastFrame;
        private bool _suppressUntilRelease;
        private bool _noBackendWarned;

        public InputGestureState State => _state;

        public bool IsDraggingSelection => _state == InputGestureState.Dragging;

        public bool IsGesturing => _state == InputGestureState.Gesturing;

        /// <summary>How long the current press has been held, in seconds. Used as the long-press hook.</summary>
        public float PressDuration => _state == InputGestureState.None ? 0f : Time.unscaledTime - _pressTime;

        private void Awake()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (selection == null) ServiceLocator.TryGet(out selection);
            if (cameraRig == null) ServiceLocator.TryGet(out cameraRig);
            if (placement == null) ServiceLocator.TryGet(out placement);
        }

        private void Update()
        {
            if (selection == null || cameraRig == null) ResolveReferences();
            if (cameraRig == null) return;

            ReadPointer();
            ProcessGestures();
        }

        /// <summary>The rectangle being dragged out this frame, for the UI preview.</summary>
        public bool TryGetDragRectangle(out Rect rectangle)
        {
            rectangle = default;
            if (_state != InputGestureState.Dragging) return false;

            rectangle = BuildRect(_pressPosition, _dragCurrent);
            return true;
        }

        // -----------------------------------------------------------------------------------------
        // Gesture state machine
        // -----------------------------------------------------------------------------------------

        private void ProcessGestures()
        {
            if (_touches.Count >= 2)
            {
                _suppressUntilRelease = true;
                _state = InputGestureState.Gesturing;
                cameraRig.BeginGesture();

                Vector2 midpoint = (_touches[0] + _touches[1]) * 0.5f;
                float distance = Vector2.Distance(_touches[0], _touches[1]);

                if (_hadTwoTouchesLastFrame)
                {
                    cameraRig.ZoomByPinch(distance - _lastPinchDistance);
                    cameraRig.PanByScreenDelta(midpoint - _lastPinchMidpoint);
                }

                _lastPinchDistance = distance;
                _lastPinchMidpoint = midpoint;
                _hadTwoTouchesLastFrame = true;
                return;
            }

            _hadTwoTouchesLastFrame = false;

            if (_touches.Count == 1)
            {
                // A finger left behind by a pinch must not start a fresh press.
                if (_suppressUntilRelease)
                {
                    _state = InputGestureState.Gesturing;
                    return;
                }

                Vector2 point = _touches[0];

                if (_state == InputGestureState.None)
                {
                    _state = InputGestureState.Pressed;
                    _pressPosition = point;
                    _pressTime = Time.unscaledTime;
                    _dragCurrent = point;
                }
                else if (_state == InputGestureState.Pressed)
                {
                    if (Vector2.Distance(point, _pressPosition) > tapMovementThresholdPixels) _state = InputGestureState.Dragging;
                    _dragCurrent = point;
                }
                else if (_state == InputGestureState.Dragging)
                {
                    _dragCurrent = point;
                }

                // While placing, the ghost follows the finger the whole time it is down.
                if (placement != null && placement.IsPlacing) FollowGhostWithFinger(point);
                return;
            }

            // Nothing is touching the screen: resolve whatever the interaction turned out to be.
            if (_suppressUntilRelease)
            {
                _suppressUntilRelease = false;
                _state = InputGestureState.None;
                cameraRig.EndGesture();
                return;
            }

            InputGestureState resolved = _state;
            _state = InputGestureState.None;
            cameraRig.EndGesture();

            switch (resolved)
            {
                case InputGestureState.Pressed:
                    HandleTap(_pressPosition);
                    break;

                case InputGestureState.Dragging:
                    CommitDragBox(BuildRect(_pressPosition, _dragCurrent));
                    break;
            }
        }

        private static Rect BuildRect(Vector2 a, Vector2 b)
        {
            float x = Mathf.Min(a.x, b.x);
            float y = Mathf.Min(a.y, b.y);
            return new Rect(x, y, Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        private void CommitDragBox(Rect rectangle)
        {
            if (selection == null) return;

            // A drag that never opened a real box is a sloppy tap, not an empty selection.
            if (rectangle.width < dragBoxMinSizePixels && rectangle.height < dragBoxMinSizePixels)
            {
                HandleTap(_pressPosition);
                return;
            }

            selection.SelectUnitsInScreenRect(rectangle, selection.dragBoxFilter);
        }

        // -----------------------------------------------------------------------------------------
        // Tap resolution
        // -----------------------------------------------------------------------------------------

        private void HandleTap(Vector2 screenPoint)
        {
            if (selection == null || cameraRig == null) return;

            if (placement != null && placement.IsPlacing)
            {
                ConfirmPlacement(screenPoint);
                return;
            }

            // A tap on a hostile takes priority: with an army selected, tapping an enemy is an order.
            IDamageable hostile = FindHostileUnderScreenPoint(screenPoint);
            if (hostile != null && selection.SelectionCount > 0)
            {
                OrderAttack(hostile);
                return;
            }

            if (selection.TrySelectAtScreenPoint(screenPoint)) return;

            if (selection.SelectionCount > 0 && cameraRig.TryScreenToGround(screenPoint, out Vector3 groundPoint))
            {
                selection.CommandMove(groundPoint);
            }
        }

        private void OrderAttack(IDamageable target)
        {
            IReadOnlyList<UnitController> chosen = selection.Selection;
            for (int i = 0; i < chosen.Count; i++)
            {
                UnitController unit = chosen[i];
                if (unit == null || unit.IsDead || !unit.IsCombatUnit) continue;
                unit.Attack(target);
            }
        }

        private void FollowGhostWithFinger(Vector2 screenPoint)
        {
            if (!cameraRig.TryScreenToGround(screenPoint, out Vector3 worldPoint)) return;
            placement.UpdateGhost(worldPoint, ResolvePlayerResources());
        }

        private void ConfirmPlacement(Vector2 screenPoint)
        {
            if (!cameraRig.TryScreenToGround(screenPoint, out Vector3 worldPoint)) return;

            ResourceManager payer = ResolvePlayerResources();
            placement.UpdateGhost(worldPoint, payer);
            placement.Confirm(payer);
        }

        private static ResourceManager ResolvePlayerResources()
        {
            BattleContext context = BattleContext.Current;
            return context?.PlayerResources;
        }

        /// <summary>
        /// Nearest hostile to a screen point, by the same screen-space test selection uses — so
        /// tapping an enemy is exactly as forgiving as tapping a friend.
        /// </summary>
        public IDamageable FindHostileUnderScreenPoint(Vector2 screenPoint)
        {
            Camera camera = cameraRig != null ? cameraRig.Camera : Camera.main;
            BattleContext context = BattleContext.Current;
            if (camera == null || context == null) return null;

            float radius = selection != null ? selection.tapScreenRadius : 72f;
            float bestSqr = radius * radius;
            IDamageable best = null;

            for (int i = 0; i < context.EnemyUnits.Count; i++)
            {
                UnitController unit = context.EnemyUnits[i];
                if (unit == null || unit.IsDead) continue;
                Consider(unit, camera, screenPoint, ref best, ref bestSqr);
            }

            for (int i = 0; i < context.EnemyBuildings.Count; i++)
            {
                BuildingController building = context.EnemyBuildings[i];
                if (building == null || building.IsDestroyed) continue;
                Consider(building, camera, screenPoint, ref best, ref bestSqr);
            }

            return best;
        }

        private static void Consider(IDamageable candidate, Camera camera, Vector2 screenPoint, ref IDamageable best, ref float bestSqr)
        {
            Vector3 projected = camera.WorldToScreenPoint(candidate.WorldPosition);
            if (projected.z <= 0f) return;

            float sqr = ((Vector2)projected - screenPoint).sqrMagnitude;
            if (sqr > bestSqr) return;

            bestSqr = sqr;
            best = candidate;
        }

        // -----------------------------------------------------------------------------------------
        // Input backends
        // -----------------------------------------------------------------------------------------

        private void ReadPointer()
        {
            _touches.Clear();

#if ENABLE_INPUT_SYSTEM
            ReadPointerInputSystem();
#elif ENABLE_LEGACY_INPUT_MANAGER
            ReadPointerLegacy();
#else
            if (!_noBackendWarned)
            {
                Debug.LogError("BattleInputController: neither ENABLE_INPUT_SYSTEM nor ENABLE_LEGACY_INPUT_MANAGER is defined; no input will be read. Set Active Input Handling in Player Settings.");
                _noBackendWarned = true;
            }
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void ReadPointerInputSystem()
        {
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (!touches[i].press.isPressed) continue;
                    _touches.Add(touches[i].position.ReadValue());
                }
            }

            if (_touches.Count > 0) return;
            if (!enableMouseInput) return;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) _touches.Add(mouse.position.ReadValue());
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        private void ReadPointerLegacy()
        {
            int touchCount = Input.touchCount;
            for (int i = 0; i < touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                // Fully qualified: the Input System package also defines a TouchPhase enum.
                if (touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled) continue;
                _touches.Add(touch.position);
            }

            if (_touches.Count > 0) return;
            if (!enableMouseInput) return;
            if (Input.GetMouseButton(0)) _touches.Add(Input.mousePosition);
        }
#endif
    }
}
