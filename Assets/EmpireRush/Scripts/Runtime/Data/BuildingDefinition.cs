using UnityEngine;

namespace EmpireRush
{
    /// <summary>
    /// Static definition of a placeable building. Balance numbers live in the ScriptableObject asset.
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingDefinition", menuName = "EmpireRush/Building Definition", order = 11)]
    public class BuildingDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable machine id. Referenced by missions and the AI.")]
        public string id = "building";

        [Tooltip("Player-facing name.")]
        public string displayName = "Building";

        [Header("Durability")]
        [Min(1)] public int maxHealth = 500;

        [Header("Economy")]
        public ResourceCost cost = ResourceCost.Zero;

        [Tooltip("Seconds of construction time. A worker must be on site for progress to advance.")]
        [Min(0.1f)] public float buildSeconds = 10.0f;

        [Header("Placement")]
        [Tooltip("Axis-aligned footprint in grid cells. X is width, Y is depth.")]
        public Vector2Int footprint = new Vector2Int(2, 2);

        [Header("Production")]
        [Tooltip("Unit id this building trains. Empty means it trains nothing.")]
        public string producesUnitId = "";

        [Tooltip("Population capacity this building grants to its owner.")]
        [Min(0)] public int popProvided;

        [Header("Role")]
        [Tooltip("Town halls are the victory/defeat condition targets.")]
        public bool isTownHall;

        /// <summary>Number of grid cells covered by this building's footprint.</summary>
        public int FootprintArea => Mathf.Max(1, footprint.x) * Mathf.Max(1, footprint.y);

        public bool CanProduceUnit => !string.IsNullOrEmpty(producesUnitId);
    }
}
