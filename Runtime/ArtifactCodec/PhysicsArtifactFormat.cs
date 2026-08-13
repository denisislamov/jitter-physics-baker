namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// The on-disk shape of an artifact, fixed by the golden-bytes test.
    /// <para>
    /// Layout of schema 1, little-endian throughout:
    /// </para>
    /// <code>
    /// magic                    4 bytes  "JPHY"
    /// schemaVersion            uint16
    /// reserved                 uint16   must be 0
    /// runtimeCompatibilityId  32 bytes  raw SHA-256 digest
    /// levelId                  string   uint16 byte length + UTF-8
    /// gravity                  3 x float32
    /// tickRate                 int32
    /// substepCount             int32
    /// solverIterations         int32
    /// relaxationIterations     int32
    /// allowDeactivation        uint8    0 or 1
    /// solveMode                uint8    1 = deterministic, the only accepted value
    /// multiThreaded            uint8    always 0
    /// bodyCount                int32
    ///   sourceId               string
    ///   position               3 x float32
    ///   orientation            4 x float32   (x, y, z, w), normalized and sign-canonical
    ///   friction               float32
    ///   restitution            float32
    ///   shapeCount             int32
    ///     shapeKey             string
    ///     shapeType            uint8
    ///     localPosition        3 x float32
    ///     localRotation        4 x float32
    ///     Box:      size       3 x float32
    ///     Sphere:   radius     float32
    ///     Capsule:  radius     float32, length float32
    ///     Mesh:     vertexCount int32, vertices 3 x float32 each,
    ///               indexCount  int32, indices int32 each
    /// </code>
    /// <para>
    /// Any change to this layout after it has been merged requires a schema version bump —
    /// silently reinterpreting old bytes is how a "small format tweak" becomes geometry that
    /// is subtly wrong on one side of the network.
    /// </para>
    /// </summary>
    internal static class PhysicsArtifactFormat
    {
        /// <summary>Magic bytes: <c>J P H Y</c>.</summary>
        internal static readonly byte[] Magic = { 0x4A, 0x50, 0x48, 0x59 };

        /// <summary>Length of the raw compatibility digest embedded in the header.</summary>
        internal const int CompatibilityIdBytes = 32;

        /// <summary>Reserved header word, kept for a future flags field; must be zero.</summary>
        internal const ushort Reserved = 0;

        /// <summary>Marker for a world that is not multi-threaded; the only accepted value.</summary>
        internal const byte SingleThreaded = 0;
    }
}
