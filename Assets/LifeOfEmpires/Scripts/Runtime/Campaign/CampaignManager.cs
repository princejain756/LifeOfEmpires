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
