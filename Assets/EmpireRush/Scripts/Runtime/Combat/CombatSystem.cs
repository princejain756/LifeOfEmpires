using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireRush
{
    /// <summary>Anything that can absorb damage. Implemented by units and buildings.</summary>
    public interface IDamageable
    {
        Team Team { get; }
        bool IsAlive { get; }
        int Health { get; }
        int MaxHealth { get; }
        Vector3 WorldPosition { get; }

        /// <summary>Apply damage. Implementations must clamp at zero and never go negative.</summary>
        void ReceiveDamage(int amount, Team sourceTeam);
    }

    /// <summary>
    /// The single funnel for damage. Every hit in the battle goes through <see cref="ApplyDamage"/>,
    /// which is what makes <see cref="OnDamaged"/> and <see cref="OnDied"/> reliable: objectives,
    /// health bars and the AI all subscribe here instead of each polling their own targets.
    ///
    /// Deliberately a plain object, not a MonoBehaviour, so EditMode tests can exercise combat
    /// without a scene.
    /// </summary>
    public class CombatSystem
    {
        private readonly List<IDamageable> _tracked = new List<IDamageable>(128);

        /// <summary>Fired for every applied hit: target, damage dealt, health remaining.</summary>
        public event Action<IDamageable, int, int> OnDamaged;

        /// <summary>Fired exactly once per target, the moment its health reaches zero.</summary>
        public event Action<IDamageable> OnDied;

        /// <summary>Projectile pool used for ranged attacks. Null disables pooled projectiles.</summary>
        public ProjectilePool Projectiles { get; private set; }

        public IReadOnlyList<IDamageable> Tracked => _tracked;

        public void AttachProjectilePool(ProjectilePool pool) => Projectiles = pool;

        public void Track(IDamageable target)
        {
            if (target == null || _tracked.Contains(target)) return;
            _tracked.Add(target);
        }

        /// <summary>True when this entity is currently routed through the damage funnel.</summary>
        public bool IsTracked(IDamageable target) => target != null && _tracked.Contains(target);

        public void Untrack(IDamageable target)
        {
            if (target == null) return;
            _tracked.Remove(target);
        }

        /// <summary>Drop entities that have died or been destroyed, so the list stays bounded.</summary>
        public void PruneDead()
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                IDamageable entry = _tracked[i];

                // Unity-object liveness is checked before touching IsAlive: reading a property on a
                // destroyed MonoBehaviour throws MissingReferenceException.
                bool alive;
                if (entry == null) alive = false;
                else if (entry is UnityEngine.Object unityObject) alive = unityObject != null && entry.IsAlive;
                else alive = entry.IsAlive;

                if (!alive) _tracked.RemoveAt(i);
            }
        }

        /// <summary>
        /// Apply damage to a target. Returns true when the hit landed. Fires
        /// <see cref="OnDamaged"/> on every landed hit and <see cref="OnDied"/> exactly once, on the
        /// hit that took the target to zero. Damage is floored at zero; a dead target absorbs nothing.
        /// </summary>
        public bool ApplyDamage(IDamageable target, int amount, Team sourceTeam)
        {
            if (target == null || !target.IsAlive || amount <= 0) return false;

            target.ReceiveDamage(amount, sourceTeam);

            int remaining = Mathf.Max(0, target.Health);
            OnDamaged?.Invoke(target, amount, remaining);

            if (remaining <= 0) OnDied?.Invoke(target);
            return true;
        }

        /// <summary>
        /// Resolve one attack from <paramref name="attacker"/> against <paramref name="target"/>.
        /// Ranged attackers fire a pooled projectile; melee applies damage on contact immediately.
        /// </summary>
        public bool ResolveAttack(UnitController attacker, IDamageable target)
        {
            if (attacker == null || target == null || !target.IsAlive) return false;

            UnitDefinition definition = attacker.Definition;
            if (definition == null) return false;

            float range = Mathf.Max(0.05f, definition.attackRange);
            if ((attacker.WorldPosition - target.WorldPosition).sqrMagnitude > range * range) return false;

            if (definition.isRanged && Projectiles != null)
            {
                return Projectiles.Fire(attacker.WorldPosition, target, definition.damage, attacker.Team);
            }

            if (definition.isRanged && Projectiles == null)
            {
                // An unconfigured pool must not make ranged units harmless. Report loudly, then hit
                // directly so the battle stays correct.
                Debug.LogError("CombatSystem: ranged attack with no projectile pool attached; applying damage directly.");
            }

            return ApplyDamage(target, definition.damage, attacker.Team);
        }

        /// <summary>Living enemies of <paramref name="team"/> within <paramref name="radius"/> of a point.</summary>
        public IDamageable FindNearestEnemy(Team team, Vector3 from, float radius)
        {
            float bestSqr = radius * radius;
            IDamageable best = null;

            for (int i = 0; i < _tracked.Count; i++)
            {
                IDamageable candidate = _tracked[i];
                if (candidate == null || !candidate.IsAlive) continue;
                if (candidate.Team == team || candidate.Team == Team.Neutral) continue;

                float sqr = (candidate.WorldPosition - from).sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                best = candidate;
            }

            return best;
        }

        /// <summary>All living enemies of <paramref name="team"/> inside a radius, in ascending distance order.</summary>
        public void FindEnemiesInRadius(Team team, Vector3 from, float radius, List<IDamageable> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();

            float radiusSqr = radius * radius;
            for (int i = 0; i < _tracked.Count; i++)
            {
                IDamageable candidate = _tracked[i];
                if (candidate == null || !candidate.IsAlive) continue;
                if (candidate.Team == team || candidate.Team == Team.Neutral) continue;
                if ((candidate.WorldPosition - from).sqrMagnitude > radiusSqr) continue;
                results.Add(candidate);
            }
        }

        /// <summary>Living, non-dead entities of one team. Used by the AI's threat and army counts.</summary>
        public int CountLiving(Team team)
        {
            int count = 0;
            for (int i = 0; i < _tracked.Count; i++)
            {
                IDamageable entry = _tracked[i];
                if (entry == null || !entry.IsAlive) continue;
                if (entry.Team == team) count++;
            }

            return count;
        }

        public void Clear()
        {
            _tracked.Clear();
            OnDamaged = null;
            OnDied = null;
            Projectiles = null;
        }
    }
}
