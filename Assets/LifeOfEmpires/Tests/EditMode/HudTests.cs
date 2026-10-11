using LifeOfEmpires;
using LifeOfEmpires.UI;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The HUD built from code against a real <see cref="TestWorld"/>: behaviour, not pixel geometry.
    /// A layout claim cannot be tested in batchmode, so these cover what the panels do with the live
    /// systems — follow the stockpile, refuse what the player cannot pay for, and reach the
    /// production slot through <see cref="BuildingController.TryTrain"/>.
    /// </summary>
    public class HudTests
    {
        private GameObject _hudObject;
        private BattleHud _hud;

        [TearDown]
        public void TearDown()
        {
            if (_hudObject != null) Object.DestroyImmediate(_hudObject);
            _hudObject = null;
            _hud = null;

            TestWorld.TearDown();
        }

        private BattleHud BuildHud()
        {
            _hudObject = new GameObject("TestHud");
            _hud = _hudObject.AddComponent<BattleHud>();
            _hud.Build(TestWorld.Database);
            _hud.Bind();
            return _hud;
        }

        [Test]
        public void HudBuildsEveryPanelWithTheOneBuiltinFont()
        {
            TestWorld.Build();
            BattleHud hud = BuildHud();

            Assert.IsNotNull(hud.canvas, "the HUD must own a canvas");
            Assert.IsNotNull(hud.raycaster, "without a raycaster no button would ever receive a tap");
            Assert.IsNotNull(hud.safeArea, "the layout must respect the device safe area");
            Assert.IsNotNull(hud.resourcesBar, "the resource bar must exist");
            Assert.IsNotNull(hud.selectionPanel, "the selection panel must exist");
            Assert.IsNotNull(hud.buildMenu, "the build menu must exist");
            Assert.IsNotNull(hud.productionPanel, "the production panel must exist");
            Assert.IsNotNull(hud.resultsPanel, "the results panel must exist");
            Assert.IsNotNull(hud.pauseButton, "the pause button must exist");

            Assert.IsNotNull(hud.resourcesBar.woodLabel.font,
                "a label without a font renders nothing, which is what a missing LegacyRuntime.ttf would look like");
            Assert.AreSame(hud.resourcesBar.woodLabel.font, hud.selectionPanel.summaryLabel.font,
                "every label must share the one builtin font");
        }

        [Test]
        public void HudBuildsTheFirstRunHintLines()
        {
            TestWorld.Build();
            BattleHud hud = BuildHud();

            Assert.IsNotNull(hud.hintLabel, "the objective hint must exist");
            Assert.IsNotNull(hud.cueLabel, "the gather cue must exist");
            Assert.AreSame(hud.resourcesBar.woodLabel.font, hud.hintLabel.font,
                "the hints must share the one builtin font");
            Assert.AreSame(hud.resourcesBar.woodLabel.font, hud.cueLabel.font,
                "the hints must share the one builtin font");
            Assert.IsNotEmpty(hud.cueLabel.text, "the gather cue must actually tell the player something");
        }

        [Test]
        public void ResourceBar_FollowsTheStockpileInsteadOfPolling()
        {
            TestWorld.Build();
            BattleHud hud = BuildHud();

            Assert.AreEqual("0", hud.resourcesBar.woodLabel.text);

            TestWorld.Player.Add(ResourceType.Wood, 120);
            TestWorld.Player.Add(ResourceType.Gold, 45);

            Assert.AreEqual("120", hud.resourcesBar.woodLabel.text,
                "the bar must update from ResourceManager.OnResourceChanged");
            Assert.AreEqual("45", hud.resourcesBar.goldLabel.text);
            Assert.AreEqual("0", hud.resourcesBar.foodLabel.text, "an untouched resource must stay at zero");
        }

        [Test]
        public void BuildMenu_DisablesBuildingsThePlayerCannotAfford()
        {
            TestWorld.Build();
            BattleHud hud = BuildHud();

            BuildingDefinition barracks = TestWorld.Database.FindBuilding(TestWorld.BarracksId);
            Assert.IsNotNull(barracks, "the test database must define a barracks");

            Assert.IsFalse(hud.buildMenu.IsButtonEnabled(TestWorld.BarracksId),
                "an empty stockpile cannot afford a barracks");

            TestWorld.Player.Add(ResourceType.Wood, barracks.cost.wood);
            Assert.IsFalse(hud.buildMenu.IsButtonEnabled(TestWorld.BarracksId),
                "part of the cost is not the whole cost");

            TestWorld.Player.Add(ResourceType.Gold, barracks.cost.gold);
            Assert.IsTrue(hud.buildMenu.IsButtonEnabled(TestWorld.BarracksId),
                "the full cost must enable the button without any other interaction");
        }

        [Test]
        public void BuildMenu_ExcludesTownHalls()
        {
            TestWorld.Build();
            BattleHud hud = BuildHud();

            CollectionAssert.DoesNotContain(hud.buildMenu.BuildingIds, TestWorld.TownHallId,
                "the town hall is not a player-placed building");
            CollectionAssert.Contains(hud.buildMenu.BuildingIds, TestWorld.BarracksId);
            CollectionAssert.Contains(hud.buildMenu.BuildingIds, TestWorld.TowerId);
        }

        [Test]
        public void ProductionPanel_ShowsForASelectedBuildingAndTrainsThroughTryTrain()
        {
            TestWorld.Build();
            BattleHud hud = BuildHud();

            BuildingController barracks = TestWorld.CreateBuilding(TestWorld.BarracksId, Team.Player, new Vector3(8f, 0f, 8f));
            hud.ShowProductionFor(barracks);

            Assert.IsTrue(hud.productionPanel.IsShown, "selecting a building must open its production panel");
            Assert.AreSame(barracks, hud.productionPanel.Building);

            UnitDefinition swordsman = TestWorld.Database.FindUnit(TestWorld.CombatUnitId);
            int foodBefore = TestWorld.Player.Get(ResourceType.Food);
            Assert.AreEqual(0, foodBefore, "precondition: the test stockpile starts empty");

            Assert.IsFalse(hud.productionPanel.Train(TestWorld.CombatUnitId),
                "training must be refused while the payer cannot afford it");
            Assert.IsFalse(barracks.IsTraining, "a refused train must leave the production slot idle");
            Assert.AreEqual(foodBefore, TestWorld.Player.Get(ResourceType.Food), "a refused train must not spend");

            TestWorld.Player.Add(ResourceType.Food, swordsman.cost.food);
            TestWorld.Player.Add(ResourceType.Gold, swordsman.cost.gold);

            Assert.IsTrue(hud.productionPanel.Train(TestWorld.CombatUnitId), "an affordable train must start");
            Assert.IsTrue(barracks.IsTraining, "the production slot must be busy");
            Assert.AreEqual(swordsman.id, barracks.TrainingUnitId);
            Assert.AreEqual(0, TestWorld.Player.Get(ResourceType.Food),
                "training must charge exactly the unit's food cost and nothing more");

            hud.ShowProductionFor(null);
            Assert.IsFalse(hud.productionPanel.IsShown, "closing the panel must clear the selection");
        }
    }
}
