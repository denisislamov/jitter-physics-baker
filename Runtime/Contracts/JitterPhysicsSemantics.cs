namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Versions of the runtime semantics that the artifact format cannot express.
    /// <para>
    /// Two builds can agree on every byte of the format and still build different worlds —
    /// if one of them changed how a capsule's length is derived, how shapes are constructed,
    /// or what a world defaults to. Those changes are invisible to the schema version, so
    /// each of them is versioned here and folded into the runtime compatibility id.
    /// </para>
    /// <para>
    /// Rule: change the behaviour, bump the number in the same commit. A stale number is
    /// worse than no number, because it makes an incompatible pair look compatible.
    /// </para>
    /// </summary>
    public static class JitterPhysicsSemantics
    {
        /// <summary>
        /// Version of the Unity collider to shape descriptor conversion: box/sphere/capsule
        /// scaling rules, capsule axis handling, mesh transform and winding policy.
        /// </summary>
        public const int ColliderConversionVersion = 1;

        /// <summary>
        /// Version of the descriptor to Jitter shape construction: which shape types are
        /// created and how a local pose is applied to them.
        /// </summary>
        public const int ShapeConstructionVersion = 1;

        /// <summary>
        /// Version of the static world builder: body creation order, motion type, material
        /// assignment and the guard against applying an artifact twice.
        /// </summary>
        public const int WorldBuilderVersion = 1;

        /// <summary>
        /// Version of the world-affecting defaults the package applies when the artifact does
        /// not carry a value for them.
        /// </summary>
        public const int WorldDefaultsVersion = 1;

        /// <summary>
        /// Floating point precision of the Jitter build the package integrates with. Mixing a
        /// single precision client with a double precision server is a silent divergence, so
        /// the mode participates in the compatibility id.
        /// </summary>
        public const string PrecisionMode = "f32";
    }
}
