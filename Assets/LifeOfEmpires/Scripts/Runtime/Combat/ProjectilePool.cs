using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>One pooled projectile. Never created or destroyed during combat.</summary>
    public class Projectile : MonoBehaviour
    {
        private ProjectilePool _owner;
        private CombatSystem _combat;
        private IDamageable _target;
        private Team _sourceTeam;
        private int _damage;
        private float _speed;
        private float _lifeRemaining;
        private Vector3 _fallbackDestination;

        public bool InFlight { get; private set; }

        internal void Launch(
            ProjectilePool owner,
            CombatSystem combat,
            Vector3 origin,
            Vector3 destination,
            IDamageable target,
            int damage,
            Team sourceTeam,
            float speed,
            float maxLifetime)
        {
            _owner = owner;
            _combat = combat;
            _target = target;
            _damage = damage;
            _sourceTeam = sourceTeam;
            _speed = Mathf.Max(0.5f, speed);
            _lifeRemaining = Mathf.Max(0.1f, maxLifetime);
            _fallbackDestination = destination;
            InFlight = true;

            transform.position = origin;
            transform.rotation = Quaternion.LookRotation((destination - origin).sqrMagnitude > 0.0001f
                ? (destination - origin).normalized
                : Vector3.forward);
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!InFlight) return;

            // Track a moving target, but keep flying to the last known point if it dies mid-flight.
            Vector3 destination = _fallbackDestination;
            if (_target != null && _target.IsAlive)
            {
                destination = _target.WorldPosition;
                _fallbackDestination = destination;
            }

            Vector3 toTarget = destination - transform.position;
            float step = _speed * Time.deltaTime;

            if (toTarget.sqrMagnitude <= step * step)
            {
                transform.position = destination;
                Impact();
                return;
            }

            transform.position += toTarget.normalized * step;

            _lifeRemaining -= Time.deltaTime;
            if (_lifeRemaining <= 0f) Impact();
        }

        private void Impact()
        {
            if (!InFlight) return;
            InFlight = false;

            // A projectile that outlived its target simply expires: no damage, no explosion.
            if (_target != null && _target.IsAlive && _combat != null)
            {
                _combat.ApplyDamage(_target, _damage, _sourceTeam);
            }

            _target = null;
            _combat = null;
            _owner?.Return(this);
        }

        internal void ResetForPool()
        {
            InFlight = false;
            _target = null;
            _combat = null;
            _owner = null;
            _damage = 0;
        }
    }

    /// <summary>
    /// Prewarmed pool of projectiles. Combat paths only ever call <see cref="Fire"/> and internal
    /// returns — no <c>Instantiate</c> and no <c>Destroy</c> once play begins, so a large battle does
    /// not churn the allocator or trigger periodic garbage collection spikes.
    /// </summary>
    public class ProjectilePool : MonoBehaviour
    {
        [Header("Pool")]
        [Tooltip("Projectiles created before play and kept alive for the whole battle.")]
        [Min(1)] public int prewarmCount = 64;

        [Tooltip("Hard ceiling. Requests beyond it are refused rather than allocating mid-battle.")]
        [Min(1)] public int maxSize = 256;

        [Header("Flight")]
        [Min(0.5f)] public float speed = 18f;

        [Tooltip("Seconds a projectile may fly before it expires. Guards against a target that never resolves.")]
        [Min(0.1f)] public float maxLifetime = 4f;

        [Min(0.01f)] public float visualScale = 0.28f;

        private readonly Stack<Projectile> _idle = new Stack<Projectile>(64);
        private readonly List<Projectile> _all = new List<Projectile>(256);
        private CombatSystem _combat;
        private Material _material;
        private int _activeCount;

        public int IdleCount => _idle.Count;
        public int ActiveCount => _activeCount;
        public int TotalCreated => _all.Count;
        public int RefusedCount { get; private set; }

        public void Initialize(CombatSystem combat, int prewarm = -1)
        {
            _combat = combat;
            if (prewarm > 0) prewarmCount = prewarm;

            _material = CreateProjectileMaterial();
            int target = Mathf.Clamp(prewarmCount, 1, maxSize);
            for (int i = 0; i < target; i++) CreateProjectile();
        }

        /// <summary>
        /// Send a projectile from <paramref name="origin"/> toward <paramref name="target"/>. Returns
        /// false only when the pool is at <see cref="maxSize"/> and every projectile is in flight, in
        /// which case the caller should treat the shot as lost rather than force an allocation.
        /// </summary>
        public bool Fire(Vector3 origin, IDamageable target, int damage, Team sourceTeam)
        {
            if (target == null || !target.IsAlive) return false;

            Projectile projectile = Rent();
            if (projectile == null)
            {
                RefusedCount++;
                Debug.LogWarning($"ProjectilePool: at capacity ({maxSize}); shot dropped rather than allocating mid-combat.");
                return false;
            }

            _activeCount++;
            projectile.Launch(this, _combat, origin, target.WorldPosition, target, damage, sourceTeam, speed, maxLifetime);
            return true;
        }

        internal void Return(Projectile projectile)
        {
            if (projectile == null) return;
            if (_idle.Contains(projectile)) return;

            projectile.ResetForPool();
            projectile.gameObject.SetActive(false);
            _idle.Push(projectile);
            _activeCount = Mathf.Max(0, _activeCount - 1);
        }

        private Projectile Rent()
        {
            while (_idle.Count > 0)
            {
                Projectile candidate = _idle.Pop();
                if (candidate != null) return candidate;
            }

            if (_all.Count < maxSize) return CreateProjectile();
            return null;
        }

        private Projectile CreateProjectile()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Projectile_{_all.Count:000}";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * visualScale;

            // Projectiles must never be pickable — a stray raycast would break tap-to-select.
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                // Prewarming happens in edit mode too (bootstrap, tests), where Destroy is illegal.
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && _material != null) renderer.sharedMaterial = _material;

            var projectile = go.AddComponent<Projectile>();
            go.SetActive(false);
            _all.Add(projectile);
            _idle.Push(projectile);
            return projectile;
        }

        private static Material CreateProjectileMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            var material = new Material(shader) { name = "ProjectileRuntime" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(1f, 0.86f, 0.4f));
            else if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(1f, 0.86f, 0.4f));
            return material;
        }
    }
}
