using System;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// The sidecar description of a baked artifact: what it is, what it contains and what it
    /// hashes to.
    /// <para>
    /// The manifest is not a convenience — it is the cross-check. Counts written here are
    /// compared with the counts decoded from the binary, so a payload that was truncated,
    /// swapped or re-baked under a different name is caught before a world is built from it.
    /// Nondeterministic values such as a build timestamp are deliberately absent: they would
    /// make two identical bakes look different.
    /// </para>
    /// </summary>
    public sealed class PhysicsArtifactManifest
    {
        /// <summary>Schema version, written as a string to match the artifact manifest format.</summary>
        public string SchemaVersion { get; }

        /// <summary>Runtime semantics id the artifact was baked for.</summary>
        public string RuntimeCompatibilityId { get; }

        /// <summary>Version of the package that produced the artifact.</summary>
        public string GeneratorVersion { get; }

        /// <summary>Canonical level id.</summary>
        public string LevelId { get; }

        /// <summary>Lowercase hex SHA-256 of the binary payload.</summary>
        public string ArtifactHash { get; }

        /// <summary>Number of static bodies in the payload.</summary>
        public int BodyCount { get; }

        /// <summary>Number of shapes in the payload.</summary>
        public int ShapeCount { get; }

        /// <summary>Number of mesh vertices in the payload.</summary>
        public int VertexCount { get; }

        /// <summary>Number of mesh triangles in the payload.</summary>
        public int TriangleCount { get; }

        /// <summary>Tick rate the level was authored for.</summary>
        public int TickRate { get; }

        /// <summary>File name of the binary payload this manifest belongs to.</summary>
        public string FileName { get; }

        public PhysicsArtifactManifest(
            string schemaVersion,
            string runtimeCompatibilityId,
            string generatorVersion,
            string levelId,
            string artifactHash,
            int bodyCount,
            int shapeCount,
            int vertexCount,
            int triangleCount,
            int tickRate,
            string fileName)
        {
            SchemaVersion = schemaVersion ?? throw new ArgumentNullException(nameof(schemaVersion));
            RuntimeCompatibilityId = runtimeCompatibilityId
                ?? throw new ArgumentNullException(nameof(runtimeCompatibilityId));
            GeneratorVersion = generatorVersion ?? throw new ArgumentNullException(nameof(generatorVersion));
            LevelId = levelId ?? throw new ArgumentNullException(nameof(levelId));
            ArtifactHash = artifactHash ?? throw new ArgumentNullException(nameof(artifactHash));
            BodyCount = bodyCount;
            ShapeCount = shapeCount;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            TickRate = tickRate;
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        }

        /// <summary>Builds the manifest that describes a decoded artifact and its payload hash.</summary>
        public static PhysicsArtifactManifest ForArtifact(
            PhysicsArtifact artifact,
            string artifactHash,
            string generatorVersion)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            return new PhysicsArtifactManifest(
                artifact.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                artifact.RuntimeCompatibilityId,
                generatorVersion,
                artifact.LevelId,
                artifactHash,
                artifact.Bodies.Count,
                artifact.ShapeCount,
                artifact.VertexCount,
                artifact.TriangleCount,
                artifact.WorldSettings.TickRate,
                JitterPhysicsArtifactNaming.BinaryFileName(artifact.LevelId, artifactHash));
        }
    }
}
