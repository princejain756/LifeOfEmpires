using System.Reflection;
using LifeOfEmpires;
using LifeOfEmpires.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// Where a tap on the player's own building goes.
    ///
    /// One finger can only mean one thing, so the HUD's building panel and the army's move order must
    /// not both claim the same tap: tapping a building opens its panel and issues no command, and
    /// having an army selected must never lock the train buttons away. Both halves are exercised here
    /// against the real <see cref="BattleInputController"/> and the real <see cref="BattleHud"/>, on a
    /// real camera and a real <see cref="BattleContext"/>.
    /// </summary>
    public class TapRoutingTests
    {
        private Camera _camera;
        private IsometricCameraRig _rig;
        private UnitSelectionManager _selection;
        private BattleInputController _input;

        [SetUp]
        public void SetUp()
        {
            TestWorld.Build(gridSize: 32);

            _camera = TestWorld.CreateCamera(new Vector3(0f, 60f, 0f), new Vector3(90f, 0f, 0f), orthographicSize: 14f);
            _camera.aspect = 800f / 600f;

            var rigGo = TestWorld.Track(new GameObject("Rig"));
            _rig = rigGo.AddComponent<IsometricCameraRig>();
            _camera.transform.SetParent(_rig.transform, false);
            _rig.pitch = 90f;
            _rig.yaw = 0f;
            _rig.ApplyConfiguration();
            _camera.aspect = 800f / 600f;
            ServiceLocator.Register(_rig);

            var selectionGo = TestWorld.Track(new GameObject("Selection"));
            _selection = selectionGo.AddComponent<UnitSelectionManager>();
            _selection.SelectionCamera = _camera;
            ServiceLocator.Register(_selection);

            var inputGo = TestWorld.Track(new GameObject("Input"));
            _input = inputGo.AddComponent<BattleInputController>();
            _input.selection = _selection;
            _input.cameraRig = _rig;
            _input.placement = null;
        }

        [TearDown]
        public void TearDown() => TestWorld.TearDown();

        [Test]
        public void TapOnAPlayerBuilding_WithAnArmySelected_OpensNoMoveOrder()
        {
            (UnitController unit, BuildingController barracks, Vector2 buildingPoint) = SelectArmyAndBuildBarracks();

            InvokePrivate(_input, "HandleTap", buildingPoint);

            Assert.IsFalse(unit.IsMoving,
                "the tap belongs to the building panel: the selected army must not be sent to the building");
        }

        [Test]
        public void TapOnEmptyGround_WithAnArmySelected_StillOrdersTheMove()
        {
            (UnitController unit, BuildingController barracks, Vector2 _) = SelectArmyAndBuildBarracks();

            // Roughly eight metres from both the army and the building: far outside any tap radius.
            Vector2 emptyGround = _camera.WorldToScreenPoint(new Vector3(2f, 0f, 2f));
            Assert.IsNull(BattleContext.FindPlayerBuildingNearScreenPoint(_camera, emptyGround, _selection.tapScreenRadius),
                "precondition: the empty-ground point must not pick up the barracks");

            InvokePrivate(_input, "HandleTap", emptyGround);

            Assert.IsTrue(unit.IsMoving,
                "a tap on open ground is still a move order — the building rule must not swallow every tap");
        }

        [Test]
        public void BuildingPick_RespectsTheTapRadius()
        {
            (UnitController _, BuildingController barracks, Vector2 buildingPoint) = SelectArmyAndBuildBarracks();

            BuildingController picked = BattleContext.FindPlayerBuildingNearScreenPoint(_camera, buildingPoint, _selection.tapScreenRadius);
            Assert.AreSame(barracks, picked, "a tap on the building must pick it out");

            Vector2 faraway = _camera.WorldToScreenPoint(new Vector3(10f, 0f, 30f));
            Assert.IsNull(BattleContext.FindPlayerBuildingNearScreenPoint(_camera, faraway, _selection.tapScreenRadius),
                "a tap tens of metres away must not pick up the building");
        }

        [Test]
        public void HUDTapOnAPlayerBuilding_WithAnArmySelected_OpensAndDismissesThePanel()
        {
            (UnitController _, BuildingController barracks, Vector2 buildingPoint) = SelectArmyAndBuildBarracks();

            var hudGo = TestWorld.Track(new GameObject("Hud"));
            BattleHud hud = hudGo.AddComponent<BattleHud>();
            hud.Build(TestWorld.Database);
            hud.Bind();

            Assert.IsFalse(hud.productionPanel.IsShown, "precondition: the panel starts hidden");

            InvokePrivate(hud, "HandleWorldTap", buildingPoint);

            Assert.IsTrue(hud.productionPanel.IsShown,
                "an army being selected must not stop the building's production panel from opening");
            Assert.AreSame(barracks, hud.productionPanel.Building);
            Assert.AreSame(barracks, hud.SelectedBuilding);

            Vector2 emptyGround = _camera.WorldToScreenPoint(new Vector3(2f, 0f, 2f));
            InvokePrivate(hud, "HandleWorldTap", emptyGround);

            Assert.IsFalse(hud.productionPanel.IsShown, "tapping away from every building must dismiss the panel");
            Assert.IsNull(hud.SelectedBuilding);
        }

        /// <summary>An army of one selected on screen, plus a finished player barracks to tap.</summary>
        private (UnitController, BuildingController, Vector2) SelectArmyAndBuildBarracks()
        {
            UnitController unit = TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(6f, 0f, 6f));
            BuildingController barracks = TestWorld.CreateBuilding(TestWorld.BarracksId, Team.Player, new Vector3(10f, 0f, 10f));

            Vector2 unitPoint = _camera.WorldToScreenPoint(unit.transform.position);
            Assert.IsTrue(_selection.TrySelectAtScreenPoint(unitPoint), "precondition: the army must be selectable");
            Assert.AreEqual(1, _selection.SelectionCount, "precondition: exactly the army is selected");

            Vector2 buildingPoint = _camera.WorldToScreenPoint(barracks.WorldPosition);
            Assert.AreSame(barracks,
                BattleContext.FindPlayerBuildingNearScreenPoint(_camera, buildingPoint, _selection.tapScreenRadius),
                "precondition: the building point must land on the barracks");

            return (unit, barracks, buildingPoint);
        }

        /// <summary>
        /// Tap resolution is private on both routers — a public surface for it would invite callers to
        /// bypass the gesture state machine — so it is invoked directly here.
        /// </summary>
        private static void InvokePrivate(object target, string methodName, Vector2 screenPoint)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"{target.GetType().Name}.{methodName} must exist for the tap path to be exercised");
            method.Invoke(target, new object[] { screenPoint });
        }
    }
}
