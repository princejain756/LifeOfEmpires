using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireRush
{
    /// <summary>
    /// The single source of truth for resource arithmetic. <see cref="ResourceManager"/> owns the
    /// storage; every mutation of that storage funnels through the static methods here so there is
    /// exactly one place where stockpile maths happens. Nothing clamps silently: an unaffordable
    /// spend is refused and reported, never rounded down to whatever the player happens to hold.
    /// </summary>
    [CreateAssetMenu(fileName = "EconomyBalance", menuName = "EmpireRush/Economy Balance", order = 20)]
    public class EconomyBalance : ScriptableObject
    {
        [Header("Starting stockpile (missions override these)")]
        public ResourceBundle startingResources = new ResourceBundle(200, 150, 50, 0);

        [Header("Gathering")]
        [Tooltip("Seconds between gather ticks while a worker stands on a resource node.")]
        [Min(0.05f)] public float gatherIntervalSeconds = 1.5f;

        [Tooltip("Resource units removed from a node per gather tick.")]
        [Min(1)] public int gatherAmountPerTick = 5;

        [Tooltip("Resource units a worker may hold before it must walk to a drop-off building.")]
        [Min(1)] public int workerCarryCapacity = 20;

        [Tooltip("Seconds to hand the carried load over at the drop-off point.")]
        [Min(0f)] public float depositSeconds = 0.35f;

        [Header("Population")]
        [Tooltip("Population capacity every player starts with before any building is raised.")]
        [Min(1)] public int basePopulationCap = 10;

        [Header("Construction")]
        [Tooltip("Construction progress added per second per worker standing on the site.")]
        [Min(0.01f)] public float buildProgressPerWorkerSecond = 0.25f;

        // ---------------------------------------------------------------------------------------
        // Arithmetic. All of it. Nowhere else in the codebase adds to or subtracts from a stockpile.
        // ---------------------------------------------------------------------------------------

        /// <summary>True when every component of <paramref name="cost"/> is covered by the stockpile.</summary>
        public static bool CanAfford(Dictionary<ResourceType, int> stock, ResourceCost cost)
        {
            if (stock == null) throw new ArgumentNullException(nameof(stock));

            for (int i = 0; i < ResourceTypeInfo.All.Length; i++)
            {
                ResourceType type = ResourceTypeInfo.All[i];
                int required = cost.AmountOf(type);
                if (required <= 0) continue;
                if (Get(stock, type) < required) return false;
            }

            return true;
        }

        /// <summary>
        /// Deduct <paramref name="cost"/> if — and only if — the stockpile covers all of it.
        /// On failure the stockpile is left completely untouched and <paramref name="reason"/>
        /// names the first missing resource.
        /// </summary>
        public static bool TrySpend(Dictionary<ResourceType, int> stock, ResourceCost cost, out string reason)
        {
            if (stock == null) throw new ArgumentNullException(nameof(stock));

            for (int i = 0; i < ResourceTypeInfo.All.Length; i++)
            {
                ResourceType type = ResourceTypeInfo.All[i];
                int required = cost.AmountOf(type);
                if (required < 0)
                {
                    reason = $"cost declares a negative {ResourceTypeInfo.DisplayName(type)} amount";
                    return false;
                }

                if (required > 0 && Get(stock, type) < required)
                {
                    reason = $"not enough {ResourceTypeInfo.DisplayName(type)}: need {required}, have {Get(stock, type)}";
                    return false;
                }
            }

            for (int i = 0; i < ResourceTypeInfo.All.Length; i++)
            {
                ResourceType type = ResourceTypeInfo.All[i];
                int required = cost.AmountOf(type);
                if (required > 0) stock[type] = Get(stock, type) - required;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Unconditional deduction for callers that already proved affordability via
        /// <see cref="CanAfford"/>. Throws rather than producing a negative stockpile.
        /// </summary>
        public static void Spend(Dictionary<ResourceType, int> stock, ResourceCost cost)
        {
            if (!TrySpend(stock, cost, out string reason))
            {
                throw new InvalidOperationException($"Spend refused: {reason}");
            }
        }

        /// <summary>Give a cost back (cancelled production, refunded placement). Never inflates illegally.</summary>
        public static void Refund(Dictionary<ResourceType, int> stock, ResourceCost cost)
        {
            if (stock == null) throw new ArgumentNullException(nameof(stock));

            for (int i = 0; i < ResourceTypeInfo.All.Length; i++)
            {
                ResourceType type = ResourceTypeInfo.All[i];
                int amount = cost.AmountOf(type);
                if (amount > 0) stock[type] = Get(stock, type) + amount;
            }
        }

        /// <summary>Grant resources. Negative amounts are a programming error, not a discount.</summary>
        public static void Add(Dictionary<ResourceType, int> stock, ResourceType type, int amount)
        {
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "use Spend to remove resources");

            stock[type] = Get(stock, type) + amount;
        }

        /// <summary>Grant every component of a bundle.</summary>
        public static void AddBundle(Dictionary<ResourceType, int> stock, ResourceBundle bundle)
        {
            if (stock == null) throw new ArgumentNullException(nameof(stock));

            for (int i = 0; i < ResourceTypeInfo.All.Length; i++)
            {
                ResourceType type = ResourceTypeInfo.All[i];
                int amount = bundle.AmountOf(type);
                if (amount > 0) stock[type] = Get(stock, type) + amount;
            }
        }

        /// <summary>Reads a resource, treating an absent key as zero rather than throwing.</summary>
        public static int Get(Dictionary<ResourceType, int> stock, ResourceType type)
        {
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            return stock.TryGetValue(type, out int value) ? value : 0;
        }
    }
}
