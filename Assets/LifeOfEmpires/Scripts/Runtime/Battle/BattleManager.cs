using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LifeOfEmpires
{
    /// <summary>
    /// Anything that can put a unit or building into the world. Implemented by
    /// <see cref="BattleManager"/> and consumed by <see cref="EnemyAIController"/>, so the AI asks for
    /// reinforcements without knowing how spawning works.
    /// </summary>
    public interface IBattleSpawner
    {
        UnitController SpawnUnit(string unitId, Team team, Vector3 position);

        BuildingController SpawnBuilding(string buildingId, Team team, Vector3 position);
    }

    /// <summary>
    /// Owns the life of one battle: it builds the navigation grid, adopts the units and buildings
    /// already in the scene, spawns whatever the mission adds, hands the controls to the enemy AI, and
    /// ends the battle when the objectives say so.
    ///
    /// Every spawn is turned into a plain primitive with the definition's colour applied, so a
    /// battlefield is playable without any imported art.
    /// </summary>
    public class BattleManager : MonoBehaviour, IBattleSpawner
    {
        [Header("Battlefield")]
        [Min(4)] public int gridWidth = 64;
        [Min(4)] public int gridHeight = 64;
        [Min(0.1f)] public float cellSize = 1f;

        [Header("Mission")]
        [Tooltip("When true, the enemy units named by the mission are spawned after its start delay.")]
        public bool spawnMissionEnemies = true;

        [Header("Wiring (auto-resolved when empty)")]
        public PathfindingManager pathfinding;
        public ProjectilePool projectilePool;
        public BattleObjectiveManager objectives;
        public EnemyAIController enemyAI;
        public IsometricCameraRig cameraRig;

        private CombatSystem _combat;
        private GameDatabase _database;
        private MissionDefinition _mission;

        private float _elapsed;
        private float _missionSpawnCountdown;
        private int _missionSpawnIndex;
        private bool _missionSpawnsArmed;
        private bool _started;
        private bool _sceneContentsAdopted;

        public BattleContext Context { get; private set; }

        public bool IsPaused { get; private set; }

        /// <summary>Seconds of battle elapsed. Frozen while paused, since it is driven by Time.deltaTime.</summary>
        public float ElapsedSeconds => _elapsed;

        private void Awake()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (pathfinding == null) pathfinding = FindFirstObjectByType<PathfindingManager>();
            if (projectilePool == null) projectilePool = FindFirstObjectByType<ProjectilePool>();
            if (objectives == null) objectives = FindFirstObjectByType<BattleObjectiveManager>();
            if (enemyAI == null) enemyAI = FindFirstObjectByType<EnemyAIController>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<IsometricCameraRig>();
        }

        /// <summary>
        /// Compose and begin the battle. Idempotent: a second call is ignored, so it is safe for
        /// <see cref="GameBootstrap"/> to call this and for the scene to also hold a reference.
        /// </summary>
        public void StartBattle(
            MissionDefinition mission,
            GameDatabase database,
            EconomyBalance balance,
            CombatSystem combat,
            ResourceManager playerResources,
            ResourceManager enemyResources)
        {
            if (_started) return;

            _mission = mission;
            _database = database;
            _combat = combat;

            if (_combat == null)
            {
                Debug.LogError("BattleManager: no CombatSystem supplied; the battle cannot start.");
                return;
            }

            if (playerResources == null || enemyResources == null)
            {
                Debug.LogError("BattleManager: both teams need a ResourceManager; the battle cannot start.");
                return;
            }

            ResolveReferences();

            if (pathfinding == null)
            {
                var go = new GameObject("PathfindingManager");
                go.transform.SetParent(transform, false);
                pathfinding = go.AddComponent<PathfindingManager>();
            }

            pathfinding.Initialize(gridWidth, gridHeight, cellSize, transform.position);

            if (projectilePool != null)
            {
                projectilePool.Initialize(_combat);
                _combat.AttachProjectilePool(projectilePool);
            }

            Context = BattleContext.Begin(mission, database, balance, playerResources, enemyResources, pathfinding, _combat);

            AdoptSceneContents();

            if (objectives == null)
            {
                objectives = gameObject.AddComponent<BattleObjectiveManager>();
            }

            objectives.Initialize(mission, _combat);
            objectives.OnBattleEnded += HandleBattleEnded;

            // Buildings look their spawner up through the locator, so production never needs a
            // direct reference to this component.
            ServiceLocator.Register<IBattleSpawner>(this);

            if (cameraRig != null)
            {
                cameraRig.SetBattlefieldBounds(new Bounds(
                    transform.position + new Vector3(gridWidth * cellSize * 0.5f, 0f, gridHeight * cellSize * 0.5f),
                    new Vector3(gridWidth * cellSize, 0f, gridHeight * cellSize)));
            }

            ArmMissionSpawns(mission);
            InitialiseEnemyAI(mission);

            _started = true;
        }

        private void ArmMissionSpawns(MissionDefinition mission)
        {
            _missionSpawnsArmed = spawnMissionEnemies
                && mission != null
                && mission.enemyUnitIds != null
                && mission.enemyUnitIds.Length > 0;

            _missionSpawnCountdown = mission != null ? mission.enemyStartDelaySeconds : 0f;
            _missionSpawnIndex = 0;
        }

        private void InitialiseEnemyAI(MissionDefinition mission)
        {
            if (enemyAI == null) return;

            Vector3 home = Context != null ? Context.TownHallOf(Team.Enemy)?.transform.position ?? transform.position : transform.position;
            enemyAI.Initialize(this, _combat, mission != null ? mission.difficulty : null, pathfinding, home);
            if (mission != null) enemyAI.SetRandomSeed(mission.terrainSeed);
        }

        /// <summary>
        /// Register the units, buildings and resource nodes the scene already contains. The editor
        /// bootstrap authors a playable default battlefield into the scene, so the battle adopts what
        /// is there rather than spawning a duplicate town hall on top of it.
        /// </summary>
        public void AdoptSceneContents()
        {
            if (_sceneContentsAdopted) return;
            _sceneContentsAdopted = true;

            UnitController[] units = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
            for (int i = 0; i < units.Length; i++) Adopt(units[i]);

            BuildingController[] buildings = FindObjectsByType<BuildingController>(FindObjectsSortMode.None);
            for (int i = 0; i < buildings.Length; i++) Adopt(buildings[i]);
        }

        private void Adopt(UnitController unit)
        {
            if (unit == null) return;

            if (!unit.IsInitialised) unit.Initialize(unit.definition, unit.team, pathfinding, _combat);

            List<UnitController> roster = Context.UnitsOf(unit.team);
            if (!roster.Contains(unit)) roster.Add(unit);

            BindWorker(unit);
        }

        private void BindWorker(UnitController unit)
        {
            if (unit.IsCombatUnit) return;

            var worker = unit.GetComponent<WorkerAgent>();
            if (worker == null) worker = unit.gameObject.AddComponent<WorkerAgent>();

            worker.Initialize(worker.preferredResource, Context.TownHallOf(unit.team));
            unit.ResourceSink = unit.team == Team.Player ? Context.PlayerResources : Context.EnemyResources;
            unit.DropOff = Context.TownHallOf(unit.team);

            if (worker.IsIdle) worker.AssignNode(null);
        }

        private void Adopt(BuildingController building)
        {
            if (building == null) return;

            if (!building.IsInitialised) building.Initialize(building.definition, building.team, _combat);

            List<BuildingController> roster = Context.BuildingsOf(building.team);
            if (!roster.Contains(building)) roster.Add(building);
        }

        // -----------------------------------------------------------------------------------------
        // Spawning
        // -----------------------------------------------------------------------------------------

        public UnitController SpawnUnit(string unitId, Team team, Vector3 position)
        {
            if (_database == null)
            {
                Debug.LogError("BattleManager: no GameDatabase; cannot resolve a unit id.");
                return null;
            }

            UnitDefinition definition = _database.FindUnit(unitId);
            if (definition == null)
            {
                Debug.LogError($"BattleManager: unknown unit id '{unitId}'.");
                return null;
            }

            if (!TrySnapToWalkable(position, out Vector3 spawnPoint))
            {
                Debug.LogWarning($"BattleManager: nowhere walkable near {position} to spawn '{unitId}'.");
                return null;
            }

            var go = new GameObject($"Unit_{definition.id}");

            var body = GameObject.CreatePrimitive(definition.IsCombatUnit ? PrimitiveType.Capsule : PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = definition.IsCombatUnit
                ? new Vector3(0.7f, 0.75f, 0.7f)
                : new Vector3(0.65f, 0.5f, 0.65f);
            body.transform.localPosition = new Vector3(0f, definition.IsCombatUnit ? 0.75f : 0.5f, 0f);

            ApplyColor(body.GetComponent<Renderer>(), definition.color);

            go.transform.position = spawnPoint;

            var unit = go.AddComponent<UnitController>();
            unit.team = team;
            unit.Initialize(definition, team, pathfinding, _combat);

            Context.UnitsOf(team).Add(unit);
            BindWorker(unit);
            return unit;
        }

        public BuildingController SpawnBuilding(string buildingId, Team team, Vector3 position)
        {
            if (_database == null)
            {
                Debug.LogError("BattleManager: no GameDatabase; cannot resolve a building id.");
                return null;
            }

            BuildingDefinition definition = _database.FindBuilding(buildingId);
            if (definition == null)
            {
                Debug.LogError($"BattleManager: unknown building id '{buildingId}'.");
                return null;
            }

            Vector2Int footprint = definition.footprint;
            if (!FootprintIsClear(position, footprint))
            {
                Debug.LogWarning($"BattleManager: footprint for '{buildingId}' is obstructed at {position}; skipping.");
                return null;
            }

            var go = new GameObject($"Building_{definition.id}");

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(footprint.x * cellSize * 0.95f, 2.4f, footprint.y * cellSize * 0.95f);
            body.transform.localPosition = new Vector3(0f, 1.2f, 0f);

            go.transform.position = new Vector3(position.x, transform.position.y, position.z);

            var building = go.AddComponent<BuildingController>();
            building.team = team;
            building.Initialize(definition, team, _combat);

            Context.BuildingsOf(team).Add(building);
            return building;
        }

        private bool FootprintIsClear(Vector3 position, Vector2Int footprint)
        {
            if (pathfinding == null) return true;

            Vector2Int origin = pathfinding.CellOf(position);
            origin = new Vector2Int(origin.x - (footprint.x / 2), origin.y - (footprint.y / 2));

            for (int y = origin.y; y < origin.y + footprint.y; y++)
            {
                for (int x = origin.x; x < origin.x + footprint.x; x++)
                {
                    if (!pathfinding.InBounds(x, y) || pathfinding.IsBlocked(x, y)) return false;
                }
            }

            return true;
        }

        private bool TrySnapToWalkable(Vector3 position, out Vector3 snapped)
        {
            snapped = position;
            if (pathfinding == null) return true;

            Vector2Int cell = pathfinding.CellOf(position);
            if (pathfinding.IsBlocked(cell.x, cell.y))
            {
                // Spiral outward for the nearest free cell rather than spawning inside a building.
                for (int radius = 1; radius <= 6; radius++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int x = cell.x + dx;
                            int y = cell.y + dy;
                            if (!pathfinding.InBounds(x, y) || pathfinding.IsBlocked(x, y)) continue;

                            snapped = pathfinding.CentreOf(x, y);
                            return true;
                        }
                    }
                }

                return false;
            }

            snapped = pathfinding.CentreOf(cell);
            return true;
        }

        private static void ApplyColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;

            var material = new Material(renderer.sharedMaterial);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            renderer.sharedMaterial = material;
        }

        // -----------------------------------------------------------------------------------------
        // Battle loop
        // -----------------------------------------------------------------------------------------

        private void Update()
        {
            if (!_started) return;
            if (IsPaused || (objectives != null && objectives.HasEnded)) return;

            _elapsed += Time.deltaTime;
            if (Context != null) Context.ElapsedSeconds = _elapsed;

            UpdateMissionSpawns();

            Context?.PruneRosters();
            objectives?.Evaluate();
        }

        private void UpdateMissionSpawns()
        {
            if (!_missionSpawnsArmed || _mission == null) return;

            _missionSpawnCountdown -= Time.deltaTime;
            if (_missionSpawnCountdown > 0f) return;

            if (_missionSpawnIndex >= _mission.enemyUnitIds.Length)
            {
                _missionSpawnsArmed = false;
                return;
            }

            string unitId = _mission.enemyUnitIds[_missionSpawnIndex];
            _missionSpawnIndex++;

            BuildingController enemyHall = Context != null ? Context.TownHallOf(Team.Enemy) : null;
            Vector3 rally = enemyHall != null ? enemyHall.transform.position : transform.position;

            SpawnUnit(unitId, Team.Enemy, rally + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f)));
            _missionSpawnCountdown = 4f;
        }

        private void HandleBattleEnded(BattleOutcome outcome)
        {
            SetPaused(true);

            if (Context != null) Context.Outcome = outcome;

            if (objectives != null && _mission != null)
            {
                SaveData save = SaveSystem.ReadFromDisk();
                save.RecordMissionResult(_mission.missionId, outcome == BattleOutcome.Victory, objectives.ScoreStars(Context), _elapsed);
                if (outcome == BattleOutcome.Victory) save.crowns += _mission.crownReward;
                SaveSystem.WriteToDisk(save);
            }
        }

        /// <summary>Freeze or resume the battle. Pausing stops simulation time, not rendering or input.</summary>
        public void SetPaused(bool paused)
        {
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
        }

        public void TogglePause() => SetPaused(!IsPaused);

        /// <summary>Return to the bootstrap scene. Used by the results panel.</summary>
        public void ReturnToBootstrap()
        {
            SetPaused(false);
            BattleContext.End();
            SceneManager.LoadScene("Bootstrap");
        }

        private void OnDestroy()
        {
            if (objectives != null) objectives.OnBattleEnded -= HandleBattleEnded;

            // Leaving time frozen would poison the next battle in the same session.
            if (IsPaused) SetPaused(false);
        }
    }
}
