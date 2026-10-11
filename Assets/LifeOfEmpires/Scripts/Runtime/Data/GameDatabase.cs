using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// The asset-level index of every authored definition. Runtime systems resolve ids through this
    /// one object, so no system ever hardcodes a balance number or a unit id.
    /// </summary>
    [CreateAssetMenu(fileName = "GameDatabase", menuName = "LifeOfEmpires/Game Database", order = 1)]
    public class GameDatabase : ScriptableObject
    {
        [Header("Balance")]
        public EconomyBalance economy;

        [Header("Content")]
        public List<UnitDefinition> units = new List<UnitDefinition>();
        public List<BuildingDefinition> buildings = new List<BuildingDefinition>();
        public List<MissionDefinition> missions = new List<MissionDefinition>();

        [Header("Metagame")]
        [Tooltip("Purchasable permanent upgrades, bought with crowns.")]
        public List<KingdomUpgradeDefinition> upgrades = new List<KingdomUpgradeDefinition>();

        /// <summary>Skirmish missions, kept apart from the campaign list so unlock rules never touch them.</summary>
        public List<MissionDefinition> skirmishes = new List<MissionDefinition>();

        private Dictionary<string, UnitDefinition> _unitById;
        private Dictionary<string, BuildingDefinition> _buildingById;

        /// <summary>Cheap O(1) id lookup. Built lazily; call <see cref="InvalidateCaches"/> after editing lists at runtime.</summary>
        public UnitDefinition FindUnit(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureUnitIndex();
            return _unitById.TryGetValue(id, out UnitDefinition def) ? def : null;
        }

        public BuildingDefinition FindBuilding(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureBuildingIndex();
            return _buildingById.TryGetValue(id, out BuildingDefinition def) ? def : null;
        }

        /// <summary>Resolve a mission id across both the campaign list and the skirmish list.</summary>
        public MissionDefinition FindMission(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (missions != null)
            {
                for (int i = 0; i < missions.Count; i++)
                {
                    if (missions[i] != null && missions[i].missionId == id) return missions[i];
                }
            }

            if (skirmishes != null)
            {
                for (int i = 0; i < skirmishes.Count; i++)
                {
                    if (skirmishes[i] != null && skirmishes[i].missionId == id) return skirmishes[i];
                }
            }

            return null;
        }

        public KingdomUpgradeDefinition FindUpgrade(string id)
        {
            if (string.IsNullOrEmpty(id) || upgrades == null) return null;
            for (int i = 0; i < upgrades.Count; i++)
            {
                if (upgrades[i] != null && upgrades[i].upgradeId == id) return upgrades[i];
            }

            return null;
        }

        public void InvalidateCaches()
        {
            _unitById = null;
            _buildingById = null;
        }

        private void EnsureUnitIndex()
        {
            if (_unitById != null) return;
            _unitById = new Dictionary<string, UnitDefinition>();
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                UnitDefinition def = units[i];
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                _unitById[def.id] = def;
            }
        }

        private void EnsureBuildingIndex()
        {
            if (_buildingById != null) return;
            _buildingById = new Dictionary<string, BuildingDefinition>();
            if (buildings == null) return;
            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingDefinition def = buildings[i];
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                _buildingById[def.id] = def;
            }
        }
    }
}
