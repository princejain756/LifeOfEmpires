using System;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>Win/lose triggers a mission can be scored against.</summary>
    public enum ObjectiveCondition
    {
        None = 0,

        /// <summary>The enemy town hall no longer exists.</summary>
        DestroyEnemyTownHall = 1,

        /// <summary>At least this many enemy buildings have been destroyed.</summary>
        DestroyEnemyBuildings = 2,

        /// <summary>The player's town hall no longer exists.</summary>
        LosePlayerTownHall = 3,

        /// <summary>The timer ran out before the primary objective completed.</summary>
        TimeLimitExpired = 4,

        /// <summary>Player unit count reached <see cref="StarObjective.threshold"/>.</summary>
        TrainUnits = 5,

        /// <summary>Player stockpile reached <see cref="StarObjective.threshold"/> of one resource.</summary>
        StockpileResource = 6
    }

    /// <summary>One optional star. Three per mission, scored independently of victory.</summary>
    [Serializable]
    public struct StarObjective
    {
        [Tooltip("Shown in the mission briefing.")]
        public string description;

        public ObjectiveCondition condition;

        [Tooltip("Meaning depends on the condition: count, resource amount, or seconds.")]
        public int threshold;

        [Tooltip("Resource the threshold applies to when condition is StockpileResource.")]
        public ResourceType resource;

        [Tooltip("Crowns awarded when this star is earned.")]
        [Min(0)] public int crownValue;

        public bool IsConfigured => condition != ObjectiveCondition.None;
    }

    /// <summary>
    /// Authored definition of a single campaign mission. Runtime reads it to spawn the battlefield;
    /// nothing in this type is mutated during play.
    /// </summary>
    [CreateAssetMenu(fileName = "MissionDefinition", menuName = "LifeOfEmpires/Mission Definition", order = 30)]
    public class MissionDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable machine id, e.g. mission_01.")]
        public string missionId = "mission_01";

        [Tooltip("Player-facing title.")]
        public string title = "Untitled Mission";

        [Tooltip("Short briefing shown before the battle.")]
        [TextArea(2, 5)] public string briefing = "";

        [Header("World")]
        [Tooltip("Seed for deterministic terrain generation. Same seed, same battlefield, every run.")]
        public int terrainSeed = 12345;

        [Header("Starting stockpile")]
        [Min(0)] public int startingWood = 200;
        [Min(0)] public int startingFood = 150;
        [Min(0)] public int startingGold = 50;

        [Header("Enemy")]
        [Tooltip("Unit ids the enemy spawns, in spawn order.")]
        public string[] enemyUnitIds = new string[0];

        [Tooltip("Grace period before the enemy's first unit appears.")]
        [Min(0f)] public float enemyStartDelaySeconds = 20f;

        public DifficultyProfile difficulty;

        [Header("Objectives")]
        public ObjectiveCondition victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
        public ObjectiveCondition defeatCondition = ObjectiveCondition.LosePlayerTownHall;

        [Tooltip("Up to three optional stars.")]
        public StarObjective[] starObjectives = new StarObjective[0];

        [Header("Rewards and progression")]
        [Tooltip("True for a quick-play skirmish. Skirmishes ignore unlockRequirement entirely and pay a flat crown reward, so they cannot be farmed for campaign progress.")]
        public bool isSkirmish;

        [Min(0)] public int crownReward = 1;

        [Tooltip("Mission id that must be completed before this one unlocks. Empty means always unlocked.")]
        public string unlockRequirement = "";

        /// <summary>Starting stockpile for this mission as a spendable bundle.</summary>
        public ResourceBundle StartingBundle => new ResourceBundle(startingWood, startingFood, startingGold, 0);

        /// <summary>Number of configured stars, skipping empty slots.</summary>
        public int ConfiguredStarCount
        {
            get
            {
                if (starObjectives == null) return 0;
                int count = 0;
                for (int i = 0; i < starObjectives.Length; i++)
                {
                    if (starObjectives[i].IsConfigured) count++;
                }

                return count;
            }
        }
    }
}
