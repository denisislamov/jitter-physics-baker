using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace DataSakura.JitterPhysics.UnityArtifact
{
    /// <summary>
    /// The Unity-side handle for one baked level: a reference a scene or a loader can hold,
    /// pointing at the immutable payload and repeating its identity for inspection.
    /// <para>
    /// The payload itself lives in a separate <see cref="TextAsset"/> rather than inside this
    /// object. Unity's serializer rewrites what it owns — it reorders, reformats and upgrades
    /// across versions — and an artifact whose bytes are not exactly the bytes that were
    /// hashed is not an artifact. A <c>.bytes</c> file is copied verbatim, so the payload a
    /// client loads is bit-for-bit the payload a server was given.
    /// </para>
    /// <para>
    /// Every field here is a <em>copy</em> of what the payload already states. They exist for
    /// the inspector and for cheap pre-checks; none of them is trusted. The loader re-hashes
    /// the payload and re-reads its header, because a metadata field is exactly what a
    /// careless merge or a manual edit changes first.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsArtifactAsset : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Immutable binary payload. Stored as a separate .bytes file so Unity copies it verbatim.")]
        private TextAsset payload;

        [SerializeField]
        [Tooltip("Canonical level id this artifact describes.")]
        private string levelId = string.Empty;

        [SerializeField]
        [Tooltip("SHA-256 of the payload. Re-verified at load time; never trusted on its own.")]
        private string artifactHash = string.Empty;

        [SerializeField]
        [Tooltip("Runtime semantics id the artifact was baked for.")]
        private string runtimeCompatibilityId = string.Empty;

        [SerializeField]
        [Tooltip("Artifact schema version.")]
        private int schemaVersion;

        [SerializeField]
        [Tooltip("Tick rate the level was authored for.")]
        private int tickRate;

        [SerializeField]
        private int bodyCount;

        [SerializeField]
        private int shapeCount;

        [SerializeField]
        private int vertexCount;

        [SerializeField]
        private int triangleCount;

        [SerializeField]
        [Tooltip("Package version that produced this artifact.")]
        private string generatorVersion = string.Empty;

        /// <summary>The immutable binary payload asset.</summary>
        public TextAsset Payload => payload;

        /// <summary>Canonical level id this artifact describes.</summary>
        public string LevelId => levelId;

        /// <summary>SHA-256 of the payload, as recorded at bake time.</summary>
        public string ArtifactHash => artifactHash;

        /// <summary>Runtime semantics id the artifact was baked for.</summary>
        public string RuntimeCompatibilityId => runtimeCompatibilityId;

        /// <summary>Artifact schema version.</summary>
        public int SchemaVersion => schemaVersion;

        /// <summary>Tick rate the level was authored for.</summary>
        public int TickRate => tickRate;

        /// <summary>Number of static bodies.</summary>
        public int BodyCount => bodyCount;

        /// <summary>Number of shapes.</summary>
        public int ShapeCount => shapeCount;

        /// <summary>Number of mesh vertices.</summary>
        public int VertexCount => vertexCount;

        /// <summary>Number of mesh triangles.</summary>
        public int TriangleCount => triangleCount;

        /// <summary>Package version that produced this artifact.</summary>
        public string GeneratorVersion => generatorVersion;

        /// <summary>Short hash used in logs and file names.</summary>
        public string ShortHash =>
            !string.IsNullOrEmpty(artifactHash)
            && artifactHash.Length >= JitterPhysicsArtifactNaming.ShortHashLength
                ? artifactHash.Substring(0, JitterPhysicsArtifactNaming.ShortHashLength)
                : artifactHash;

        /// <summary>True when a payload asset is assigned and not empty.</summary>
        public bool HasPayload => payload != null && payload.bytes != null && payload.bytes.Length > 0;

        /// <summary>
        /// Returns the raw payload bytes, or <c>null</c> when none is assigned.
        /// <para>
        /// The caller is expected to hand these to the reader together with
        /// <see cref="ArtifactHash"/>, which makes the reader verify them. Reading the bytes
        /// without that check would defeat the purpose of content addressing.
        /// </para>
        /// </summary>
        public byte[] GetPayloadBytes()
        {
            return payload != null ? payload.bytes : null;
        }

        /// <summary>
        /// Fills the metadata from a manifest and links the payload. Editor-only in practice;
        /// it is public so that the baking assembly can populate the asset without reflection.
        /// </summary>
        public void Initialize(PhysicsArtifactManifest manifest, TextAsset payloadAsset)
        {
            if (manifest == null)
            {
                throw new System.ArgumentNullException(nameof(manifest));
            }

            payload = payloadAsset;
            levelId = manifest.LevelId;
            artifactHash = manifest.ArtifactHash;
            runtimeCompatibilityId = manifest.RuntimeCompatibilityId;
            tickRate = manifest.TickRate;
            bodyCount = manifest.BodyCount;
            shapeCount = manifest.ShapeCount;
            vertexCount = manifest.VertexCount;
            triangleCount = manifest.TriangleCount;
            generatorVersion = manifest.GeneratorVersion;

            schemaVersion = int.TryParse(
                manifest.SchemaVersion,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsedSchema)
                ? parsedSchema
                : 0;
        }
    }
}

