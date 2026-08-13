namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Every world setting that changes how the rebuilt world behaves.
    /// <para>
    /// These live in the artifact rather than in two config files because a client and a
    /// server that disagree about gravity or substeps diverge in a way that looks like a
    /// physics bug and is nearly impossible to attribute. If it affects the simulation, it
    /// is baked, and the loader checks it.
    /// </para>
    /// </summary>
    public sealed class PhysicsWorldSettings
    {
        /// <summary>Default settings used by tests and by a freshly created world profile.</summary>
        public static PhysicsWorldSettings Default => new PhysicsWorldSettings(
            new PhysicsVector3(0f, -9.81f, 0f),
            tickRate: 30,
            substepCount: 1,
            solverIterations: 6,
            relaxationIterations: 4,
            allowDeactivation: true);

        /// <summary>World gravity.</summary>
        public PhysicsVector3 Gravity { get; }

        /// <summary>
        /// Fixed tick rate the level is authored for. The loader compares it with the
        /// consumer's tick rate: the same geometry stepped at a different rate is a different
        /// simulation, and silently accepting it hides the cause of client/server drift.
        /// </summary>
        public int TickRate { get; }

        /// <summary>Number of substeps per step.</summary>
        public int SubstepCount { get; }

        /// <summary>Solver iterations per substep.</summary>
        public int SolverIterations { get; }

        /// <summary>Relaxation iterations per substep.</summary>
        public int RelaxationIterations { get; }

        /// <summary>Whether bodies may deactivate.</summary>
        public bool AllowDeactivation { get; }

        /// <summary>
        /// Deterministic solve mode is the only supported value, and multi-threading is
        /// always off. Both are invariants of prediction rather than tuning knobs, so they
        /// are constants here instead of authored fields that someone can get wrong.
        /// </summary>
        public const bool MultiThreaded = false;

        /// <summary>Solve mode marker written to the artifact; only deterministic is valid.</summary>
        public const byte DeterministicSolveMode = 1;

        public PhysicsWorldSettings(
            PhysicsVector3 gravity,
            int tickRate,
            int substepCount,
            int solverIterations,
            int relaxationIterations,
            bool allowDeactivation)
        {
            Gravity = gravity;
            TickRate = tickRate;
            SubstepCount = substepCount;
            SolverIterations = solverIterations;
            RelaxationIterations = relaxationIterations;
            AllowDeactivation = allowDeactivation;
        }
    }
}
