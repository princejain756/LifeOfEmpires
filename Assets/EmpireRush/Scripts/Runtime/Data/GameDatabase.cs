using System.Collections.Generic;
using UnityEngine;

namespace EmpireRush
{
    /// <summary>
    /// The asset-level index of every authored definition. Runtime systems resolve ids through this
    /// one object, so no system ever hardcodes a balance number or a unit id.
    /// </summary>
    [CreateAssetMenu(fileName = "GameDatabase", menuName = "EmpireRush/Game Database", order = 1)]
    public class GameDatabase : ScriptableObject
    {
        [Header("Balance")]
        public EconomyBalance economy;

        [Header("Content")]
        public List<UnitDefinition> units = new List<UnitDefinition>();
        public List<BuildingDefinition> buildings = new List<BuildingDefinition>();
        public List<MissionDefinition> missions = new List<MissionDefinition>();

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
