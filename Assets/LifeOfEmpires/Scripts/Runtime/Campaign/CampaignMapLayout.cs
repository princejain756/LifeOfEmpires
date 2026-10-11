using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// One mission's placement on the campaign map surface. The map has no authored coordinate field
    /// on <see cref="MissionDefinition"/> — positions are derived: which territory the mission's title
    /// resolves to, which slot it holds inside that territory, and fixed spacing. A hand-built
    /// definition that sets only id/title/unlockRequirement lays out exactly like a shipped one.
    /// </summary>
    public readonly struct MapNode
    {
        public readonly MissionDefinition Mission;
        public readonly string Territory;
        /// <summary>Index into <see cref="CampaignManager.TerritoryOrder"/>, or -1 for the Homelands fallback.</summary>
        public readonly int TerritoryIndex;
        /// <summary>0-based slot inside the territory, in database order.</summary>
        public readonly int IndexInTerritory;
        /// <summary>
        /// Position on the map surface in map-width units: x is the column fraction (0.30 left,
        /// 0.70 right), y is the distance below the top — taller than the viewport is what the
        /// scroll viewport is for.
        /// </summary>
        public readonly Vector2 Anchor;
        /// <summary>Every mission in this territory is recorded complete.</summary>
        public readonly bool Conquered;
        /// <summary>The unlock rule refuses this mission on the current save.</summary>
        public readonly bool Locked;
        public readonly bool Completed;
        public readonly int Stars;

        public MapNode(
            MissionDefinition mission,
            string territory,
            int territoryIndex,
            int indexInTerritory,
            Vector2 anchor,
            bool conquered,
            bool locked_,
            bool completed,
            int stars)
        {
            Mission = mission;
            Territory = territory;
            TerritoryIndex = territoryIndex;
            IndexInTerritory = indexInTerritory;
            Anchor = anchor;
            Conquered = conquered;
            Locked = locked_;
            Completed = completed;
            Stars = stars;
        }
    }

    /// <summary>
    /// Pure layout for the explorable campaign map: territory bands stacked in conquest order —
    /// Homelands first, then <see cref="CampaignManager.TerritoryOrder"/> — with missions serpentined
    /// two-to-a-row inside each band. No GameObjects, no scene state, so every rule here is EditMode
    /// testable without a canvas.
    /// </summary>
    public static class CampaignMapLayout
    {
        /// <summary>Vertical pitch between missions of one territory, in map-width units.</summary>
        public const float NodeSpacing = 0.34f;

        /// <summary>Band gap below a territory's lowest node, in map-width units.</summary>
        public const float TerritoryGap = 0.16f;

        /// <summary>Space a territory header occupies above its first node, in map-width units.</summary>
        public const float HeaderGap = 0.10f;

        private const float LeftColumnX = 0.30f;
        private const float RightColumnX = 0.70f;

        /// <summary>
        /// One node per entry in <paramref name="db"/>.missions, in database order — never deduped,
        /// sorted or filtered, so the returned list pairs index-for-index with the mission list the
        /// rest of the map code already walks. Lock, completion and territory progress all come from
        /// <see cref="CampaignManager"/> — the layout only positions what the rules already say.
        /// </summary>
        public static List<MapNode> Build(GameDatabase db, SaveData save)
        {
            var nodes = new List<MapNode>();
            if (db == null || db.missions == null) return nodes;

            // Effective band order: Homelands first (the TerritoryOf fallback), then conquest order.
            // A territory with no missions never gets a band.
            var order = new List<string>(CampaignManager.TerritoryOrder.Length + 1) { "Homelands" };
            order.AddRange(CampaignManager.TerritoryOrder);

            // Where every mission's anchor lands is decided territory by territory first, so the
            // result can still be written in database order.
            var anchors = new Dictionary<MissionDefinition, Vector2>();
            var territoryIndexOf = new Dictionary<MissionDefinition, int>();
            var indexInTerritoryOf = new Dictionary<MissionDefinition, int>();
            var bandOf = new Dictionary<MissionDefinition, string>();
            var conqueredOf = new Dictionary<string, bool>();

            float bandTop = 0f;
            for (int territoryIndex = -1; territoryIndex < order.Count - 1; territoryIndex++)
            {
                string territory = order[territoryIndex + 1];
                List<MissionDefinition> missions = CampaignManager.MissionsInTerritory(db, territory);
                if (missions.Count == 0) continue;

                (int _, int _, bool conquered) = CampaignManager.TerritoryProgress(db, save, territory);
                conqueredOf[territory] = conquered;

                for (int i = 0; i < missions.Count; i++)
                {
                    float x = i % 2 == 0 ? LeftColumnX : RightColumnX;
                    float y = bandTop + HeaderGap + i * NodeSpacing;
                    MissionDefinition mission = missions[i];
                    anchors[mission] = new Vector2(x, y);
                    territoryIndexOf[mission] = territoryIndex;
                    indexInTerritoryOf[mission] = i;
                    bandOf[mission] = territory;
                }

                bandTop += HeaderGap + missions.Count * NodeSpacing + TerritoryGap;
            }

            for (int i = 0; i < db.missions.Count; i++)
            {
                MissionDefinition mission = db.missions[i];
                if (mission == null || !anchors.TryGetValue(mission, out Vector2 anchor)) continue;

                string territory = bandOf[mission];
                MissionRecord? record = save != null ? save.FindMission(mission.missionId) : null;
                bool completed = record.HasValue && record.Value.completed;

                nodes.Add(new MapNode(
                    mission,
                    territory,
                    territoryIndexOf[mission],
                    indexInTerritoryOf[mission],
                    anchor,
                    conqueredOf[territory],
                    !CampaignManager.IsUnlocked(save, db, mission),
                    completed,
                    completed ? record.Value.stars : 0));
            }

            return nodes;
        }

        /// <summary>The y range one set of nodes spans, for the connector ribbon between them.</summary>
        public static void ExtentOf(IReadOnlyList<MapNode> nodes, out float minY, out float maxY)
        {
            minY = 0f;
            maxY = 0f;
            if (nodes == null || nodes.Count == 0) return;

            minY = float.MaxValue;
            maxY = float.MinValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                minY = Mathf.Min(minY, nodes[i].Anchor.y);
                maxY = Mathf.Max(maxY, nodes[i].Anchor.y);
            }
        }

        /// <summary>
        /// Project a node's anchor into an anchored position for a content rect of the given width:
        /// x and y share the same map-width unit so the serpentine keeps its shape at any width.
        /// The result is a top-pivot <c>anchoredPosition</c> — y grows downward.
        /// </summary>
        public static Vector2 PositionOf(in MapNode node, float mapWidth)
        {
            return new Vector2(node.Anchor.x * mapWidth, -node.Anchor.y * mapWidth);
        }

        /// <summary>The total map-surface height in map-width units — the scroll content's height fraction.</summary>
        public static float ContentHeight(IReadOnlyList<MapNode> nodes)
        {
            if (nodes == null || nodes.Count == 0) return 0f;

            float max = 0f;
            for (int i = 0; i < nodes.Count; i++) max = Mathf.Max(max, nodes[i].Anchor.y);
            return max + NodeSpacing * 0.6f;
        }
    }
}
