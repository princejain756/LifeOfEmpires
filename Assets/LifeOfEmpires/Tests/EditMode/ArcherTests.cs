using System.Reflection;
using LifeOfEmpires;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The archer is the first ranged unit: its shots must travel as pooled projectiles, not as
    /// instant damage, and the pool must give every projectile back. A ranged unit that silently
    /// applies damage on contact would look correct in a single frame and be wrong in every battle.
    /// </summary>
    public class ArcherTests
    {
        [TearDown]
        public void TearDown() => TestWorld.TearDown();

        [Test]
        public void RangedAttack_FiresAPooledProjectile_AndReturnsItOnImpact()
        {
            TestWorld.Build();

            UnitController archer = TestWorld.CreateUnit(TestWorld.ArcherUnitId, Team.Player, Vector3.zero);
            UnitController target = TestWorld.CreateUnit(TestWorld.CombatUnitId, Team.Enemy, new Vector3(5f, 0f, 0f));

            ProjectilePool pool = AttachPool(prewarm: 8);
            int prewarm = pool.TotalCreated;

            Assert.AreEqual(8, prewarm, "the pool must prewarm the requested number of projectiles");
            Assert.AreEqual(0, pool.ActiveCount, "nothing is in flight before the first shot");

            int healthBefore = target.Health;

            Assert.IsTrue(TestWorld.Combat.ResolveAttack(archer, target),
                "an in-range ranged attack must resolve");
            Assert.AreEqual(1, pool.ActiveCount, "the shot must be drawn from the projectile pool");
            Assert.AreEqual(prewarm, pool.TotalCreated, "firing must reuse a pooled projectile, never allocate");
            Assert.AreEqual(0, pool.RefusedCount, "a prewarmed pool must not refuse a single shot");
            Assert.AreEqual(healthBefore, target.Health, "damage must wait for the projectile to arrive");

            // EditMode never ticks MonoBehaviour.Update, so the flight is finished by invoking the
            // same callback Update invokes on arrival. Reflection rather than SendMessage: SendMessage
            // routes through the engine's behaviour runner, which asserts outside play mode. Impact is
            // idempotent and guarded by InFlight.
            Projectile inFlight = FindInFlight(pool);
            Assert.IsNotNull(inFlight, "exactly one projectile must be in flight");
            Impact(inFlight);

            Assert.AreEqual(0, pool.ActiveCount, "an impacted projectile must go back to the pool");
            Assert.AreEqual(prewarm, pool.TotalCreated, "returning a projectile must not allocate either");

            UnitDefinition definition = TestWorld.Database.FindUnit(TestWorld.ArcherUnitId);
            Assert.AreEqual(healthBefore - definition.damage, target.Health,
                "the landed projectile must apply exactly the archer's damage");
        }

        [Test]
        public void RangedAttack_OutOfRange_DoesNotFire()
        {
            TestWorld.Build();

            UnitDefinition definition = TestWorld.Database.FindUnit(TestWorld.ArcherUnitId);
            UnitController archer = TestWorld.CreateUnit(TestWorld.ArcherUnitId, Team.Player, Vector3.zero);
            UnitController target = TestWorld.CreateUnit(
                TestWorld.CombatUnitId,
                Team.Enemy,
                new Vector3(definition.attackRange + 3f, 0f, 0f));

            ProjectilePool pool = AttachPool(prewarm: 4);

            Assert.IsFalse(TestWorld.Combat.ResolveAttack(archer, target), "a target beyond attackRange must not be hit");
            Assert.AreEqual(0, pool.ActiveCount, "a refused attack must not launch a projectile");
            Assert.AreEqual(4, pool.TotalCreated, "a refused attack must not allocate");
        }

        [Test]
        public void ArcherIsRanged_AndTheSwordsmanCounterIsReadById()
        {
            TestWorld.Build();

            UnitDefinition archer = TestWorld.Database.FindUnit(TestWorld.ArcherUnitId);
            UnitDefinition swordsman = TestWorld.Database.FindUnit(TestWorld.CombatUnitId);

            Assert.IsNotNull(archer, "the test database must define the archer");
            Assert.IsTrue(archer.isRanged, "the archer's shots travel as projectiles");
            Assert.IsTrue(archer.IsCombatUnit, "an archer with damage is a combat unit");
            Assert.IsTrue(swordsman.CountersUnit(archer), "the swordsman counters the archer by id");
            Assert.IsFalse(archer.CountersUnit(swordsman), "the archer is not authored to counter the swordsman");
        }

        private static ProjectilePool AttachPool(int prewarm)
        {
            var go = TestWorld.Track(new GameObject("TestProjectilePool"));
            var pool = go.AddComponent<ProjectilePool>();
            pool.prewarmCount = prewarm;
            pool.maxSize = prewarm * 4;
            pool.Initialize(TestWorld.Combat);
            TestWorld.Combat.AttachProjectilePool(pool);
            return pool;
        }

        private static Projectile FindInFlight(ProjectilePool pool)
        {
            Projectile[] projectiles = pool.GetComponentsInChildren<Projectile>(true);
            for (int i = 0; i < projectiles.Length; i++)
            {
                if (projectiles[i] != null && projectiles[i].InFlight) return projectiles[i];
            }

            return null;
        }

        private static void Impact(Projectile projectile)
        {
            MethodInfo impact = typeof(Projectile).GetMethod("Impact", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(impact, "Projectile must keep its private Impact callback; it is what returns the shot to the pool");
            impact.Invoke(projectile, null);
        }
    }
}
