using System.Collections.Generic;
using EmpireRush;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EmpireRush.Tests.EditMode
{
    /// <summary>
    /// Costs must be data, and the economy must honour that data component by component. The first
    /// test proves the arithmetic against the test content set; the second proves the shipped
    /// ScriptableObject assets carry the numbers, which is what stops a balance figure from being
    /// quietly hardcoded in a system.
    /// </summary>
    public class CostTests
    {
        private const string ShippedDatabasePath = "Assets/EmpireRush/Data/GameDatabase.asset";

        [TearDown]
        public void TearDown() => TestWorld.TearDown();

        [Test]
        public void EveryDefinition_CostIsNonNegativeAndDebitableFromAnExactStockpile()
        {
            TestWorld.Build();

            var definitions = new List<(string Label, ResourceCost Cost)>();
            foreach (UnitDefinition unit in TestWorld.Database.units) definitions.Add((unit.id, unit.cost));
            foreach (BuildingDefinition building in TestWorld.Database.buildings) definitions.Add((building.id, building.cost));

            Assert.IsNotEmpty(definitions, "the test database must define content");

            foreach ((string label, ResourceCost cost) in definitions)
            {
                Assert.GreaterOrEqual(cost.wood, 0, $"{label} wood");
                Assert.GreaterOrEqual(cost.food, 0, $"{label} food");
                Assert.GreaterOrEqual(cost.gold, 0, $"{label} gold");
                Assert.GreaterOrEqual(cost.crowns, 0, $"{label} crowns");

                Assert.AreEqual(cost.wood + cost.food + cost.gold + cost.crowns, cost.Total, $"{label} total");

                // A stockpile holding exactly the cost can pay it, and ends at zero.
                var exact = new ResourceManager(Team.Player, new ResourceBundle(cost.wood, cost.food, cost.gold, cost.crowns));
                Assert.IsTrue(exact.TrySpend(cost, out string reason), $"{label}: {reason}");
                Assert.AreEqual(0, exact.Get(ResourceType.Wood), $"{label} wood after spending exactly");
                Assert.AreEqual(0, exact.Get(ResourceType.Food), $"{label} food after spending exactly");
                Assert.AreEqual(0, exact.Get(ResourceType.Gold), $"{label} gold after spending exactly");
                Assert.AreEqual(0, exact.Get(ResourceType.Crowns), $"{label} crowns after spending exactly");

                // One unit short in any non-zero component must be refused.
                foreach (ResourceType type in ResourceTypeInfo.All)
                {
                    int component = cost.AmountOf(type);
                    if (component <= 0) continue;

                    var insufficient = new ResourceBundle(cost.wood, cost.food, cost.gold, cost.crowns);
                    switch (type)
                    {
                        case ResourceType.Wood: insufficient.wood = component - 1; break;
                        case ResourceType.Food: insufficient.food = component - 1; break;
                        case ResourceType.Gold: insufficient.gold = component - 1; break;
                        case ResourceType.Crowns: insufficient.crowns = component - 1; break;
                    }

                    var poor = new ResourceManager(Team.Player, insufficient);
                    Assert.IsFalse(poor.TrySpend(cost, out string _), $"{label} must be unaffordable one {type} short");
                }
            }
        }

        [Test]
        public void ShippedAssets_CarryCostsThatTheEconomyCanPay()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(ShippedDatabasePath);
            if (database == null)
            {
                Assert.Ignore($"{ShippedDatabasePath} has not been generated yet — run EmpireRush/Bootstrap Project first.");
            }

            Assert.IsNotNull(database.economy, "the shipped database must reference an EconomyBalance");
            Assert.IsNotEmpty(database.units, "the shipped database must define units");
            Assert.IsNotEmpty(database.buildings, "the shipped database must define buildings");
            Assert.IsNotEmpty(database.missions, "the shipped database must define missions");

            foreach (UnitDefinition unit in database.units)
            {
                Assert.IsNotNull(unit, "database contains a null unit entry");
                Assert.IsNotEmpty(unit.id, "every unit needs an id");
                Assert.IsNotNull(database.FindUnit(unit.id), $"'{unit.id}' must be resolvable by id");
                Assert.Greater(unit.maxHealth, 0, $"{unit.id} health");
                Assert.Greater(unit.moveSpeed, 0f, $"{unit.id} move speed");
                Assert.GreaterOrEqual(unit.cost.Total, 0, $"{unit.id} cost total");
            }

            foreach (BuildingDefinition building in database.buildings)
            {
                Assert.IsNotNull(building, "database contains a null building entry");
                Assert.IsNotEmpty(building.id, "every building needs an id");
                Assert.IsNotNull(database.FindBuilding(building.id), $"'{building.id}' must be resolvable by id");
                Assert.Greater(building.maxHealth, 0, $"{building.id} health");
                Assert.Greater(building.footprint.x, 0, $"{building.id} footprint width");
                Assert.Greater(building.footprint.y, 0, $"{building.id} footprint depth");
            }

            // Any building that trains a unit must name a unit that exists.
            foreach (BuildingDefinition building in database.buildings)
            {
                if (!building.CanProduceUnit) continue;
                Assert.IsNotNull(database.FindUnit(building.producesUnitId),
                    $"'{building.id}' produces '{building.producesUnitId}', which is not in the database");
            }

            // Any unit a mission names must exist too, or the mission would spawn nothing silently.
            foreach (MissionDefinition mission in database.missions)
            {
                Assert.IsNotNull(mission, "database contains a null mission entry");
                Assert.IsNotEmpty(mission.missionId, "every mission needs an id");
                Assert.IsNotNull(mission.difficulty, $"mission '{mission.missionId}' has no difficulty profile");

                foreach (string unitId in mission.enemyUnitIds)
                {
                    Assert.IsNotNull(database.FindUnit(unitId),
                        $"mission '{mission.missionId}' names enemy unit '{unitId}', which is not in the database");
                }
            }

            // A difficulty profile must never inflate stats: the fields that would do it do not exist.
            Assert.IsFalse(DifficultyProfile.ScalesStats, "difficulty must scale behaviour, not stats");
        }
    }
}
