using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>Lifecycle of a unit. Dead is terminal.</summary>
    public enum UnitState
    {
        Idle = 0,
        Moving = 1,
        Gathering = 2,
        Attacking = 3,
        Dead = 4
    }

    /// <summary>
    /// One unit on the battlefield: health, team, state machine and steering. It owns its own
    /// movement and combat loop, driven from Update, and delegates every consequential decision —
    /// pathfinding, damage, resource totals — to the shared systems so there is one authority for each.
    ///
    /// Steering is "seek the next path node, plus separation from nearby units": units therefore never
    /// stack on a single grid cell, which is what kills an RTS at the touch layer, since overlapping
    /// units cannot be individually tapped.
    /// </summary>
    public class UnitController : MonoBehaviour, IDamageable
    {
        [Header("Identity")]
        public Team team = Team.Player;

        [Tooltip("Authored definition supplying every balance number. Assigned by the spawner.")]
        public UnitDefinition definition;

        [Header("Steering")]
        [Tooltip("Metres from a waypoint at which the unit advances to the next one.")]
        [Min(0.05f)] public float waypointTolerance = 0.35f;

        [Tooltip("Metres of personal space kept from other units.")]
        [Min(0f)] public float separationRadius = 0.9f;

        [Range(0f, 3f)] public float separationWeight = 1.1f;

        [Tooltip("Units considered for separation per frame. Bounds the steering cost in big battles.")]
        [Min(1)] public int maxSeparationNeighbours = 8;

        [Tooltip("Enemies within this radius are engaged automatically while idle.")]
        [Min(0f)] public float aggroRadius = 7f;

        [Header("Presentation")]
        [Min(0.1f)] public float selectionRingScale = 1.35f;

        [Min(0f)] public float deathFadeSeconds = 0.15f;

        [Header("Runtime state (read-only)")]
        [SerializeField] private UnitState state = UnitState.Idle;
        [SerializeField] private int health = 100;
        [SerializeField] private bool selected;

        private PathfindingManager _pathfinding;
        private CombatSystem _combat;
        private HealthBar _healthBar;
        private UnitAnimator _animator;

        private Vector3[] _path;
        private int _pathIndex;
        private Action<bool> _arrivalCallback;
        private IDamageable _attackTarget;
        private float _attackCooldownRemaining;
        private Transform _selectionRing;
        private bool _dead;
        private bool _initialised;

        /// <summary>Explicit interface implementation of <see cref="IDamageable.Team"/>, backed by the team field.</summary>
        public Team Team => team;

        public UnitState State => state;

        /// <summary>True once <see cref="Initialize"/> has wired the shared systems.</summary>
        public bool IsInitialised => _initialised;

        public bool IsDead => _dead;

        public bool IsAlive => !_dead && health > 0;

        public int Health => health;

        public int MaxHealth => definition != null ? definition.maxHealth : Mathf.Max(1, health);

        public Vector3 WorldPosition => transform.position;

        public bool IsSelected => selected;

        public bool IsMoving => state == UnitState.Moving || (_path != null && _pathIndex < _path.Length);

        public UnitDefinition Definition => definition;

        /// <summary>Combat units fight; workers (damage 0, no counters) do not.</summary>
        public bool IsCombatUnit => definition != null && definition.IsCombatUnit;

        public float MoveSpeed => definition != null ? Mathf.Max(0.05f, definition.moveSpeed) : 1f;

        public int PopulationCost => definition != null ? definition.populationCost : 0;

        /// <summary>Where this unit drops off gathered resources. Assigned by the spawner.</summary>
        public BuildingController DropOff { get; set; }

        /// <summary>Stockpile this unit deposits into. Assigned by the spawner.</summary>
        public ResourceManager ResourceSink { get; set; }

        /// <summary>Raised when this unit's health changes. Arguments: current, max.</summary>
        public event Action<UnitController, int, int> OnHealthChanged;

        private void Awake()
        {
            health = definition != null ? definition.maxHealth : Mathf.Max(1, health);
        }

        /// <summary>Wire the shared systems. Called by the spawner immediately after AddComponent.</summary>
        public void Initialize(UnitDefinition unitDefinition, Team owningTeam, PathfindingManager pathfinding, CombatSystem combat)
        {
            if (_initialised) return;
            _initialised = true;

            definition = unitDefinition;
            team = owningTeam;
            _pathfinding = pathfinding;
            _combat = combat;
            health = definition != null ? definition.maxHealth : health;
            _dead = false;
            state = UnitState.Idle;

            if (_combat != null)
            {
                _combat.Track(this);
                _combat.OnDied += HandleDied;
            }

            EnsureHealthBar();
            RaiseHealthChanged();
        }

        private void EnsureHealthBar()
        {
            if (_healthBar != null) return;
            _healthBar = GetComponentInChildren<HealthBar>();
            if (_healthBar == null) _healthBar = gameObject.AddComponent<HealthBar>();
            _healthBar.Bind(transform, MaxHealth);
            _healthBar.Report(health);
        }

        // -----------------------------------------------------------------------------------------
        // Orders
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Walk to a point. <paramref name="onComplete"/> always runs exactly once: true when the unit
        /// arrived, false when the destination was unreachable. A failed request must never leave a
        /// worker or a squad waiting forever.
        /// </summary>
        public void MoveTo(Vector3 destination, Action<bool> onComplete = null)
        {
            if (IsDead) return;

            ClearPath();
            _arrivalCallback = onComplete;
            _attackTarget = null;
            state = UnitState.Moving;

            if (_pathfinding == null)
            {
                // No navigation grid (tests, or a battlefield without one): walk straight there.
                CompleteArrival(false);
                return;
            }

            bool queued = _pathfinding.RequestPath(transform.position, destination, HandlePathResult);
            if (!queued) CompleteArrival(false);
        }

        /// <summary>Move, then engage anything hostile found on arrival.</summary>
        public void AttackMoveTo(Vector3 destination)
        {
            if (IsDead) return;
            MoveTo(destination, arrived =>
            {
                if (!arrived) return;
                EnterCombatIfEnemyNearby();
            });
        }

        /// <summary>Order a direct attack on a specific target, closing to weapon range first.</summary>
        public void Attack(IDamageable target)
        {
            if (IsDead || target == null || !target.IsAlive) return;
            _attackTarget = target;
            state = UnitState.Attacking;
            ClearPath();
            _arrivalCallback = null;
        }

        public void Stop()
        {
            if (IsDead) return;
            ClearPath();
            _attackTarget = null;
            _arrivalCallback = null;
            state = UnitState.Idle;
        }

        /// <summary>Mark the unit as busy gathering. Called by <see cref="WorkerAgent"/>.</summary>
        public void EnterGathering()
        {
            if (IsDead) return;
            state = UnitState.Gathering;
        }

        public void SetSelected(bool value)
        {
            selected = value;
            if (_selectionRing != null) _selectionRing.gameObject.SetActive(value);
            if (_healthBar != null)
            {
                _healthBar.forceVisible = value;
                if (value) _healthBar.ShowUntil(1f);
            }

            if (value) EnsureSelectionRing();
        }

        private void EnsureSelectionRing()
        {
            if (_selectionRing != null) return;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectionRing";

            Collider collider = ring.GetComponent<Collider>();
            if (collider != null)
            {
                // Illegal outside play mode, and rings are built whenever a unit is constructed.
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            ring.transform.SetParent(transform, false);
            ring.transform.localScale = new Vector3(selectionRingScale, 0.02f, selectionRingScale);

            var renderer = ring.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    var material = new Material(shader) { name = "SelectionRing" };
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0.4f, 1f, 0.5f, 0.7f));
                    else if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(0.4f, 1f, 0.5f, 0.7f));
                    renderer.sharedMaterial = material;
                }

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _selectionRing = ring.transform;
            _selectionRing.gameObject.SetActive(selected);
        }

        // -----------------------------------------------------------------------------------------
        // Damage
        // -----------------------------------------------------------------------------------------

        public void ReceiveDamage(int amount, Team sourceTeam)
        {
            if (_dead || amount <= 0) return;
            health = Mathf.Max(0, health - amount);
            RaiseHealthChanged();

            // A unit shot while idle looks for the shooter's side rather than standing there.
            if (state == UnitState.Idle && sourceTeam != team) EnterCombatIfEnemyNearby();
        }

        private void RaiseHealthChanged()
        {
            OnHealthChanged?.Invoke(this, health, MaxHealth);
            if (_healthBar != null) _healthBar.Report(health);
        }

        private void HandleDied(IDamageable victim)
        {
            if (!ReferenceEquals(victim, this)) return;
            Die();
        }

        private void Die()
        {
            if (_dead) return;
            _dead = true;
            state = UnitState.Dead;
            health = 0;

            if (_animator == null) _animator = GetComponent<UnitAnimator>();
            if (_animator != null) _animator.PlayDeath();

            ClearPath();
            _attackTarget = null;
            _arrivalCallback = null;

            if (_combat != null) _combat.OnDied -= HandleDied;
            RemoveFromRosters();

            Collider collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;

            if (_healthBar != null) _healthBar.Hide();
            if (_selectionRing != null) _selectionRing.gameObject.SetActive(false);

            if (Application.isPlaying) Destroy(gameObject, Mathf.Max(0f, deathFadeSeconds));
            else DestroyImmediate(gameObject);
        }

        private void RemoveFromRosters()
        {
            BattleContext context = BattleContext.Current;
            if (context == null) return;
            context.UnitsOf(team).Remove(this);
        }

        // -----------------------------------------------------------------------------------------
        // Per-frame behaviour
        // -----------------------------------------------------------------------------------------

        private void Update()
        {
            if (_dead) return;

            switch (state)
            {
                case UnitState.Moving:
                    FollowPath();
                    break;

                case UnitState.Attacking:
                    UpdateAttacking();
                    break;

                case UnitState.Idle:
                    UpdateIdle();
                    break;

                case UnitState.Gathering:
                    // WorkerAgent owns the gather loop; the controller only holds the state label.
                    break;
            }

            if (_attackCooldownRemaining > 0f) _attackCooldownRemaining -= Time.deltaTime;

            if (_healthBar != null && _healthBar.isActiveAndEnabled) _healthBar.Report(health);
        }

        private void UpdateIdle()
        {
            if (!IsCombatUnit) return;
            if (_combat == null) return;
            if (_combat.FindNearestEnemy(team, transform.position, aggroRadius) != null) EnterCombatIfEnemyNearby();
        }

        private void UpdateAttacking()
        {
            if (_attackTarget == null || !_attackTarget.IsAlive)
            {
                _attackTarget = null;
                CompleteArrival(true);
                return;
            }

            float range = definition != null ? definition.attackRange : 1.5f;
            Vector3 toTarget = _attackTarget.WorldPosition - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude > range * range)
            {
                // Close the distance. The attack order must survive the walk there, and a fresh
                // request is only issued when no path is already in flight — otherwise a unit
                // crossing the map would flood the queue with one request per frame.
                if (!IsMoving)
                {
                    ClearPath();
                    state = UnitState.Moving;
                    IDamageable chased = _attackTarget;
                    _arrivalCallback = _ =>
                    {
                        if (chased != null && chased.IsAlive) state = UnitState.Attacking;
                        else state = UnitState.Idle;
                    };

                    if (_pathfinding == null || !_pathfinding.RequestPath(transform.position, _attackTarget.WorldPosition, HandlePathResult))
                    {
                        _arrivalCallback = null;
                        _attackTarget = null;
                        state = UnitState.Idle;
                    }
                }

                return;
            }

            ClearPath();

            if (_attackCooldownRemaining > 0f) return;
            if (definition != null && _combat != null && _combat.ResolveAttack(this, _attackTarget))
            {
                _attackCooldownRemaining = Mathf.Max(0.05f, definition.attackInterval);
            }
        }

        private void EnterCombatIfEnemyNearby()
        {
            if (!IsCombatUnit || _combat == null) return;
            IDamageable enemy = _combat.FindNearestEnemy(team, transform.position, Mathf.Max(aggroRadius, definition != null ? definition.attackRange : 1.5f));
            if (enemy != null) Attack(enemy);
        }

        // -----------------------------------------------------------------------------------------
        // Path following and steering
        // -----------------------------------------------------------------------------------------

        private void HandlePathResult(PathResult result)
        {
            if (_dead) return;

            if (!result.Success || result.Points == null || result.Points.Length == 0)
            {
                _path = null;
                _pathIndex = 0;
                CompleteArrival(false);
                return;
            }

            _path = result.Points;
            _pathIndex = 0;
        }

        private void FollowPath()
        {
            if (_path == null || _pathIndex >= _path.Length)
            {
                CompleteArrival(true);
                return;
            }

            Vector3 target = _path[_pathIndex];
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= waypointTolerance * waypointTolerance)
            {
                _pathIndex++;
                if (_pathIndex >= _path.Length) CompleteArrival(true);
                return;
            }

            Vector3 heading = toTarget.normalized;
            Vector3 separation = ComputeSeparation();

            Vector3 velocity = (heading + (separation * separationWeight)).normalized * MoveSpeed;
            Vector3 next = transform.position + (velocity * Time.deltaTime);
            next.y = transform.position.y;
            transform.position = next;

            if (heading.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
        }

        /// <summary>
        /// Push away from crowded neighbours so units stay individually tappable. Bounded by
        /// <see cref="maxSeparationNeighbours"/>, so steering cost does not scale with army size.
        /// </summary>
        private Vector3 ComputeSeparation()
        {
            if (separationRadius <= 0f || separationWeight <= 0f) return Vector3.zero;

            BattleContext context = BattleContext.Current;
            if (context == null) return Vector3.zero;

            Vector3 push = Vector3.zero;
            int considered = 0;
            float radiusSqr = separationRadius * separationRadius;

            AccumulateSeparation(context.PlayerUnits, ref push, ref considered, radiusSqr);
            AccumulateSeparation(context.EnemyUnits, ref push, ref considered, radiusSqr);

            if (considered == 0) return Vector3.zero;

            push.y = 0f;
            return push.sqrMagnitude > 1f ? push.normalized : push;
        }

        private void AccumulateSeparation(List<UnitController> neighbours, ref Vector3 push, ref int considered, float radiusSqr)
        {
            for (int i = 0; i < neighbours.Count && considered < maxSeparationNeighbours; i++)
            {
                UnitController other = neighbours[i];
                if (other == null || other == this || other.IsDead) continue;

                Vector3 offset = transform.position - other.transform.position;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                if (sqr > radiusSqr || sqr < 0.0001f) continue;

                push += offset.normalized * (1f - (Mathf.Sqrt(sqr) / separationRadius));
                considered++;
            }
        }

        private void CompleteArrival(bool arrived)
        {
            _path = null;
            _pathIndex = 0;
            if (state == UnitState.Moving) state = UnitState.Idle;

            Action<bool> callback = _arrivalCallback;
            _arrivalCallback = null;
            callback?.Invoke(arrived);
        }

        private void ClearPath()
        {
            _path = null;
            _pathIndex = 0;
        }

        private void OnDestroy()
        {
            if (_combat != null)
            {
                _combat.OnDied -= HandleDied;
                _combat.Untrack(this);
            }

            RemoveFromRosters();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.4f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, aggroRadius);
            Gizmos.color = new Color(0.2f, 0.7f, 0.9f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, separationRadius);

            if (_path == null) return;
            Gizmos.color = Color.yellow;
            for (int i = 1; i < _path.Length; i++) Gizmos.DrawLine(_path[i - 1], _path[i]);
        }
#endif
    }
}
