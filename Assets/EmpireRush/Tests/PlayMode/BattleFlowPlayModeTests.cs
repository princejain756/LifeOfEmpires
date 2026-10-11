using System;
using System.Collections;
using System.Collections.Generic;
using EmpireRush;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EmpireRush.Tests.PlayMode
{
    /// <summary>
    /// End-to-end integration through the production systems: place a barracks with the placement
    /// system, let a dispatched worker build it, train a swordsman from it, and send that swordsman
    /// to destroy an enemy structure.
    ///
    /// Nothing here is mocked. Every step goes through the same code the game runs: the navigation
    /// grid, the economy, the damage funnel, the spawner and the worker's construction loop.
    /// </summary>
    public class BattleFlowPlayModeTests
    {
        private const string SwordsmanId = "pm_swordsman";
        private const string WorkerId = "pm_worker";
        private const string TownHallId = "pm_town_hall";
        private const string BarracksId = "pm_barracks";
        private const string TowerId = "pm_tower";

        private readonly List<GameObject> _created = new List<GameObject>(32);

        private GameDatabase _database;
        private EconomyBalance _balance;
        private CombatSystem _combat;
        private ResourceManager _player;
        private ResourceManager _enemy;
        private BattleManager _battle;
        private BuildingPlacementSystem _placement;

        [SetUp]
        public void SetUp()
        {
            _balance = ScriptableObject.CreateInstance<EconomyBalance>();
            _balance.startingResources = new ResourceBundle(0, 0, 0, 0);
            _balance.buildProgressPerWorkerSecond = 2f;
            _balance.gatherIntervalSeconds = 0.1f;
            _balance.depositSeconds = 0.05f;

            _database = BuildDatabase();
            _combat = new CombatSystem();

            _player = new ResourceManager(Team.Player, new ResourceBundle(1000, 1000, 1000, 0));
            _enemy = new ResourceManager(Team.Enemy, new ResourceBundle(1000, 1000, 1000, 0));

            ServiceLocator.Register(_database);
            ServiceLocator.Register(_balance);

            var pathfindingGo = Track(new GameObject("PathfindingManager"));
            var pathfinding = pathfindingGo.AddComponent<PathfindingManager>();
            pathfinding.width = 32;
            pathfinding.height = 32;
            pathfinding.cellSize = 1f;
            ServiceLocator.Register(pathfinding);

            var managerGo = Track(new GameObject("BattleManager"));
            _battle = managerGo.AddComponent<BattleManager>();
            _battle.gridWidth = 32;
            _battle.gridHeight = 32;
            _battle.cellSize = 1f;
            _battle.spawnMissionEnemies = false;
            _battle.pathfinding = pathfinding;

            _battle.StartBattle(null, _database, _balance, _combat, _player, _enemy);

            var placementGo = Track(new GameObject("Placement"));
            _placement = placementGo.AddComponent<BuildingPlacementSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            ResourceNode.Active.Clear();
            BattleContext.End();
            ServiceLocator.Clear();
        }

        private GameObject Track(GameObject go)
        {
            if (go != null && !_created.Contains(go)) _created.Add(go);
            return go;
        }

        [UnityTest]
        public IEnumerator BarracksIsBuilt_TrainsASwordsman_ThatDestroysAnEnemyBuilding()
        {
            Assert.IsNotNull(BattleContext.Current, "the battle must be composed before the flow can run");

            // A worker is what makes construction progress; without one the site would sit at 0.
            UnitController worker = _battle.SpawnUnit(WorkerId, Team.Player, new Vector3(6f, 0f, 6f));
            Assert.IsNotNull(worker, "a worker must spawn on walkable ground");

            // --- Build the barracks through the real placement flow -----------------------------
            BuildingDefinition barracksDefinition = _database.FindBuilding(BarracksId);
            Assert.IsTrue(_placement.BeginPlacement(barracksDefinition), "placement must start with a valid definition");

            Vector3 site = new Vector3(10f, 0f, 10f);
            _placement.UpdateGhost(site, _player);
            Assert.IsTrue(_placement.IsGhostValid, "the chosen site must be a legal footprint");

            int woodBefore = _player.Get(ResourceType.Wood);
            BuildingController barracks = _placement.Confirm(_player);

            Assert.IsNotNull(barracks, "confirming a legal placement must produce a building");
            Assert.IsFalse(barracks.IsConstructed, "a newly placed building starts as a blueprint");
            Assert.AreEqual(woodBefore - barracksDefinition.cost.wood, _player.Get(ResourceType.Wood),
                "placement must charge exactly the definition's wood cost");

            yield return WaitUntil(() => barracks == null || barracks.IsConstructed, 10f, "the dispatched worker to finish the barracks");

            Assert.IsNotNull(barracks, "the barracks must still exist while it is being built");
            Assert.IsTrue(barracks.IsConstructed, "a worker was dispatched, so construction must complete");
            Assert.IsTrue(_combat.IsTracked(barracks), "the finished barracks must be damageable by both sides");

            // --- Train a swordsman ---------------------------------------------------------------
            int foodBefore = _player.Get(ResourceType.Food);
            Assert.IsTrue(barracks.EnqueueTraining(_player, out string reason), $"training must start: {reason}");
            Assert.IsTrue(barracks.IsTraining);
            Assert.AreEqual(foodBefore - _database.FindUnit(SwordsmanId).cost.food, _player.Get(ResourceType.Food),
                "training must charge exactly the unit's food cost");

            yield return WaitUntil(() => CountLiving(Team.Player, combatOnly: true) > 0, 10f, "the swordsman to leave the barracks");

            Assert.AreEqual(1, CountLiving(Team.Player, combatOnly: true), "exactly one swordsman must be produced");

            UnitController swordsman = FindCombatUnit(Team.Player);
            Assert.IsNotNull(swordsman, "the trained swordsman must be in the player's roster");
            Assert.IsFalse(barracks.IsTraining, "the production slot must be free again once the unit is out");

            // --- Destroy an enemy building -------------------------------------------------------
            BuildingController tower = _battle.SpawnBuilding(TowerId, Team.Enemy, new Vector3(16f, 0f, 10f));
            Assert.IsNotNull(tower, "an enemy structure must spawn on walkable ground");
            Assert.IsFalse(tower.IsDestroyed);

            swordsman.Attack(tower);

            yield return WaitUntil(() => tower == null || tower.IsDestroyed, 25f, "the swordsman to raze the enemy tower");

            Assert.IsTrue(tower == null || tower.IsDestroyed, "the enemy structure must be destroyed by the attack order");
            Assert.AreEqual(0, CountLiving(Team.Enemy, combatOnly: false), "no enemy units were spawned, and none should exist");
        }

        private int CountLiving(Team team, bool combatOnly)
        {
            BattleContext context = BattleContext.Current;
            Assert.IsNotNull(context, "a battle context is required to count units");

            List<UnitController> units = context.UnitsOf(team);
            int count = 0;
            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead) continue;
                if (combatOnly && !unit.IsCombatUnit) continue;
                count++;
            }

            return count;
        }

        private UnitController FindCombatUnit(Team team)
        {
            List<UnitController> units = BattleContext.Current.UnitsOf(team);
            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit != null && !unit.IsDead && unit.IsCombatUnit) return unit;
            }

            return null;
        }

        /// <summary>Wait for a condition, failing with a readable reason rather than hanging forever.</summary>
        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"Timed out after {timeoutSeconds:F0}s waiting for {what}.");
                }

                yield return null;
            }
        }

        private GameDatabase BuildDatabase()
        {
            var database = ScriptableObject.CreateInstance<GameDatabase>();

            var worker = ScriptableObject.CreateInstance<UnitDefinition>();
            worker.id = WorkerId;
            worker.displayName = "PlayMode Worker";
            worker.maxHealth = 40;
            worker.damage = 0;
            worker.attackInterval = 1f;
            worker.attackRange = 1.2f;
            worker.moveSpeed = 8f;
            worker.counters = new string[0];
            worker.populationCost = 1;
            worker.cost = new ResourceCost(10, 0, 0, 0);
            worker.trainSeconds = 0.1f;
            worker.color = Color.white;

            var swordsman = ScriptableObject.CreateInstance<UnitDefinition>();
            swordsman.id = SwordsmanId;
            swordsman.displayName = "PlayMode Swordsman";
            swordsman.maxHealth = 120;
            swordsman.damage = 20;
            swordsman.attackInterval = 0.15f;
            swordsman.attackRange = 2.2f;
            swordsman.moveSpeed = 8f;
            swordsman.counters = new string[0];
            swordsman.populationCost = 2;
            swordsman.cost = new ResourceCost(0, 20, 5, 0);
            swordsman.trainSeconds = 0.15f;
            swordsman.color = Color.white;

            var townHall = ScriptableObject.CreateInstance<BuildingDefinition>();
            townHall.id = TownHallId;
            townHall.displayName = "PlayMode Town Hall";
            townHall.maxHealth = 400;
            townHall.cost = ResourceCost.Zero;
            townHall.buildSeconds = 0.2f;
            townHall.footprint = new Vector2Int(2, 2);
            townHall.producesUnitId = WorkerId;
            townHall.popProvided = 10;
            townHall.isTownHall = true;

            var barracks = ScriptableObject.CreateInstance<BuildingDefinition>();
            barracks.id = BarracksId;
            barracks.displayName = "PlayMode Barracks";
            barracks.maxHealth = 300;
            barracks.cost = new ResourceCost(30, 0, 10, 0);
            barracks.buildSeconds = 0.2f;
            barracks.footprint = new Vector2Int(2, 2);
            barracks.producesUnitId = SwordsmanId;
            barracks.popProvided = 5;
            barracks.isTownHall = false;

            var tower = ScriptableObject.CreateInstance<BuildingDefinition>();
            tower.id = TowerId;
            tower.displayName = "PlayMode Tower";
            tower.maxHealth = 120;
            tower.cost = new ResourceCost(20, 0, 0, 0);
            tower.buildSeconds = 0.2f;
            tower.footprint = new Vector2Int(2, 2);
            tower.producesUnitId = string.Empty;
            tower.popProvided = 0;
            tower.isTownHall = false;

            database.economy = _balance;
            database.units = new List<UnitDefinition> { worker, swordsman };
            database.buildings = new List<BuildingDefinition> { townHall, barracks, tower };
            database.missions = new List<MissionDefinition>();
            database.InvalidateCaches();
            return database;
        }
    }
}
