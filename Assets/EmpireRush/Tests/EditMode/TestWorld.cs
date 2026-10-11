using System.Collections.Generic;
using EmpireRush;
using NUnit.Framework;
using UnityEngine;

namespace EmpireRush.Tests.EditMode
{
    /// <summary>
    /// Builds a real battle world for EditMode tests: a live navigation grid, a combat system, two
    /// resource managers and a <see cref="BattleContext"/>. Everything is the production type — no
    /// mocks or stubs — so a test that passes here exercises the code that ships.
    ///
    /// Created objects are tracked and destroyed in <see cref="TearDown"/> because EditMode tests
    /// share one editor scene.
    /// </summary>
    internal static class TestWorld
    {
        public const string CombatUnitId = "test_swordsman";
        public const string WorkerUnitId = "test_worker";
        public const string TownHallId = "test_town_hall";
        public const string BarracksId = "test_barracks";
        public const string TowerId = "test_tower";

        private static readonly List<GameObject> Created = new List<GameObject>(64);

        public static GameDatabase Database { get; private set; }
        public static EconomyBalance Balance { get; private set; }
        public static PathfindingManager Pathfinding { get; private set; }
        public static CombatSystem Combat { get; private set; }
        public static ResourceManager Player { get; private set; }
        public static ResourceManager Enemy { get; private set; }
        public static BattleContext Context { get; private set; }

        /// <summary>Create the world. Call from [SetUp] or at the top of a test.</summary>
        public static void Build(int gridSize = 32, Vector3? origin = null, ResourceBundle? playerStart = null)
        {
            TearDown();

            Balance = ScriptableObject.CreateInstance<EconomyBalance>();
            Balance.startingResources = new ResourceBundle(0, 0, 0, 0);
            Balance.gatherIntervalSeconds = 0.1f;
            Balance.gatherAmountPerTick = 5;
            Balance.workerCarryCapacity = 10;
            Balance.depositSeconds = 0.05f;
            Balance.buildProgressPerWorkerSecond = 1f;

            Database = BuildDatabase();

            Vector3 worldOrigin = origin ?? Vector3.zero;
            Pathfinding = Track(new GameObject("TestPathfinding").AddComponent<PathfindingManager>());
            Pathfinding.maxPathRequestsPerFrame = 4;
            Pathfinding.maxExpandedNodesPerRequest = 4000;
            Pathfinding.Initialize(gridSize, gridSize, 1f, worldOrigin);

            Combat = new CombatSystem();

            // Both sides start empty so every test controls its own stockpile explicitly. An
            // economy test that inherits a baseline is testing the baseline, not the arithmetic.
            Player = new ResourceManager(Team.Player, playerStart ?? default);
            Enemy = new ResourceManager(Team.Enemy, playerStart ?? default);

            Context = BattleContext.Begin(null, Database, Balance, Player, Enemy, Pathfinding, Combat);

            ServiceLocator.Register(Database);
            ServiceLocator.Register(Balance);
            ServiceLocator.Register(Pathfinding);
        }

        public static void TearDown()
        {
            for (int i = 0; i < Created.Count; i++)
            {
                if (Created[i] != null) Object.DestroyImmediate(Created[i]);
            }

            Created.Clear();
            ResourceNode.Active.Clear();

            BattleContext.End();
            ServiceLocator.Clear();

            Database = null;
            Balance = null;
            Pathfinding = null;
            Combat = null;
            Player = null;
            Enemy = null;
            Context = null;
        }

