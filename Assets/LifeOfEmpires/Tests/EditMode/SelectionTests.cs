using System.Collections.Generic;
using LifeOfEmpires;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// Selection behaviour at the touch layer: select-all picks up only fighters, a drag box honours
    /// the type filter it was given, and a tap lands on a small unit even when the finger is off by
    /// tens of pixels.
    /// </summary>
    public class SelectionTests
    {
        private UnitSelectionManager _selection;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            TestWorld.Build(gridSize: 64, origin: Vector3.zero);

            _camera = TestWorld.CreateCamera(new Vector3(0f, 30f, 0f), new Vector3(90f, 0f, 0f), orthographicSize: 12f);
            _camera.aspect = 800f / 600f;

            var go = TestWorld.Track(new GameObject("Selection"));
            _selection = go.AddComponent<UnitSelectionManager>();
            _selection.SelectionCamera = _camera;
        }

        [TearDown]
        public void TearDown() => TestWorld.TearDown();

        [Test]
        public void SelectAllCombatUnits_ReturnsOnlyCombatUnits()
        {
            TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(-4f, 0f, -4f));
            TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(-2f, 0f, -4f));
            TestWorld.CreateUnit(TestWorld.WorkerUnitId, Team.Player, new Vector3(4f, 0f, 4f));
            TestWorld.CreateUnit(TestWorld.WorkerUnitId, Team.Player, new Vector3(6f, 0f, 4f));

            int count = _selection.SelectAllCombatUnits();

            Assert.AreEqual(2, count, "only the two fighters may be selected");
            foreach (UnitController unit in _selection.Selection)
            {
                Assert.IsTrue(unit.IsCombatUnit, $"{unit.name} is not a combat unit and must not be in an army selection");
                Assert.AreEqual(TestWorld.CombatUnitId, unit.Definition.id);
            }

            Assert.AreEqual(4, _selection.SelectAll());
            Assert.AreEqual(4, _selection.SelectionCount, "select-all takes workers too");
        }

        [Test]
        public void DragBox_RespectsTheTypeFilter()
        {
            UnitController fighterA = TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(-6f, 0f, -6f));
            UnitController fighterB = TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(-4f, 0f, -4f));
            UnitController workerA = TestWorld.CreateUnit(TestWorld.WorkerUnitId, Team.Player, new Vector3(4f, 0f, 4f));
            UnitController workerB = TestWorld.CreateUnit(TestWorld.WorkerUnitId, Team.Player, new Vector3(6f, 0f, 6f));

            Rect all = EnclosingRect(fighterA, fighterB, workerA, workerB);

            Assert.AreEqual(2, _selection.SelectUnitsInScreenRect(all, UnitFilter.CombatOnly));
            CollectionAssert.AreEquivalent(
                new[] { fighterA, fighterB },
                new List<UnitController>(_selection.Selection),
                "the combat filter must exclude the workers");

            Assert.AreEqual(2, _selection.SelectUnitsInScreenRect(all, UnitFilter.WorkersOnly));
            CollectionAssert.AreEquivalent(
                new[] { workerA, workerB },
                new List<UnitController>(_selection.Selection),
                "the worker filter must exclude the fighters");

            Assert.AreEqual(4, _selection.SelectUnitsInScreenRect(all, UnitFilter.All));
        }

        [Test]
        public void DragBox_ExcludesUnitsOutsideTheRectangle()
        {
            UnitController inside = TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(-2f, 0f, -2f));
            TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(12f, 0f, 12f));

            Vector2 projected = _camera.WorldToScreenPoint(inside.transform.position);
            var tightRect = new Rect(projected.x - 20f, projected.y - 20f, 40f, 40f);

            var results = new List<UnitController>();
            _selection.CollectUnitsInScreenRect(tightRect, UnitFilter.All, results);

            CollectionAssert.AreEqual(new[] { inside }, results,
                "a unit outside the rectangle must not be collected");
        }

        [Test]
        public void Tap_SelectsATinyUnitEvenWhenTheFingerIsOffCentre()
        {
            UnitController unit = TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(1f, 0f, 1f));
            Vector2 projected = _camera.WorldToScreenPoint(unit.transform.position);

            // 30 px off centre, still inside the generous tap radius.
            Vector2 offsetTap = projected + new Vector2(30f, 0f);
            Assert.IsTrue(_selection.TrySelectAtScreenPoint(offsetTap), "a 30 px miss must still hit the unit");
            Assert.AreEqual(1, _selection.SelectionCount);
            Assert.AreSame(unit, _selection.Selection[0]);
            Assert.IsTrue(unit.IsSelected);
        }

        [Test]
        public void Tap_FarFromEverything_SelectsNothing()
        {
            TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(1f, 0f, 1f));
            Vector2 projected = _camera.WorldToScreenPoint(new Vector3(1f, 0f, 1f));

            Vector2 farTap = projected + new Vector2(400f, 0f);
            Assert.IsFalse(_selection.TrySelectAtScreenPoint(farTap), "a tap 400 px away must not select anything");
            Assert.AreEqual(0, _selection.SelectionCount);
        }

        [Test]
        public void DoubleTap_SelectsEveryNearbyUnitOfTheSameType()
        {
            UnitController fighterA = TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(0f, 0f, 0f));
            UnitController fighterB = TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(2f, 0f, 0f));
            UnitController worker = TestWorld.CreateUnit(TestWorld.WorkerUnitId, Team.Player, new Vector3(0f, 0f, 2f));

            Vector2 projected = _camera.WorldToScreenPoint(fighterA.transform.position);

            Assert.IsTrue(_selection.TrySelectAtScreenPoint(projected));
            Assert.AreEqual(1, _selection.SelectionCount);

            // Second tap inside the double-tap window (scaled time does not advance within a frame).
            Assert.IsTrue(_selection.TrySelectAtScreenPoint(projected));

            Assert.AreEqual(2, _selection.SelectionCount, "the double tap must take both swordsmen");
            CollectionAssert.AreEquivalent(new[] { fighterA, fighterB }, new List<UnitController>(_selection.Selection));
            CollectionAssert.DoesNotContain(new List<UnitController>(_selection.Selection), worker,
                "a double tap must not pull in a different unit type");
        }

        [Test]
        public void DragBox_SelectsNothingWhenTheCameraIsMissing()
        {
            TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(0f, 0f, 0f));
            _selection.SelectionCamera = null;
            Object.DestroyImmediate(_camera.gameObject);

            var results = new List<UnitController>();
            _selection.CollectUnitsInScreenRect(new Rect(0f, 0f, 800f, 600f), UnitFilter.All, results);

            Assert.IsEmpty(results, "without a camera nothing may be selected by accident");
        }

        [Test]
        public void ClearingSelection_DeselectsEveryUnit()
        {
            UnitController unit = TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(0f, 0f, 0f));
            _selection.SelectAllCombatUnits();
            Assert.IsTrue(unit.IsSelected);

            _selection.ClearSelection();

            Assert.AreEqual(0, _selection.SelectionCount);
            Assert.IsFalse(unit.IsSelected, "clearing must deselect the unit itself, not just forget it");
        }

        private Rect EnclosingRect(params UnitController[] units)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (UnitController unit in units)
            {
                Vector3 projected = _camera.WorldToScreenPoint(unit.transform.position);
                minX = Mathf.Min(minX, projected.x);
                maxX = Mathf.Max(maxX, projected.x);
                minY = Mathf.Min(minY, projected.y);
                maxY = Mathf.Max(maxY, projected.y);
            }

            return new Rect(minX - 10f, minY - 10f, (maxX - minX) + 20f, (maxY - minY) + 20f);
        }
    }
}
