using System.IO;
using EmpireRush;
using NUnit.Framework;
using UnityEngine;

namespace EmpireRush.Tests.EditMode
{
    /// <summary>
    /// Persistence must round-trip campaign progress and entitlements exactly, and must degrade to
    /// defaults rather than throwing when the payload is damaged. A save file is the one thing a
    /// player cannot be asked to recreate.
    /// </summary>
    public class SaveSystemTests
    {
        private string _tempPath;

        [SetUp]
        public void SetUp()
        {
            _tempPath = Path.Combine(Application.persistentDataPath, "empirerush_test_save.json");
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
        }

        private static SaveData Populate()
        {
            var data = new SaveData { crowns = 7 };
            data.RecordMissionResult("mission_01", completed: true, stars: 3, seconds: 123.5f);
            data.RecordMissionResult("mission_02", completed: false, stars: 1, seconds: 240f);
            data.GrantEntitlement("noads");
            data.purchasedProducts.Add("com.empirerush.kingdom.full_campaign");
            return data;
        }

        [Test]
        public void RoundTrip_PreservesProgressAndEntitlements()
        {
            SaveData original = Populate();

            string json = SaveSystem.Serialize(original);
            Assert.IsNotEmpty(json);

            Assert.IsTrue(SaveSystem.TryDeserialize(json, out SaveData restored), "a payload we just wrote must load");

            Assert.AreEqual(original.crowns, restored.crowns);
            Assert.AreEqual(SaveData.CurrentVersion, restored.saveVersion);
            Assert.IsTrue(restored.IsComplete("mission_01"));
            Assert.IsFalse(restored.IsComplete("mission_02"));
            Assert.IsTrue(restored.HasEntitlement("noads"));
            Assert.IsFalse(restored.HasEntitlement("remove_ads"), "an entitlement that was never granted must not appear");

            CollectionAssert.Contains(restored.purchasedProducts, "com.empirerush.kingdom.full_campaign");

            MissionRecord? record = restored.FindMission("mission_01");
            Assert.IsTrue(record.HasValue, "mission_01 must survive the round trip");
            Assert.AreEqual(3, record.Value.stars);
            Assert.AreEqual(123.5f, record.Value.bestSeconds, 0.001f);
            Assert.IsTrue(record.Value.completed);

            Assert.IsNull(restored.FindMission("mission_99"), "an unknown mission must read as absent, not as a default record");
        }

        [Test]
        public void CorruptPayload_FallsBackToDefaultsWithoutThrowing()
        {
            foreach (string corrupt in new[] { "{not json at all", "[1,2,3", "null", "", "   ", "{}" })
            {
                Assert.IsFalse(SaveSystem.TryDeserialize(corrupt, out SaveData parsed),
                    $"'{corrupt}' must not be accepted as a save");

                SaveData fallback = SaveSystem.DeserializeOrDefault(corrupt);
                Assert.IsNotNull(fallback, $"'{corrupt}' must still yield a usable save");
                Assert.AreEqual(0, fallback.crowns);
                Assert.IsNotNull(fallback.missions);
                Assert.IsNotNull(fallback.entitlements);
                Assert.IsNotNull(fallback.purchasedProducts);
                Assert.IsFalse(fallback.HasEntitlement("noads"));
            }
        }

        [Test]
        public void TruncatedPayload_FallsBackToDefaults()
        {
            string json = SaveSystem.Serialize(Populate());
            string truncated = json.Substring(0, json.Length / 2);

            Assert.IsFalse(SaveSystem.TryDeserialize(truncated, out SaveData parsed));
            Assert.IsNull(parsed);

            SaveData fallback = SaveSystem.DeserializeOrDefault(truncated);
            Assert.AreEqual(0, fallback.crowns, "a damaged save must not partially apply");
        }

        [Test]
        public void DiskRoundTrip_PreservesData()
        {
            SaveData original = Populate();
            SaveSystem.WriteToDisk(original, _tempPath);

            Assert.IsTrue(File.Exists(_tempPath));

            SaveData reloaded = SaveSystem.ReadFromDisk(_tempPath);
            Assert.AreEqual(7, reloaded.crowns);
            Assert.IsTrue(reloaded.HasEntitlement("noads"));
            Assert.IsTrue(reloaded.IsComplete("mission_01"));
        }

        [Test]
        public void DiskRead_OnGarbageFile_ReturnsDefaultsInsteadOfThrowing()
        {
            File.WriteAllText(_tempPath, "this is not json");

            SaveData data = SaveSystem.ReadFromDisk(_tempPath);

            Assert.IsNotNull(data);
            Assert.AreEqual(0, data.crowns);
            Assert.IsEmpty(data.missions);
        }

        [Test]
        public void DiskRead_OnMissingFile_ReturnsDefaults()
        {
            Assert.IsFalse(File.Exists(_tempPath));

            SaveData data = SaveSystem.ReadFromDisk(_tempPath);

            Assert.IsNotNull(data);
            Assert.AreEqual(0, data.crowns);
        }

        [Test]
        public void RecordMissionResult_KeepsBestStarsAndFastestTime()
        {
            var data = new SaveData();

            data.RecordMissionResult("mission_01", completed: false, stars: 2, seconds: 100f);
            data.RecordMissionResult("mission_01", completed: true, stars: 1, seconds: 250f);

            MissionRecord? record = data.FindMission("mission_01");
            Assert.IsTrue(record.HasValue);
            Assert.AreEqual(2, record.Value.stars, "a worse run must not lower the star count");
            Assert.AreEqual(100f, record.Value.bestSeconds, 0.001f, "a slower run must not replace the best time");
            Assert.IsTrue(record.Value.completed, "completing on a later attempt must still mark the mission complete");
            Assert.AreEqual(2, record.Value.timesPlayed);

            data.RecordMissionResult("mission_01", completed: true, stars: 3, seconds: 80f);
            record = data.FindMission("mission_01");
            Assert.AreEqual(3, record.Value.stars);
            Assert.AreEqual(80f, record.Value.bestSeconds, 0.001f);
        }

        [Test]
        public void GrantEntitlement_IsIdempotent()
        {
            var data = new SaveData();

            Assert.IsTrue(data.GrantEntitlement("noads"), "the first grant must report a change");
            Assert.IsFalse(data.GrantEntitlement("noads"), "a repeat grant must report no change");
            Assert.AreEqual(1, data.entitlements.Count, "an entitlement must be stored once");
        }

        [Test]
        public void RecordMissionResult_RejectsAnEmptyMissionId()
        {
            var data = new SaveData();
            Assert.Throws<System.ArgumentException>(() => data.RecordMissionResult(string.Empty, true, 1, 1f));
        }
    }
}
