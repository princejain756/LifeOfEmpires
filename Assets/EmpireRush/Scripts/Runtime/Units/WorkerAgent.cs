using System;
using UnityEngine;

namespace EmpireRush
{
    /// <summary>
    /// The economy worker's gather loop: walk to a node, gather on a tick, carry the load to a
    /// drop-off, deposit, repeat. It drives a <see cref="UnitController"/> rather than duplicating
    /// movement, so gathering, marching and building all share one steering and pathfinding path.
    ///
    /// Every abort path is explicit — depleted node, destroyed drop-off, unreachable destination,
    /// reassignment — and each one either finds a replacement or parks the worker in Idle. A worker
    /// can therefore never wedge itself in a loop waiting for a resource that no longer exists.
    /// </summary>
    [RequireComponent(typeof(UnitController))]
    public class WorkerAgent : MonoBehaviour
    {
        [Header("Assignment")]
        [Tooltip("Resource this worker prefers when it needs to find a new node.")]
        public ResourceType preferredResource = ResourceType.Wood;

        [Tooltip("How far to look for a replacement node or drop-off, in metres.")]
        [Min(1f)] public float searchRadius = 80f;

        [Header("Diagnostics (read-only)")]
        [SerializeField] private Phase phase = Phase.Idle;
        [SerializeField] private int carried;
        [SerializeField] private ResourceType carriedType = ResourceType.Wood;
        [SerializeField] private int completedTrips;

        private enum Phase
        {
            Idle = 0,
            ToNode = 1,
            Gathering = 2,
            ToDropOff = 3,
            Depositing = 4,
            ToBuildSite = 5,
            Building = 6
        }

        private UnitController _unit;
        private ResourceNode _node;
        private BuildingController _dropOff;
        private BuildingController _buildTarget;
        private float _tickTimer;
        private float _depositTimer;
        private bool _missingBalanceReported;

        public bool IsIdle => phase == Phase.Idle;
        public int Carried => carried;
        public int CompletedTrips => completedTrips;
        public ResourceNode CurrentNode => _node;
        public BuildingController BuildTarget => _buildTarget;

        private void Awake()
        {
            _unit = GetComponent<UnitController>();
        }

        /// <summary>Resolve the shared systems once the spawner has finished wiring the unit.</summary>
        public void Initialize(ResourceType resource, BuildingController dropOff)
        {
            preferredResource = resource;
            _dropOff = dropOff;
        }

        // -----------------------------------------------------------------------------------------
        // Orders
        // -----------------------------------------------------------------------------------------

        /// <summary>Send the worker to gather from a specific node.</summary>
        public void AssignNode(ResourceNode node)
        {
            if (_unit == null || _unit.IsDead) return;

            ReturnCarried();

            _buildTarget = null;

            if (node == null || node.IsDepleted)
            {
                node = ResourceNode.FindNearest(preferredResource, transform.position, searchRadius);
            }

            if (node == null)
            {
                Debug.LogWarning($"WorkerAgent: no {preferredResource} node within {searchRadius}m; worker standing by.");
                Abort();
                return;
            }

            _node = node;
            preferredResource = node.resourceType;
            GoToNode();
        }

        /// <summary>Send the worker to construct a blueprint.</summary>
        public void AssignBuild(BuildingController building)
        {
            if (_unit == null || _unit.IsDead) return;
            if (building == null || building.IsDestroyed)
            {
                Abort();
                return;
            }

            ReturnCarried();

            _node = null;
            _buildTarget = building;
            phase = Phase.ToBuildSite;
            _unit.EnterGathering();

            _unit.MoveTo(building.InteractionPoint, arrived =>
            {
                if (_buildTarget == null || _buildTarget.IsDestroyed)
                {
                    Abort();
                    return;
                }

                if (!arrived)
                {
                    Debug.LogWarning($"WorkerAgent: build site {_buildTarget.name} is unreachable; worker standing by.");
                    Abort();
                    return;
                }

                phase = Phase.Building;
                _unit.EnterGathering();
            });
        }

        /// <summary>
        /// Stop whatever the worker is doing and return its load to the node it came from, so a
        /// reassignment neither destroys resources nor duplicates them.
        /// </summary>
        public void Abort()
        {
            ReturnCarried();

            _node = null;
            _buildTarget = null;
            _tickTimer = 0f;
            _depositTimer = 0f;
            phase = Phase.Idle;

            if (_unit != null && !_unit.IsDead) _unit.Stop();
        }

        // -----------------------------------------------------------------------------------------
        // Loop
        // -----------------------------------------------------------------------------------------

        private void Update()
        {
            if (_unit == null || _unit.IsDead)
            {
                phase = Phase.Idle;
                return;
            }

            EconomyBalance balance = ResolveBalance();
            if (balance == null)
            {
                if (!_missingBalanceReported)
                {
                    Debug.LogWarning("WorkerAgent: no EconomyBalance in the battle context; worker idling.");
                    _missingBalanceReported = true;
                }

                return;
            }

            switch (phase)
            {
                case Phase.Gathering:
                    UpdateGathering(balance);
                    break;

                case Phase.Depositing:
                    UpdateDepositing(balance);
                    break;

                case Phase.Building:
                    UpdateBuilding(balance);
                    break;
            }
        }

