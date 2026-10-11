using System.Collections.Generic;
using System.IO;
using LifeOfEmpires;
using LifeOfEmpires.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The three metagame screens built from code against an in-memory <see cref="TestWorld"/> database.
    /// These cover what the screens do with the live save and the runtime systems — lock the rows the
    /// save has not unlocked, park a mission through <see cref="CampaignManager"/> instead of starting a
    /// battle, and refuse a kingdom purchase the player cannot afford — not pixel geometry, which
    /// batchmode cannot see.
    ///
    /// Every write redirects through <see cref="SaveSystem.OverridePath"/> to a scratch file that is
    /// deleted in teardown, so no test can ever leave a real save behind.
    /// </summary>
    public class CampaignMapScreenTests
    {
        private string _tempPath;
        private readonly List<Object> _content = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            TestWorld.Build();
            AppendContent(TestWorld.Database);

            _tempPath = Path.Combine(Application.persistentDataPath, "life_of_empires_menu_test_save.json");
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
            SaveSystem.OverridePath = _tempPath;

            CampaignManager.ClearPendingMission();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.OverridePath = null;
            if (File.Exists(_tempPath)) File.Delete(_tempPath);

            CampaignManager.ClearPendingMission();

            for (int i = 0; i < _content.Count; i++)
            {
                if (_content[i] != null) Object.DestroyImmediate(_content[i]);
            }

            _content.Clear();
            TestWorld.TearDown();
        }

        [Test]
        public void CampaignMap_BuildsARowPerMission()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            Assert.AreEqual(TestWorld.Database.missions.Count, screen.state.Rows.Count,
                "the map must show one row per authored mission");
            Assert.AreSame(TestWorld.Database.missions[0], screen.state.Rows[0].Mission,
                "the first row must be the first mission");
        }

        [Test]
        public void CampaignMap_LocksRowsTheSaveHasNotUnlocked()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            Assert.AreEqual(1, CountInteractable(screen), "on a fresh save only the first mission is open");

            var save = new SaveData();
            save.RecordMissionResult("mission_01", completed: true, stars: 3, seconds: 30f);
            SaveSystem.WriteToDisk(save);

            screen.Refresh();

            Assert.IsTrue(screen.state.Rows[1].Button.interactable,
                "completing mission_01 must make the second row playable");
        }

        [Test]
        public void CampaignMap_PlaySelectedRefusesALockedMission()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            screen.SelectRow(1);
            Assert.IsFalse(screen.state.Rows[1].Button.interactable, "precondition: the second mission is locked");

            screen.PlaySelected();

            Assert.IsNull(CampaignManager.PendingMissionId, "a locked mission must never be parked for battle");
        }

        [Test]
        public void CampaignMap_PlaySelectedParksTheMissionForTheBattle()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            screen.SelectRow(0);
            screen.PlaySelected();

            Assert.AreEqual(TestWorld.Database.missions[0].missionId, CampaignManager.PendingMissionId,
                "an unlocked row must park its mission for the battle to pick up");
        }

        [Test]
        public void KingdomScreen_BuyButtonIsDisabledWithoutTheCrowns()
        {
            KingdomScreen screen = CreateScreen<KingdomScreen>();
            screen.Build(TestWorld.Database);

            Assert.IsNotEmpty(screen.Rows, "the kingdom must show one row per upgrade");
            Assert.IsFalse(screen.Rows[0].BuyButton.interactable, "nothing is affordable on a fresh, crownless save");

            SaveSystem.WriteToDisk(new SaveData { crowns = 1000 });
            screen.Refresh();

            Assert.IsTrue(screen.Rows[0].BuyButton.interactable, "enough crowns must enable the buy button");
        }

        [Test]
        public void SkirmishScreen_StartParksTheResolvedSkirmishWithoutConsultingUnlocks()
        {
            SkirmishScreen screen = CreateScreen<SkirmishScreen>();
            screen.Build(TestWorld.Database);

            screen.StartSkirmish();

            Assert.AreEqual("skirmish_01", CampaignManager.PendingMissionId,
                "a fresh save must still be able to start the default skirmish");
        }

        private static int CountInteractable(CampaignMapScreen screen)
        {
            int count = 0;
            for (int i = 0; i < screen.state.Rows.Count; i++)
            {
                if (screen.state.Rows[i].Button != null && screen.state.Rows[i].Button.interactable) count++;
            }

            return count;
        }

        private static T CreateScreen<T>() where T : Component
        {
            GameObject go = TestWorld.Track(new GameObject($"{typeof(T).Name}Screen"));
            return go.AddComponent<T>();
        }

        /// <summary>Twelve chained missions in six territories, three skirmishes and five upgrades.</summary>
        private void AppendContent(GameDatabase database)
        {
            string[] territories =
            {
                "Greenwood Valley", "Stonebridge", "The Iron Hills",
                "Sunfall Plains", "Ashen Frontier", "The Crownlands"
            };

            var missions = new List<MissionDefinition>(12);
            for (int i = 0; i < 12; i++)
            {
                var mission = ScriptableObject.CreateInstance<MissionDefinition>();
                mission.missionId = $"mission_{i + 1:00}";
                mission.title = $"{territories[i / 2]}: Objective {i + 1}";
                mission.unlockRequirement = i == 0 ? string.Empty : $"mission_{i:00}";
                _content.Add(mission);
                missions.Add(mission);
            }

            var skirmishes = new List<MissionDefinition>(3);
            string[] maps = { "Greenwood", "Stonebridge", "Iron Hills" };
            for (int i = 0; i < 3; i++)
            {
                var skirmish = ScriptableObject.CreateInstance<MissionDefinition>();
                skirmish.missionId = $"skirmish_{i + 1:00}";
                skirmish.title = $"Skirmish: {maps[i]}";
                skirmish.isSkirmish = true;
                skirmish.unlockRequirement = string.Empty;
                skirmish.crownReward = i + 1;
                _content.Add(skirmish);
                skirmishes.Add(skirmish);
            }

            var upgrades = new List<KingdomUpgradeDefinition>(5);
            int[] costs = { 8, 14, 22, 35, 18 };
            for (int i = 0; i < 5; i++)
            {
                var upgrade = ScriptableObject.CreateInstance<KingdomUpgradeDefinition>();
                upgrade.upgradeId = $"upg_{i}";
                upgrade.title = $"Upgrade {i}";
                upgrade.crownCost = costs[i];
                _content.Add(upgrade);
                upgrades.Add(upgrade);
            }

            database.missions = missions;
            database.skirmishes = skirmishes;
            database.upgrades = upgrades;
            database.InvalidateCaches();
        }
    }
}
