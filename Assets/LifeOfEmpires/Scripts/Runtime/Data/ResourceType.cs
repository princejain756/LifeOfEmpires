using System;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>Every spendable resource in the game. Ordinal order is the canonical display order.</summary>
    public enum ResourceType
    {
        Wood = 0,
        Food = 1,
        Gold = 2,
        Crowns = 3
    }

    /// <summary>Number of distinct <see cref="ResourceType"/> values. Keeps arrays and loops honest.</summary>
    public static class ResourceTypeInfo
    {
        public const int Count = 4;

        /// <summary>Stable, allocation-free list of every resource. Do not mutate.</summary>
        public static readonly ResourceType[] All =
        {
            ResourceType.Wood,
            ResourceType.Food,
            ResourceType.Gold,
            ResourceType.Crowns
        };

        public static string DisplayName(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Wood: return "Wood";
                case ResourceType.Food: return "Food";
                case ResourceType.Gold: return "Gold";
                case ResourceType.Crowns: return "Crowns";
                default: return type.ToString();
            }
        }
    }

    /// <summary>
    /// A price expressed in a subset of the resources. The one and only currency container:
    /// every unit, building and mission cost flows through it, so all spend arithmetic lives in
    /// <see cref="EconomyBalance"/> and nowhere else.
    /// </summary>
    [Serializable]
    public struct ResourceCost
    {
        public int wood;
        public int food;
        public int gold;
        public int crowns;

        public ResourceCost(int wood, int food, int gold, int crowns)
        {
            this.wood = wood;
            this.food = food;
            this.gold = gold;
            this.crowns = crowns;
        }

        public static ResourceCost Zero => new ResourceCost(0, 0, 0, 0);

        public static ResourceCost OfWood(int amount) => new ResourceCost(amount, 0, 0, 0);
        public static ResourceCost OfFood(int amount) => new ResourceCost(0, amount, 0, 0);
        public static ResourceCost OfGold(int amount) => new ResourceCost(0, 0, amount, 0);
        public static ResourceCost OfCrowns(int amount) => new ResourceCost(0, 0, 0, amount);

        public int AmountOf(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Wood: return wood;
                case ResourceType.Food: return food;
                case ResourceType.Gold: return gold;
                case ResourceType.Crowns: return crowns;
                default: return 0;
            }
        }

        public bool IsFree => wood == 0 && food == 0 && gold == 0 && crowns == 0;

        /// <summary>Total units of resource required. Used for UI weighting and cost ranking only.</summary>
        public int Total => wood + food + gold + crowns;

        public override string ToString()
        {
            return $"wood={wood} food={food} gold={gold} crowns={crowns}";
        }
    }

    /// <summary>A bundle of resources, used for deposits, rewards and starting stockpiles.</summary>
    [Serializable]
    public struct ResourceBundle
    {
        public int wood;
        public int food;
        public int gold;
        public int crowns;

        public ResourceBundle(int wood, int food, int gold, int crowns)
        {
            this.wood = wood;
            this.food = food;
            this.gold = gold;
            this.crowns = crowns;
        }

        public int AmountOf(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Wood: return wood;
                case ResourceType.Food: return food;
                case ResourceType.Gold: return gold;
                case ResourceType.Crowns: return crowns;
                default: return 0;
            }
        }

        public override string ToString()
        {
            return $"wood={wood} food={food} gold={gold} crowns={crowns}";
        }
    }
}
