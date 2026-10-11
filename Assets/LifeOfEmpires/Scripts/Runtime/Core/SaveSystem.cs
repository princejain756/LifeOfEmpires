using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>Per-mission progress record. One row per mission the player has touched.</summary>
    [Serializable]
    public struct MissionRecord
    {
        public string missionId;
        public bool completed;
        public int stars;
        /// <summary>Fastest recorded run in seconds; 0 until a timed run is recorded.</summary>
        public float bestSeconds;
        public int timesPlayed;

        public MissionRecord(string missionId)
        {
            this.missionId = missionId;
            completed = false;
            stars = 0;
            bestSeconds = 0f;
            timesPlayed = 0;
        }
    }

    /// <summary>
    /// Everything persisted between sessions. Plain serialisable data — JsonUtility handles it, so
    /// there is no serializer dependency and no reflection cost at save time.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>Bumped whenever the shape below changes; used to migrate rather than discard.</summary>
        public const int CurrentVersion = 3;

        /// <summary>
        /// Bring a payload written by an older build up to the current shape. Version 1 had no kingdom
        /// upgrades and no skirmish preferences; every added field is additive, so nothing is lost and
        /// nothing has to be discarded.
        /// </summary>
        public static void MigrateInPlace(SaveData data)
        {
            if (data == null) return;
            data.ownedUpgrades ??= new List<string>();
            if (data.saveVersion < 2) data.saveVersion = 2;

            if (data.saveVersion < 3)
            {
                // An absent float deserialises to 0, which the volume readers treat as "muted" — so
                // without this branch every player who saved before audio existed would be shipped a
                // silent game. Only an explicit default fixes that.
                if (data.musicVolume <= 0f) data.musicVolume = 0.6f;
                if (data.sfxVolume <= 0f) data.sfxVolume = 0.8f;
                data.saveVersion = 3;
            }
        }

        /// <summary>
        /// Marker proving the payload came from <see cref="SaveSystem.Serialize"/>. Its presence in
        /// the raw text is checked before parsing, because JsonUtility happily returns an object for
        /// arbitrary JSON — including <c>{}</c> — and a save file must not be reconstructed from
        /// something that was never a save file.
        /// </summary>
        public string magic = SaveSystem.Magic;

        public int saveVersion = CurrentVersion;

        /// <summary>Campaign progress, one record per mission.</summary>
        public List<MissionRecord> missions = new List<MissionRecord>();

        /// <summary>Crowns earned across the campaign.</summary>
        public int crowns;

        /// <summary>Purchased non-consumable entitlements (e.g. "noads", "full_campaign").</summary>
        public List<string> entitlements = new List<string>();

        /// <summary>Product ids the player has bought. Entitlement grants are derived from these.</summary>
        public List<string> purchasedProducts = new List<string>();

        /// <summary>Ids of kingdom upgrades the player has bought. Crowns are spent once, at purchase.</summary>
        public List<string> ownedUpgrades = new List<string>();

        /// <summary>Last skirmish configuration, so quick play survives a restart (spec §12).</summary>
        public string skirmishMap = "";
        public string skirmishDifficulty = "";
        public int skirmishSeed;

        /// <summary>Skirmish wins recorded, used to gate the skirmish crown reward (spec §12).</summary>
        public int skirmishWins;

        /// <summary>Master music gain 0..1 (spec §17 "Audio preferences", §18 "Music").</summary>
        public float musicVolume = 0.6f;

        /// <summary>True when the player muted music; separate from the gain so unmuting restores it.</summary>
        public bool musicMuted;

        /// <summary>Master sound-effect gain 0..1 (spec §17 "Audio preferences", §18 "Sound effects").</summary>
        public float sfxVolume = 0.8f;

        /// <summary>True when the player muted sound effects.</summary>
        public bool sfxMuted;

        /// <summary>Set on the first campaign victory, so a later build can skip the guided opening (spec §17 "Tutorial completion").</summary>
        public bool tutorialCompleted;

        public MissionRecord? FindMission(string missionId)
        {
            if (string.IsNullOrEmpty(missionId) || missions == null) return null;
            for (int i = 0; i < missions.Count; i++)
            {
                if (missions[i].missionId == missionId) return missions[i];
            }

            return null;
        }

        public void RecordMissionResult(string missionId, bool completed, int stars, float seconds)
        {
            if (string.IsNullOrEmpty(missionId)) throw new ArgumentException("missionId must be non-empty", nameof(missionId));
            if (missions == null) missions = new List<MissionRecord>();

            MissionRecord record = FindMission(missionId) ?? new MissionRecord(missionId);
            record.completed |= completed;
            record.stars = Mathf.Max(record.stars, stars);
            record.timesPlayed++;
            if (seconds > 0f && (record.bestSeconds <= 0f || seconds < record.bestSeconds))
            {
                record.bestSeconds = seconds;
            }

            int index = missions.FindIndex(m => m.missionId == missionId);
            if (index >= 0) missions[index] = record;
            else missions.Add(record);
        }

        public bool IsComplete(string missionId)
        {
            MissionRecord? record = FindMission(missionId);
            return record.HasValue && record.Value.completed;
        }

        public bool HasEntitlement(string entitlementId)
        {
            if (string.IsNullOrEmpty(entitlementId) || entitlements == null) return false;
            for (int i = 0; i < entitlements.Count; i++)
            {
                if (entitlements[i] == entitlementId) return true;
            }

            return false;
        }

        public bool GrantEntitlement(string entitlementId)
        {
            if (string.IsNullOrEmpty(entitlementId)) return false;
            if (entitlements == null) entitlements = new List<string>();
            if (HasEntitlement(entitlementId)) return false;
            entitlements.Add(entitlementId);
            return true;
        }

        public bool HasUpgrade(string upgradeId)
        {
            if (string.IsNullOrEmpty(upgradeId) || ownedUpgrades == null) return false;
            for (int i = 0; i < ownedUpgrades.Count; i++)
            {
                if (ownedUpgrades[i] == upgradeId) return true;
            }

            return false;
        }

        /// <summary>Spend crowns on an upgrade. Returns false and changes nothing when it is already
        /// owned — upgrades are permanent, so a double tap can never double-charge.</summary>
        public bool PurchaseUpgrade(KingdomUpgradeDefinition upgrade)
        {
            if (upgrade == null) return false;
            if (HasUpgrade(upgrade.upgradeId)) return false;
            if (crowns < upgrade.crownCost) return false;
            if (ownedUpgrades == null) ownedUpgrades = new List<string>();
            crowns -= upgrade.crownCost;
            ownedUpgrades.Add(upgrade.upgradeId);
            return true;
        }

        /// <summary>High-water stage across every owned upgrade, clamped to the authored 0..4 range.</summary>
        public int KingdomStage(IReadOnlyList<KingdomUpgradeDefinition> catalogue)
        {
            if (catalogue == null) return 0;
            int stage = 0;
            for (int i = 0; i < catalogue.Count; i++)
            {
                KingdomUpgradeDefinition upgrade = catalogue[i];
                if (upgrade == null || !HasUpgrade(upgrade.upgradeId)) continue;
                if (upgrade.kingdomStage > stage) stage = upgrade.kingdomStage;
            }

            return Mathf.Clamp(stage, 0, 4);
        }
    }

    /// <summary>
    /// JsonUtility-backed persistence. Deliberately total: a corrupt or truncated payload yields
    /// defaults and a false return instead of an exception, because losing a save file must never
    /// take the game down with it.
    /// </summary>
    public static class SaveSystem
    {
        public const string FileName = "life_of_empires_save.json";

        /// <summary>Written into every payload and required back on load.</summary>
        public const string Magic = "LifeOfEmpiresSave";

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>
        /// Optional path override used by tests to redirect every implicit read and write to a
        /// scratch file. Null in a shipped build, so the real save path is the default; the tests set
        /// it and clear it again so no test can ever leave a real save file behind.
        /// </summary>
        public static string OverridePath { get; set; }

        private static string ResolvePath(string path)
        {
            if (!string.IsNullOrEmpty(path)) return path;
            return string.IsNullOrEmpty(OverridePath) ? DefaultPath : OverridePath;
        }

        public static string Serialize(SaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            data.magic = Magic;
            data.saveVersion = SaveData.CurrentVersion;
            return JsonUtility.ToJson(data, true);
        }

        /// <summary>True only when <paramref name="json"/> parsed into a usable save.</summary>
        public static bool TryDeserialize(string json, out SaveData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(json)) return false;

            // Gate on the raw text first: this rejects empty objects, arrays, foreign JSON and
            // truncated writes before the parser is given a chance to invent a plausible object.
            if (!json.Contains(Magic)) return false;

            try
            {
                SaveData parsed = JsonUtility.FromJson<SaveData>(json);
                if (parsed == null) return false;
                if (!string.Equals(parsed.magic, Magic, StringComparison.Ordinal)) return false;
                if (parsed.saveVersion <= 0) return false;

                // Normalise the collections so downstream code never has to null-check them.
                parsed.missions ??= new List<MissionRecord>();
                parsed.entitlements ??= new List<string>();
                parsed.purchasedProducts ??= new List<string>();

                parsed.ownedUpgrades ??= new List<string>();
                SaveData.MigrateInPlace(parsed);

                data = parsed;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"SaveSystem: discarded unreadable save payload ({exception.GetType().Name}).");
                return false;
            }
        }

        /// <summary>Never throws: unreadable payloads degrade to a fresh save.</summary>
        public static SaveData DeserializeOrDefault(string json)
        {
            return TryDeserialize(json, out SaveData data) ? data : new SaveData();
        }

        public static void WriteToDisk(SaveData data, string path = null)
        {
            string target = ResolvePath(path);
            string directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(target, Serialize(data));
        }

        public static SaveData ReadFromDisk(string path = null)
        {
            string target = ResolvePath(path);
            try
            {
                if (!File.Exists(target)) return new SaveData();
                return DeserializeOrDefault(File.ReadAllText(target));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"SaveSystem: could not read '{target}' ({exception.GetType().Name}); using defaults.");
                return new SaveData();
            }
        }

        public static void DeleteOnDisk(string path = null)
        {
            string target = ResolvePath(path);
            if (File.Exists(target)) File.Delete(target);
        }
    }
}
