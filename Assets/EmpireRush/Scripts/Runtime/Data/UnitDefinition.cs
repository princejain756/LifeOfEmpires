using UnityEngine;

namespace EmpireRush
{
    /// <summary>
    /// Static definition of a trainable unit. All balance numbers live here (in a ScriptableObject
    /// asset under Assets/EmpireRush/Data), never in runtime code.
    /// </summary>
    [CreateAssetMenu(fileName = "UnitDefinition", menuName = "EmpireRush/Unit Definition", order = 10)]
    public class UnitDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable machine id. Used by missions, saves and the AI. Never rename in place.")]
        public string id = "unit";

        [Tooltip("Player-facing name.")]
        public string displayName = "Unit";

        [Header("Combat")]
        [Min(1)] public int maxHealth = 100;

        [Tooltip("Damage applied per attack, before defence of the target.")]
        [Min(0)] public int damage = 10;

        [Tooltip("Seconds between attacks while engaged.")]
        [Min(0.05f)] public float attackInterval = 1.0f;

        [Tooltip("World-space metres at which this unit can hit a target. Melee is roughly footprint-sized.")]
        [Min(0.05f)] public float attackRange = 1.5f;

        [Header("Movement")]
        [Tooltip("Metres per second along the path.")]
        [Min(0.05f)] public float moveSpeed = 3.0f;

        [Header("Ranged")]
        [Tooltip("When true, attacks spawn a pooled projectile instead of applying damage directly.")]
        public bool isRanged;

        [Header("Roster")]
        [Tooltip("Unit ids this unit is strong against. Data only: read by combat/AI weighting for the UI.")]
        public string[] counters = new string[0];

        [Tooltip("Population this unit occupies against its owner's town hall capacity.")]
        [Min(0)] public int populationCost = 1;

        [Header("Economy")]
        public ResourceCost cost = ResourceCost.Zero;

        [Tooltip("Seconds of build queue time required to train one of these.")]
        [Min(0.1f)] public float trainSeconds = 5.0f;

        [Header("Presentation")]
        public Color color = Color.white;

        /// <summary>True when this unit can fight. Workers (damage 0 and no counters) are not combat units.</summary>
        public bool IsCombatUnit => damage > 0 || (counters != null && counters.Length > 0);

        public bool CountersUnit(UnitDefinition other)
        {
            if (other == null || counters == null) return false;
            for (int i = 0; i < counters.Length; i++)
            {
                if (!string.IsNullOrEmpty(counters[i]) && counters[i] == other.id) return true;
            }

            return false;
        }
    }
}
