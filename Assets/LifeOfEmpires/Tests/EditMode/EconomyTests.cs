using System;
using System.Collections.Generic;
using LifeOfEmpires;
using NUnit.Framework;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The economy's contract: a spend removes exactly what it says, an unaffordable spend changes
    /// nothing and says why, and a stockpile can never go negative.
    /// </summary>
    public class EconomyTests
    {
        [TearDown]
        public void TearDown() => TestWorld.TearDown();

        [Test]
        public void TrySpend_RemovesExactlyTheCostComponents()
        {
            TestWorld.Build();
            ResourceManager manager = TestWorld.Player;
            manager.Add(new ResourceBundle(100, 100, 100, 40));

            var cost = new ResourceCost(30, 20, 10, 5);
            Assert.IsTrue(manager.TrySpend(cost, out string reason), reason);

            Assert.AreEqual(70, manager.Get(ResourceType.Wood));
            Assert.AreEqual(80, manager.Get(ResourceType.Food));
            Assert.AreEqual(90, manager.Get(ResourceType.Gold));
            Assert.AreEqual(35, manager.Get(ResourceType.Crowns));
        }

        [Test]
        public void TrySpend_Unaffordable_ReturnsFalseAndMutatesNothing()
        {
            TestWorld.Build();
            ResourceManager manager = TestWorld.Player;
            manager.Add(new ResourceBundle(10, 100, 100, 100));

            ResourceBundle before = manager.Snapshot();
            var cost = new ResourceCost(25, 5, 0, 0);

            Assert.IsFalse(manager.TrySpend(cost, out string reason));
            Assert.IsNotEmpty(reason, "a refused spend must explain itself");
            StringAssert.Contains("Wood", reason);

            ResourceBundle after = manager.Snapshot();
            Assert.AreEqual(before.wood, after.wood, "wood must be untouched after a refused spend");
            Assert.AreEqual(before.food, after.food, "food must be untouched after a refused spend");
            Assert.AreEqual(before.gold, after.gold);
            Assert.AreEqual(before.crowns, after.crowns);
        }

        [Test]
        public void CanAfford_IsExactAtTheBoundary()
        {
            TestWorld.Build();
            ResourceManager manager = TestWorld.Player;
            manager.Add(new ResourceBundle(50, 0, 0, 0));

            Assert.IsTrue(manager.CanAfford(new ResourceCost(50, 0, 0, 0)), "exactly affordable must pass");
            Assert.IsFalse(manager.CanAfford(new ResourceCost(51, 0, 0, 0)), "one over must fail");

            // A zero cost is affordable even with nothing in the bank.
            var empty = new ResourceManager(Team.Player);
            Assert.IsTrue(empty.CanAfford(ResourceCost.Zero));
        }

        [Test]
        public void Stockpile_NeverGoesNegative()
        {
            TestWorld.Build();
            ResourceManager manager = TestWorld.Player;
            manager.Add(ResourceType.Wood, 10);

            Assert.IsTrue(manager.TrySpend(new ResourceCost(10, 0, 0, 0), out string first), first);
            Assert.AreEqual(0, manager.Get(ResourceType.Wood));

            Assert.IsFalse(manager.TrySpend(new ResourceCost(1, 0, 0, 0), out string second));
            Assert.IsNotEmpty(second);
            Assert.AreEqual(0, manager.Get(ResourceType.Wood), "a refused spend must not push the balance below zero");
        }

        [Test]
        public void Spend_ThrowsRatherThanClampingSilently()
        {
            TestWorld.Build();

            var stock = new Dictionary<ResourceType, int>
            {
                { ResourceType.Wood, 0 },
                { ResourceType.Food, 0 },
                { ResourceType.Gold, 5 },
                { ResourceType.Crowns, 0 }
            };

            var tooExpensive = new ResourceCost(0, 0, 50, 0);
            Assert.Throws<InvalidOperationException>(() => EconomyBalance.Spend(stock, tooExpensive),
                "the unconditional spend must refuse loudly rather than clamp the balance");
            Assert.AreEqual(5, stock[ResourceType.Gold], "the refused spend must leave the balance alone");

            // The same refusal, seen through the manager that gameplay actually calls.
            ResourceManager manager = TestWorld.Player;
            manager.Add(ResourceType.Gold, 5);
            Assert.IsFalse(manager.TrySpend(tooExpensive, out string reason));
            Assert.IsNotEmpty(reason);
            Assert.AreEqual(5, manager.Get(ResourceType.Gold));
        }

        [Test]
        public void Add_RejectsNegativeAmounts()
        {
            TestWorld.Build();
            ResourceManager manager = TestWorld.Player;
            manager.Add(ResourceType.Wood, 100);

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Add(ResourceType.Wood, -1));
            Assert.AreEqual(100, manager.Get(ResourceType.Wood));
        }

        [Test]
        public void Refund_RestoresExactlyWhatWasSpent()
        {
            TestWorld.Build();
            ResourceManager manager = TestWorld.Player;
            manager.Add(new ResourceBundle(100, 100, 0, 0));

            var cost = new ResourceCost(40, 25, 0, 0);
            Assert.IsTrue(manager.TrySpend(cost, out string reason), reason);
            manager.Refund(cost);

            Assert.AreEqual(100, manager.Get(ResourceType.Wood));
            Assert.AreEqual(100, manager.Get(ResourceType.Food));
        }

        [Test]
        public void OnResourceChanged_ReportsOnlyResourcesThatActuallyMoved()
        {
            TestWorld.Build();
            ResourceManager manager = TestWorld.Player;
            manager.Add(new ResourceBundle(100, 0, 0, 0));

            var changes = new List<(ResourceType Type, int Before, int After)>();
            manager.OnResourceChanged += (type, before, after) => changes.Add((type, before, after));

            Assert.IsTrue(manager.TrySpend(new ResourceCost(30, 0, 0, 0), out string reason), reason);

            Assert.AreEqual(1, changes.Count, "only wood changed, so only wood may be reported");
            Assert.AreEqual(ResourceType.Wood, changes[0].Type);
            Assert.AreEqual(100, changes[0].Before);
            Assert.AreEqual(70, changes[0].After);
        }

        [Test]
        public void BorrowedResources_CannotBeProducedBySpendingZero()
        {
            TestWorld.Build();
            ResourceManager manager = TestWorld.Player;
            manager.Add(new ResourceBundle(0, 0, 0, 0));

            Assert.IsTrue(manager.TrySpend(ResourceCost.Zero, out string reason), reason);
            Assert.AreEqual(0, manager.Total, "spending nothing must create nothing");
        }
    }
}
