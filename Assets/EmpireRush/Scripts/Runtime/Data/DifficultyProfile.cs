using UnityEngine;

namespace EmpireRush
{
    /// <summary>
    /// Per-mission AI personality. Difficulty scales *behaviour* — how fast the opponent decides,
    /// how many workers it runs, how large an army it waits for and how quickly it produces — and
    /// deliberately never inflates health or damage. The spec forbids stat inflation, and inflated
    /// stats are what make an RTS opponent feel like a cheat rather than an opponent.
    /// </summary>
    [CreateAssetMenu(fileName = "DifficultyProfile", menuName = "EmpireRush/Difficulty Profile", order = 21)]
    public class DifficultyProfile : ScriptableObject
    {
        [Header("Identity")]
        public string id = "normal";
        public string displayName = "Normal";

        [Header("AI cadence")]
        [Tooltip("Seconds between AI evaluations. The AI thinks on a timer, never per frame.")]
        [Min(0.25f)] public float decisionIntervalSeconds = 2.0f;

        [Header("AI economy")]
        [Tooltip("Economy workers the AI tries to keep alive.")]
        [Min(0)] public int workerCount = 3;

        [Tooltip("Multiplier applied to the AI's production throughput. Scales output, not unit stats.")]
        [Range(0.25f, 3f)] public float productionRateMultiplier = 1.0f;

        [Header("AI military")]
        [Tooltip("Army size at which the AI transitions to Attack.")]
        [Min(1)] public int armySize = 4;

        [Tooltip("Seconds between the AI's attack waves.")]
        [Min(1f)] public float attackCooldownSeconds = 25f;

        [Header("AI threat response")]
        [Tooltip("Radius in metres within which a player unit counts as a threat to the AI's town.")]
        [Min(1f)] public float threatRadius = 18f;

        [Tooltip("Town hall health fraction below which the AI switches to Recover.")]
        [Range(0.05f, 0.9f)] public float recoverHealthFraction = 0.3f;

        /// <summary>Documents the invariant in code so a future balance pass cannot quietly break it.</summary>
        public const bool ScalesStats = false;
    }
}
