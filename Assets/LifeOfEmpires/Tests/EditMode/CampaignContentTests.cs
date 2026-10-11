using System.Collections.Generic;
using LifeOfEmpires;
using LifeOfEmpires.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The shipped campaign content and the campaign's pure logic. The first group proves the twelve
    /// missions, three skirmishes and five upgrades that <c>ProjectBootstrap</c> authors actually
    /// satisfy the rules the map and the metagame depend on; the second group pins the rules
    /// themselves against hand-built databases.
    /// </summary>
    public class CampaignContentTests
    {
        private const string ShippedDatabasePath = "Assets/LifeOfEmpires/Data/GameDatabase.asset";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            CampaignManager.ClearPendingMission();

            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            TestWorld.TearDown();
        }

        private static GameDatabase ShippedDatabase()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(ShippedDatabasePath);
            if (database == null)
            {
                Assert.Ignore($"{ShippedDatabasePath} has not been generated yet — run LifeOfEmpires/Bootstrap Project first.");
            }

            return database;
        }

        private GameDatabase NewDatabase()
        {
            var database = ScriptableObject.CreateInstance<GameDatabase>();
            _created.Add(database);
            return database;
        }

        private MissionDefinition NewMission(string id, string title, string requirement = "")
        {
            var mission = ScriptableObject.CreateInstance<MissionDefinition>();
            mission.missionId = id;
            mission.title = title;
            mission.unlockRequirement = requirement;
            _created.Add(mission);
            return mission;
        }

        // -----------------------------------------------------------------------------------------
        // Shipped-content integrity
        // -----------------------------------------------------------------------------------------

        [Test]
        public void ShippedDatabase_DefinesTwelveCampaignMissionsInOrder()
        {
            GameDatabase database = ShippedDatabase();

            Assert.AreEqual(12, database.missions.Count, "the campaign ships twelve main missions");
            for (int i = 0; i < 12; i++)
            {
                Assert.AreEqual($"mission_{i + 1:00}", database.missions[i].missionId,
                    "missions must be authored mission_01 .. mission_12 in ascending order");
            }
        }

        [Test]
        public void EveryCampaignMissionUnlocksFromItsPredecessor()
        {
            GameDatabase database = ShippedDatabase();

            Assert.IsEmpty(database.missions[0].unlockRequirement, "the first mission is the way in");
            for (int i = 1; i < database.missions.Count; i++)
            {
                Assert.AreEqual(database.missions[i - 1].missionId, database.missions[i].unlockRequirement,
                    $"mission at index {i} must unlock from the mission before it");
            }
        }

        [Test]
        public void EveryCampaignMissionHasThreeScorableStars()
        {
            GameDatabase database = ShippedDatabase();

            foreach (MissionDefinition mission in database.missions)
            {
                Assert.AreEqual(3, mission.ConfiguredStarCount, $"'{mission.missionId}' must configure three stars");
                foreach (StarObjective star in mission.starObjectives)
                {
                    if (!star.IsConfigured) continue;

                    bool scorable = star.condition == ObjectiveCondition.DestroyEnemyTownHall
                        || star.condition == ObjectiveCondition.DestroyEnemyBuildings
                        || star.condition == ObjectiveCondition.TrainUnits
                        || star.condition == ObjectiveCondition.StockpileResource;
                    Assert.IsTrue(scorable, $"'{mission.missionId}' has an unscorable star '{star.condition}'");
                }
            }
        }

        [Test]
        public void EveryCampaignMissionResolvesItsEnemyUnitsAndDifficulty()
        {
            GameDatabase database = ShippedDatabase();

            foreach (MissionDefinition mission in database.missions)
            {
                Assert.IsNotNull(mission.difficulty, $"'{mission.missionId}' must reference a difficulty profile");
                Assert.IsNotEmpty(mission.enemyUnitIds, $"'{mission.missionId}' must name enemy units");
                foreach (string unitId in mission.enemyUnitIds)
                {
                    Assert.IsNotNull(database.FindUnit(unitId),
                        $"'{mission.missionId}' names enemy unit '{unitId}', which the database does not define");
                }
            }
        }

        [Test]
        public void CrownRewardsNeverDecreaseAlongTheCampaign()
        {
            GameDatabase database = ShippedDatabase();

            for (int i = 0; i < database.missions.Count; i++)
            {
                Assert.GreaterOrEqual(database.missions[i].crownReward, 1, "every mission must pay at least one crown");
                if (i + 1 >= database.missions.Count) continue;

                Assert.LessOrEqual(database.missions[i].crownReward, database.missions[i + 1].crownReward,
                    "crown rewards must never decrease as the campaign advances");
            }
        }

        [Test]
        public void ShippedDatabase_DefinesThreeSkirmishesAndFiveUpgrades()
        {
            GameDatabase database = ShippedDatabase();

            Assert.AreEqual(3, database.skirmishes.Count, "the game ships three skirmish maps");
            Assert.AreEqual(5, database.upgrades.Count, "the kingdom ships five permanent upgrades");
        }

        [Test]
        public void SkirmishesAreUngatedAndFlagged()
        {
            GameDatabase database = ShippedDatabase();

            foreach (MissionDefinition skirmish in database.skirmishes)
            {
                Assert.IsTrue(skirmish.isSkirmish, $"'{skirmish.missionId}' must be flagged as a skirmish");
                Assert.IsEmpty(skirmish.unlockRequirement, $"'{skirmish.missionId}' must never be gated");

                foreach (MissionDefinition mission in database.missions)
                {
                    Assert.AreNotEqual(skirmish.missionId, mission.missionId,
                        "a skirmish id must not appear in the campaign list");
                }
            }
        }

        [Test]
        public void KingdomUpgradesRespectTheCampaignBalanceGuardrail()
        {
            GameDatabase database = ShippedDatabase();
            var fullyOwned = new SaveData();

            foreach (KingdomUpgradeDefinition upgrade in database.upgrades)
            {
                Assert.GreaterOrEqual(upgrade.crownCost, 1, $"'{upgrade.upgradeId}' must cost crowns");
                Assert.That(upgrade.bonusStartingWorkers, Is.InRange(0, 2));
                Assert.That(upgrade.startingResourceBonus, Is.InRange(0f, KingdomUpgradeDefinition.MaxStartingResourceBonus));
                Assert.That(upgrade.kingdomStage, Is.InRange(0, 4));

                fullyOwned.ownedUpgrades.Add(upgrade.upgradeId);
            }

            Assert.LessOrEqual(ProgressionManager.ResourceBonusFraction(database, fullyOwned),
                KingdomUpgradeDefinition.MaxStartingResourceBonus,
                "even a fully owned kingdom must not exceed the resource-bonus ceiling");
            Assert.LessOrEqual(ProgressionManager.BonusWorkers(database, fullyOwned), 2,
                "even a fully owned kingdom must not exceed the worker ceiling");
        }

        // -----------------------------------------------------------------------------------------
        // Pure logic
        // -----------------------------------------------------------------------------------------

        [Test]
        public void TerritoryOf_GroupsMissionsByTheirTerritoryPrefix()
        {
            foreach (string territory in CampaignManager.TerritoryOrder)
            {
                MissionDefinition mission = NewMission("grouped", $"{territory}: Objective");
                Assert.AreEqual(territory, CampaignManager.TerritoryOf(mission), $"'{territory}' must group its mission");
            }

            MissionDefinition odd = NewMission("odd_id", "Nowhere Special");
            Assert.AreEqual("Homelands", CampaignManager.TerritoryOf(odd),
                "a title with no known territory prefix groups under Homelands, never a raw mission id or empty");
            Assert.IsEmpty(CampaignManager.TerritoryOf(null));
        }

        [Test]
        public void MissionsInTerritory_ReturnsEveryMissionOfThatTerritory_AndNothingElse()
        {
            GameDatabase database = NewDatabase();
            MissionDefinition first = NewMission("a1", "Greenwood Valley: One");
            MissionDefinition second = NewMission("a2", "Greenwood Valley: Two");
            MissionDefinition other = NewMission("b1", "Stonebridge: Elsewhere");
            database.missions = new List<MissionDefinition> { first, second, other };
            database.InvalidateCaches();

            List<MissionDefinition> grouped = CampaignManager.MissionsInTerritory(database, "Greenwood Valley");

            CollectionAssert.AreEqual(new[] { first, second }, grouped);
            Assert.IsEmpty(CampaignManager.MissionsInTerritory(database, "No Such Territory"));
        }

        [Test]
        public void TerritoryProgress_SumsStarsAndReportsConquest()
        {
            GameDatabase database = NewDatabase();
            MissionDefinition first = NewMission("a1", "Greenwood Valley: One");
            MissionDefinition second = NewMission("a2", "Greenwood Valley: Two");
            database.missions = new List<MissionDefinition> { first, second };
            database.InvalidateCaches();

            var save = new SaveData();
            save.RecordMissionResult("a1", completed: true, stars: 3, seconds: 10f);

            (int Stars, int ConfiguredStars, bool Conquered) progress =
                CampaignManager.TerritoryProgress(database, save, "Greenwood Valley");
            Assert.IsFalse(progress.Conquered, "one completed mission is not a conquered territory");
            Assert.AreEqual(3, progress.Stars, "only the completed mission's stars count");

            save.RecordMissionResult("a2", completed: true, stars: 2, seconds: 10f);
            progress = CampaignManager.TerritoryProgress(database, save, "Greenwood Valley");
            Assert.IsTrue(progress.Conquered, "every mission complete is a conquered territory");
            Assert.AreEqual(5, progress.Stars);
        }

        [Test]
        public void IsCampaignComplete_RequiresEveryMission()
        {
            GameDatabase database = NewDatabase();
            MissionDefinition first = NewMission("a1", "Greenwood Valley: One");
            MissionDefinition second = NewMission("a2", "Greenwood Valley: Two");
            database.missions = new List<MissionDefinition> { first, second };
            database.InvalidateCaches();

            var save = new SaveData();
            Assert.IsFalse(CampaignManager.IsCampaignComplete(database, save), "a fresh save has finished nothing");

            save.RecordMissionResult("a1", completed: true, stars: 3, seconds: 10f);
            Assert.IsFalse(CampaignManager.IsCampaignComplete(database, save), "a partial save is not a finished campaign");

            save.RecordMissionResult("a2", completed: true, stars: 3, seconds: 10f);
            Assert.IsTrue(CampaignManager.IsCampaignComplete(database, save), "all missions complete finishes the campaign");

            GameDatabase empty = NewDatabase();
            Assert.IsFalse(CampaignManager.IsCampaignComplete(empty, save), "an empty campaign is never complete");
        }

        [Test]
        public void SkirmishNeverBecomesALockedMission()
        {
            GameDatabase database = NewDatabase();
            MissionDefinition campaign = NewMission("mission_01", "Greenwood Valley: Start");
            MissionDefinition skirmish = NewMission("skirmish_01", "Skirmish: Greenwood");
            skirmish.isSkirmish = true;
            database.missions = new List<MissionDefinition> { campaign };
            database.skirmishes = new List<MissionDefinition> { skirmish };
            database.InvalidateCaches();

            Assert.IsTrue(CampaignManager.IsUnlocked(new SaveData(), database, skirmish),
                "a skirmish with no requirement is always playable");

            CampaignManager.SelectMission("skirmish_01");
            Assert.AreSame(skirmish, CampaignManager.ResolvePending(database, new SaveData()),
                "a skirmish resolves even against a fresh save");
        }

        [Test]
        public void ResolvePending_StillRefusesALockedCampaignMission()
        {
            GameDatabase database = NewDatabase();
            MissionDefinition first = NewMission("mission_01", "Greenwood Valley: Start");
            MissionDefinition fourth = NewMission("mission_04", "Stonebridge: Watch", "mission_03");
            MissionDefinition fifth = NewMission("mission_05", "Stonebridge: Recruits", "mission_04");
            database.missions = new List<MissionDefinition> { first, fourth, fifth };
            database.InvalidateCaches();

            CampaignManager.SelectMission("mission_05");
            Assert.IsNull(CampaignManager.ResolvePending(database, new SaveData()),
                "a campaign mission whose prerequisite is unmet must not be playable on request");
        }

        [Test]
        public void SaveDataPurchaseUpgrade_IsIdempotentAndRefusesAPoorPurse()
        {
            var upgrade = ScriptableObject.CreateInstance<KingdomUpgradeDefinition>();
            upgrade.upgradeId = "upg_test";
            upgrade.crownCost = 10;
            _created.Add(upgrade);

            var poor = new SaveData { crowns = 5 };
            Assert.IsFalse(poor.PurchaseUpgrade(upgrade), "a purse below the cost must be refused");
            Assert.AreEqual(5, poor.crowns, "a refused purchase must not spend");
            Assert.IsEmpty(poor.ownedUpgrades, "a refused purchase must not grant");

            var rich = new SaveData { crowns = 12 };
            Assert.IsTrue(rich.PurchaseUpgrade(upgrade), "a sufficient purse must buy the upgrade");
            Assert.AreEqual(2, rich.crowns, "exactly the cost must be deducted");
            Assert.IsFalse(rich.PurchaseUpgrade(upgrade), "a second purchase of a permanent upgrade must be refused");
            Assert.AreEqual(2, rich.crowns, "a repeat purchase must not deduct again");
            Assert.AreEqual(1, rich.ownedUpgrades.Count);
        }

        [Test]
        public void SaveDataRoundTripsKingdomAndSkirmishFields()
        {
            var save = new SaveData { crowns = 3 };
            save.ownedUpgrades.Add("upg_village");
            save.skirmishMap = "Stonebridge";
            save.skirmishDifficulty = "Hard";
            save.skirmishSeed = 4242;
            save.skirmishWins = 3;

            string json = SaveSystem.Serialize(save);
            Assert.IsTrue(SaveSystem.TryDeserialize(json, out SaveData restored), "a payload we just wrote must load");

            CollectionAssert.Contains(restored.ownedUpgrades, "upg_village");
            Assert.AreEqual("Stonebridge", restored.skirmishMap);
            Assert.AreEqual("Hard", restored.skirmishDifficulty);
            Assert.AreEqual(4242, restored.skirmishSeed);
            Assert.AreEqual(3, restored.skirmishWins);
            Assert.AreEqual(SaveData.CurrentVersion, restored.saveVersion);
        }

        [Test]
        public void Version1PayloadMigratesWithoutLoss()
        {
            const string json =
                "{\"magic\":\"LifeOfEmpiresSave\",\"saveVersion\":1,\"crowns\":4," +
                "\"missions\":[{\"missionId\":\"mission_01\",\"completed\":true,\"stars\":2,\"bestSeconds\":30.0,\"timesPlayed\":1}]," +
                "\"entitlements\":[],\"purchasedProducts\":[]}";

            Assert.IsTrue(SaveSystem.TryDeserialize(json, out SaveData migrated), "a version 1 payload must still load");
            Assert.IsTrue(migrated.IsComplete("mission_01"), "existing progress must survive the migration");
            Assert.AreEqual(4, migrated.crowns);
            Assert.IsNotNull(migrated.ownedUpgrades, "a migrated payload must carry a usable upgrade list");
            Assert.IsEmpty(migrated.ownedUpgrades, "version 1 had no upgrades, so none may be invented");
            Assert.AreEqual(SaveData.CurrentVersion, migrated.saveVersion, "the payload must be brought up to date");
        }

        [Test]
        public void ProgressionManager_IsNeutralWithoutProgressOrContent()
        {
            var save = new SaveData();

            Assert.AreEqual(0, ProgressionManager.BonusWorkers(null, null));
            Assert.AreEqual(0f, ProgressionManager.ResourceBonusFraction(null, save));
            Assert.AreEqual(0, ProgressionManager.KingdomStage(null, save));
            Assert.AreEqual(0, ProgressionManager.BonusWorkers(NewDatabase(), save));
            Assert.AreEqual(0f, ProgressionManager.ResourceBonusFraction(NewDatabase(), save));
            Assert.AreEqual(0, ProgressionManager.KingdomStage(NewDatabase(), save));
        }

        [Test]
        public void SkirmishCrownsStopAfterFiveWins()
        {
            var skirmish = ScriptableObject.CreateInstance<MissionDefinition>();
            skirmish.missionId = "skirmish_01";
            skirmish.crownReward = 3;
            _created.Add(skirmish);

            var save = new SaveData { skirmishWins = 4 };
            Assert.AreEqual(3, SkirmishComposer.CrownReward(null, skirmish, save),
                "the fifth win still pays the full reward, and a null database must be tolerated");

            save.skirmishWins = 5;
            Assert.AreEqual(0, SkirmishComposer.CrownReward(null, skirmish, save), "the sixth win pays nothing");

            save.skirmishWins = 50;
            Assert.AreEqual(0, SkirmishComposer.CrownReward(null, skirmish, save), "later wins pay nothing");

            save.skirmishWins = 4;
            int first = SkirmishComposer.CrownReward(null, skirmish, save);
            int second = SkirmishComposer.CrownReward(null, skirmish, save);
            Assert.AreEqual(first, second, "CrownReward must be a pure read");
            Assert.AreEqual(4, save.skirmishWins, "CrownReward must not increment the win count");

            Assert.AreEqual(0, SkirmishComposer.CrownReward(null, null, save), "no mission pays nothing");
        }

        [Test]
        public void SkirmishSettingsRoundTripAndSurviveCorruption()
        {
            var save = new SaveData();
            var settings = new SkirmishSettings
            {
                map = SkirmishMap.IronHills,
                difficulty = DifficultyTier.Hard,
                seed = 777
            };

            SkirmishComposer.WriteSettings(save, settings);
            SkirmishSettings restored = SkirmishComposer.ReadSettings(save);

            Assert.AreEqual(SkirmishMap.IronHills, restored.map);
            Assert.AreEqual(DifficultyTier.Hard, restored.difficulty);
            Assert.AreEqual(777, restored.seed);

            save.skirmishMap = "not_a_map";
            save.skirmishDifficulty = null;

            SkirmishSettings fallback = SkirmishComposer.ReadSettings(save);
            Assert.AreEqual(SkirmishSettings.Default.map, fallback.map, "an unknown map must fall back to the default");
            Assert.AreEqual(SkirmishSettings.Default.difficulty, fallback.difficulty, "an unknown tier must fall back to the default");
            Assert.AreEqual(SkirmishSettings.Default.map, SkirmishComposer.ReadSettings(null).map, "a null save must fall back to the default");
        }

        [Test]
        public void StageName_CoversTheFiveAuthoredStages()
        {
            Assert.AreEqual("Small settlement", ProgressionManager.StageName(0));
            Assert.AreEqual("Growing village", ProgressionManager.StageName(1));
            Assert.AreEqual("Fortified town", ProgressionManager.StageName(2));
            Assert.AreEqual("Stone castle", ProgressionManager.StageName(3));
            Assert.AreEqual("Royal stronghold", ProgressionManager.StageName(4));
        }
    }
}
