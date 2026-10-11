using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// Tap-to-place flow: a translucent ghost follows the finger, turns green or red as the footprint
    /// is checked against terrain and existing buildings, and on confirm the cost is spent and a
    /// worker is dispatched to raise the structure.
    ///
    /// Validity is evaluated against the same navigation grid the units walk on, so a building can
    /// never be placed somewhere a worker could not reach to build it.
    /// </summary>
    public class BuildingPlacementSystem : MonoBehaviour
    {
        [Header("Placement rules")]
        [Tooltip("Highest terrain cost a footprint cell may have. 1.0 means flat ground only.")]
        [Min(0.01f)] public float maxBuildableTerrainCost = 1.01f;

        [Tooltip("Ghost opacity.")]
        [Range(0.1f, 1f)] public float ghostAlpha = 0.45f;

        [Header("Ghost colours")]
        public Color validColor = new Color(0.35f, 0.9f, 0.4f);
        public Color invalidColor = new Color(0.9f, 0.3f, 0.25f);

        private GameObject _ghost;
        private Renderer _ghostRenderer;
        private Material _ghostMaterial;
        private BuildingDefinition _definition;
        private Team _team = Team.Player;
        private Vector2Int _ghostCell;
        private bool _hasGhost;
        private bool _ghostValid;

        public bool IsPlacing => _hasGhost;

        public BuildingDefinition PendingDefinition => _definition;

        /// <summary>True when the ghost currently sits on a legal footprint.</summary>
        public bool IsGhostValid => _ghostValid;

        /// <summary>
        /// Start placing <paramref name="definition"/>. Returns false when the definition is missing
        /// or no navigation grid exists — placement without a grid cannot be validated.
        /// </summary>
        public bool BeginPlacement(BuildingDefinition definition, Team team = Team.Player)
        {
            if (definition == null)
            {
                Debug.LogWarning("BuildingPlacementSystem: cannot place a null definition.");
                return false;
            }

            if (ResolvePathfinding() == null)
            {
                Debug.LogWarning("BuildingPlacementSystem: no navigation grid; placement needs one to validate footprints.");
                return false;
            }

            CancelPlacement();

            _definition = definition;
            _team = team;
            _hasGhost = true;
            CreateGhost(definition);
            return true;
        }

        /// <summary>Move the ghost to a world point, snapped to the grid.</summary>
        public void UpdateGhost(Vector3 worldPoint, ResourceManager payer = null)
        {
            if (!_hasGhost) return;

            PathfindingManager pathfinding = ResolvePathfinding();
            if (pathfinding == null) return;

            Vector2Int cell = pathfinding.CellOf(worldPoint);
            Vector2Int footprint = _definition.footprint;

            // Snap so the footprint is centred on the witnessed cell.
            _ghostCell = new Vector2Int(cell.x - (footprint.x / 2), cell.y - (footprint.y / 2));

            // Integer division on purpose: the centre cell of an odd-sized footprint, biased low for
            // an even one, so the ghost lands on a real cell rather than between two.
            Vector3 centre = pathfinding.CentreOf(
                _ghostCell.x + ((footprint.x - 1) / 2),
                _ghostCell.y + ((footprint.y - 1) / 2));
            centre.y = transform.position.y;
            _ghost.transform.position = centre;
            _ghostValid = Evaluate(_ghostCell, footprint, payer, out string _);
            ApplyGhostTint(_ghostValid);
        }

        /// <summary>
        /// Check whether a footprint could be built here. <paramref name="reason"/> explains a refusal
        /// so the UI can surface it rather than silently ignoring the tap.
        /// </summary>
        public bool Evaluate(Vector2Int cell, Vector2Int footprint, ResourceManager payer, out string reason)
        {
            PathfindingManager pathfinding = ResolvePathfinding();
            if (pathfinding == null)
            {
                reason = "no navigation grid";
                return false;
            }

            for (int y = cell.y; y < cell.y + footprint.y; y++)
            {
                for (int x = cell.x; x < cell.x + footprint.x; x++)
                {
                    if (!pathfinding.InBounds(x, y))
                    {
                        reason = "outside the battlefield";
                        return false;
                    }

                    if (pathfinding.IsBlocked(x, y))
                    {
                        reason = "occupied or impassable";
                        return false;
                    }

                    if (pathfinding.GetTerrainCost(x, y) > maxBuildableTerrainCost)
                    {
                        reason = "ground is too rough to build on";
                        return false;
                    }
                }
            }

            if (payer != null && _definition != null && !payer.CanAfford(_definition.cost))
            {
                reason = $"cannot afford {_definition.displayName} ({_definition.cost})";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Commit the placement. Spends the cost, spawns the blueprint, and sends the nearest free
        /// worker to build it. Returns the created building, or null when the spot is illegal or the
        /// player cannot pay — in which case nothing at all is mutated.
        /// </summary>
        public BuildingController Confirm(ResourceManager payer)
        {
            if (!_hasGhost || _definition == null) return null;

            PathfindingManager pathfinding = ResolvePathfinding();
            if (pathfinding == null) return null;

            Vector2Int footprint = _definition.footprint;
            if (!Evaluate(_ghostCell, footprint, payer, out string reason))
            {
                Debug.LogWarning($"BuildingPlacementSystem: placement refused — {reason}.");
                return null;
            }

            // Charge only after the footprint is proven legal, and through the one economy entry point.
            if (payer != null && !payer.TrySpend(_definition.cost, out string spendReason))
            {
                Debug.LogWarning($"BuildingPlacementSystem: placement refused — {spendReason}.");
                return null;
            }

            Vector3 position = _ghost != null ? _ghost.transform.position : pathfinding.CentreOf(_ghostCell);
            BuildingController building = SpawnBlueprint(position);

            CancelPlacement();

            if (building != null) DispatchBuilder(building);
            return building;
        }

        private BuildingController SpawnBlueprint(Vector3 position)
        {
            var go = new GameObject($"Building_{_definition.id}");
            go.transform.SetPositionAndRotation(position, Quaternion.identity);

            Vector2Int footprint = _definition.footprint;
            float cellSize = ResolvePathfinding() != null ? ResolvePathfinding().cellSize : 1f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(footprint.x * cellSize * 0.95f, 2.4f, footprint.y * cellSize * 0.95f);
            body.transform.localPosition = new Vector3(0f, 1.2f, 0f);

            var controller = go.AddComponent<BuildingController>();
            var combat = ResolveCombat();
            controller.Initialize(_definition, _team, combat);
            controller.BeginConstruction();

            BattleContext context = BattleContext.Current;
            if (context != null) context.BuildingsOf(_team).Add(controller);

            return controller;
        }

        private void DispatchBuilder(BuildingController building)
        {
            WorkerAgent worker = FindNearestFreeWorker(building.transform.position, _team);
            if (worker != null) worker.AssignBuild(building);
        }

        private static WorkerAgent FindNearestFreeWorker(Vector3 position, Team team)
        {
            BattleContext context = BattleContext.Current;
            if (context == null) return null;

            List<UnitController> units = context.UnitsOf(team);
            WorkerAgent best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead) continue;

                var agent = unit.GetComponent<WorkerAgent>();
                if (agent == null || !agent.IsIdle) continue;

                float sqr = (unit.transform.position - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = agent;
            }

            return best;
        }

        public void CancelPlacement()
        {
            _hasGhost = false;
            _ghostValid = false;
            _definition = null;

            if (_ghost != null)
            {
                if (Application.isPlaying) Destroy(_ghost);
                else DestroyImmediate(_ghost);
            }
            _ghost = null;
            _ghostRenderer = null;
        }

        private void CreateGhost(BuildingDefinition definition)
        {
            PathfindingManager pathfinding = ResolvePathfinding();
            float cellSize = pathfinding != null ? pathfinding.cellSize : 1f;
            Vector2Int footprint = definition.footprint;

            _ghost = new GameObject("PlacementGhost");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(_ghost.transform, false);
            body.transform.localScale = new Vector3(footprint.x * cellSize * 0.95f, 2.4f, footprint.y * cellSize * 0.95f);
            body.transform.localPosition = new Vector3(0f, 1.2f, 0f);

            Collider collider = body.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            _ghostRenderer = body.GetComponent<Renderer>();
            _ghostMaterial = CreateGhostMaterial();
            if (_ghostRenderer != null && _ghostMaterial != null) _ghostRenderer.sharedMaterial = _ghostMaterial;
        }

        private Material CreateGhostMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var material = new Material(shader) { name = "PlacementGhost" };
            ConfigureTransparent(material, validColor);
            return material;
        }

        private void ConfigureTransparent(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, ghostAlpha));
            if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(color.r, color.g, color.b, ghostAlpha));
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f); // URP: transparent
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);     // URP: alpha blend
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private void ApplyGhostTint(bool valid)
        {
            if (_ghostMaterial == null) return;
            ConfigureTransparent(_ghostMaterial, valid ? validColor : invalidColor);
        }

        private static PathfindingManager ResolvePathfinding()
        {
            BattleContext context = BattleContext.Current;
            if (context != null && context.Pathfinding != null) return context.Pathfinding;
            return ServiceLocator.TryGet(out PathfindingManager found) ? found : null;
        }

        private static CombatSystem ResolveCombat()
        {
            BattleContext context = BattleContext.Current;
            if (context != null && context.Combat != null) return context.Combat;
            return ServiceLocator.TryGet(out CombatSystem found) ? found : null;
        }

        private void OnDestroy()
        {
            if (_ghostMaterial == null) return;
            if (Application.isPlaying) Destroy(_ghostMaterial);
            else DestroyImmediate(_ghostMaterial);
        }
    }
}
