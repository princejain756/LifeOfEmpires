using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// Mission progression: which missions the player may play, and how far they have taken each one.
    ///
    /// This is the consumer <see cref="MissionDefinition.unlockRequirement"/> never had. It is pure
    /// logic over the database plus a <see cref="SaveData"/> — no scene, no singleton — so the unlock
    /// rule can be exercised directly by tests and can never disagree with what the results card shows.
    ///
    /// The campaign map itself is Milestone 4; the only piece needed now is choosing which mission the
    /// next battle plays.
    /// </summary>
    public static class CampaignManager
    {
        /// <summary>
        /// Mission the player asked to play next, parked here because a scene load cannot carry
        /// arguments. Consumed by <see cref="GameBootstrap"/> when it composes a battle.
        /// </summary>
        public static string PendingMissionId { get; private set; }

        /// <summary>Remember a mission to play next. An empty id clears the choice.</summary>
        public static void SelectMission(string missionId)
        {
            PendingMissionId = string.IsNullOrEmpty(missionId) ? null : missionId;
        }

        public static void ClearPendingMission() => PendingMissionId = null;

        /// <summary>
        /// Whether <paramref name="mission"/> may be played: an empty requirement is always open, and
        /// otherwise the requirement carries the whole decision. A null mission is never unlocked.
        /// </summary>
        public static bool IsUnlocked(SaveData save, MissionDefinition mission)
        {
            if (mission == null) return false;
            if (string.IsNullOrEmpty(mission.unlockRequirement)) return true;
            return save != null && save.IsComplete(mission.unlockRequirement);
        }

        /// <summary>
        /// Whether <paramref name="mission"/> may be played given the campaign's shape: the first
        /// authored mission is the player's way in and is always open, whatever it declares.
        /// </summary>
        public static bool IsUnlocked(SaveData save, GameDatabase database, MissionDefinition mission)
        {
            if (mission == null) return false;
            if (IsFirstMission(database, mission)) return true;
            return IsUnlocked(save, mission);
        }

        /// <summary>
        /// The mission to put in front of the player: the first unlocked one they have not completed,
        /// or — when everything unlocked is done — the first unlocked mission, so a finished campaign
        /// can be replayed rather than dead-ending.
        /// </summary>
        public static MissionDefinition NextPlayableMission(GameDatabase database, SaveData save)
        {
            if (database == null || database.missions == null) return null;

            MissionDefinition firstUnlocked = null;
            for (int i = 0; i < database.missions.Count; i++)
            {
                MissionDefinition mission = database.missions[i];
                if (!IsUnlocked(save, database, mission)) continue;

                if (firstUnlocked == null) firstUnlocked = mission;
                if (save == null || !save.IsComplete(mission.missionId)) return mission;
            }

            return firstUnlocked;
        }

        /// <summary>
        /// The mission held on <see cref="PendingMissionId"/>, or null when nothing is pending, the id
        /// is unknown, or the mission is not unlocked yet. With no save supplied the on-disk save is
        /// read, which is what the running game wants.
        /// </summary>
        public static MissionDefinition ResolvePending(GameDatabase database, SaveData save = null)
        {
            if (string.IsNullOrEmpty(PendingMissionId) || database == null || database.missions == null) return null;

            SaveData progress = save ?? SaveSystem.ReadFromDisk();
            for (int i = 0; i < database.missions.Count; i++)
            {
                MissionDefinition mission = database.missions[i];
                if (mission == null || mission.missionId != PendingMissionId) continue;
                return IsUnlocked(progress, database, mission) ? mission : null;
            }

            // A skirmish is always playable: quick play must never be gated behind campaign progress.
            if (database.skirmishes != null)
            {
                for (int i = 0; i < database.skirmishes.Count; i++)
                {
                    MissionDefinition skirmish = database.skirmishes[i];
                    if (skirmish == null || skirmish.missionId != PendingMissionId) continue;
                    return skirmish;
                }
            }

            return null;
        }

        /// <summary>Progress text for a mission: stars earned against stars configured, or a prompt.</summary>
        public static string StarLabel(SaveData save, MissionDefinition mission)
        {
            if (mission == null) return string.Empty;

            MissionRecord? record = save != null ? save.FindMission(mission.missionId) : null;
            if (!record.HasValue || !record.Value.completed) return "Not yet conquered";

            return $"{record.Value.stars}/{mission.ConfiguredStarCount}";
        }

        /// <summary>Names of the six campaign territories from spec §11, in conquest order.</summary>
        public static readonly string[] TerritoryOrder =
        {
            "Greenwood Valley", "Stonebridge", "The Iron Hills",
            "Sunfall Plains", "Ashen Frontier", "The Crownlands"
        };

        /// <summary>
        /// A mission's territory, taken from the territory block its title opens with, so the campaign
        /// map can group missions into conquered kingdoms without a second field that could disagree
        /// with the title. A title with no known territory prefix falls back to the mission's own id so
        /// a mission is never hidden from the map.
        /// </summary>
        public static string TerritoryOf(MissionDefinition mission)
        {
            if (mission == null) return string.Empty;
            for (int i = 0; i < TerritoryOrder.Length; i++)
            {
                if (!string.IsNullOrEmpty(mission.title) && mission.title.StartsWith(TerritoryOrder[i], StringComparison.OrdinalIgnoreCase))
                {
                    return TerritoryOrder[i];
                }
            }

            // Early missions predate the territory system: group them under the homeland instead of
            // leaking a raw mission id into the map header.
            return "Homelands";
        }

        /// <summary>Every mission authored for one territory, in database order.</summary>
        public static List<MissionDefinition> MissionsInTerritory(GameDatabase database, string territory)
        {
            var result = new List<MissionDefinition>();
            if (database == null || database.missions == null || string.IsNullOrEmpty(territory)) return result;

            for (int i = 0; i < database.missions.Count; i++)
            {
                MissionDefinition mission = database.missions[i];
                if (mission == null) continue;
                if (string.Equals(TerritoryOf(mission), territory, StringComparison.OrdinalIgnoreCase)) result.Add(mission);
            }

            return result;
        }

        /// <summary>
        /// Stars recorded against one territory, and whether the whole territory is conquered. Replayed
        /// missions keep their best star count, so the total only ever rises.
        /// </summary>
        public static (int Stars, int ConfiguredStars, bool Conquered) TerritoryProgress(
            GameDatabase database, SaveData save, string territory)
        {
            int stars = 0;
            int configured = 0;
            bool allComplete = true;

            List<MissionDefinition> missions = MissionsInTerritory(database, territory);
            for (int i = 0; i < missions.Count; i++)
            {
                MissionDefinition mission = missions[i];
                configured += mission.ConfiguredStarCount;
                MissionRecord? record = save != null ? save.FindMission(mission.missionId) : null;
                if (!record.HasValue || !record.Value.completed)
                {
                    allComplete = false;
                    continue;
                }

                stars += record.Value.stars;
            }

            return (stars, configured, missions.Count > 0 && allComplete);
        }

        /// <summary>
        /// Whether the campaign is finished: every mission in the database is recorded complete. Used by
        /// the campaign map to keep replaying reachable instead of dead-ending.
        /// </summary>
        public static bool IsCampaignComplete(GameDatabase database, SaveData save)
        {
            if (database == null || database.missions == null || database.missions.Count == 0) return false;
            if (save == null) return false;

            for (int i = 0; i < database.missions.Count; i++)
            {
                MissionDefinition mission = database.missions[i];
                if (mission == null || !save.IsComplete(mission.missionId)) return false;
            }

            return true;
        }

        private static bool IsFirstMission(GameDatabase database, MissionDefinition mission)
        {
            if (database == null || database.missions == null || mission == null) return false;
            for (int i = 0; i < database.missions.Count; i++)
            {
                if (database.missions[i] == null) continue;
                return ReferenceEquals(database.missions[i], mission);
            }

            return false;
        }
    }
}
