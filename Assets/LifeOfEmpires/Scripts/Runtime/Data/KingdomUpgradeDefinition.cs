using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// Authored definition of one permanent kingdom upgrade, bought with crowns.
    ///
    /// Spec §13 balancing rule: permanent upgrades scale behaviour and cosmetics, never make every
    /// battle trivial. Both numeric effects are clamped by the <see cref="RangeAttribute"/> attributes
    /// below, and <see cref="MaxStartingResourceBonus"/> is the ceiling the runtime re-clamps the
    /// summed bonus to — so a fully owned catalogue still cannot trivialise the campaign.
    /// </summary>
    [CreateAssetMenu(fileName = "KingdomUpgrade", menuName = "LifeOfEmpires/Kingdom Upgrade", order = 40)]
    public class KingdomUpgradeDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string upgradeId = "upgrade_id";

        [Tooltip("Player-facing name.")]
        public string title = "Untitled Upgrade";

        [Header("Cost")]
        [Min(0)] public int crownCost = 10;

        [Header("Effect")]
        [Tooltip("Extra starting workers the player fields in every battle. Campaign balance guardrail: capped low so upgrades cannot trivialise the campaign.")]
        [Range(0, 2)] public int bonusStartingWorkers = 0;

        [Tooltip("Fraction added to the starting stockpile of every resource, 0.10 = +10%. Capped at 0.25.")]
        [Range(0f, 0.25f)] public float startingResourceBonus = 0f;

        [Tooltip("0 = Small settlement, 1 = Growing village, 2 = Fortified town, 3 = Stone castle, 4 = Royal stronghold. Visual stage of the home kingdom.")]
        [Range(0, 4)] public int kingdomStage = 0;

        [Tooltip("Player-facing summary of what the upgrade does.")]
        [TextArea(2, 4)] public string description = "";

        /// <summary>The ceiling the runtime clamps the summed starting-resource bonus to (spec §13).</summary>
        public const float MaxStartingResourceBonus = 0.25f;
    }
}
