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
        public const int CurrentVersion = 1;

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
            string target = string.IsNullOrEmpty(path) ? DefaultPath : path;
            string directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(target, Serialize(data));
        }

        public static SaveData ReadFromDisk(string path = null)
        {
            string target = string.IsNullOrEmpty(path) ? DefaultPath : path;
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
            string target = string.IsNullOrEmpty(path) ? DefaultPath : path;
            if (File.Exists(target)) File.Delete(target);
        }
    }
}
