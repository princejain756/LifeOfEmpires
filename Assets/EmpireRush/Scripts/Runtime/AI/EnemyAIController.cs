using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireRush
{
    /// <summary>The opponent's high-level intent. Every transition is visible to the player in the log.</summary>
    public enum EnemyAIState
    {
        /// <summary>Gathering and expanding. The default resting state.</summary>
        DevelopEconomy = 0,

        /// <summary>Raising defensive structures and holding position.</summary>
        BuildDefenses = 1,

        /// <summary>Building an army up to the difficulty's threshold before committing.</summary>
        PrepareArmy = 2,

        /// <summary>Marching the army onto the player's town hall.</summary>
        Attack = 3,

        /// <summary>Reacting to a threat inside its own territory.</summary>
        Defend = 4,

        /// <summary>Town hall badly damaged: pull everything back and rebuild.</summary>
        Recover = 5
    }

    /// <summary>
    /// The opponent. It is a timer-driven state machine — evaluated every
    /// <see cref="evaluationInterval"/> seconds, never per frame — with six explicit states and
    /// legible transitions between them, so the player can read what the enemy is doing and why.
    ///
    /// Difficulty perturbs cadence, worker count, army threshold and production rate. It never
    /// inflates health or damage: a harder opponent acts more decisively, it does not cheat.
    ///
    /// Entirely offline. No networking, no analytics, no service calls of any kind.
    /// </summary>
    public class EnemyAIController : MonoBehaviour
    {
        [Header("Identity")]
        public Team team = Team.Enemy;

        [Tooltip("Unit ids this opponent may train, in preference order.")]
        public string[] trainableUnitIds = { "swordsman" };

        [Tooltip("Unit id used for economy workers.")]
        public string workerUnitId = "worker";

        [Tooltip("Building id raised when on the defensive. Empty disables defensive building.")]
        public string defensiveBuildingId = "";

        [Header("Cadence (defaults; a DifficultyProfile overrides these)")]
        [Min(0.25f)] public float evaluationInterval = 2f;

        [Tooltip("Seconds between production completions at productionRateMultiplier = 1.")]
        [Min(0.5f)] public float productionIntervalSeconds = 9f;

        [Header("Thresholds (defaults; a DifficultyProfile overrides these)")]
        [Min(1)] public int armySize = 4;
        [Min(0)] public int workerCount = 3;
        [Min(1f)] public float threatRadius = 18f;
        [Range(0.05f, 0.9f)] public float recoverHealthFraction = 0.3f;
        [Min(1f)] public float attackCooldownSeconds = 25f;

        [Header("Diagnostics (read-only)")]
        [SerializeField] private EnemyAIState state = EnemyAIState.DevelopEconomy;
        [SerializeField] private string lastDecision = "initialising";

        private IBattleSpawner _spawner;
        private CombatSystem _combat;
        private DifficultyProfile _difficulty;
        private PathfindingManager _pathfinding;

        /// <summary>
        /// Seeded once per battle so a mission's opponent behaves reproducibly. The engine-global
        /// Random would make the same mission play out differently every attempt.
        /// </summary>
        private System.Random _rng = new System.Random(0);

        private float _evaluationTimer;
        private float _productionTimer;
        private float _lastAttackTime = float.NegativeInfinity;
        private Vector3 _homePosition;
        private readonly List<IDamageable> _threatScratch = new List<IDamageable>(32);

        /// <summary>Fired on every state change. Argument: the new state.</summary>
        public event Action<EnemyAIState> OnStateChanged;

        public EnemyAIState State => state;

        /// <summary>Why the AI last took the decision it did. Surfaced in the debug overlay.</summary>
        public string LastDecision => lastDecision;

        public bool IsInitialised => _spawner != null;

        /// <summary>
        /// Wire the opponent. Values are read from the difficulty profile when one is supplied, so
        /// balance stays in data rather than in this component's inspector defaults.
        /// </summary>
        public void Initialize(IBattleSpawner spawner, CombatSystem combat, DifficultyProfile difficulty, PathfindingManager pathfinding, Vector3 homePosition)
        {
            _spawner = spawner;
            _combat = combat;
            _difficulty = difficulty;
            _pathfinding = pathfinding;
            _homePosition = homePosition;

            if (difficulty != null)
            {
                evaluationInterval = difficulty.decisionIntervalSeconds;
                armySize = difficulty.armySize;
                workerCount = difficulty.workerCount;
                threatRadius = difficulty.threatRadius;
                recoverHealthFraction = difficulty.recoverHealthFraction;
                attackCooldownSeconds = difficulty.attackCooldownSeconds;
            }

            _productionTimer = 0f;
            _evaluationTimer = 0f;
        }

        /// <summary>Set once the enemy's town hall exists, so threat detection has an anchor.</summary>
        public void SetHome(Vector3 homePosition) => _homePosition = homePosition;

        /// <summary>Seed the AI's deterministic randomness, normally from the mission's terrain seed.</summary>
        public void SetRandomSeed(int seed) => _rng = new System.Random(seed);

        private void Update()
        {
            if (_spawner == null) return;

            _evaluationTimer += Time.deltaTime;

            // Production accumulates continuously, weighted by the difficulty's rate; the decisions
            // themselves are only taken on the slower evaluation tick.
            _productionTimer += Time.deltaTime * (_difficulty != null ? _difficulty.productionRateMultiplier : 1f);

            if (_evaluationTimer < Mathf.Max(0.25f, evaluationInterval)) return;
            _evaluationTimer -= Mathf.Max(0.25f, evaluationInterval);
            Evaluate();
        }

        /// <summary>
        /// One decision tick. Public and side-effect-light so it can be driven deterministically by a
        /// test rather than waiting on wall-clock time.
        /// </summary>
        public void Evaluate()
        {
            if (_spawner == null)
            {
                lastDecision = "no spawner";
                return;
            }

            BattleContext context = BattleContext.Current;
            if (context == null || context.Outcome != BattleOutcome.InProgress)
            {
                lastDecision = "battle not in progress";
                return;
            }

            BuildingController townHall = context.TownHallOf(team);
            if (townHall == null)
            {
                lastDecision = "no town hall";
                return;
            }

            _homePosition = townHall.transform.position;

            int army = CountArmy(context);
            int workers = CountWorkers(context);
            IDamageable threat = FindThreat(context, townHall.transform.position);
            float townHallFraction = townHall.MaxHealth <= 0 ? 1f : townHall.Health / (float)townHall.MaxHealth;

            // Priority order is deliberate: survival, then defence, then expansion, then offence.
            if (townHallFraction < recoverHealthFraction)
            {
                TransitionTo(EnemyAIState.Recover, $"town hall at {townHallFraction:P0} health; regrouping");
                RecallArmy(context, townHall.transform.position);
                return;
            }

            if (threat != null)
            {
                TransitionTo(EnemyAIState.Defend, $"threat detected at {threat.WorldPosition}");
                RecallArmy(context, threat.WorldPosition);
                return;
            }

            if (army >= armySize && Time.time - _lastAttackTime >= attackCooldownSeconds)
            {
                BuildingController target = context.TownHallOf(Team.Player);
                if (target != null)
                {
                    TransitionTo(EnemyAIState.Attack, $"{army} units committed to the assault");
                    RecallArmy(context, target.transform.position);
                    _lastAttackTime = Time.time;
                    return;
                }
            }

            if (workers < workerCount && TryProduce(workerUnitId, out _))
            {
                TransitionTo(EnemyAIState.DevelopEconomy, $"training worker {workers + 1}/{workerCount}");
                AssignIdleWorker();
                return;
            }

            if (army < armySize)
            {
                if (TryProduce(PickCombatUnitId(), out _))
                {
                    TransitionTo(EnemyAIState.PrepareArmy, $"army {army + 1}/{armySize}");
                    return;
                }

                TransitionTo(EnemyAIState.PrepareArmy, $"army {army}/{armySize}; awaiting resources");
                return;
            }

            if (!string.IsNullOrEmpty(defensiveBuildingId) && TryBuild(defensiveBuildingId))
            {
                TransitionTo(EnemyAIState.BuildDefenses, $"raising {defensiveBuildingId}");
                return;
            }

            TransitionTo(EnemyAIState.DevelopEconomy, "economy ticking over");
        }

        private int CountArmy(BattleContext context)
        {
            List<UnitController> units = context.UnitsOf(team);
            int count = 0;
            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead || !unit.IsCombatUnit) continue;
                count++;
            }

            return count;
        }

        private int CountWorkers(BattleContext context)
        {
            List<UnitController> units = context.UnitsOf(team);
            int count = 0;
            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead || unit.IsCombatUnit) continue;
                count++;
            }

            return count;
        }

        private IDamageable FindThreat(BattleContext context, Vector3 around)
        {
            if (_combat == null) return null;

            _combat.FindEnemiesInRadius(team, around, threatRadius, _threatScratch);
            if (_threatScratch.Count == 0) return null;

            IDamageable nearest = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _threatScratch.Count; i++)
            {
                float sqr = (_threatScratch[i].WorldPosition - around).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                nearest = _threatScratch[i];
            }

            return nearest;
        }

        /// <summary>
        /// Commit the fighting units to a point. Workers are deliberately never recalled, in any
        /// state: the economy has to keep running or the opponent can never rebuild, and a
        /// difficulty that stops gathering is a difficulty that beats itself.
        /// </summary>
        private void RecallArmy(BattleContext context, Vector3 destination)
        {
            List<UnitController> units = context.UnitsOf(team);

            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead || !unit.IsCombatUnit) continue;
                unit.AttackMoveTo(destination);
            }
        }

        private bool TryProduce(string unitId, out UnitController spawned)
        {
            spawned = null;
            if (string.IsNullOrEmpty(unitId) || _spawner == null) return false;

            // Production is paced, not free: the AI waits out the interval like the player does.
            if (_productionTimer < productionIntervalSeconds) return false;

            UnitDefinition definition = ResolveUnit(unitId);
            if (definition == null) return false;

            // The opponent buys its units from the same stockpile the player does. Anything else
            // would be a difficulty that cheats rather than one that plays well.
            BattleContext context = BattleContext.Current;
            ResourceManager purse = context != null ? context.EnemyResources : null;
            if (purse != null && !purse.TrySpend(definition.cost, out string _)) return false;

            Vector3 rally = _homePosition + new Vector3(NextRange(-4f, 4f), 0f, NextRange(-4f, 4f));
            spawned = _spawner.SpawnUnit(unitId, team, rally);

            if (spawned == null)
            {
                purse?.Refund(definition.cost);
                return false;
            }

            _productionTimer = 0f;
            return true;
        }

        private UnitDefinition ResolveUnit(string unitId)
        {
            BattleContext context = BattleContext.Current;
            if (context != null && context.Database != null) return context.Database.FindUnit(unitId);
            return ServiceLocator.TryGet(out GameDatabase database) ? database.FindUnit(unitId) : null;
        }

        private string PickCombatUnitId()
        {
            if (trainableUnitIds == null || trainableUnitIds.Length == 0) return string.Empty;
            return trainableUnitIds[_rng.Next(trainableUnitIds.Length)];
        }

        private bool TryBuild(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId) || _spawner == null) return false;
            if (_productionTimer < productionIntervalSeconds) return false;

            Vector3 spot = FindBuildSpot();
            BuildingController built = _spawner.SpawnBuilding(buildingId, team, spot);
            if (built == null) return false;

            _productionTimer = 0f;
            return true;
        }

        /// <summary>Find a legal footprint near home so the AI never stacks buildings on each other.</summary>
        private Vector3 FindBuildSpot()
        {
            if (_pathfinding == null) return _homePosition;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                float radius = 5f + (float)(_rng.NextDouble() * 5.0);
                var candidate = _homePosition + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                Vector2Int cell = _pathfinding.CellOf(candidate);
                if (_pathfinding.InBounds(cell.x, cell.y) && !_pathfinding.IsBlocked(cell.x, cell.y)) return candidate;
            }

            return _homePosition;
        }

        private void AssignIdleWorker()
        {
            BattleContext context = BattleContext.Current;
            if (context == null) return;

            List<UnitController> units = context.UnitsOf(team);
            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead || unit.IsCombatUnit) continue;

                var agent = unit.GetComponent<WorkerAgent>();
                if (agent == null || !agent.IsIdle) continue;

                agent.AssignNode(null);
                return;
            }
        }

        private float NextRange(float minimum, float maximum)
            => minimum + ((float)_rng.NextDouble() * (maximum - minimum));

        private void TransitionTo(EnemyAIState next, string reason)
        {
            lastDecision = reason;

            if (state == next) return;

            state = next;
            Debug.Log($"EnemyAI: {next} — {reason}");
            OnStateChanged?.Invoke(next);
        }
    }
}
