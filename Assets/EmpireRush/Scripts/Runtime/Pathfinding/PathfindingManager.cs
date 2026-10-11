using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireRush
{
    /// <summary>Why a path request produced no usable path.</summary>
    public enum PathFailureReason
    {
        None = 0,

        /// <summary>No grid exists yet.</summary>
        NoGrid = 1,

        /// <summary>The destination is off-grid or blocked, and no walkable cell was found near it.</summary>
        GoalBlocked = 2,

        /// <summary>A walkable start and goal exist, but nothing connects them.</summary>
        Unreachable = 3,

        /// <summary>A* hit <see cref="PathfindingManager.maxExpandedNodesPerRequest"/> without finishing.</summary>
        NodeBudgetExceeded = 4,

        /// <summary>The battle ended and the queued request was dropped.</summary>
        Cancelled = 5
    }

    /// <summary>Outcome of one path request, delivered to the caller's completion delegate.</summary>
    public struct PathResult
    {
        public bool Success;

        /// <summary>World-space waypoints from the start cell to the destination. Empty on failure.</summary>
        public Vector3[] Points;

        public PathFailureReason Reason;

        /// <summary>The destination the caller asked for, which may differ from the snapped goal cell.</summary>
        public Vector3 RequestedDestination;

        public static PathResult Failed(PathFailureReason reason, Vector3 requested) => new PathResult
        {
            Success = false,
            Points = Array.Empty<Vector3>(),
            Reason = reason,
            RequestedDestination = requested
        };
    }

    /// <summary>
    /// Grid A* over a flat <c>bool[] blocked</c> array, 8-way with corner-cutting prevention.
    ///
    /// Three properties make this safe to run for an entire army:
    /// <list type="bullet">
    /// <item>Requests are queued and only <see cref="maxPathRequestsPerFrame"/> are solved per frame,
    /// so ordering fifty units across the map cannot resolve fifty searches in one frame.</item>
    /// <item>Every search is capped at <see cref="maxExpandedNodesPerRequest"/> expansions; a search
    /// that hits the cap fails and reports rather than eating the frame.</item>
    /// <item>A failed search always invokes its completion delegate with a
    /// <see cref="PathFailureReason"/>. Nothing waits forever for a path that will never arrive and
    /// nothing loops unbounded — the open set either reaches the goal or empties.</item>
    /// </list>
    ///
    /// A per-goal expansion cache lets a later unit ordered to the same place reconstruct its route
    /// from the first search's came-from table instead of expanding anything — the common RTS case,
    /// where an army is sent to one point. Reuse is restricted to start cells the earlier search
    /// actually closed, so a reused route is as optimal as a fresh search would have been.
    /// </summary>
    public class PathfindingManager : MonoBehaviour
    {
        [Header("Grid")]
        [Min(1)] public int width = 64;
        [Min(1)] public int height = 64;
        [Min(0.1f)] public float cellSize = 1f;

        [Header("Budget")]
        [Tooltip("Path requests solved per frame. The remainder stays queued for later frames.")]
        [Min(1)] public int maxPathRequestsPerFrame = 4;

        [Tooltip("Hard ceiling on A* expansions per request. Reaching it fails the request instead of stalling the frame.")]
        [Min(16)] public int maxExpandedNodesPerRequest = 4000;

        [Tooltip("How many cells out from a blocked destination to look for a standable cell.")]
        [Min(0)] public int goalSnapRadius = 4;

        [Tooltip("Distinct goal cells kept in the expansion cache. Exceeding it clears the cache.")]
        [Min(1)] public int maxCachedGoals = 64;

        [Header("Diagnostics")]
        [SerializeField] private int pendingCount;
        [SerializeField] private int totalServed;
        [SerializeField] private int totalRejected;
        [SerializeField] private int lastExpandedNodes;

        private const int CostStraight = 10;
        private const int CostDiagonal = 14;

        private bool[] _blocked = Array.Empty<bool>();
        private float[] _terrainCost = Array.Empty<float>();
        private Vector3 _origin;
        private bool _initialised;

        /// <summary>Bumped by every blocking change; invalidates cached expansions wholesale.</summary>
        private int _gridVersion;

        private readonly Queue<PendingRequest> _queue = new Queue<PendingRequest>(64);
        private readonly Dictionary<int, ExpansionCache> _cache = new Dictionary<int, ExpansionCache>(64);
        private readonly IntMinHeap _open = new IntMinHeap(1024);

        // Scratch buffers reused across searches, so a search allocates no working memory.
        private int[] _gScore = Array.Empty<int>();
        private int[] _cameFrom = Array.Empty<int>();
        private bool[] _closed = Array.Empty<bool>();
        private int[] _visitStamp = Array.Empty<int>();
        private int _stamp;

        private struct PendingRequest
        {
            public int StartIndex;
            public int GoalIndex;
            public Vector3 RequestedDestination;
            public Action<PathResult> OnComplete;
        }

        private sealed class ExpansionCache
        {
            public int Version;
            public int[] GScore;
            public int[] CameFrom;
            public bool[] Closed;
            public int[] VisitStamp;
            public int Stamp;
        }

        public bool IsInitialised => _initialised;
        public int PendingRequestCount => _queue.Count;
        public int TotalServed => totalServed;
        public int TotalRejected => totalRejected;

        /// <summary>Expansions performed by the most recent search, for budgeting diagnostics.</summary>
        public int LastExpandedNodes => lastExpandedNodes;

        public int CellCount => width * height;

        private void Awake()
        {
            if (!_initialised) Initialize(width, height, cellSize, transform.position);
        }

        private void Update()
        {
            ProcessPendingRequests();
            pendingCount = _queue.Count;
        }

        // -----------------------------------------------------------------------------------------
        // Grid construction
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Build (or rebuild) the grid. <paramref name="origin"/> is the world position of the corner
        /// of cell (0,0); cell centres sit half a cell in from it.
        /// </summary>
        public void Initialize(int gridWidth, int gridHeight, float cell, Vector3 origin)
        {
            width = Mathf.Max(1, gridWidth);
            height = Mathf.Max(1, gridHeight);
            cellSize = Mathf.Max(0.1f, cell);
            _origin = origin;

            int count = width * height;
            _blocked = new bool[count];
            _terrainCost = new float[count];
            for (int i = 0; i < count; i++) _terrainCost[i] = 1f;

            _gScore = new int[count];
            _cameFrom = new int[count];
            _closed = new bool[count];
            _visitStamp = new int[count];

            _cache.Clear();
            _queue.Clear();
            _gridVersion++;
            _stamp = 0;
            _initialised = true;
        }

        // -----------------------------------------------------------------------------------------
        // Coordinates
        // -----------------------------------------------------------------------------------------

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

        public int IndexOf(int x, int y) => (y * width) + x;

        public Vector2Int CellOf(Vector3 world)
        {
            Vector3 local = world - _origin;
            return new Vector2Int(
                Mathf.FloorToInt(local.x / cellSize),
                Mathf.FloorToInt(local.z / cellSize));
        }

        public Vector3 CentreOf(int x, int y)
            => _origin + new Vector3((x + 0.5f) * cellSize, 0f, (y + 0.5f) * cellSize);

        public Vector3 CentreOf(Vector2Int cell) => CentreOf(cell.x, cell.y);

        // -----------------------------------------------------------------------------------------
        // Blocking
        // -----------------------------------------------------------------------------------------

        /// <summary>Out-of-bounds and uninitialised cells count as blocked, so callers cannot walk off the map.</summary>
        public bool IsBlocked(int x, int y)
            => !_initialised || !InBounds(x, y) || _blocked[IndexOf(x, y)];

        public bool IsBlocked(Vector3 world)
        {
            Vector2Int cell = CellOf(world);
            return IsBlocked(cell.x, cell.y);
        }

        public void SetBlocked(int x, int y, bool blocked)
        {
            if (!_initialised || !InBounds(x, y)) return;
            int index = IndexOf(x, y);
            if (_blocked[index] == blocked) return;
            _blocked[index] = blocked;
            Invalidate();
        }

        public void SetBlocked(Vector3 world, bool blocked)
        {
            Vector2Int cell = CellOf(world);
            SetBlocked(cell.x, cell.y, blocked);
        }

        /// <summary>Mark a rectangular footprint. Used by building placement and terrain carving.</summary>
        public void SetBlockedRect(int x, int y, int rectWidth, int rectHeight, bool blocked)
        {
            if (!_initialised) return;
            bool changed = false;
            for (int yy = y; yy < y + rectHeight; yy++)
            {
                for (int xx = x; xx < x + rectWidth; xx++)
                {
                    if (!InBounds(xx, yy)) continue;
                    int index = IndexOf(xx, yy);
                    if (_blocked[index] == blocked) continue;
                    _blocked[index] = blocked;
                    changed = true;
                }
            }

            if (changed) Invalidate();
        }

        /// <summary>Terrain movement multiplier per cell. 1 is flat ground; higher is slower.</summary>
        public void SetTerrainCost(int x, int y, float cost)
        {
            if (!_initialised || !InBounds(x, y)) return;
            _terrainCost[IndexOf(x, y)] = Mathf.Max(0.01f, cost);
        }

        /// <summary>Terrain multiplier at a cell. Out-of-bounds reads as a wall.</summary>
        public float GetTerrainCost(int x, int y)
            => !_initialised || !InBounds(x, y) ? float.MaxValue : _terrainCost[IndexOf(x, y)];

        public void ClearCache() => _cache.Clear();

        private void Invalidate()
        {
            _gridVersion++;
            _cache.Clear();
        }

        // -----------------------------------------------------------------------------------------
        // Requests
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Queue a path request. Returns false — with a warning — only when the destination cannot
        /// possibly be served: no grid, or a destination that is off-grid/blocked with no walkable
        /// cell within <see cref="goalSnapRadius"/>. Once queued, the completion delegate is
        /// guaranteed to run exactly once, carrying a <see cref="PathFailureReason"/> if the path
        /// turns out to be impossible.
        /// </summary>
        public bool RequestPath(Vector3 from, Vector3 to, Action<PathResult> onComplete)
            => RequestPath(from, to, onComplete, false);

        /// <summary>
        /// Queue a path request. <paramref name="flushImmediately"/> solves it inside this call,
        /// which tests and one-shot callers use; gameplay leaves it false so the frame budget applies.
        /// </summary>
        public bool RequestPath(Vector3 from, Vector3 to, Action<PathResult> onComplete, bool flushImmediately)
        {
            if (!_initialised)
            {
                Debug.LogWarning("PathfindingManager: RequestPath ignored — the grid has not been initialised.");
                Fail(onComplete, PathFailureReason.NoGrid, to);
                totalRejected++;
                return false;
            }

            Vector2Int startCell = CellOf(from);
            Vector2Int goalCell = CellOf(to);

            if (!TryResolveWalkable(ref startCell) || !TryResolveWalkable(ref goalCell))
            {
                Debug.LogWarning($"PathfindingManager: unreachable request {from} -> {to} — no walkable cell within {goalSnapRadius} cells of the start or the destination.");
                Fail(onComplete, PathFailureReason.GoalBlocked, to);
                totalRejected++;
                return false;
            }

            _queue.Enqueue(new PendingRequest
            {
                StartIndex = IndexOf(startCell.x, startCell.y),
                GoalIndex = IndexOf(goalCell.x, goalCell.y),
                RequestedDestination = to,
                OnComplete = onComplete
            });

            pendingCount = _queue.Count;
            if (flushImmediately) ProcessPendingRequests();

            return true;
        }

        /// <summary>
        /// Solve up to <see cref="maxPathRequestsPerFrame"/> queued requests and return how many
        /// were solved. Called from Update; called directly by tests so budgeting is deterministic.
        /// </summary>
        public int ProcessPendingRequests()
        {
            int budget = Mathf.Max(1, maxPathRequestsPerFrame);
            int solved = 0;

            while (solved < budget && _queue.Count > 0)
            {
                PendingRequest request = _queue.Dequeue();
                solved++;

                PathResult result = Solve(request);
                if (result.Success) totalServed++;
                else totalRejected++;
                request.OnComplete?.Invoke(result);
            }

            pendingCount = _queue.Count;
            return solved;
        }

        /// <summary>Fail every queued request without solving it. Used when a battle ends.</summary>
        public void CancelAll()
        {
            while (_queue.Count > 0)
            {
                PendingRequest request = _queue.Dequeue();
                Fail(request.OnComplete, PathFailureReason.Cancelled, request.RequestedDestination);
            }

            pendingCount = 0;
        }

        private static void Fail(Action<PathResult> onComplete, PathFailureReason reason, Vector3 requested)
            => onComplete?.Invoke(PathResult.Failed(reason, requested));

        /// <summary>
        /// Snap a cell onto the nearest walkable cell, spiralling outward by Chebyshev radius, so a
        /// tap on a tree or a building still produces a usable order.
        /// </summary>
        private bool TryResolveWalkable(ref Vector2Int cell)
        {
            if (InBounds(cell.x, cell.y) && !_blocked[IndexOf(cell.x, cell.y)]) return true;

            for (int radius = 1; radius <= goalSnapRadius; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        // Only the ring at this radius; earlier radii covered the interior.
                        if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius) continue;

                        int x = cell.x + dx;
                        int y = cell.y + dy;
                        if (!InBounds(x, y) || _blocked[IndexOf(x, y)]) continue;

                        cell = new Vector2Int(x, y);
                        return true;
                    }
                }
            }

            return false;
        }

        // -----------------------------------------------------------------------------------------
        // Search
        // -----------------------------------------------------------------------------------------

        private PathResult Solve(PendingRequest request)
        {
            lastExpandedNodes = 0;

            if (request.StartIndex == request.GoalIndex)
            {
                return new PathResult
                {
                    Success = true,
                    Points = new[] { CentreOf(request.GoalIndex % width, request.GoalIndex / width) },
                    Reason = PathFailureReason.None,
                    RequestedDestination = request.RequestedDestination
                };
            }

            if (TryReuseCache(request.StartIndex, request.GoalIndex, out List<int> cached))
            {
                return BuildResult(cached, request);
            }

            if (TrySearch(request.StartIndex, request.GoalIndex, out List<int> nodes, out PathFailureReason reason))
            {
                return BuildResult(nodes, request);
            }

            Debug.LogWarning($"PathfindingManager: no path from cell {request.StartIndex} to cell {request.GoalIndex} ({reason}) after {lastExpandedNodes} expansions.");
            return PathResult.Failed(reason, request.RequestedDestination);
        }

        private PathResult BuildResult(List<int> cellPath, PendingRequest request) => new PathResult
        {
            Success = true,
            Points = Simplify(cellPath),
            Reason = PathFailureReason.None,
            RequestedDestination = request.RequestedDestination
        };

        /// <summary>
        /// A* with a binary-heap open set and lazy deletion. Returns the cell-index path from start
        /// to goal, or a failure reason. Never loops unbounded: either the goal is closed, the open
        /// set empties (unreachable), or the expansion cap is hit.
        /// </summary>
        private bool TrySearch(int startIndex, int goalIndex, out List<int> path, out PathFailureReason reason)
        {
            path = null;
            reason = PathFailureReason.Unreachable;

            _stamp++;
            _open.Clear();

            _gScore[startIndex] = 0;
            _cameFrom[startIndex] = -1;
            _closed[startIndex] = false;
            _visitStamp[startIndex] = _stamp;
            _open.Push(startIndex, Heuristic(startIndex, goalIndex));

            int expansions = 0;
            int nodeCap = Mathf.Max(16, maxExpandedNodesPerRequest);

            while (_open.TryPop(out int current, out int poppedScore))
            {
                if (_visitStamp[current] != _stamp) continue;
                if (_closed[current]) continue;
                // The heap orders by f = g + h, so staleness is judged against the node's f-score.
                // Comparing against g alone would discard every node that is not the goal, because
                // h is positive everywhere else and the search would end on its first pop.
                if (poppedScore > _gScore[current] + Heuristic(current, goalIndex)) continue; // stale duplicate heap entry

                if (current == goalIndex)
                {
                    lastExpandedNodes = expansions;
                    path = Reconstruct(current);
                    reason = PathFailureReason.None;
                    StoreCache(goalIndex);
                    return true;
                }

                _closed[current] = true;
                expansions++;

                if (expansions >= nodeCap)
                {
                    lastExpandedNodes = expansions;
                    reason = PathFailureReason.NodeBudgetExceeded;
                    return false;
                }

                int cx = current % width;
                int cy = current / width;
                int currentG = _gScore[current];

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if (ny < 0 || ny >= height) continue;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        int nx = cx + dx;
                        if (nx < 0 || nx >= width) continue;

                        int neighbour = IndexOf(nx, ny);
                        if (_blocked[neighbour]) continue;

                        // Stale _closed entries from earlier searches must not be read: a cell only
                        // counts as closed when the current search visited it.
                        bool visitedThisSearch = _visitStamp[neighbour] == _stamp;
                        if (visitedThisSearch && _closed[neighbour]) continue;

                        bool diagonal = dx != 0 && dy != 0;
                        if (diagonal)
                        {
                            // Corner-cutting prevention: both orthogonal cells must be open, or the
                            // unit would clip through the corner of a building.
                            if (_blocked[IndexOf(cx + dx, cy)]) continue;
                            if (_blocked[IndexOf(cx, cy + dy)]) continue;
                        }

                        int step = diagonal ? CostDiagonal : CostStraight;
                        int tentative = currentG + Mathf.Max(1, Mathf.RoundToInt(step * _terrainCost[neighbour]));

                        if (visitedThisSearch && tentative >= _gScore[neighbour]) continue;

                        _visitStamp[neighbour] = _stamp;
                        _gScore[neighbour] = tentative;
                        _cameFrom[neighbour] = current;
                        _open.Push(neighbour, tentative + Heuristic(neighbour, goalIndex));
                    }
                }
            }

            lastExpandedNodes = expansions;
            reason = PathFailureReason.Unreachable;
            return false;
        }

        private int Heuristic(int fromIndex, int goalIndex)
        {
            int fx = fromIndex % width;
            int fy = fromIndex / width;
            int gx = goalIndex % width;
            int gy = goalIndex / width;

            // Octile distance matching the 10/14 step costs, so the heuristic stays admissible.
            int dx = Mathf.Abs(fx - gx);
            int dy = Mathf.Abs(fy - gy);
            int diagonal = Mathf.Min(dx, dy);
            int straight = Mathf.Max(dx, dy) - diagonal;
            return (diagonal * CostDiagonal) + (straight * CostStraight);
        }

        private List<int> Reconstruct(int goalIndex)
        {
            List<int> path = new List<int>(32);
            int current = goalIndex;
            int guard = CellCount + 1;
            while (current != -1 && guard-- > 0)
            {
                path.Add(current);
                current = _cameFrom[current];
            }

            path.Reverse();
            return path;
        }

        // -----------------------------------------------------------------------------------------
        // Per-goal expansion cache
        // -----------------------------------------------------------------------------------------

        private void StoreCache(int goalIndex)
        {
            if (_cache.Count >= maxCachedGoals) _cache.Clear();

            int count = CellCount;
            ExpansionCache cache = new ExpansionCache
            {
                Version = _gridVersion,
                GScore = new int[count],
                CameFrom = new int[count],
                Closed = new bool[count],
                VisitStamp = new int[count],
                Stamp = _stamp
            };

            Array.Copy(_gScore, cache.GScore, count);
            Array.Copy(_cameFrom, cache.CameFrom, count);
            Array.Copy(_closed, cache.Closed, count);
            Array.Copy(_visitStamp, cache.VisitStamp, count);
            _cache[goalIndex] = cache;
        }

        /// <summary>
        /// Try to route from <paramref name="startIndex"/> using a cached expansion to the same goal.
        /// Only start cells that the earlier search actually closed are accepted: for a closed cell
        /// the came-from chain is the optimal route, whereas a merely relaxed cell might hold a
        /// tentative g-score the search never finalised.
        /// </summary>
        private bool TryReuseCache(int startIndex, int goalIndex, out List<int> path)
        {
            path = null;
            if (!_cache.TryGetValue(goalIndex, out ExpansionCache cache)) return false;
            if (cache.Version != _gridVersion) return false;
            if (cache.VisitStamp[startIndex] != cache.Stamp || !cache.Closed[startIndex]) return false;

            // Came-from points at a node's parent, so the route has to be recovered from the goal
            // end and reversed. Walking it forwards from the new start would head away from the
            // goal and dead-end at the earlier search's start cell.
            var reversed = new List<int>(32);
            int current = goalIndex;
            int guard = CellCount + 1;
            while (current != startIndex)
            {
                if (guard-- <= 0) return false;
                reversed.Add(current);
                int next = cache.CameFrom[current];
                if (next == -1 || next == current) return false; // no cached route through the new start
                current = next;
            }

            reversed.Add(startIndex);
            reversed.Reverse();
            path = reversed;
            return true;
        }

        // -----------------------------------------------------------------------------------------
        // Path smoothing
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Collapse a cell path to corner waypoints using grid line-of-sight, so units walk straight
        /// lines instead of hugging the grid. Always keeps the first and last cell.
        /// </summary>
        private Vector3[] Simplify(List<int> cellPath)
        {
            if (cellPath.Count <= 1)
            {
                int only = cellPath[0];
                return new[] { CentreOf(only % width, only / width) };
            }

            List<int> kept = new List<int>(cellPath.Count) { cellPath[0] };
            int anchor = 0;
            for (int i = 2; i < cellPath.Count; i++)
            {
                if (HasLineOfSight(cellPath[anchor], cellPath[i])) continue;
                kept.Add(cellPath[i - 1]);
                anchor = i - 1;
            }

            kept.Add(cellPath[cellPath.Count - 1]);

            Vector3[] points = new Vector3[kept.Count];
            for (int i = 0; i < kept.Count; i++) points[i] = CentreOf(kept[i] % width, kept[i] / width);
            return points;
        }

        /// <summary>Grid line-of-sight: true when every cell the segment crosses is walkable.</summary>
        public bool HasLineOfSight(int fromIndex, int toIndex)
        {
            int x = fromIndex % width;
            int y = fromIndex / width;
            int x1 = toIndex % width;
            int y1 = toIndex / width;

            int dx = Mathf.Abs(x1 - x);
            int dy = Mathf.Abs(y1 - y);
            int sx = x < x1 ? 1 : -1;
            int sy = y < y1 ? 1 : -1;
            int error = dx - dy;
            int guard = dx + dy + 2;

            while (guard-- > 0)
            {
                if (IsBlocked(x, y)) return false;
                if (x == x1 && y == y1) return true;

                int e2 = 2 * error;
                int px = x;
                int py = y;
                if (e2 > -dy)
                {
                    error -= dy;
                    x += sx;
                }

                if (e2 < dx)
                {
                    error += dx;
                    y += sy;
                }

                // Diagonal step: both orthogonal cells must be open, mirroring the search.
                if (x != px && y != py && (IsBlocked(x, py) || IsBlocked(px, y))) return false;
            }

            return false;
        }

        /// <summary>Lazy binary min-heap over (node, score) pairs. Stale entries are skipped on pop.</summary>
        private sealed class IntMinHeap
        {
            private int[] _node;
            private int[] _score;
            private int _count;

            public IntMinHeap(int capacity)
            {
                capacity = Mathf.Max(16, capacity);
                _node = new int[capacity];
                _score = new int[capacity];
                _count = 0;
            }

            public int Count => _count;

            public void Clear() => _count = 0;

            public void Push(int node, int score)
            {
                if (_count == _node.Length) Grow();
                int index = _count++;
                _node[index] = node;
                _score[index] = score;
                SiftUp(index);
            }

            public bool TryPop(out int node, out int score)
            {
                if (_count == 0)
                {
                    node = -1;
                    score = 0;
                    return false;
                }

                node = _node[0];
                score = _score[0];
                _count--;
                if (_count > 0)
                {
                    _node[0] = _node[_count];
                    _score[0] = _score[_count];
                    SiftDown(0);
                }

                return true;
            }

            private void Grow()
            {
                int capacity = _node.Length * 2;
                Array.Resize(ref _node, capacity);
                Array.Resize(ref _score, capacity);
            }

            private void SiftUp(int index)
            {
                int node = _node[index];
                int score = _score[index];
                while (index > 0)
                {
                    int parent = (index - 1) >> 1;
                    if (_score[parent] <= score) break;
                    _node[index] = _node[parent];
                    _score[index] = _score[parent];
                    index = parent;
                }

                _node[index] = node;
                _score[index] = score;
            }

            private void SiftDown(int index)
            {
                int node = _node[index];
                int score = _score[index];
                int half = _count >> 1;

                while (index < half)
                {
                    int child = (index << 1) + 1;
                    int right = child + 1;
                    if (right < _count && _score[right] < _score[child]) child = right;
                    if (_score[child] >= score) break;
                    _node[index] = _node[child];
                    _score[index] = _score[child];
                    index = child;
                }

                _node[index] = node;
                _score[index] = score;
            }
        }

        private void OnDestroy() => CancelAll();
    }
}
