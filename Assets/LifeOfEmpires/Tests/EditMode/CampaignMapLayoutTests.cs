using System.Collections.Generic;
using System.IO;
using LifeOfEmpires;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The pure campaign-map layout: one node per mission in database order, territory bands in
    /// Homelands-then-conquest order, serpentine columns inside a band, and lock/conquest state
    /// replayed from <see cref="CampaignManager"/> rather than recomputed here.
    /// </summary>
    public class CampaignMapLayoutTests
    {
        private string _tempPath;
        private readonly List<Object> _content = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            TestWorld.Build();

            _tempPath = Path.Combine(Application.persistentDataPath, "life_of_empires_layout_test_save.json");
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
            SaveSystem.OverridePath = _tempPath;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.OverridePath = null;
            if (File.Exists(_tempPath)) File.Delete(_tempPath);

            for (int i = 0; i < _content.Count; i++)
            {
                if (_content[i] != null) Object.DestroyImmediate(_content[i]);
            }

            _content.Clear();
            TestWorld.TearDown();
        }

        [Test]
        public void Build_ReturnsOneNodePerMission_InDatabaseOrder()
        {
            GameDatabase db = Database(4);
            var save = new SaveData();

            List<MapNode> nodes = CampaignMapLayout.Build(db, save);

            Assert.AreEqual(db.missions.Count, nodes.Count, "exactly one node per mission");
            for (int i = 0; i < nodes.Count; i++)
            {
                Assert.AreSame(db.missions[i], nodes[i].Mission, $"node {i} must reference mission {i}");
            }
        }

        [Test]
        public void Build_TerritoryBandsFollowHomelandsThenConquestOrder()
        {
            var missions = new List<MissionDefinition>
            {
                NewMission("m1", "The Iron Hills: A"),
                NewMission("m2", "Greenwood Valley: B"),
                NewMission("m3", "No Prefix Home"),
                NewMission("m4", "Greenwood Valley: C"),
            };
            GameDatabase db = Database(missions);
            var save = new SaveData();

            List<MapNode> nodes = CampaignMapLayout.Build(db, save);

            // Band order is positional, not list order: Homelands (the TerritoryOf fallback) sits
            // above Greenwood Valley, which sits above The Iron Hills — regardless of database
            // order — and a territory with no mission never produces a band.
            Assert.AreEqual("Homelands", nodes[2].Territory);
            Assert.AreEqual("Greenwood Valley", nodes[1].Territory);
            Assert.AreEqual("The Iron Hills", nodes[0].Territory);

            Assert.Less(nodes[2].Anchor.y, nodes[1].Anchor.y, "Homelands must sit above Greenwood");
            Assert.Less(nodes[2].Anchor.y, nodes[3].Anchor.y, "Homelands above every Greenwood node");
            Assert.Less(nodes[3].Anchor.y, nodes[0].Anchor.y, "Greenwood above The Iron Hills");

            Assert.AreEqual(-1, nodes[2].TerritoryIndex, "Homelands is the fallback, not a TerritoryOrder member");
            Assert.AreEqual(0, nodes[1].TerritoryIndex, "Greenwood Valley is TerritoryOrder[0]");
        }

        [Test]
        public void Build_SerpentinesInsideATerritoryBand()
        {
            var missions = new List<MissionDefinition>
            {
                NewMission("m1", "Greenwood Valley: A"),
                NewMission("m2", "Greenwood Valley: B"),
                NewMission("m3", "Greenwood Valley: C"),
            };
            GameDatabase db = Database(missions);

            List<MapNode> nodes = CampaignMapLayout.Build(db, new SaveData());

            Assert.AreEqual(3, nodes.Count);
            Assert.Less(nodes[0].Anchor.x, nodes[1].Anchor.x, "even index left, odd index right");
            Assert.Greater(nodes[2].Anchor.x, nodes[0].Anchor.x - 0.001f, "third mission returns to the left column");
            Assert.Less(nodes[0].Anchor.x, 0.5f);
            Assert.Greater(nodes[1].Anchor.x, 0.5f);
            Assert.Less(nodes[0].Anchor.y, nodes[1].Anchor.y, "y must strictly descend within the band");
            Assert.Less(nodes[1].Anchor.y, nodes[2].Anchor.y);
        }

        [Test]
        public void Build_TerritoryBandsNeverOverlap()
        {
            var missions = new List<MissionDefinition>();
            for (int i = 0; i < 4; i++) missions.Add(NewMission($"gv{i}", $"Greenwood Valley: {i}"));
            for (int i = 0; i < 3; i++) missions.Add(NewMission($"sb{i}", $"Stonebridge: {i}"));
            GameDatabase db = Database(missions);

            List<MapNode> nodes = CampaignMapLayout.Build(db, new SaveData());

            float gvMax = 0f;
            float sbMin = float.MaxValue;
            foreach (MapNode node in nodes)
            {
                if (node.Territory == "Greenwood Valley") gvMax = Mathf.Max(gvMax, node.Anchor.y);
                else sbMin = Mathf.Min(sbMin, node.Anchor.y);
            }

            Assert.Greater(sbMin, gvMax, "every node in territory n must sit below all of territory n-1");
        }

        [Test]
        public void Build_LocksReplayTheUnlockRuleNotACopy()
        {
            var missions = new List<MissionDefinition>();
            for (int i = 0; i < 3; i++)
            {
                missions.Add(NewMission($"mission_{i + 1:00}", $"Greenwood Valley: {i + 1}",
                    i == 0 ? string.Empty : $"mission_{i:00}"));
            }
            GameDatabase db = Database(missions);

            var save = new SaveData();
            List<MapNode> nodes = CampaignMapLayout.Build(db, save);

            int unlocked = 0;
            foreach (MapNode node in nodes) if (!node.Locked) unlocked++;
            Assert.AreEqual(1, unlocked, "a fresh save opens exactly the first mission");

            save.RecordMissionResult("mission_01", completed: true, stars: 3, seconds: 30f);
            SaveSystem.WriteToDisk(save);

            nodes = CampaignMapLayout.Build(db, SaveSystem.ReadFromDisk());

            Assert.IsFalse(nodes[1].Locked, "completing mission_01 must unlock the second node");
            Assert.IsTrue(nodes[2].Locked, "the third node stays locked until mission_02 completes");
            Assert.IsTrue(nodes[0].Completed);
            Assert.AreEqual(3, nodes[0].Stars);
        }

        [Test]
        public void Build_NullDatabaseReturnsEmpty_NeverThrows()
        {
            Assert.DoesNotThrow(() =>
            {
                List<MapNode> nodes = CampaignMapLayout.Build(null, new SaveData());
                Assert.AreEqual(0, nodes.Count);
            });
        }

        [Test]
        public void ContentHeight_CoversEveryNode()
        {
            GameDatabase db = Database(6);
            List<MapNode> nodes = CampaignMapLayout.Build(db, new SaveData());

            float height = CampaignMapLayout.ContentHeight(nodes);

            Assert.Greater(height, 0f);
            foreach (MapNode node in nodes)
            {
                Assert.GreaterOrEqual(height, node.Anchor.y, "content height must cover every node");
            }
        }

        private GameDatabase Database(int missionCount)
        {
            var missions = new List<MissionDefinition>();
            for (int i = 0; i < missionCount; i++)
            {
                missions.Add(NewMission($"m{i}", $"Greenwood Valley: {i}"));
            }
            return Database(missions);
        }

        private GameDatabase Database(List<MissionDefinition> missions)
        {
            GameDatabase db = TestWorld.Database;
            db.missions = missions;
            db.InvalidateCaches();
            return db;
        }

        private MissionDefinition NewMission(string id, string title, string unlockRequirement = "")
        {
            var mission = ScriptableObject.CreateInstance<MissionDefinition>();
            mission.missionId = id;
            mission.title = title;
            mission.unlockRequirement = unlockRequirement;
            _content.Add(mission);
            return mission;
        }
    }
}
