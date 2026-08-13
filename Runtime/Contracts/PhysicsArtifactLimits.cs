namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Hard safety caps applied while decoding, before anything is allocated.
    /// <para>
    /// A loader that trusts the counts in a file will happily allocate gigabytes when the
    /// file is corrupt or hostile. Every count is therefore checked against a cap first, and
    /// the caps are part of the contract so that the client, the server and the tests refuse
    /// exactly the same inputs.
    /// </para>
    /// </summary>
    public static class PhysicsArtifactLimits
    {
        /// <summary>Largest accepted artifact payload.</summary>
        public const int MaxArtifactBytes = 64 * 1024 * 1024;

        /// <summary>Largest accepted number of static bodies in one level.</summary>
        public const int MaxBodies = 65_536;

        /// <summary>Largest accepted number of shapes on one body.</summary>
        public const int MaxShapesPerBody = 4_096;

        /// <summary>Largest accepted number of shapes in one level.</summary>
        public const int MaxShapes = 262_144;

        /// <summary>Largest accepted number of vertices in one mesh shape.</summary>
        public const int MaxVerticesPerMesh = 1_000_000;

        /// <summary>Largest accepted number of indices in one mesh shape.</summary>
        public const int MaxIndicesPerMesh = 3_000_000;

        /// <summary>Largest accepted number of vertices in one level.</summary>
        public const int MaxVertices = 4_000_000;

        /// <summary>Largest accepted number of indices in one level.</summary>
        public const int MaxIndices = 12_000_000;

        /// <summary>Largest accepted length of an id or a shape key, in UTF-8 bytes.</summary>
        public const int MaxStringBytes = 512;

        /// <summary>Lowest accepted tick rate.</summary>
        public const int MinTickRate = 1;

        /// <summary>Highest accepted tick rate.</summary>
        public const int MaxTickRate = 1_000;

        /// <summary>Highest accepted substep count.</summary>
        public const int MaxSubstepCount = 64;

        /// <summary>Highest accepted solver or relaxation iteration count.</summary>
        public const int MaxIterations = 256;

        /// <summary>
        /// Largest coordinate magnitude accepted for a position or a vertex. Beyond this,
        /// float precision degrades enough that the same geometry behaves differently on two
        /// runtimes, so the artifact is rejected rather than quietly imprecise.
        /// </summary>
        public const float MaxCoordinateMagnitude = 1e6f;

        /// <summary>Largest accepted extent of a primitive shape.</summary>
        public const float MaxShapeExtent = 1e5f;
    }
}
