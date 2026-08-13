using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace DataSakura.JitterPhysics.Authoring
{
    /// <summary>
    /// The world settings a level is baked for, authored once and shared by every consumer
    /// of the artifact.
    /// <para>
    /// These values live in an asset rather than in two configuration files because a client
    /// and a server that disagree about gravity or substeps diverge in a way that looks like
    /// a physics bug and is nearly impossible to attribute. Whatever is set here is written
    /// into the artifact and re-checked when it is loaded.
    /// </para>
    /// <para>
    /// Deterministic solving and single-threaded stepping are deliberately absent: they are
    /// invariants of prediction, not tuning knobs, so they are constants of the format
    /// instead of fields somebody can get wrong.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        fileName = "JitterPhysicsWorldProfile",
        menuName = "Jitter Physics/World Profile",
        order = JitterPhysicsAuthoringConstants.LevelMenuOrder)]
    public sealed class JitterPhysicsWorldProfile : ScriptableObject
    {
        [Header("World")]
        [SerializeField]
        [Tooltip("Gravity applied to the rebuilt world. Baked into the artifact.")]
        private Vector3 gravity = new Vector3(0f, -9.81f, 0f);

        [Header("Stepping")]
        [SerializeField]
        [Tooltip(
            "Fixed tick rate this level is authored for. The loader compares it with the "
            + "consumer's tick rate: the same geometry stepped at a different rate is a "
            + "different simulation.")]
        [Range(PhysicsArtifactLimits.MinTickRate, PhysicsArtifactLimits.MaxTickRate)]
        private int tickRate = 30;

        [SerializeField]
        [Tooltip("Substeps per step.")]
        [Range(1, PhysicsArtifactLimits.MaxSubstepCount)]
        private int substepCount = 1;

        [Header("Solver")]
        [SerializeField]
        [Range(1, PhysicsArtifactLimits.MaxIterations)]
        private int solverIterations = 6;

        [SerializeField]
        [Range(0, PhysicsArtifactLimits.MaxIterations)]
        private int relaxationIterations = 4;

        [SerializeField]
        [Tooltip("Whether bodies may deactivate. Baked, so both sides agree.")]
        private bool allowDeactivation = true;

        /// <summary>Gravity applied to the rebuilt world.</summary>
        public Vector3 Gravity => gravity;

        /// <summary>Fixed tick rate this level is authored for.</summary>
        public int TickRate => tickRate;

        /// <summary>Substeps per step.</summary>
        public int SubstepCount => substepCount;

        /// <summary>Solver iterations per substep.</summary>
        public int SolverIterations => solverIterations;

        /// <summary>Relaxation iterations per substep.</summary>
        public int RelaxationIterations => relaxationIterations;

        /// <summary>Whether bodies may deactivate.</summary>
        public bool AllowDeactivation => allowDeactivation;

        /// <summary>Converts the authored values into the portable settings record.</summary>
        public PhysicsWorldSettings ToWorldSettings()
        {
            return new PhysicsWorldSettings(
                new PhysicsVector3(gravity.x, gravity.y, gravity.z).Canonical(),
                tickRate,
                substepCount,
                solverIterations,
                relaxationIterations,
                allowDeactivation);
        }

        private void OnValidate()
        {
            // The ranges above already constrain the sliders, but a value can also arrive
            // through a script or a merged asset, and an out-of-range setting must not reach
            // the codec as a bake failure that points at the wrong place.
            tickRate = Mathf.Clamp(
                tickRate, PhysicsArtifactLimits.MinTickRate, PhysicsArtifactLimits.MaxTickRate);
            substepCount = Mathf.Clamp(substepCount, 1, PhysicsArtifactLimits.MaxSubstepCount);
            solverIterations = Mathf.Clamp(solverIterations, 1, PhysicsArtifactLimits.MaxIterations);
            relaxationIterations = Mathf.Clamp(
                relaxationIterations, 0, PhysicsArtifactLimits.MaxIterations);

            if (!IsFinite(gravity))
            {
                gravity = new Vector3(0f, -9.81f, 0f);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return PhysicsCanonicalization.IsFinite(value.x)
                && PhysicsCanonicalization.IsFinite(value.y)
                && PhysicsCanonicalization.IsFinite(value.z);
        }
    }
}

