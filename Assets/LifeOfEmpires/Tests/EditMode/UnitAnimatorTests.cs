using System.Collections.Generic;
using LifeOfEmpires;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The procedural unit animator. These cover the contract the animation must never break —
    /// the motion lives on the <c>Body</c> child, it is bounded, and it cannot move the unit root
    /// the agent, collider and health bar hang on — not frame-by-frame output, which batchmode
    /// cannot render.
    /// </summary>
    public class UnitAnimatorTests
    {
        private readonly List<Object> _content = new List<Object>();

        [SetUp]
        public void SetUp() => TestWorld.Build();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _content.Count; i++)
            {
                if (_content[i] != null) Object.DestroyImmediate(_content[i]);
            }

            _content.Clear();
            TestWorld.TearDown();
        }

        [Test]
        public void UnitAnimator_BindsTheBodyChild_NotTheRoot()
        {
            UnitController unit = NewUnit();

            var animator = unit.gameObject.AddComponent<UnitAnimator>();
            animator.Bind();

            Assert.AreSame(unit.transform.Find("Body"), animator.Body,
                "the animation must write the Body child, never the unit root");
        }

        [Test]
        public void UnitAnimator_TickStaysBounded_AndNeverMovesTheRoot()
        {
            UnitController unit = NewUnit();
            var animator = unit.gameObject.AddComponent<UnitAnimator>();
            animator.Bind();

            Transform body = animator.Body;
            Vector3 basePosition = body.localPosition;
            Vector3 rootPosition = unit.transform.localPosition;

            // Idle ticks: the breathing bob must stay inside twice the configured amplitude —
            // an unbounded drift would leak the body away from the agent over a long battle.
            for (int i = 0; i < 240; i++) animator.Tick(1f / 60f);

            Vector3 delta = body.localPosition - basePosition;
            Assert.Less(delta.magnitude, animator.bobAmplitude * 2f + 0.01f,
                "the idle animation must stay bounded by the configured amplitude");
            Assert.AreEqual(rootPosition, unit.transform.localPosition,
                "animation writes are confined to the Body child — the unit root must not move");
        }

        [Test]
        public void UnitAnimator_PlayDeathIsSafeOutsidePlayMode()
        {
            UnitController unit = NewUnit();
            var animator = unit.gameObject.AddComponent<UnitAnimator>();
            animator.Bind();

            Assert.DoesNotThrow(() => animator.PlayDeath(),
                "PlayDeath runs inside DestroyImmediate in EditMode and must be a no-op there");
        }

        private UnitController NewUnit()
        {
            var go = TestWorld.Track(new GameObject("Unit_test"));
            var body = TestWorld.Track(new GameObject("Body"));
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.75f, 0f);

            var unit = go.AddComponent<UnitController>();
            var definition = ScriptableObject.CreateInstance<UnitDefinition>();
            definition.maxHealth = 100;
            _content.Add(definition);
            unit.Initialize(definition, Team.Player, TestWorld.Pathfinding, TestWorld.Combat);
            return unit;
        }
    }
}
