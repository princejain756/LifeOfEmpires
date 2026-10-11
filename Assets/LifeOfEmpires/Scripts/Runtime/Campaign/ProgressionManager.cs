using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// The single consumer of <see cref="GameDatabase.upgrades"/> for battle purposes. Every bonus a
    /// permanent kingdom upgrade grants is computed here, so a battle can never see a different
    /// economy depending on which screen composed it, and no UI script re-implements the arithmetic.
    ///
    /// Every method is a pure read — none mutates the save. A null database or null save returns the
    /// neutral value (0 workers, 0f bonus, stage 0) rather than throwing.
    /// </summary>
    public static class ProgressionManager
    {
        /// <summary>Extra starting workers the owned upgrades grant, clamped to the authored [0, 2].</summary>
        public static int BonusWorkers(GameDatabase database, SaveData save)
        {
            if (database == null || database.upgrades == null || save == null) return 0;

            int total = 0;
            for (int i = 0; i < database.upgrades.Count; i++)
            {
                KingdomUpgradeDefinition upgrade = database.upgrades[i];
                if (upgrade == null || !save.HasUpgrade(upgrade.upgradeId)) continue;
                total += upgrade.bonusStartingWorkers;
            }

            return Mathf.Clamp(total, 0, 2);
        }

        /// <summary>
        /// Fraction added to the opening stockpile of every resource, summed over the owned upgrades
        /// and clamped to <see cref="KingdomUpgradeDefinition.MaxStartingResourceBonus"/>.
        /// </summary>
        public static float ResourceBonusFraction(GameDatabase database, SaveData save)
        {
            if (database == null || database.upgrades == null || save == null) return 0f;

            float total = 0f;
            for (int i = 0; i < database.upgrades.Count; i++)
            {
                KingdomUpgradeDefinition upgrade = database.upgrades[i];
                if (upgrade == null || !save.HasUpgrade(upgrade.upgradeId)) continue;
                total += upgrade.startingResourceBonus;
            }

            return Mathf.Clamp(total, 0f, KingdomUpgradeDefinition.MaxStartingResourceBonus);
        }

        /// <summary>Highest kingdom stage among the owned upgrades, clamped to the authored 0..4.</summary>
        public static int KingdomStage(GameDatabase database, SaveData save)
        {
            if (database == null || save == null) return 0;
            return save.KingdomStage(database.upgrades);
        }

        /// <summary>Player-facing name of a kingdom stage (spec §13), clamped to the authored 0..4.</summary>
        public static string StageName(int stage)
        {
            switch (Mathf.Clamp(stage, 0, 4))
            {
                case 0: return "Small settlement";
                case 1: return "Growing village";
                case 2: return "Fortified town";
                case 3: return "Stone castle";
                default: return "Royal stronghold";
            }
        }
    }
}
