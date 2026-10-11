using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// A finite, depletable deposit of one resource — a tree, a farm plot, a gold vein. Workers read
    /// from it and write to a drop-off; the node itself only knows how much is left and refuses to
    /// hand out more than it has.
    /// </summary>
    public class ResourceNode : MonoBehaviour
    {
        [Header("Contents")]
        public ResourceType resourceType = ResourceType.Wood;

        [Tooltip("Total the node holds when play begins.")]
        [Min(1)] public int capacity = 100;

        [Tooltip("Current remaining amount. Runtime state; seeded from capacity on Awake.")]
        [SerializeField] private int remaining;

        [Tooltip("Metres from the node centre where a worker stands to gather.")]
        [Min(0.1f)] public float interactionRadius = 1.6f;

        [Tooltip("When true the node is removed from the world once depleted.")]
        public bool destroyWhenDepleted = true;

        /// <summary>Fired the first time <see cref="remaining"/> reaches zero.</summary>
        public event Action<ResourceNode> OnDepleted;

        /// <summary>
        /// Every live node, registered as it is enabled. Workers search this instead of the scene
        /// graph, so finding a replacement deposit is a list walk rather than a hierarchy traversal.
        /// </summary>
        public static readonly List<ResourceNode> Active = new List<ResourceNode>(64);

        public int Remaining => remaining;
        public bool IsDepleted => remaining <= 0;

        /// <summary>Fraction of the original deposit still in the ground, for the depletion tint.</summary>
        public float FillFraction => capacity <= 0 ? 0f : Mathf.Clamp01(remaining / (float)capacity);

        /// <summary>Where a worker should stand to reach this node.</summary>
        public Vector3 InteractionPoint => transform.position - (transform.forward * interactionRadius);

        private void Awake()
        {
            if (remaining <= 0) remaining = capacity;
            capacity = Mathf.Max(capacity, remaining);
        }

        /// <summary>
        /// Set the deposit's kind and size. Use this rather than assigning the fields directly: the
        /// serialised fields can otherwise disagree about how much is left, because Awake may already
        /// have seeded <c>remaining</c> from the previous capacity.
        /// </summary>
        public void Configure(ResourceType type, int amount, float radius = 1.6f)
        {
            resourceType = type;
            capacity = Mathf.Max(1, amount);
            remaining = capacity;
            interactionRadius = Mathf.Max(0.1f, radius);
        }

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        /// <summary>
        /// Nearest node of a given resource that still holds something, within
        /// <paramref name="maxDistance"/>. Returns null when the map is out of that resource.
        /// </summary>
        public static ResourceNode FindNearest(ResourceType type, Vector3 from, float maxDistance = float.MaxValue)
        {
            ResourceNode best = null;
            float bestSqr = maxDistance * maxDistance;

            for (int i = 0; i < Active.Count; i++)
            {
                ResourceNode node = Active[i];
                if (node == null || node.IsDepleted || node.resourceType != type) continue;
                if (!node.isActiveAndEnabled) continue;

                float sqr = (node.transform.position - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = node;
            }

            return best;
        }

        /// <summary>
        /// Take up to <paramref name="requested"/> units out of the node. Returns what was actually
        /// granted, which may be less than requested (or zero) when the node is nearly dry.
        /// </summary>
        public int Gather(int requested)
        {
            if (requested <= 0 || IsDepleted) return 0;

            int granted = Mathf.Min(requested, remaining);
            remaining -= granted;
            if (IsDepleted) HandleDepleted();
            return granted;
        }

        /// <summary>Hand resources back to the node, never exceeding capacity (e.g. worker reassigned mid-trip).</summary>
        public void Return(int amount)
        {
            if (amount <= 0) return;
            remaining = Mathf.Min(capacity, remaining + amount);
        }

        private void HandleDepleted()
        {
            OnDepleted?.Invoke(this);
            if (destroyWhenDepleted)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = resourceType == ResourceType.Gold ? new Color(1f, 0.85f, 0.2f, 0.35f) : new Color(0.3f, 0.8f, 0.3f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, interactionRadius);
        }
#endif
    }
}
