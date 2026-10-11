using System.Collections.Generic;
using LifeOfEmpires;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// <see cref="MissionDefinition.unlockRequirement"/> is data, and until now nothing consumed it.
    /// These tests pin the rule the campaign uses: the first authored mission is the way in, every
    /// other mission needs its prerequisite completed, and a replay never revokes progress.
    /// </summary>
    public class CampaignTests
    {
        private GameDatabase _database;
        private MissionDefinition _mission01;
        private MissionDefinition _mission02;

        [SetUp]
        public void SetUp()
        {
            _database = ScriptableObject.CreateInstance<GameDatabase>();
            _mission01 = CreateMission("mission_01", "First Settlement", string.Empty);
            _mission02 = CreateMission("mission_02", "The Enemy Camp", "mission_01");

            _database.missions = new List<MissionDefinition> { _mission01, _mission02 };
            _database.InvalidateCaches();
        }

        [TearDown]
        public void TearDown()
        {
            CampaignManager.ClearPendingMission();

            Object.DestroyImmediate(_database);
            Object.DestroyImmediate(_mission01);
            Object.DestroyImmediate(_mission02);
        }

        private static MissionDefinition CreateMission(string id, string title, string requirement)
        {
            var mission = ScriptableObject.CreateInstance<MissionDefinition>();
            mission.missionId = id;
            mission.title = title;
            mission.unlockRequirement = requirement;
            return mission;
        }

        [Test]
        public void FirstMission_IsAlwaysUnlocked_AndTheSecondIsGated()
        {
            var save = new SaveData();

            Assert.IsTrue(CampaignManager.IsUnlocked(save, _database, _mission01),
                "the first mission needs no progress to be playable");
            Assert.IsFalse(CampaignManager.IsUnlocked(save, _database, _mission02),
                "mission_02 declares mission_01, which has not been completed");
        }

        [Test]
        public void UnlockRequirement_OpensOnceThePrerequisiteIsRecordedComplete()
        {
            var save = new SaveData();
            Assert.IsFalse(CampaignManager.IsUnlocked(save, _database, _mission02), "precondition: still locked");

            save.RecordMissionResult("mission_01", completed: true, stars: 3, seconds: 60f);

            Assert.IsTrue(CampaignManager.IsUnlocked(save, _database, _mission02),
                "completing mission_01 must unlock mission_02");
        }

        [Test]
        public void Replay_KeepsACompletedMissionUnlocked()
        {
            var save = new SaveData();
            save.RecordMissionResult("mission_01", completed: true, stars: 3, seconds: 60f);

            // A later, worse attempt: losing the replay must not take the campaign backwards.
            save.RecordMissionResult("mission_01", completed: false, stars: 0, seconds: 10f);

            Assert.IsTrue(save.IsComplete("mission_01"), "a defeat cannot revoke a completion");
            Assert.AreEqual(3, save.FindMission("mission_01").Value.stars, "the best star count is kept");
            Assert.IsTrue(CampaignManager.IsUnlocked(save, _database, _mission02));
        }

        [Test]
        public void NullMission_IsNeverUnlocked()
        {
            var save = new SaveData();

            Assert.IsFalse(CampaignManager.IsUnlocked(save, null));
            Assert.IsFalse(CampaignManager.IsUnlocked(save, _database, null));
        }

        [Test]
        public void NextPlayableMission_SkipsCompletedMissions_AndReplaysWhenEverythingIsDone()
        {
            var save = new SaveData();

            Assert.AreSame(_mission01, CampaignManager.NextPlayableMission(_database, save),
                "with nothing played, the first mission is next");

            save.RecordMissionResult("mission_01", completed: true, stars: 2, seconds: 30f);
            Assert.AreSame(_mission02, CampaignManager.NextPlayableMission(_database, save),
                "a completed mission must not be offered again while an unlocked one is unfinished");

            save.RecordMissionResult("mission_02", completed: true, stars: 3, seconds: 20f);
            Assert.AreSame(_mission01, CampaignManager.NextPlayableMission(_database, save),
                "a finished campaign replays from the first mission rather than dead-ending");
        }

        [Test]
        public void StarLabel_ReportsProgressOnlyForACompletedMission()
        {
            var save = new SaveData();

            Assert.AreEqual("Not yet conquered", CampaignManager.StarLabel(save, _mission01));

            save.RecordMissionResult("mission_01", completed: true, stars: 2, seconds: 30f);
            Assert.AreEqual($"2/{_mission01.ConfiguredStarCount}", CampaignManager.StarLabel(save, _mission01));
        }

        [Test]
        public void PendingMission_ResolvesOnlyWhenItIsUnlocked()
        {
            var fresh = new SaveData();

            CampaignManager.SelectMission("mission_02");
            Assert.IsNull(CampaignManager.ResolvePending(_database, fresh),
                "a mission whose prerequisite is unmet must not be playable on request");

            CampaignManager.SelectMission("mission_01");
            Assert.AreSame(_mission01, CampaignManager.ResolvePending(_database, fresh),
                "the first mission is always selectable");

            CampaignManager.SelectMission("mission_that_does_not_exist");
            Assert.IsNull(CampaignManager.ResolvePending(_database, fresh), "an unknown id resolves to nothing");
        }
    }
}
