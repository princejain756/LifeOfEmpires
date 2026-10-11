using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// Turns a player's skirmish choice into a playable mission, and owns the spec §12 reward rule.
    ///
    /// A skirmish is always playable: <see cref="Resolve"/> ignores <see cref="MissionDefinition.unlockRequirement"/>
    /// entirely, so quick play can never be gated behind campaign progress. The crown reward stops
    /// after the first five wins so an indefinite replay loop cannot farm crowns.
    /// </summary>
    public static class SkirmishComposer
    {
        /// <summary>Wins that still pay the mission's crown reward; later wins pay nothing.</summary>
        public const int PaidWinCount = 5;

        /// <summary>
        /// The skirmish matching the chosen map, or the first authored one when the choice is absent.
        /// Returns null only when the database has no skirmishes at all.
        /// </summary>
        public static MissionDefinition Resolve(GameDatabase database, SkirmishSettings settings)
        {
            if (database == null || database.skirmishes == null || database.skirmishes.Count == 0) return null;

            string wanted = MissionIdFor(settings.map);
            for (int i = 0; i < database.skirmishes.Count; i++)
            {
                MissionDefinition skirmish = database.skirmishes[i];
                if (skirmish != null && skirmish.missionId == wanted) return skirmish;
            }

            return FirstAuthored(database.skirmishes);
        }

        /// <summary>
        /// Read the stored skirmish choice from a save. Anything unrecognised or missing falls back to
        /// <see cref="SkirmishSettings.Default"/>; a corrupt save string never throws.
        /// </summary>
        public static SkirmishSettings ReadSettings(SaveData save)
        {
            SkirmishSettings settings = SkirmishSettings.Default;
            if (save == null) return settings;

            if (Enum.TryParse(save.skirmishMap, out SkirmishMap map)) settings.map = map;
            if (Enum.TryParse(save.skirmishDifficulty, out DifficultyTier difficulty)) settings.difficulty = difficulty;
            if (save.skirmishSeed >= 0) settings.seed = save.skirmishSeed;
            return settings;
        }

        /// <summary>Write the choice back onto the save. The save shape stays owned by SaveData.</summary>
        public static void WriteSettings(SaveData save, SkirmishSettings settings)
        {
            if (save == null) return;

            save.skirmishMap = settings.map.ToString();
            save.skirmishDifficulty = settings.difficulty.ToString();
            save.skirmishSeed = settings.seed;
        }

        /// <summary>
        /// Crowns a skirmish victory pays: the mission's reward for the first
        /// <see cref="PaidWinCount"/> wins, then nothing. A pure read of <see cref="SaveData.skirmishWins"/>
        /// — it never increments, so the win is counted exactly once, in
        /// <c>BattleManager.HandleBattleEnded</c>.
        /// </summary>
        public static int CrownReward(GameDatabase database, MissionDefinition skirmish, SaveData save)
        {
            if (skirmish == null || save == null) return 0;
            if (save.skirmishWins >= PaidWinCount) return 0;
            return skirmish.crownReward;
        }

        /// <summary>Explicit map-to-id mapping, so the runtime never string-matches enum names.</summary>
        private static string MissionIdFor(SkirmishMap map)
        {
            switch (map)
            {
                case SkirmishMap.Stonebridge: return "skirmish_02";
                case SkirmishMap.IronHills: return "skirmish_03";
                default: return "skirmish_01";
            }
        }

        private static MissionDefinition FirstAuthored(List<MissionDefinition> skirmishes)
        {
            for (int i = 0; i < skirmishes.Count; i++)
            {
                if (skirmishes[i] != null) return skirmishes[i];
            }

            return null;
        }
    }
}
