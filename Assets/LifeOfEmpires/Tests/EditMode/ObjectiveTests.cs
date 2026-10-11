using System.Collections.Generic;
using LifeOfEmpires;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// Victory and defeat must be edge-triggered exactly once, and must not fire before the thing they
    /// watch has even appeared. Both properties are load-bearing: a victory that fires twice double-
    /// awards crowns and re-opens the results panel, and a victory that fires on frame one ends the
    /// mission before the enemy town hall has spawned.
    /// </summary>
    public class ObjectiveTests
    {
        [TearDown]
        public void TearDown() => TestWorld.TearDown();

        private static MissionDefinition Mission(
            ObjectiveCondition victory = ObjectiveCondition.DestroyEnemyTownHall,
            ObjectiveCondition defeat = ObjectiveCondition.LosePlayerTownHall,
            params StarObjective[] stars)
        {
            var mission = ScriptableObject.CreateInstance<MissionDefinition>();
            mission.missionId = "test_mission";
            mission.title = "Test Mission";
            mission.victoryCondition = victory;
            mission.defeatCondition = defeat;
            mission.starObjectives = stars;
            return mission;
        }

        private static (BattleObjectiveManager Manager, List<BattleOutcome> Outcomes) CreateManager(MissionDefinition mission)
        {
            var go = TestWorld.Track(new GameObject("Objectives"));
            var manager = go.AddComponent<BattleObjectiveManager>();

            var outcomes = new List<BattleOutcome>();
            manager.Initialize(mission, TestWorld.Combat);
            manager.OnBattleEnded += outcome => outcomes.Add(outcome);
            return (manager, outcomes);
        }

        [Test]
        public void Victory_FiresOnceWhenTheEnemyTownHallDies()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, List<BattleOutcome> outcomes) = CreateManager(Mission());

            TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Player, new Vector3(2f, 0f, 2f));
            BuildingController enemyHall = TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Enemy, new Vector3(20f, 0f, 20f));

            // First evaluation latches the fact that both town halls exist.
            manager.Evaluate();
            Assert.IsFalse(manager.HasEnded, "the battle must not be over while both town halls stand");
            Assert.IsEmpty(outcomes);

            TestWorld.Combat.ApplyDamage(enemyHall, enemyHall.MaxHealth, Team.Player);

            Assert.AreEqual(BattleOutcome.Victory, manager.Outcome);
            Assert.AreEqual(1, outcomes.Count, "victory must be raised exactly once");
            Assert.AreEqual(BattleOutcome.Victory, outcomes[0]);
            Assert.AreEqual(1, manager.EndRaisedCount);

            // Further frames, and further evaluations, must not re-raise the result.
            manager.Evaluate();
            manager.Evaluate();
            Assert.AreEqual(1, outcomes.Count, "later evaluations must not re-raise a resolved battle");
            Assert.AreEqual(1, manager.EndRaisedCount);
        }

        [Test]
        public void Defeat_FiresOnceWhenThePlayerTownHallDies()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, List<BattleOutcome> outcomes) = CreateManager(Mission());

            BuildingController playerHall = TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Player, new Vector3(2f, 0f, 2f));
            TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Enemy, new Vector3(20f, 0f, 20f));

            manager.Evaluate();
            Assert.IsFalse(manager.HasEnded);

            TestWorld.Combat.ApplyDamage(playerHall, playerHall.MaxHealth, Team.Enemy);

            Assert.AreEqual(BattleOutcome.Defeat, manager.Outcome);
            Assert.AreEqual(1, outcomes.Count, "defeat must be raised exactly once");
            Assert.AreEqual(BattleOutcome.Defeat, outcomes[0]);

            manager.Evaluate();
            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(1, manager.EndRaisedCount);
        }

        [Test]
        public void DefeatIsCheckedBeforeVictory_SoAMutualKillIsNotAWin()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, List<BattleOutcome> outcomes) = CreateManager(Mission());

            BuildingController playerHall = TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Player, new Vector3(2f, 0f, 2f));
            BuildingController enemyHall = TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Enemy, new Vector3(20f, 0f, 20f));

            manager.Evaluate();

            // Both fall in the same frame: the player losing their hall must decide the outcome.
            TestWorld.Combat.ApplyDamage(playerHall, playerHall.MaxHealth, Team.Enemy);
            TestWorld.Combat.ApplyDamage(enemyHall, enemyHall.MaxHealth, Team.Player);

            Assert.AreEqual(BattleOutcome.Defeat, manager.Outcome);
            Assert.AreEqual(1, outcomes.Count);
        }

        [Test]
        public void NoOutcome_IsDeclaredWhileTheEnemyTownHallHasNeverExisted()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, List<BattleOutcome> outcomes) = CreateManager(Mission());

            TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Player, new Vector3(2f, 0f, 2f));

            manager.Evaluate();
            manager.Evaluate();

            Assert.IsFalse(manager.HasEnded,
                "with no enemy town hall there is nothing to have destroyed, so the battle cannot be won");
            Assert.IsEmpty(outcomes);
        }

        [Test]
        public void VictoryAfterTheEnemyTownHallIsSeenAndThenDestroyed()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, List<BattleOutcome> outcomes) = CreateManager(Mission());

            TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Player, new Vector3(2f, 0f, 2f));

            // Frame 1: the enemy hall has not spawned yet.
            manager.Evaluate();
            Assert.IsFalse(manager.HasEnded);

            // Frame 2: it spawns late, as a mission's delayed enemy camp would.
            BuildingController enemyHall = TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Enemy, new Vector3(20f, 0f, 20f));
            manager.Evaluate();
            Assert.IsFalse(manager.HasEnded);

            TestWorld.Combat.ApplyDamage(enemyHall, enemyHall.MaxHealth, Team.Player);

            Assert.AreEqual(BattleOutcome.Victory, manager.Outcome);
            Assert.AreEqual(1, outcomes.Count);
        }

        [Test]
        public void DestroyingANonTownHallBuilding_DoesNotEndTheBattle()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, List<BattleOutcome> outcomes) = CreateManager(Mission());

            TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Player, new Vector3(2f, 0f, 2f));
            TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Enemy, new Vector3(20f, 0f, 20f));
            BuildingController enemyBarracks = TestWorld.CreateBuilding(TestWorld.BarracksId, Team.Enemy, new Vector3(16f, 0f, 20f));

            manager.Evaluate();
            TestWorld.Combat.ApplyDamage(enemyBarracks, enemyBarracks.MaxHealth, Team.Player);

            Assert.IsFalse(manager.HasEnded, "razing a barracks is progress, not victory");
            Assert.IsEmpty(outcomes);
            Assert.AreEqual(1, manager.EnemyBuildingsDestroyed, "the destroyed building must still be counted for stars");
        }

        [Test]
        public void DestroyedEnemyBuildings_AreCountedOnceEach()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, _) = CreateManager(Mission(
                stars: new StarObjective
                {
                    description = "Raze two structures",
                    condition = ObjectiveCondition.DestroyEnemyBuildings,
                    threshold = 2
                }));

            TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Player, new Vector3(2f, 0f, 2f));
            TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Enemy, new Vector3(20f, 0f, 20f));
            BuildingController first = TestWorld.CreateBuilding(TestWorld.BarracksId, Team.Enemy, new Vector3(14f, 0f, 20f));
            BuildingController second = TestWorld.CreateBuilding(TestWorld.TowerId, Team.Enemy, new Vector3(16f, 0f, 20f));

            manager.Evaluate();

            TestWorld.Combat.ApplyDamage(first, first.MaxHealth, Team.Player);
            Assert.AreEqual(1, manager.EnemyBuildingsDestroyed);
            Assert.AreEqual(0, manager.ScoreStars(TestWorld.Context), "one of two does not earn the star");

            TestWorld.Combat.ApplyDamage(second, second.MaxHealth, Team.Player);
            Assert.AreEqual(2, manager.EnemyBuildingsDestroyed);
            Assert.AreEqual(1, manager.ScoreStars(TestWorld.Context), "two of two earns the star");
        }

        [Test]
        public void StarsAreScoredAgainstLiveWorldState()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, _) = CreateManager(Mission(
                stars: new[]
                {
                    new StarObjective { description = "Train two units", condition = ObjectiveCondition.TrainUnits, threshold = 2 },
                    new StarObjective { description = "Stockpile 800 gold", condition = ObjectiveCondition.StockpileResource, threshold = 800, resource = ResourceType.Gold }
                }));

            Assert.AreEqual(2, manager.TotalStars);
            Assert.AreEqual(0, manager.ScoreStars(TestWorld.Context), "neither star is earned at the start");

            TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(1f, 0f, 1f));

            var results = new List<bool>();
            int earned = manager.ScoreStars(TestWorld.Context, results);

            Assert.AreEqual(0, earned, "one unit is not two units, and an empty bank is not 800 gold");
            Assert.AreEqual(2, results.Count, "one result per configured star");
            Assert.IsFalse(results[0]);
            Assert.IsFalse(results[1]);

            TestWorld.Player.Add(ResourceType.Gold, 799);
            Assert.AreEqual(0, manager.ScoreStars(TestWorld.Context), "one gold short must not earn the star");

            TestWorld.Player.Add(ResourceType.Gold, 1);
            Assert.AreEqual(1, manager.ScoreStars(TestWorld.Context), "the stockpile star is earned on reaching the threshold");

            TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Player, new Vector3(3f, 0f, 1f));
            Assert.AreEqual(2, manager.ScoreStars(TestWorld.Context), "both stars are now earned");
        }

        [Test]
        public void UnsupportedPrimaryCondition_NeverEndsTheBattleSilently()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, List<BattleOutcome> outcomes) = CreateManager(Mission(victory: ObjectiveCondition.TrainUnits));

            BuildingController enemyHall = TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Enemy, new Vector3(20f, 0f, 20f));
            manager.Evaluate();
            TestWorld.Combat.ApplyDamage(enemyHall, enemyHall.MaxHealth, Team.Player);
            manager.Evaluate();

            Assert.IsFalse(manager.HasEnded,
                "an objective the build cannot evaluate must not be treated as satisfied");
            Assert.IsEmpty(outcomes);
        }

        [Test]
        public void OutcomeIsWrittenBackToTheBattleContext()
        {
            TestWorld.Build();
            (BattleObjectiveManager manager, _) = CreateManager(Mission());

            TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Player, new Vector3(2f, 0f, 2f));
            BuildingController enemyHall = TestWorld.CreateBuilding(TestWorld.TownHallId, Team.Enemy, new Vector3(20f, 0f, 20f));
            manager.Evaluate();

            TestWorld.Combat.ApplyDamage(enemyHall, enemyHall.MaxHealth, Team.Player);

            Assert.AreEqual(BattleOutcome.Victory, TestWorld.Context.Outcome,
                "the battle context must reflect the resolved outcome for the rest of the systems");
        }
    }
}
