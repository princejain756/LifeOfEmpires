using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>What a selection gesture is allowed to pick up.</summary>
    public enum UnitFilter
    {
        /// <summary>Anything the player owns.</summary>
        All = 0,

        /// <summary>Combat units only — the "select army" behaviour.</summary>
        CombatOnly = 1,

        /// <summary>Workers only.</summary>
        WorkersOnly = 2
    }

    /// <summary>
    /// Player selection. Taps resolve by projecting every owned unit to screen space and taking the
    /// nearest within <see cref="tapScreenRadius"/> pixels rather than by physics raycast. On a phone
    /// a soldier is a handful of pixels wide, and a raycast against a thin primitive misses far more
    /// often than it hits; a screen-space radius makes small units reliably tappable and is
    /// resolution-independent besides.
    ///
    /// Dragging selects by screen rectangle with an optional type filter; a double-tap selects every
    /// nearby unit of the same type.
    /// </summary>
    public class UnitSelectionManager : MonoBehaviour
    {
        [Header("Tapping")]
        [Tooltip("Pixel radius around a tap inside which the nearest owned unit is selected.")]
        [Min(1f)] public float tapScreenRadius = 72f;

        [Header("Double tap")]
        [Tooltip("Two taps closer together in time than this count as a double tap.")]
        [Min(0.05f)] public float doubleTapMaxDelay = 0.35f;

        [Tooltip("Maximum pixel drift between the two taps of a double tap.")]
        [Min(1f)] public float doubleTapMaxDrift = 90f;

        [Tooltip("World radius searched for same-type units when a double tap fires.")]
        [Min(0.5f)] public float doubleTapSearchRadius = 8f;

        [Header("Drag box")]
        [Tooltip("Type filter applied to rectangle selections.")]
        public UnitFilter dragBoxFilter = UnitFilter.CombatOnly;

        [Header("Selection ring")]
        public bool showSelectionRings = true;

        private readonly List<UnitController> _selection = new List<UnitController>(64);
        private readonly List<UnitController> _scratch = new List<UnitController>(64);

        [Tooltip("Gameplay camera used to project units to screen space. Must be assigned: the manager never guesses one.")]
        [SerializeField] private Camera _camera;
        private float _lastTapTime = float.NegativeInfinity;
        private Vector2 _lastTapPosition;
        private UnitController _lastTapUnit;

        /// <summary>Fired whenever the selection set changes. The list passed is the live selection.</summary>
        public event Action<IReadOnlyList<UnitController>> OnSelectionChanged;

        public IReadOnlyList<UnitController> Selection => _selection;

        public int SelectionCount => _selection.Count;

        /// <summary>
        /// The camera used for projection. Never resolved implicitly: falling back to
        /// <see cref="Camera.main"/> would let a stray scene camera answer taps on the player's
        /// behalf and select units nobody touched. Unset means selection is disabled with a warning.
        /// </summary>
        public Camera SelectionCamera
        {
            get => _camera;
            set => _camera = value;
        }

        // -----------------------------------------------------------------------------------------
        // Tapping
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Select the owned unit nearest to a screen point, within <see cref="tapScreenRadius"/>.
        /// Returns false when nothing was close enough, leaving the current selection untouched.
        /// </summary>
        public bool TrySelectAtScreenPoint(Vector2 screenPoint, bool additive = false)
        {
            UnitController hit = FindNearestUnitToScreenPoint(screenPoint);

            if (hit == null)
            {
                // A double tap that misses a unit still selects the same type as the first tap.
                if (IsDoubleTap(screenPoint, null)) SelectSameTypeNear(_lastTapUnit);
                else RecordTap(screenPoint, null);
                return false;
            }

            if (IsDoubleTap(screenPoint, hit))
            {
                SelectSameTypeNear(hit);
                RecordTap(screenPoint, hit);
                return true;
            }

            if (additive)
            {
                if (_selection.Contains(hit))
                {
                    SetSelected(hit, false);
                    _selection.Remove(hit);
                }
                else
                {
                    _selection.Add(hit);
                    SetSelected(hit, true);
                }
            }
            else
            {
                ClearSelectionInternal();
                _selection.Add(hit);
                SetSelected(hit, true);
            }

            RecordTap(screenPoint, hit);
            RaiseChanged();
            return true;
        }

        /// <summary>Project every owned unit and return the closest one inside the tap radius.</summary>
        public UnitController FindNearestUnitToScreenPoint(Vector2 screenPoint)
        {
            Camera camera = SelectionCamera;
            if (camera == null)
            {
                Debug.LogWarning("UnitSelectionManager: no camera available; tap selection skipped.");
                return null;
            }

            List<UnitController> units = OwnUnits();
            if (units == null) return null;

            float bestSqr = tapScreenRadius * tapScreenRadius;
            UnitController best = null;

            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead) continue;

                Vector3 projected = camera.WorldToScreenPoint(unit.transform.position);
                if (projected.z <= 0f) continue; // behind the camera

                float sqr = ((Vector2)projected - screenPoint).sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                best = unit;
            }

            return best;
        }

        private bool IsDoubleTap(Vector2 screenPoint, UnitController candidate)
        {
            if (Time.unscaledTime - _lastTapTime > doubleTapMaxDelay) return false;
            if (Vector2.Distance(screenPoint, _lastTapPosition) > doubleTapMaxDrift) return false;
            if (candidate == null || _lastTapUnit == null) return false;
            return candidate.Definition != null && _lastTapUnit.Definition != null
                && candidate.Definition.id == _lastTapUnit.Definition.id;
        }

        private void RecordTap(Vector2 screenPoint, UnitController unit)
        {
            _lastTapTime = Time.unscaledTime;
            _lastTapPosition = screenPoint;
            _lastTapUnit = unit;
        }

        /// <summary>Select every owned unit sharing <paramref name="reference"/>'s type within double-tap range.</summary>
        public int SelectSameTypeNear(UnitController reference)
        {
            if (reference == null || reference.Definition == null) return 0;

            ClearSelectionInternal();

            string typeId = reference.Definition.id;
            float radiusSqr = doubleTapSearchRadius * doubleTapSearchRadius;
            List<UnitController> units = OwnUnits();

            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    UnitController unit = units[i];
                    if (unit == null || unit.IsDead || unit.Definition == null) continue;
                    if (unit.Definition.id != typeId) continue;
                    if ((unit.transform.position - reference.transform.position).sqrMagnitude > radiusSqr) continue;

                    _selection.Add(unit);
                    SetSelected(unit, true);
                }
            }

            RaiseChanged();
            return _selection.Count;
        }

        // -----------------------------------------------------------------------------------------
        // Drag box
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Select every owned unit projected inside a screen rectangle, honouring the type filter.
        /// </summary>
        public int SelectUnitsInScreenRect(Rect screenRect, UnitFilter filter, bool additive = false)
        {
            if (!additive) ClearSelectionInternal();

            CollectUnitsInScreenRect(screenRect, filter, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                UnitController unit = _scratch[i];
                if (_selection.Contains(unit)) continue;
                _selection.Add(unit);
                SetSelected(unit, true);
            }

            RaiseChanged();
            return _selection.Count;
        }

        /// <summary>
        /// Gather the units a rectangle would select. Exposed so the drag preview can render an
        /// accurate count before the player releases their finger.
        /// </summary>
        public void CollectUnitsInScreenRect(Rect screenRect, UnitFilter filter, List<UnitController> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();

            Camera camera = SelectionCamera;
            if (camera == null)
            {
                Debug.LogWarning("UnitSelectionManager: no camera available; rectangle selection skipped.");
                return;
            }

            List<UnitController> units = OwnUnits();
            if (units == null) return;

            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead) continue;
                if (!MatchesFilter(unit, filter)) continue;

                Vector3 projected = camera.WorldToScreenPoint(unit.transform.position);
                if (projected.z <= 0f) continue;
                if (!screenRect.Contains(projected)) continue;

                results.Add(unit);
            }
        }

        /// <summary>Type filter used by select-all, drag boxes and buttons.</summary>
        public static bool MatchesFilter(UnitController unit, UnitFilter filter)
        {
            if (unit == null || unit.IsDead) return false;

            switch (filter)
            {
                case UnitFilter.CombatOnly: return unit.IsCombatUnit;
                case UnitFilter.WorkersOnly: return !unit.IsCombatUnit;
                default: return true;
            }
        }

        // -----------------------------------------------------------------------------------------
        // Buttons
        // -----------------------------------------------------------------------------------------

        /// <summary>"Select all combat units" button. Never picks up workers.</summary>
        public int SelectAllCombatUnits()
        {
            ClearSelectionInternal();

            List<UnitController> units = OwnUnits();
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    UnitController unit = units[i];
                    if (!MatchesFilter(unit, UnitFilter.CombatOnly)) continue;
                    _selection.Add(unit);
                    SetSelected(unit, true);
                }
            }

            RaiseChanged();
            return _selection.Count;
        }

        /// <summary>Select every owned unit, combat and worker alike.</summary>
        public int SelectAll()
        {
            ClearSelectionInternal();

            List<UnitController> units = OwnUnits();
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    UnitController unit = units[i];
                    if (unit == null || unit.IsDead) continue;
                    _selection.Add(unit);
                    SetSelected(unit, true);
                }
            }

            RaiseChanged();
            return _selection.Count;
        }

        /// <summary>"Center on town hall" button: snaps the camera rig back to the player's town hall.</summary>
        public bool CenterOnTownHall()
        {
            BattleContext context = BattleContext.Current;
            if (context == null) return false;

            BuildingController townHall = context.TownHallOf(Team.Player);
            if (townHall == null)
            {
                Debug.LogWarning("UnitSelectionManager: no player town hall to centre on.");
                return false;
            }

            if (!ServiceLocator.TryGet(out IsometricCameraRig rig))
            {
                Debug.LogWarning("UnitSelectionManager: no camera rig registered; cannot centre on the town hall.");
                return false;
            }

            rig.FocusOn(townHall.transform.position);
            return true;
        }

        public void ClearSelection()
        {
            ClearSelectionInternal();
            RaiseChanged();
        }

        // -----------------------------------------------------------------------------------------
        // Commands
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Order the selection to move to a world point, spread into a loose formation so the squad
        /// does not pile onto one cell.
        /// </summary>
        public int CommandMove(Vector3 destination, float formationSpacing = 1.4f)
        {
            if (_selection.Count == 0) return 0;

            int columns = Mathf.CeilToInt(Mathf.Sqrt(_selection.Count));
            int ordered = 0;

            for (int i = 0; i < _selection.Count; i++)
            {
                UnitController unit = _selection[i];
                if (unit == null || unit.IsDead) continue;

                int row = i / Mathf.Max(1, columns);
                int column = i % Mathf.Max(1, columns);
                float offsetX = (column - ((columns - 1) * 0.5f)) * formationSpacing;
                float offsetZ = (row - ((columns - 1) * 0.5f)) * formationSpacing;

                unit.MoveTo(destination + new Vector3(offsetX, 0f, offsetZ));
                ordered++;
            }

            return ordered;
        }

        /// <summary>Order the selection to advance on a point and engage whatever it meets.</summary>
        public int CommandAttackMove(Vector3 destination)
        {
            int ordered = 0;
            for (int i = 0; i < _selection.Count; i++)
            {
                UnitController unit = _selection[i];
                if (unit == null || unit.IsDead || !unit.IsCombatUnit) continue;
                unit.AttackMoveTo(destination);
                ordered++;
            }

            return ordered;
        }

        // -----------------------------------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------------------------------

        private static List<UnitController> OwnUnits()
        {
            BattleContext context = BattleContext.Current;
            return context?.PlayerUnits;
        }

        private void ClearSelectionInternal()
        {
            for (int i = 0; i < _selection.Count; i++) SetSelected(_selection[i], false);
            _selection.Clear();
        }

        private void SetSelected(UnitController unit, bool value)
        {
            if (unit == null) return;
            unit.SetSelected(showSelectionRings && value);
        }

        private void RaiseChanged() => OnSelectionChanged?.Invoke(_selection);

        private void Update()
        {
            // Drop anything that died since the last frame so orders never target a corpse.
            bool changed = false;
            for (int i = _selection.Count - 1; i >= 0; i--)
            {
                UnitController unit = _selection[i];
                if (unit != null && !unit.IsDead) continue;
                _selection.RemoveAt(i);
                changed = true;
            }

            if (changed) RaiseChanged();
        }
    }
}
