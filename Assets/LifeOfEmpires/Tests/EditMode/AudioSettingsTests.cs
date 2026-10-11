using System.IO;
using LifeOfEmpires;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// Audio preferences must survive a round trip and, more importantly, must come back audible for a
    /// save written before they existed. A silent game shipped to an existing player is the failure
    /// this file exists to prevent.
    /// </summary>
    public class AudioSettingsTests
    {
        private string _tempPath;

        [SetUp]
        public void SetUp()
        {
            _tempPath = Path.Combine(Application.persistentDataPath, "loe_audio_test.json");
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
        }

        [Test]
        public void Version2PayloadMigratesToAudibleDefaults()
        {
            // A v2 payload carries no volume fields at all. JsonUtility leaves absent floats at 0,
            // which the volume readers would treat as muted, so the migration branch has to fill them.
            const string version2 =
                "{\"magic\":\"LifeOfEmpiresSave\",\"saveVersion\":2,\"missions\":[],\"crowns\":7," +
                "\"entitlements\":[],\"purchasedProducts\":[],\"ownedUpgrades\":[]}";

            Assert.IsTrue(SaveSystem.TryDeserialize(version2, out SaveData save), "a v2 payload must still load");

            Assert.AreEqual(SaveData.CurrentVersion, save.saveVersion, "the migration must stamp the current version");
            Assert.AreEqual(7, save.crowns, "migration must not disturb fields the payload did carry");
            Assert.Greater(save.musicVolume, 0f, "an upgraded save must be audible, not silent");
            Assert.Greater(save.sfxVolume, 0f, "an upgraded save must be audible, not silent");
            Assert.IsFalse(save.musicMuted);
            Assert.IsFalse(save.sfxMuted);
        }

        [Test]
        public void AudioPreferencesRoundTrip()
        {
            var original = new SaveData
            {
                musicVolume = 0.35f,
                sfxVolume = 0.5f,
                musicMuted = true,
                sfxMuted = false,
                tutorialCompleted = true
            };

            string json = SaveSystem.Serialize(original);
            Assert.IsTrue(SaveSystem.TryDeserialize(json, out SaveData restored), "a payload we just wrote must load");

            Assert.AreEqual(0.35f, restored.musicVolume, 0.001f);
            Assert.AreEqual(0.5f, restored.sfxVolume, 0.001f);
            Assert.IsTrue(restored.musicMuted);
            Assert.IsFalse(restored.sfxMuted);
            Assert.IsTrue(restored.tutorialCompleted, "tutorial completion must persist across the round trip");
        }

        [Test]
        public void FreshSaveIsAudibleByDefault()
        {
            var save = new SaveData();

            Assert.Greater(save.musicVolume, 0f, "a brand-new player must hear the menu theme");
            Assert.Greater(save.sfxVolume, 0f);
            Assert.IsFalse(save.musicMuted);
            Assert.IsFalse(save.sfxMuted);
        }
    }
}
