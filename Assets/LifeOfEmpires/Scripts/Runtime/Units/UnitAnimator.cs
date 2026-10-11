using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// Procedural unit animation. The authored FBX models carry no rigs and no clips, so life is
    /// written directly onto the <c>Body</c> child's transform: a bob and sway while moving, a slow
    /// breath while idle, a short forward lunge on attack. Offsets are always computed relative to
    /// the captured base transform — nothing ever accumulates, so the animation cannot drift the
    /// unit away from its agent's real position.
    ///
    /// Only <c>Body</c> is touched. The unit root — which owns the collider, the agent, and the
    /// <see cref="HealthBar"/> offset — is never written; the selection ring lives outside
    /// <c>Body</c> and is untouched too.
    /// </summary>
    [RequireComponent(typeof(UnitController))]
    public class UnitAnimator : MonoBehaviour
    {
        [Header("Movement")]
        [Min(0f)] public float bobAmplitude = 0.06f;
        [Min(0f)] public float bobFrequency = 7f;
        [Range(0f, 45f)] public float swayDegrees = 5f;

        [Header("Attack")]
        [Min(0f)] public float attackLunge = 0.22f;
        [Min(0.01f)] public float attackReturnSeconds = 0.16f;

        /// <summary>The visual child being animated. Never the unit root.</summary>
        public Transform Body { get; private set; }

        private UnitController _controller;
        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation;
        private bool _bound;
        private float _time;
        private UnitState _lastState = UnitState.Idle;
        private float _lungeElapsed = -1f;
        private bool _dying;

        /// <summary>
        /// Find the <c>Body</c> child and capture its authored base transform. Idempotent — a second
        /// call never re-captures mid-animation values.
        /// </summary>
        public void Bind()
        {
            if (_bound) return;
            _bound = true;

            _controller = GetComponentInChildren<UnitController>();
            Body = transform.Find("Body");
            if (Body == null)
            {
                // Primitive fallback path and future models without a named child: animate the first
                // rendered child rather than the unit root itself.
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform child = transform.GetChild(i);
                    if (child.GetComponentInChildren<Renderer>() != null)
                    {
                        Body = child;
                        break;
                    }
                }
            }

            if (Body != null)
            {
                _baseLocalPosition = Body.localPosition;
                _baseLocalRotation = Body.localRotation;
            }
        }

        /// <summary>
        /// The death pose: a tip-over and a slight sink held until the scheduled destroy lands.
        /// Safe from <c>DestroyImmediate</c> in EditMode — outside play mode it does nothing.
        /// </summary>
        public void PlayDeath()
        {
            if (!Application.isPlaying) return;
            _dying = true;
        }

        /// <summary>
        /// Advance the animation by <paramref name="dt"/> seconds. <see cref="Update"/> drives this
        /// with frame time; tests drive it directly so no assertion depends on a rendered frame.
        /// </summary>
        public void Tick(float dt)
        {
            if (!_bound) Bind();
            if (Body == null) return;

            _time += dt;

            if (_dying)
            {
                // Held, not accumulated: the object is already scheduled for destruction, so the
                // pose only needs to read as "fallen" for the remaining frames.
                Body.localPosition = _baseLocalPosition + new Vector3(0f, -0.1f, 0f);
                Body.localRotation = _baseLocalRotation * Quaternion.Euler(0f, 0f, 70f);
                return;
            }

            if (_controller == null || _controller.IsDead)
            {
                return;
            }

            UnitState state = _controller.State;
            if (state != _lastState)
            {
                // A fresh attack winds the lunge clock; any other transition just switches pose.
                if (state == UnitState.Attacking) _lungeElapsed = 0f;
                _lastState = state;
            }

            float bobY = 0f;
            float lungeZ = 0f;
            float swayZ = 0f;

            switch (state)
            {
                case UnitState.Moving:
                {
                    float stride = _controller.IsCombatUnit ? 1f : 0.7f;
                    bobY = Mathf.Abs(Mathf.Sin(_time * bobFrequency)) * bobAmplitude * stride;
                    swayZ = Mathf.Sin(_time * bobFrequency * 0.5f) * swayDegrees * stride;
                    break;
                }
                case UnitState.Attacking:
                {
                    if (_lungeElapsed >= 0f)
                    {
                        _lungeElapsed += dt;
                        float progress = _lungeElapsed / attackReturnSeconds;
                        if (progress >= 1f)
                        {
                            _lungeElapsed = -1f;
                        }
                        else
                        {
                            // Out and back on one sine lobe — the strike reads as a motion, not a slide.
                            lungeZ = Mathf.Sin(progress * Mathf.PI) * attackLunge;
                        }
                    }
                    break;
                }
                case UnitState.Idle:
                {
                    // Breathing: a slow quarter-amplitude rise that never fully flattens to zero.
                    bobY = (Mathf.Sin(_time * 2.1f) * 0.5f + 0.5f) * bobAmplitude * 0.25f;
                    break;
                }
            }

            Body.localPosition = _baseLocalPosition + new Vector3(0f, bobY, lungeZ);
            Body.localRotation = _baseLocalRotation * Quaternion.Euler(0f, 0f, swayZ);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }
    }
}
