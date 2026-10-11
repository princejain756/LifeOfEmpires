using System;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>The curated skirmish maps from spec §12 (Forest / Plains / Highlands).</summary>
    public enum SkirmishMap
    {
        Greenwood = 0,
        Stonebridge = 1,
        IronHills = 2
    }

    /// <summary>The three skirmish difficulty tiers from spec §12.</summary>
    public enum DifficultyTier
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }

    /// <summary>
    /// A player's skirmish choice: which curated map, which difficulty, and the deterministic seed.
    /// Persisted as plain strings on <see cref="SaveData"/> so a corrupt save degrades to
    /// <see cref="Default"/> instead of throwing.
    /// </summary>
    [Serializable]
    public struct SkirmishSettings
    {
        public SkirmishMap map;
        public DifficultyTier difficulty;

        [Min(0)] public int seed;

        public static SkirmishSettings Default => new SkirmishSettings
        {
            map = SkirmishMap.Greenwood,
            difficulty = DifficultyTier.Normal,
            seed = 20261011
        };
    }
}
