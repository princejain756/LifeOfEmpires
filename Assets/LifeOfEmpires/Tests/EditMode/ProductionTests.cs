using LifeOfEmpires;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The multi-unit production path. <see cref="BuildingController.TryTrain"/> is what the HUD's
    /// unit list calls, so it must charge exactly once — and only after every precondition has
    /// passed, so a refused train leaves the stockpile bit-for-bit unchanged.
    /// <see cref="BuildingController.CancelTraining"/> must hand back the cost of the unit that was
    /// actually queued, which for the archer is <em>not</em> the barracks' hard-wired
    /// <see cref="BuildingDefinition.producesUnitId"/>.
    /// </summary>
    public class ProductionTests
    {
        [TearDown]
        public void TearDown() => TestWorld.TearDown();

        /// <summary>The barracks produces the swordsman; the archer is only reachable via TryTrain.</summary>
        private static BuildingController BuildBarracks(out UnitDefinition archer, out UnitDefinition swordsman)
        {
            BuildingController barracks = TestWorld.CreateBuilding(TestWorld.BarracksId, Team.Player, new Vector3(8f, 0f, 8f));
            archer = TestWorld.Database.FindUnit(TestWorld.ArcherUnitId);
            swordsman = TestWorld.Database.FindUnit(TestWorld.CombatUnitId);

            Assert.IsNotNull(archer, "the test database must define an archer");
            Assert.IsNotNull(swordsman, "the test database must define the barracks' own unit");
            Assert.AreEqual(swordsman.id, barracks.ProducesUnitId, "precondition: the barracks produces the swordsman");
            Assert.AreNotEqual(swordsman.cost, archer.cost,
                "the two costs must differ for the refund to prove which unit it resolved");
            return barracks;
        }

        [Test]
        public void CancelTraining_OfAnArcher_RefundsTheArchersCostNotTheBarracksOwnUnit()
        {
            TestWorld.Build(playerStart: new ResourceBundle(500, 500, 500, 0));
            BuildingController barracks = BuildBarracks(out UnitDefinition archer, out _);

            int woodBefore = TestWorld.Player.Get(ResourceType.Wood);
            int foodBefore = TestWorld.Player.Get(ResourceType.Food);
            int goldBefore = TestWorld.Player.Get(ResourceType.Gold);

            Assert.IsTrue(barracks.TryTrain(archer, TestWorld.Player, out string reason), $"training must start: {reason}");
            Assert.AreEqual(archer.id, barracks.TrainingUnitId, "the queue must hold the unit that was asked for");

            Assert.AreEqual(woodBefore - archer.cost.wood, TestWorld.Player.Get(ResourceType.Wood),
                "starting an archer must charge exactly the archer's wood");
            Assert.AreEqual(foodBefore - archer.cost.food, TestWorld.Player.Get(ResourceType.Food),
                "starting an archer must charge exactly the archer's food");
            Assert.AreEqual(goldBefore - archer.cost.gold, TestWorld.Player.Get(ResourceType.Gold),
                "starting an archer must charge exactly the archer's gold");

            barracks.CancelTraining();

            Assert.IsFalse(barracks.IsTraining, "cancelling must free the production slot");
            Assert.AreEqual(string.Empty, barracks.TrainingUnitId, "cancelling must empty the queue");
            Assert.AreEqual(woodBefore, TestWorld.Player.Get(ResourceType.Wood),
                "cancelling an archer must refund the archer's wood, not the swordsman's");
            Assert.AreEqual(foodBefore, TestWorld.Player.Get(ResourceType.Food),
                "cancelling an archer must refund the archer's food, not the swordsman's");
            Assert.AreEqual(goldBefore, TestWorld.Player.Get(ResourceType.Gold),
                "cancelling an archer must refund the archer's gold, not the swordsman's");
        }

        [Test]
        public void TryTrain_Refused_LeavesTheSlotIdleAndSpendsNothing()
        {
            TestWorld.Build();
            BuildingController barracks = BuildBarracks(out UnitDefinition archer, out _);

            Assert.AreEqual(0, TestWorld.Player.Total, "precondition: the stockpile starts empty");

            // No wood and no food: the archer cannot be paid for.
            Assert.IsFalse(barracks.TryTrain(archer, TestWorld.Player, out string reason),
                "an empty stockpile cannot afford an archer");
            Assert.IsNotEmpty(reason, "a refusal must say why");
            Assert.IsFalse(barracks.IsTraining, "a refused train must leave the production slot idle");
            Assert.AreEqual(string.Empty, barracks.TrainingUnitId, "a refused train must not queue anything");
            Assert.AreEqual(0f, barracks.TrainingProgress, "a refused train must not start progress");
            Assert.AreEqual(0, TestWorld.Player.Total, "a refused train must not mutate a single resource");

            // Enough food this time, still no wood: the refusal must still leave the food alone.
            TestWorld.Player.Add(ResourceType.Food, archer.cost.food);
            Assert.IsFalse(barracks.TryTrain(archer, TestWorld.Player, out reason),
                "food alone is not the whole cost");
            Assert.AreEqual(archer.cost.food, TestWorld.Player.Get(ResourceType.Food),
                "a refusal must not touch the resources it could not complete the payment with");
            Assert.IsFalse(barracks.IsTraining);

            // Now affordable: the single charge lands, and the busy slot refuses a second order.
            TestWorld.Player.Add(ResourceType.Wood, archer.cost.wood);
            Assert.IsTrue(barracks.TryTrain(archer, TestWorld.Player, out reason), $"an affordable train must start: {reason}");
            Assert.AreEqual(0, TestWorld.Player.Get(ResourceType.Wood), "the archer's wood cost must be spent exactly once");
            Assert.AreEqual(0, TestWorld.Player.Get(ResourceType.Food), "the archer's food cost must be spent exactly once");

            Assert.IsFalse(barracks.TryTrain(archer, TestWorld.Player, out reason),
                "a busy production slot must refuse a second order");
            Assert.AreEqual(0, TestWorld.Player.Total, "the refused second order must not spend anything more");
        }
    }
}
