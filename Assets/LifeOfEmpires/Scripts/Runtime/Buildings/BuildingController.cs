using System;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// One building on the battlefield: health, team, construction progress, and the footprint it
    /// occupies on the navigation grid. Buildings are <see cref="IDamageable"/>, so they flow through
    /// the same damage funnel as units and the objective manager can simply subscribe to deaths.
    /// </summary>
    public class BuildingController : MonoBehaviour, IDamageable
    {
        [Header("Identity")]
        public Team team = Team.Player;

        [Tooltip("Authored definition. Assigned by the spawner or the placement system.")]
        public BuildingDefinition definition;

        [Header("Presentation")]
        public Color completedColor = new Color(0.72f, 0.66f, 0.55f);
        public Color blueprintColor = new Color(0.45f, 0.62f, 0.85f);

        [Header("Runtime state (read-only)")]
        [SerializeField] private int health;
        [SerializeField] private float constructionProgress;
        [SerializeField] private bool destroyed;

        [Header("Training (read-only)")]
        [SerializeField] private string trainingUnitId = string.Empty;
        [SerializeField] private float trainingSecondsRemaining;
        [SerializeField] private float trainingSecondsTotal;

        private CombatSystem _combat;
        private Renderer _renderer;
        private Material _instanceMaterial;
        private ResourceManager _trainingPayer;
        private bool _occupiesGrid;
        private bool _initialised;
        private int _gridOriginX;
        private int _gridOriginY;

        /// <summary>Fired when construction reaches 1.0.</summary>
        public event Action<BuildingController> OnConstructionCompleted;

        /// <summary>Fired once, when health reaches zero.</summary>
        public event Action<BuildingController> OnDestroyed;

        /// <summary>Fired whenever health changes. Arguments: current, max.</summary>
        public event Action<BuildingController, int, int> OnHealthChanged;

        /// <summary>Fired when a trained unit leaves the building. Argument: the spawned unit.</summary>
        public event Action<UnitController> OnUnitTrained;

        /// <summary>True while a unit is being produced. One production slot per building.</summary>
        public bool IsTraining => !string.IsNullOrEmpty(trainingUnitId);

        public string TrainingUnitId => trainingUnitId;

        /// <summary>0..1 production progress, for the training UI.</summary>
        public float TrainingProgress => !IsTraining || trainingSecondsTotal <= 0f
            ? 0f
            : Mathf.Clamp01(1f - (trainingSecondsRemaining / trainingSecondsTotal));

        public Team Team => team;
        public bool IsAlive => !destroyed && health > 0;
        public int Health => health;
        public int MaxHealth => definition != null ? definition.maxHealth : Mathf.Max(1, health);
        public Vector3 WorldPosition => transform.position;
        public bool IsDestroyed => destroyed;

        /// <summary>0 while a blueprint, 1 when finished.</summary>
        public float ConstructionProgress => constructionProgress;

        public bool IsConstructed => constructionProgress >= 1f;

        public bool IsTownHall => definition != null && definition.isTownHall;

        /// <summary>True once <see cref="Initialize"/> has wired the shared systems.</summary>
        public bool IsInitialised => _initialised;

        /// <summary>The authored definition behind this building.</summary>
        public BuildingDefinition Definition => definition;

        public string ProducesUnitId => definition != null ? definition.producesUnitId : string.Empty;

        public int PopulationProvided => definition != null ? definition.popProvided : 0;

        /// <summary>Where a worker stands to build or to hand over gathered resources.</summary>
        public Vector3 InteractionPoint
        {
            get
            {
                Vector2Int footprint = definition != null ? definition.footprint : new Vector2Int(2, 2);
                return transform.position + new Vector3(0f, 0f, (-footprint.y * 0.5f) - 0.6f);
            }
        }

        /// <summary>Wire shared systems. Called by the spawner straight after AddComponent.</summary>
        public void Initialize(BuildingDefinition buildingDefinition, Team owningTeam, CombatSystem combat)
        {
            if (_initialised) return;
            _initialised = true;

            definition = buildingDefinition;
            team = owningTeam;
            _combat = combat;
            health = MaxHealth;
            destroyed = false;

            _renderer = GetComponentInChildren<Renderer>();
            _instanceMaterial = _renderer != null ? _renderer.material : null;

            if (_combat != null)
            {
                _combat.Track(this);
                _combat.OnDied += HandleDied;
            }

            // Spawned buildings start finished; the placement system rewinds this to zero.
            constructionProgress = 1f;
            ApplyTint();
            OccupyGrid();
        }

        /// <summary>Begin as an unfunded blueprint: zero progress, no grid occupancy yet.</summary>
        public void BeginConstruction()
        {
            constructionProgress = 0f;
            health = Mathf.Max(1, MaxHealth);
            ApplyTint();
        }

        /// <summary>
        /// Advance construction. The caller multiplies elapsed time by the worker count and by
        /// <see cref="EconomyBalance.buildProgressPerWorkerSecond"/>, so the pace stays a data value.
        /// </summary>
        public bool AddConstructionWork(float progressDelta)
        {
            if (destroyed || progressDelta <= 0f) return IsConstructed;
            if (IsConstructed) return true;

            constructionProgress = Mathf.Clamp01(constructionProgress + progressDelta);
            ApplyTint();

            if (constructionProgress >= 1f)
            {
                OccupyGrid();
                OnConstructionCompleted?.Invoke(this);
                return true;
            }

            return false;
        }

        // -----------------------------------------------------------------------------------------
        // Production
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Start producing the unit this building makes. Returns false and explains why when the
        /// building trains nothing, is still a blueprint, is already busy, or the payer cannot afford
        /// it — in which case no resources were taken.
        /// </summary>
        public bool EnqueueTraining(ResourceManager payer, out string reason)
        {
            reason = string.Empty;

            if (destroyed)
            {
                reason = "building is destroyed";
                return false;
            }

            if (!IsConstructed)
            {
                reason = "building is still under construction";
                return false;
            }

            if (IsTraining)
            {
                reason = $"already training {trainingUnitId}";
                return false;
            }

            if (definition == null || !definition.CanProduceUnit)
            {
                reason = $"{name} trains nothing";
                return false;
            }

            UnitDefinition unit = ResolveUnit(definition.producesUnitId);
            if (unit == null)
            {
                reason = $"no UnitDefinition for id '{definition.producesUnitId}'";
                return false;
            }

            // Charge only after every precondition passed, and through the one economy entry point.
            if (payer != null && !payer.TrySpend(unit.cost, out reason)) return false;

            _trainingPayer = payer;
            trainingUnitId = unit.id;
            trainingSecondsTotal = Mathf.Max(0.01f, unit.trainSeconds);
            trainingSecondsRemaining = trainingSecondsTotal;
            return true;
        }

        /// <summary>
        /// Train any unit definition this building can field — not only the one wired through
        /// <see cref="BuildingDefinition.producesUnitId"/>. This is the entry point the HUD's unit
        /// list uses, so a barracks keeps a single production slot and a single charge path while
        /// still offering more than one trainable unit.
        ///
        /// Returns false and explains why when the unit is missing, the building is destroyed, still
        /// a blueprint or already busy — in which case no resources were taken. When
        /// <paramref name="unit"/> is the building's own produced unit this simply delegates to
        /// <see cref="EnqueueTraining"/>, so there is exactly one code path per case.
        /// </summary>
        public bool TryTrain(UnitDefinition unit, ResourceManager payer, out string reason)
        {
            reason = string.Empty;

            if (unit == null)
            {
                reason = "no unit definition given";
                return false;
            }

            if (destroyed)
            {
                reason = "building is destroyed";
                return false;
            }

            if (!IsConstructed)
            {
                reason = "building is still under construction";
                return false;
            }

            if (IsTraining)
            {
                reason = $"already training {trainingUnitId}";
                return false;
            }

            if (unit.id == ProducesUnitId)
            {
                return EnqueueTraining(payer, out reason);
            }

            // Charge only after every precondition passed, and through the one economy entry point.
            // Nothing is mutated before this point, so a refused spend leaves the building idle.
            if (payer != null && !payer.TrySpend(unit.cost, out reason)) return false;

            _trainingPayer = payer;
            trainingUnitId = unit.id;
            trainingSecondsTotal = Mathf.Max(0.01f, unit.trainSeconds);
            trainingSecondsRemaining = trainingSecondsTotal;
            return true;
        }

        /// <summary>Cancel production and hand the cost back. Refunds, never silently keeps the money.</summary>
        public void CancelTraining()
        {
            if (!IsTraining) return;

            UnitDefinition unit = ResolveUnit(trainingUnitId);
            if (_trainingPayer != null && unit != null) _trainingPayer.Refund(unit.cost);

            ClearTraining();
        }

        private void ClearTraining()
        {
            trainingUnitId = string.Empty;
            trainingSecondsRemaining = 0f;
            trainingSecondsTotal = 0f;
            _trainingPayer = null;
        }

        private void Update()
        {
            if (destroyed || !IsTraining || !IsConstructed) return;

            trainingSecondsRemaining -= Time.deltaTime;
            if (trainingSecondsRemaining > 0f) return;

            CompleteTraining();
        }

        private void CompleteTraining()
        {
            UnitDefinition unit = ResolveUnit(trainingUnitId);
            string producedId = trainingUnitId;
            ResourceManager payer = _trainingPayer;

            ClearTraining();

            if (unit == null)
            {
                Debug.LogError($"{name}: cannot complete training of unknown unit id '{producedId}'.");
                return;
            }

            IBattleSpawner spawner = ResolveSpawner();
            if (spawner == null)
            {
                // Nothing to hand the unit to: give the player their resources back rather than
                // quietly eating the cost.
                Debug.LogError($"{name}: no spawner available; refunding {unit.displayName}.");
                payer?.Refund(unit.cost);
                return;
            }

            UnitController spawned = spawner.SpawnUnit(unit.id, team, InteractionPoint);
            if (spawned == null)
            {
                Debug.LogError($"{name}: failed to spawn {unit.displayName}; refunding.");
                payer?.Refund(unit.cost);
                return;
            }

            OnUnitTrained?.Invoke(spawned);
        }

        private UnitDefinition ResolveUnit(string unitId)
        {
            BattleContext context = BattleContext.Current;
            if (context != null && context.Database != null) return context.Database.FindUnit(unitId);
            return ServiceLocator.TryGet(out GameDatabase database) ? database.FindUnit(unitId) : null;
        }

        private static IBattleSpawner ResolveSpawner()
        {
            return ServiceLocator.TryGet(out IBattleSpawner spawner) ? spawner : null;
        }

        // -----------------------------------------------------------------------------------------
        // Damage
        // -----------------------------------------------------------------------------------------

        public void ReceiveDamage(int amount, Team sourceTeam)
        {
            if (destroyed || amount <= 0) return;
            health = Mathf.Max(0, health - amount);
            ApplyTint();
            OnHealthChanged?.Invoke(this, health, MaxHealth);
        }

        private void HandleDied(IDamageable victim)
        {
            if (!ReferenceEquals(victim, this)) return;
            HandleDestroyed();
        }

        private void HandleDestroyed()
        {
            if (destroyed) return;
            destroyed = true;
            health = 0;

            // A destroyed building stops producing; the in-flight cost is lost, as it is in any RTS.
            ClearTraining();
            ReleaseGrid();

            if (_combat != null) _combat.OnDied -= HandleDied;

            BattleContext context = BattleContext.Current;
            if (context != null) context.BuildingsOf(team).Remove(this);

            OnDestroyed?.Invoke(this);

            Collider collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;

            // Collapse immediately: a destroyed town hall must stop being a target the instant it
            // dies. DestroyImmediate is used outside play mode, where Destroy is illegal.
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        // -----------------------------------------------------------------------------------------
        // Navigation-grid footprint
        // -----------------------------------------------------------------------------------------

        private void OccupyGrid()
        {
            if (_occupiesGrid) return;

            PathfindingManager pathfinding = ResolvePathfinding();
            if (pathfinding == null || definition == null) return;

            Vector2Int footprint = definition.footprint;
            Vector2Int origin = pathfinding.CellOf(transform.position);
            _gridOriginX = origin.x - (footprint.x / 2);
            _gridOriginY = origin.y - (footprint.y / 2);

            pathfinding.SetBlockedRect(_gridOriginX, _gridOriginY, footprint.x, footprint.y, true);
            _occupiesGrid = true;
        }

        private void ReleaseGrid()
        {
            if (!_occupiesGrid) return;

            PathfindingManager pathfinding = ResolvePathfinding();
            if (pathfinding == null || definition == null) return;

            Vector2Int footprint = definition.footprint;
            pathfinding.SetBlockedRect(_gridOriginX, _gridOriginY, footprint.x, footprint.y, false);
            _occupiesGrid = false;
        }

        private static PathfindingManager ResolvePathfinding()
        {
            BattleContext context = BattleContext.Current;
            if (context != null && context.Pathfinding != null) return context.Pathfinding;
            return ServiceLocator.TryGet(out PathfindingManager found) ? found : null;
        }

        // -----------------------------------------------------------------------------------------
        // Presentation
        // -----------------------------------------------------------------------------------------

        private void ApplyTint()
        {
            if (_instanceMaterial == null) return;

            Color color;
            if (!IsConstructed) color = blueprintColor;
            else
            {
                float fraction = MaxHealth <= 0 ? 1f : health / (float)MaxHealth;
                Color healthy = completedColor;
                Color hurt = Color.Lerp(new Color(0.45f, 0.18f, 0.14f), completedColor, Mathf.Clamp01(fraction));
                color = fraction < 1f ? hurt : healthy;
            }

            if (_instanceMaterial.HasProperty("_BaseColor")) _instanceMaterial.SetColor("_BaseColor", color);
            else if (_instanceMaterial.HasProperty("_Color")) _instanceMaterial.SetColor("_Color", color);
        }

        private void OnDestroy()
        {
            if (_combat != null)
            {
                _combat.OnDied -= HandleDied;
                _combat.Untrack(this);
            }

            ReleaseGrid();
            if (_instanceMaterial != null)
            {
                // A building torn down in edit mode (tests, bootstrap) may not use Destroy.
                if (Application.isPlaying) Destroy(_instanceMaterial);
                else DestroyImmediate(_instanceMaterial);
            }
        }
    }
}
