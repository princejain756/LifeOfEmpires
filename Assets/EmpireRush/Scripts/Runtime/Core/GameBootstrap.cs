using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmpireRush
{
    /// <summary>
    /// The entry point. It composes the battle in dependency order and then hands control to
    /// <see cref="BattleManager"/>; it owns no gameplay state itself, so there is exactly one place
    /// that knows the initialisation order and exactly one place to look when a service is missing.
    ///
    /// In the Bootstrap scene it simply loads the battle scene. In the Battle scene it composes the
    /// systems. That split keeps the entry point free of battlefield wiring and the battlefield free
    /// of scene-flow concerns.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Asset index of every unit, building and mission. Required.")]
        public GameDatabase database;

        [Tooltip("Mission to play. Optional: with none set, the battle adopts the scene as authored.")]
        public MissionDefinition mission;

        [Tooltip("Economy tuning. Falls back to the database's balance asset.")]
        public EconomyBalance economyBalance;

        [Header("Scene flow")]
        [Tooltip("When true this object is an entry point: it loads the battle scene and composes nothing.")]
        public bool loadBattleSceneOnStart = true;

        public string battleSceneName = "Battle";

        [Header("Scene systems (found automatically when empty)")]
        public BattleManager battleManager;
        public PathfindingManager pathfinding;
        public ProjectilePool projectilePool;
        public UnitSelectionManager selection;
        public IsometricCameraRig cameraRig;
        public BuildingPlacementSystem placement;
        public BattleInputController input;

        /// <summary>Set once the battle has been composed. Null in the bootstrap scene.</summary>
        public BattleContext Context { get; private set; }

        private bool _composed;

        private void Awake()
        {
            // An entry-point instance must not compose a battlefield that is not loaded yet.
            if (loadBattleSceneOnStart) return;
            ComposeBattle();
        }

        private void Start()
        {
            if (!loadBattleSceneOnStart) return;
            SceneManager.LoadScene(battleSceneName);
        }

        /// <summary>
        /// Build every service in dependency order: data, then navigation, then combat, then the
        /// economy, then the battle context, then the systems that consume them. Safe to call twice.
        /// </summary>
        public void ComposeBattle()
        {
            if (_composed) return;
            _composed = true;

            ResolveReferences();

            if (database == null)
            {
                Debug.LogError("GameBootstrap: no GameDatabase assigned; the battle cannot be composed.");
                return;
            }

            if (economyBalance == null) economyBalance = database.economy;
            if (economyBalance == null)
            {
                Debug.LogError("GameBootstrap: no EconomyBalance assigned or present on the database; the battle cannot be composed.");
                return;
            }

            ServiceLocator.Register(database);
            ServiceLocator.Register(economyBalance);

            // 1. Navigation must exist before anything that walks.
            if (pathfinding == null)
            {
                var go = new GameObject("PathfindingManager");
                go.transform.SetParent(transform, false);
                pathfinding = go.AddComponent<PathfindingManager>();
            }

            ServiceLocator.Register(pathfinding);

            // 2. Combat, and the pool its ranged attacks draw from.
            var combat = new CombatSystem();
            ServiceLocator.Register(combat);

            if (projectilePool == null) projectilePool = FindFirstObjectByType<ProjectilePool>();
            if (projectilePool != null)
            {
                projectilePool.Initialize(combat);
                combat.AttachProjectilePool(projectilePool);
            }

            // 3. Economy, one manager per side, seeded from the mission or the balance asset.
            ResourceBundle playerOpening = mission != null ? mission.StartingBundle : economyBalance.startingResources;
            var playerResources = new ResourceManager(Team.Player, playerOpening);
            var enemyResources = new ResourceManager(Team.Enemy, economyBalance.startingResources);

            // 4. The battle context binds the above together.
            Context = BattleContext.Begin(mission, database, economyBalance, playerResources, enemyResources, pathfinding, combat);

            // 5. Systems that consume the context.
            if (battleManager == null) battleManager = FindFirstObjectByType<BattleManager>();
            if (battleManager != null) battleManager.StartBattle(mission, database, economyBalance, combat, playerResources, enemyResources);

            ServiceLocator.Register(playerResources);
            if (battleManager != null) ServiceLocator.Register(battleManager);
            if (selection != null) ServiceLocator.Register(selection);
            if (cameraRig != null) ServiceLocator.Register(cameraRig);
            if (placement != null) ServiceLocator.Register(placement);
            if (input != null) ServiceLocator.Register(input);

            Debug.Log($"GameBootstrap: battle composed ({database.units.Count} unit definitions, {database.buildings.Count} building definitions, mission '{mission?.missionId ?? "scene-authored"}').");
        }

        private void ResolveReferences()
        {
            if (battleManager == null) battleManager = FindFirstObjectByType<BattleManager>();
            if (pathfinding == null) pathfinding = FindFirstObjectByType<PathfindingManager>();
            if (projectilePool == null) projectilePool = FindFirstObjectByType<ProjectilePool>();
            if (selection == null) selection = FindFirstObjectByType<UnitSelectionManager>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<IsometricCameraRig>();
            if (placement == null) placement = FindFirstObjectByType<BuildingPlacementSystem>();
            if (input == null) input = FindFirstObjectByType<BattleInputController>();

            // Selection must be given a camera: it will not fall back to an arbitrary Camera.main,
            // because a tap answered by the wrong camera selects units the player never touched.
            if (selection != null && selection.SelectionCamera == null && cameraRig != null)
            {
                selection.SelectionCamera = cameraRig.Camera;
            }
        }

        private void OnDestroy()
        {
            ServiceLocator.Clear();
            BattleContext.End();
        }
    }
}