        /// <summary>A small, explicit content set: two units, three buildings, numbers chosen to be obvious.</summary>
        private static GameDatabase BuildDatabase()
        {
            var database = ScriptableObject.CreateInstance<GameDatabase>();

            var worker = ScriptableObject.CreateInstance<UnitDefinition>();
            worker.id = WorkerUnitId;
            worker.displayName = "Test Worker";
            worker.maxHealth = 40;
            worker.damage = 0;
            worker.attackInterval = 1f;
            worker.attackRange = 1.2f;
            worker.moveSpeed = 6f;
            worker.counters = new string[0];
            worker.populationCost = 1;
            worker.cost = new ResourceCost(10, 0, 0, 0);
            worker.trainSeconds = 0.15f;

            var swordsman = ScriptableObject.CreateInstance<UnitDefinition>();
            swordsman.id = CombatUnitId;
            swordsman.displayName = "Test Swordsman";
            swordsman.maxHealth = 100;
            swordsman.damage = 10;
            swordsman.attackInterval = 0.15f;
            swordsman.attackRange = 2.2f;
            swordsman.moveSpeed = 6f;
            swordsman.counters = new[] { "test_archer" };
            swordsman.populationCost = 2;
            swordsman.cost = new ResourceCost(0, 20, 5, 0);
            swordsman.trainSeconds = 0.15f;

            var townHall = ScriptableObject.CreateInstance<BuildingDefinition>();
            townHall.id = TownHallId;
            townHall.displayName = "Test Town Hall";
            townHall.maxHealth = 200;
            townHall.cost = ResourceCost.Zero;
            townHall.buildSeconds = 0.2f;
            townHall.footprint = new Vector2Int(2, 2);
            townHall.producesUnitId = WorkerUnitId;
            townHall.popProvided = 10;
            townHall.isTownHall = true;

            var barracks = ScriptableObject.CreateInstance<BuildingDefinition>();
            barracks.id = BarracksId;
            barracks.displayName = "Test Barracks";
            barracks.maxHealth = 150;
            barracks.cost = new ResourceCost(30, 0, 10, 0);
            barracks.buildSeconds = 0.2f;
            barracks.footprint = new Vector2Int(2, 2);
            barracks.producesUnitId = CombatUnitId;
            barracks.popProvided = 5;
            barracks.isTownHall = false;

            var tower = ScriptableObject.CreateInstance<BuildingDefinition>();
            tower.id = TowerId;
            tower.displayName = "Test Tower";
            tower.maxHealth = 120;
            tower.cost = new ResourceCost(20, 0, 0, 0);
            tower.buildSeconds = 0.2f;
            tower.footprint = new Vector2Int(2, 2);
            tower.producesUnitId = string.Empty;
            tower.popProvided = 0;
            tower.isTownHall = false;

            database.economy = Balance;
            database.units = new List<UnitDefinition> { worker, swordsman };
            database.buildings = new List<BuildingDefinition> { townHall, barracks, tower };
            database.missions = new List<MissionDefinition>();
            database.InvalidateCaches();
            return database;
        }

        // -----------------------------------------------------------------------------------------
        // Factories
        // -----------------------------------------------------------------------------------------

        public static UnitController CreateUnit(string unitId, Team team, Vector3 position)
        {
            UnitDefinition definition = Database.FindUnit(unitId);
            Assert.IsNotNull(definition, $"test database has no unit '{unitId}'");

            var go = Track(new GameObject($"Unit_{unitId}"));
            go.transform.position = position;

            var unit = go.AddComponent<UnitController>();
            unit.team = team;
            unit.Initialize(definition, team, Pathfinding, Combat);
            Context.UnitsOf(team).Add(unit);
            return unit;
        }

        public static BuildingController CreateBuilding(string buildingId, Team team, Vector3 position, bool constructed = true)
        {
            BuildingDefinition definition = Database.FindBuilding(buildingId);
            Assert.IsNotNull(definition, $"test database has no building '{buildingId}'");

            var go = Track(new GameObject($"Building_{buildingId}"));
            go.transform.position = position;

            var building = go.AddComponent<BuildingController>();
            building.team = team;
            building.Initialize(definition, team, Combat);
            if (!constructed) building.BeginConstruction();
            Context.BuildingsOf(team).Add(building);
            return building;
        }

        public static ResourceNode CreateNode(ResourceType type, Vector3 position, int capacity)
        {
            var go = Track(new GameObject($"Node_{type}"));
            go.transform.position = position;

            var node = go.AddComponent<ResourceNode>();
            node.Configure(type, capacity);
            return node;
        }

        public static Camera CreateCamera(Vector3 position, Vector3 eulerAngles, float orthographicSize = 12f, int pixelWidth = 800, int pixelHeight = 600)
        {
            var go = Track(new GameObject("TestCamera"));
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(eulerAngles);

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;

            // Batchmode has no display, so the pixel rect must be pinned for WorldToScreenPoint to
            // produce stable, resolution-independent coordinates.
            camera.pixelRect = new Rect(0f, 0f, pixelWidth, pixelHeight);
            return camera;
        }

        public static T Track<T>(T component) where T : Component
        {
            if (component != null && component.gameObject != null) Track(component.gameObject);
            return component;
        }

        public static GameObject Track(GameObject go)
        {
            if (go != null && !Created.Contains(go)) Created.Add(go);
            return go;
        }
    }
}
