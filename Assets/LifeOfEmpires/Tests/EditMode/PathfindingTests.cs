using System.Collections.Generic;
using System.Diagnostics;
using LifeOfEmpires;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The properties that keep pathfinding from freezing the game: a route exists around obstacles,
    /// an impossible request is refused instead of hanging, the per-frame budget is respected, and a
    /// walled-in destination fails cleanly rather than spinning.
    /// </summary>
    public class PathfindingTests
    {
        private const int GridSize = 16;

        [TearDown]
        public void TearDown() => TestWorld.TearDown();

        private static PathfindingManager Grid(int snapRadius = 4)
        {
            TestWorld.Build(GridSize);
            TestWorld.Pathfinding.goalSnapRadius = snapRadius;
            return TestWorld.Pathfinding;
        }

        private static Vector3 Centre(PathfindingManager grid, int x, int y) => grid.CentreOf(x, y);

        [Test]
        public void Path_IsFoundAroundABlockedWall()
        {
            PathfindingManager grid = Grid();

            // A wall across the middle of the map with a single gap at y = 13.
            for (int y = 0; y < 13; y++) grid.SetBlocked(8, y, true);

            Vector3 from = Centre(grid, 2, 2);
            Vector3 to = Centre(grid, 13, 2);

            PathResult? received = null;
            Assert.IsTrue(grid.RequestPath(from, to, result => received = result, flushImmediately: true));
            Assert.IsTrue(received.HasValue, "a queued request must always complete");

            PathResult result = received.Value;
            Assert.IsTrue(result.Success, $"expected a route around the wall, got {result.Reason}");
            Assert.Greater(result.Points.Length, 1, "the route must have waypoints");

            // Approximate the goal: the snapped goal cell must be the requested one on an open grid.
            Assert.Less(Vector3.Distance(result.Points[result.Points.Length - 1], to), 1.5f, "the route must end at the destination");

            // No waypoint may sit inside the wall.
            foreach (Vector3 point in result.Points)
            {
                Vector2Int cell = grid.CellOf(point);
                Assert.IsFalse(cell.x == 8 && cell.y < 13, $"route crosses the wall at cell {cell}");
            }
        }

        [Test]
        public void StraightLinePath_HasNoRedundantWaypoints()
        {
            PathfindingManager grid = Grid();

            PathResult? received = null;
            grid.RequestPath(Centre(grid, 1, 1), Centre(grid, 10, 1), result => received = result, flushImmediately: true);

            Assert.IsTrue(received.HasValue);
            Assert.IsTrue(received.Value.Success);
            Assert.LessOrEqual(received.Value.Points.Length, 2,
                "a clear straight line must be smoothed to its endpoints, not walked cell by cell");
        }

        [Test]
        public void UnreachableGoal_FailsCleanlyAndDoesNotHang()
        {
            PathfindingManager grid = Grid();

            // Seal a single free cell behind a complete ring of blocked cells.
            for (int y = 4; y <= 6; y++)
            {
                for (int x = 4; x <= 6; x++)
                {
                    if (x == 5 && y == 5) continue;
                    grid.SetBlocked(x, y, true);
                }
            }

            Vector3 sealedGoal = Centre(grid, 5, 5);
            PathResult? received = null;

            var stopwatch = Stopwatch.StartNew();
            bool queued = grid.RequestPath(Centre(grid, 1, 1), sealedGoal, result => received = result, flushImmediately: true);
            stopwatch.Stop();

            Assert.IsTrue(queued, "a sealed but walkable goal is queued, then reported as unreachable");
            Assert.IsTrue(received.HasValue, "the completion delegate must run even when no path exists");
            Assert.IsFalse(received.Value.Success);
            Assert.AreEqual(PathFailureReason.Unreachable, received.Value.Reason);
            Assert.IsEmpty(received.Value.Points, "a failed request must not hand back a partial path");
            Assert.Less(stopwatch.Elapsed.TotalSeconds, 5.0, "an unreachable request must fail promptly, not hang");
            Assert.AreEqual(0, grid.PendingRequestCount, "the queue must drain");
        }

        [Test]
        public void BlockedDestination_WithNoFreeCellNearby_IsRejectedImmediately()
        {
            PathfindingManager grid = Grid(snapRadius: 0);

            grid.SetBlocked(5, 5, true);
            Vector3 blockedGoal = Centre(grid, 5, 5);

            int completions = 0;
            var stopwatch = Stopwatch.StartNew();
            bool queued = grid.RequestPath(Centre(grid, 1, 1), blockedGoal, _ => completions++, flushImmediately: true);
            stopwatch.Stop();

            Assert.IsFalse(queued, "a destination with no standable cell must be refused outright");
            Assert.AreEqual(1, completions, "a refused request must still invoke its completion delegate exactly once");
            Assert.AreEqual(1, grid.TotalRejected);
            Assert.Less(stopwatch.Elapsed.TotalSeconds, 2.0);
        }

        [Test]
        public void BlockedDestination_IsSnappedToTheNearestFreeCell()
        {
            PathfindingManager grid = Grid(snapRadius: 4);

            grid.SetBlocked(5, 5, true);
            PathResult? received = null;

            Assert.IsTrue(grid.RequestPath(Centre(grid, 1, 1), Centre(grid, 5, 5), result => received = result, flushImmediately: true));
            Assert.IsTrue(received.HasValue);
            Assert.IsTrue(received.Value.Success, $"expected a route to the nearest free cell, got {received.Value.Reason}");

            Vector2Int end = grid.CellOf(received.Value.Points[received.Value.Points.Length - 1]);
            Assert.IsFalse(grid.IsBlocked(end.x, end.y), "the route must not end inside an obstacle");
        }

        [Test]
        public void RequestBudget_LimitsHowManyRequestsAreSolvedPerCall()
        {
            PathfindingManager grid = Grid();
            grid.maxPathRequestsPerFrame = 3;

            var completed = new List<PathResult>();
            for (int i = 0; i < 10; i++)
            {
                Vector3 from = Centre(grid, 1 + i, 1);
                Vector3 to = Centre(grid, 14, 14 - i);
                Assert.IsTrue(grid.RequestPath(from, to, result => completed.Add(result)));
            }

            Assert.AreEqual(10, grid.PendingRequestCount);

            Assert.AreEqual(3, grid.ProcessPendingRequests(), "exactly the budget may be solved per call");
            Assert.AreEqual(3, completed.Count);
            Assert.AreEqual(7, grid.PendingRequestCount);

            grid.ProcessPendingRequests();
            grid.ProcessPendingRequests();
            Assert.AreEqual(9, completed.Count);

            grid.ProcessPendingRequests();
            Assert.AreEqual(10, completed.Count, "the whole queue must drain over successive frames");
            Assert.AreEqual(0, grid.PendingRequestCount);
        }

        [Test]
        public void ExpansionCap_FailsTheRequestInsteadOfRunningForever()
        {
            // Corner to corner on an open 32x32 grid: the route itself is 32 cells, so a 16-node cap
            // must abort the search rather than let it expand the whole map.
            TestWorld.Build(32);
            PathfindingManager grid = TestWorld.Pathfinding;
            grid.maxExpandedNodesPerRequest = 16;

            PathResult? received = null;
            Assert.IsTrue(grid.RequestPath(Centre(grid, 0, 0), Centre(grid, 31, 31), result => received = result, flushImmediately: true));

            Assert.IsTrue(received.HasValue, "hitting the cap must still complete the request");
            Assert.IsFalse(received.Value.Success);
            Assert.AreEqual(PathFailureReason.NodeBudgetExceeded, received.Value.Reason);
            Assert.LessOrEqual(grid.LastExpandedNodes, 17, "the cap must actually bound the search");
        }

        [Test]
        public void RepeatedRequestsToTheSameGoal_ReuseTheEarlierExpansion()
        {
            PathfindingManager grid = Grid();

            Vector3 goal = Centre(grid, 14, 14);

            var first = new Stopwatch();
            first.Start();
            PathResult? firstResult = null;
            grid.RequestPath(Centre(grid, 1, 1), goal, result => firstResult = result, flushImmediately: true);
            first.Stop();

            int firstExpansions = grid.LastExpandedNodes;

            PathResult? secondResult = null;
            grid.RequestPath(Centre(grid, 2, 2), goal, result => secondResult = result, flushImmediately: true);

            Assert.IsTrue(firstResult.HasValue && firstResult.Value.Success);
            Assert.IsTrue(secondResult.HasValue && secondResult.Value.Success, "a cached goal must still yield a path");
            Assert.Greater(firstExpansions, 0, "the first search must actually expand something");
            Assert.AreEqual(0, grid.LastExpandedNodes,
                "the second search to the same goal must reconstruct from the cached expansion, expanding nothing");
        }

        [Test]
        public void BlockingChange_InvalidatesCachedExpansions()
        {
            PathfindingManager grid = Grid();
            Vector3 goal = Centre(grid, 14, 14);

            PathResult? before = null;
            grid.RequestPath(Centre(grid, 1, 1), goal, result => before = result, flushImmediately: true);
            Assert.IsTrue(before.HasValue && before.Value.Success);
            Assert.Greater(grid.LastExpandedNodes, 0);

            // Wall off row 14 and the cells around the goal, so the old cached expansion is now wrong.
            for (int x = 0; x < GridSize; x++) grid.SetBlocked(x, 14, true);
            grid.SetBlocked(14, 13, true);
            grid.SetBlocked(13, 14, true);
            grid.SetBlocked(15, 14, true);

            PathResult? after = null;
            grid.RequestPath(Centre(grid, 1, 1), Centre(grid, 14, 14), result => after = result, flushImmediately: true);

            Assert.IsTrue(after.HasValue, "the request must complete");
            Assert.Greater(grid.LastExpandedNodes, 0,
                "a blocking change must force a fresh search rather than reuse the stale expansion");

            if (after.Value.Success)
            {
                foreach (Vector3 point in after.Value.Points)
                {
                    Assert.IsFalse(grid.IsBlocked(point),
                        $"the new route must not pass through a newly blocked cell ({point})");
                }
            }
            else
            {
                Assert.AreEqual(PathFailureReason.Unreachable, after.Value.Reason);
            }
        }

        [Test]
        public void UninitialisedGrid_RefusesRequestsWithAWarning()
        {
            TestWorld.Build();
            var orphan = new GameObject("OrphanPathfinding");
            var grid = orphan.AddComponent<PathfindingManager>();

            List<string> warnings = new List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) => warnings.Add(condition);
            Application.logMessageReceived += handler;

            int completions = 0;
            bool queued;
            try
            {
                queued = grid.RequestPath(Vector3.zero, new Vector3(5f, 0f, 5f), _ => completions++, flushImmediately: true);
            }
            finally
            {
                Application.logMessageReceived -= handler;
                Object.DestroyImmediate(orphan);
            }

            Assert.IsFalse(queued);
            Assert.AreEqual(1, completions);
            Assert.IsNotEmpty(warnings, "an uninitialised grid must warn rather than fail silently");
        }

        [Test]
        public void CornerCutting_IsPreventedButDiagonalsAreStillAllowed()
        {
            PathfindingManager grid = Grid();

            // Two diagonal neighbours meet at a corner; blocking exactly one orthogonal cell must
            // stop the unit cutting across it, but the open diagonal must be walkable.
            grid.SetBlocked(5, 6, true);

            Assert.IsFalse(grid.HasLineOfSight(grid.IndexOf(4, 5), grid.IndexOf(6, 7)),
                "line of sight must not pass through a blocked corner");
            Assert.IsTrue(grid.HasLineOfSight(grid.IndexOf(4, 5), grid.IndexOf(5, 5)),
                "an open straight step is still visible");
        }

        [Test]
        public void Path_EndsAtTheRequestedDestinationOnAnOpenGrid()
        {
            PathfindingManager grid = Grid();
            Vector3 from = Centre(grid, 0, 0);
            Vector3 to = Centre(grid, 15, 15);

            PathResult? received = null;
            grid.RequestPath(from, to, result => received = result, flushImmediately: true);

            Assert.IsTrue(received.HasValue && received.Value.Success);
            Vector3[] points = received.Value.Points;
            Assert.Less(Vector3.Distance(points[0], from), 1.5f, "the route must start at the unit");
            Assert.Less(Vector3.Distance(points[points.Length - 1], to), 0.01f, "the route must end exactly at the goal cell centre");
        }

        [Test]
        public void SameStartAndGoal_YieldsASinglePointRatherThanFailing()
        {
            PathfindingManager grid = Grid();
            Vector3 point = Centre(grid, 7, 7);

            PathResult? received = null;
            grid.RequestPath(point, point, result => received = result, flushImmediately: true);

            Assert.IsTrue(received.HasValue);
            Assert.IsTrue(received.Value.Success);
            Assert.AreEqual(1, received.Value.Points.Length);
        }

        [Test]
        public void CancelAll_FailsEveryQueuedRequest()
        {
            PathfindingManager grid = Grid();

            var completed = new List<PathResult>();
            for (int i = 0; i < 4; i++)
            {
                grid.RequestPath(Centre(grid, 1, 1 + i), Centre(grid, 14, 14 - i), result => completed.Add(result));
            }

            grid.CancelAll();

            Assert.AreEqual(0, grid.PendingRequestCount);
            Assert.AreEqual(4, completed.Count, "cancelled requests must still be reported to their callers");
            foreach (PathResult result in completed)
            {
                Assert.IsFalse(result.Success);
                Assert.AreEqual(PathFailureReason.Cancelled, result.Reason);
            }
        }
    }
}