        /// <summary>
        /// Walk to the current node (or find one). <paramref name="attempt"/> bounds the retry chain:
        /// a failed move must not bounce between "find another node" and "walk to it" forever, which
        /// is exactly what happens when the battlefield has no navigation grid and the move fails
        /// synchronously.
        /// </summary>
        private void GoToNode(int attempt = 0)
        {
            if (_node == null || _node.IsDepleted)
            {
                _node = ResourceNode.FindNearest(preferredResource, transform.position, searchRadius);
                if (_node == null)
                {
                    Abort();
                    return;
                }
            }

            phase = Phase.ToNode;
            _unit.EnterGathering();

            ResourceNode target = _node;
            _unit.MoveTo(target.InteractionPoint, arrived =>
            {
                // The node may have been stripped bare while the worker was walking.
                if (target == null || target.IsDepleted)
                {
                    _node = null;
                    RetryOrAbort(attempt, GoToNode);
                    return;
                }

                if (!arrived)
                {
                    Debug.LogWarning($"WorkerAgent: {target.resourceType} node unreachable; looking for another.");
                    _node = null;
                    RetryOrAbort(attempt, GoToNode);
                    return;
                }

                _node = target;
                phase = Phase.Gathering;
                _tickTimer = 0f;
                _unit.EnterGathering();
            });
        }

        private void UpdateGathering(EconomyBalance balance)
        {
            if (_node == null || _node.IsDepleted)
            {
                if (carried > 0) GoToDropOff();
                else GoToNode();
                return;
            }

            _tickTimer += Time.deltaTime;
            if (_tickTimer < balance.gatherIntervalSeconds) return;

            _tickTimer -= balance.gatherIntervalSeconds;

            int room = Mathf.Max(0, balance.workerCarryCapacity - carried);
            if (room > 0)
            {
                int granted = _node.Gather(Mathf.Min(balance.gatherAmountPerTick, room));
                if (granted > 0)
                {
                    carried += granted;
                    carriedType = _node.resourceType;
                }
            }

            if (carried >= balance.workerCarryCapacity) GoToDropOff();
            else if (_node.IsDepleted && carried > 0) GoToDropOff();
        }

        private void GoToDropOff(int attempt = 0)
        {
            if (_dropOff == null || _dropOff.IsDestroyed) _dropOff = FindDropOff();

            if (_dropOff == null)
            {
                Debug.LogWarning("WorkerAgent: no drop-off building available; worker holding its load.");
                phase = Phase.Idle;
                return;
            }

            phase = Phase.ToDropOff;
            _unit.EnterGathering();

            BuildingController target = _dropOff;
            _unit.MoveTo(target.InteractionPoint, arrived =>
            {
                if (target == null || target.IsDestroyed)
                {
                    _dropOff = null;
                    RetryOrAbort(attempt, GoToDropOff);
                    return;
                }

                if (!arrived)
                {
                    Debug.LogWarning($"WorkerAgent: drop-off {target.name} unreachable; worker standing by with its load.");
                    phase = Phase.Idle;
                    return;
                }

                phase = Phase.Depositing;
                _depositTimer = 0f;
                _unit.EnterGathering();
            });
        }

        private void UpdateDepositing(EconomyBalance balance)
        {
            _depositTimer += Time.deltaTime;
            if (_depositTimer < balance.depositSeconds) return;

            _depositTimer = 0f;

            ResourceManager sink = _unit.ResourceSink;
            if (sink != null && carried > 0)
            {
                sink.Add(carriedType, carried);
                carried = 0;
                completedTrips++;
            }

            // GoToNode finds a replacement when the old node is spent, so this is the only exit.
            GoToNode();
        }

        private void UpdateBuilding(EconomyBalance balance)
        {
            if (_buildTarget == null || _buildTarget.IsDestroyed)
            {
                Abort();
                return;
            }

            if (_buildTarget.IsConstructed)
            {
                _buildTarget = null;
                phase = Phase.Idle;
                return;
            }

            _buildTarget.AddConstructionWork(balance.buildProgressPerWorkerSecond * Time.deltaTime);
        }

        private BuildingController FindDropOff()
        {
            BattleContext context = BattleContext.Current;
            if (context == null) return null;

            var candidates = context.BuildingsOf(_unit.team);
            BuildingController best = null;
            float bestSqr = searchRadius * searchRadius;

            for (int i = 0; i < candidates.Count; i++)
            {
                BuildingController building = candidates[i];
                if (building == null || building.IsDestroyed || !building.IsConstructed) continue;

                float sqr = (building.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = building;
            }

            return best;
        }

        /// <summary>
        /// Re-run a locator after a failed move, but only a bounded number of times. Without the cap a
        /// synchronous failure — no navigation grid, or a single reachable-but-unwalkable target —
        /// would re-enter the same method until the stack overflowed.
        /// </summary>
        private void RetryOrAbort(int attempt, Action<int> retry)
        {
            const int maxAttempts = 2;

            if (attempt >= maxAttempts)
            {
                Abort();
                return;
            }

            retry(attempt + 1);
        }

        private void ReturnCarried()
        {
            if (carried <= 0) return;

            // Conservation: hand the load back to the source node rather than deleting it. Return
            // clamps at capacity, so a depleted node cannot be inflated by this.
            if (_node != null) _node.Return(carried);
            carried = 0;
        }

        private static EconomyBalance ResolveBalance()
        {
            BattleContext context = BattleContext.Current;
            if (context != null && context.Balance != null) return context.Balance;
            return ServiceLocator.TryGet(out EconomyBalance found) ? found : null;
        }

        /// <summary>Used by the placement system to find a worker with nothing on its plate.</summary>
        private void OnValidate()
        {
            if (searchRadius < 1f) searchRadius = 1f;
        }
    }
}
