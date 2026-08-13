using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// Decodes an artifact payload, strictly and fail-fast.
    /// <para>
    /// The order of checks matters and is deliberate: the hash is verified <em>before</em>
    /// the payload is parsed, so a corrupted or substituted file is rejected without its
    /// counts ever being trusted. Then the header is checked, then the records are decoded
    /// against hard caps, then the semantic validator runs, and only then is a manifest
    /// cross-check performed. Nothing is returned unless every step passed.
    /// </para>
    /// </summary>
    public static class PhysicsArtifactReader
    {
        /// <summary>
        /// Decodes a payload. <paramref name="expectedHash"/> and <paramref name="manifest"/>
        /// are optional; when supplied they are enforced rather than assumed.
        /// </summary>
        public static PhysicsArtifactResult Read(
            byte[] payload,
            string expectedHash = null,
            PhysicsArtifactManifest manifest = null)
        {
            if (payload == null || payload.Length == 0)
            {
                return PhysicsArtifactResult.Failure(
                    PhysicsArtifactErrorCode.EmptyPayload,
                    "Artifact payload is empty.");
            }

            if (payload.Length > PhysicsArtifactLimits.MaxArtifactBytes)
            {
                return PhysicsArtifactResult.Failure(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"Artifact payload is {payload.Length} bytes, over the limit of "
                    + PhysicsArtifactLimits.MaxArtifactBytes + ".");
            }

            string actualHash = JitterPhysicsHash.Sha256Hex(payload);

            if (expectedHash != null && !JitterPhysicsHash.HexEquals(actualHash, expectedHash))
            {
                return PhysicsArtifactResult.Failure(
                    PhysicsArtifactErrorCode.HashMismatch,
                    $"Artifact hash mismatch: expected {expectedHash}, payload hashes to {actualHash}.",
                    manifest?.LevelId,
                    actualHash);
            }

            if (manifest != null && !JitterPhysicsHash.HexEquals(actualHash, manifest.ArtifactHash))
            {
                return PhysicsArtifactResult.Failure(
                    PhysicsArtifactErrorCode.ManifestMismatch,
                    $"Manifest describes artifact {manifest.ArtifactHash}, payload hashes to {actualHash}.",
                    manifest.LevelId,
                    actualHash);
            }

            PhysicsArtifact artifact;
            try
            {
                artifact = Decode(payload);
            }
            catch (CanonicalBinaryException exception)
            {
                return PhysicsArtifactResult.Failure(
                    exception.Code,
                    exception.Message,
                    manifest?.LevelId,
                    actualHash);
            }

            PhysicsArtifactError validationError = PhysicsArtifactValidator.Validate(artifact);
            if (validationError.IsError)
            {
                return PhysicsArtifactResult.Failure(
                    validationError.Code,
                    validationError.Message,
                    artifact.LevelId,
                    actualHash);
            }

            if (manifest != null)
            {
                PhysicsArtifactError manifestError = CrossCheckManifest(artifact, manifest, actualHash);
                if (manifestError.IsError)
                {
                    return PhysicsArtifactResult.Failure(
                        manifestError.Code,
                        manifestError.Message,
                        artifact.LevelId,
                        actualHash);
                }
            }

            return PhysicsArtifactResult.Success(artifact);
        }

        /// <summary>
        /// Confirms that the artifact was baked for the runtime semantics of this build.
        /// Kept separate from <see cref="Read"/> because a tool may legitimately inspect an
        /// artifact it cannot run, while a loader may not.
        /// </summary>
        public static PhysicsArtifactError CheckRuntimeCompatibility(
            PhysicsArtifact artifact,
            string expectedRuntimeCompatibilityId)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            if (JitterPhysicsHash.HexEquals(artifact.RuntimeCompatibilityId, expectedRuntimeCompatibilityId))
            {
                return default;
            }

            return new PhysicsArtifactError(
                PhysicsArtifactErrorCode.IncompatibleRuntime,
                $"Artifact was baked for runtime {Short(artifact.RuntimeCompatibilityId)}, this build is "
                + Short(expectedRuntimeCompatibilityId) + ".",
                artifact.LevelId);
        }

        private static PhysicsArtifact Decode(byte[] payload)
        {
            var reader = new CanonicalBinaryReader(payload);

            byte[] magic = reader.ReadBytes(PhysicsArtifactFormat.Magic.Length);
            for (int i = 0; i < magic.Length; i++)
            {
                if (magic[i] != PhysicsArtifactFormat.Magic[i])
                {
                    throw new CanonicalBinaryException(
                        PhysicsArtifactErrorCode.BadMagic,
                        "Payload does not start with the artifact magic and is not a Jitter physics artifact.");
                }
            }

            int schemaVersion = reader.ReadUInt16();
            if (schemaVersion != JitterPhysicsPackage.ArtifactSchemaVersion)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.UnsupportedSchema,
                    $"Artifact schema {schemaVersion} is not supported; this build reads "
                    + JitterPhysicsPackage.ArtifactSchemaVersion + ".");
            }

            ushort reserved = reader.ReadUInt16();
            if (reserved != PhysicsArtifactFormat.Reserved)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Reserved header field is {reserved}, expected 0.");
            }

            string runtimeCompatibilityId = JitterPhysicsHash.ToHex(
                reader.ReadBytes(PhysicsArtifactFormat.CompatibilityIdBytes));
            string levelId = reader.ReadString("Level id");

            PhysicsVector3 gravity = reader.ReadVector3();
            int tickRate = reader.ReadInt32();
            int substepCount = reader.ReadInt32();
            int solverIterations = reader.ReadInt32();
            int relaxationIterations = reader.ReadInt32();

            byte allowDeactivation = reader.ReadByte();
            if (allowDeactivation > 1)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Deactivation flag is {allowDeactivation}, expected 0 or 1.");
            }

            byte solveMode = reader.ReadByte();
            if (solveMode != PhysicsWorldSettings.DeterministicSolveMode)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Solve mode {solveMode} is not deterministic; prediction requires the deterministic solver.");
            }

            byte multiThreaded = reader.ReadByte();
            if (multiThreaded != PhysicsArtifactFormat.SingleThreaded)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.InvalidValue,
                    "Artifact requests a multi-threaded world, which breaks the prediction invariant.");
            }

            var settings = new PhysicsWorldSettings(
                gravity,
                tickRate,
                substepCount,
                solverIterations,
                relaxationIterations,
                allowDeactivation == 1);

            int bodyCount = reader.ReadCount("Body", PhysicsArtifactLimits.MaxBodies);
            var bodies = new List<PhysicsBodyRecord>(bodyCount);
            int totalShapes = 0;
            int totalVertices = 0;
            int totalIndices = 0;

            for (int i = 0; i < bodyCount; i++)
            {
                bodies.Add(ReadBody(reader, ref totalShapes, ref totalVertices, ref totalIndices));
            }

            reader.RequireEndOfPayload();

            return new PhysicsArtifact(
                schemaVersion,
                runtimeCompatibilityId,
                levelId,
                settings,
                bodies);
        }

        private static PhysicsBodyRecord ReadBody(
            CanonicalBinaryReader reader,
            ref int totalShapes,
            ref int totalVertices,
            ref int totalIndices)
        {
            string sourceId = reader.ReadString("Body id");
            PhysicsVector3 position = reader.ReadVector3();
            PhysicsQuaternion orientation = reader.ReadQuaternion();
            float friction = reader.ReadSingle();
            float restitution = reader.ReadSingle();

            int shapeCount = reader.ReadCount("Shape", PhysicsArtifactLimits.MaxShapesPerBody);
            totalShapes += shapeCount;
            if (totalShapes > PhysicsArtifactLimits.MaxShapes)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"Level exceeds the total shape limit of {PhysicsArtifactLimits.MaxShapes}.");
            }

            var shapes = new List<PhysicsShapeRecord>(shapeCount);
            for (int i = 0; i < shapeCount; i++)
            {
                shapes.Add(ReadShape(reader, ref totalVertices, ref totalIndices));
            }

            return new PhysicsBodyRecord(sourceId, position, orientation, friction, restitution, shapes);
        }

        private static PhysicsShapeRecord ReadShape(
            CanonicalBinaryReader reader,
            ref int totalVertices,
            ref int totalIndices)
        {
            string shapeKey = reader.ReadString("Shape key");
            var shapeType = (PhysicsShapeType)reader.ReadByte();
            PhysicsVector3 localPosition = reader.ReadVector3();
            PhysicsQuaternion localRotation = reader.ReadQuaternion();

            switch (shapeType)
            {
                case PhysicsShapeType.Box:
                    return PhysicsShapeRecord.Box(shapeKey, localPosition, localRotation, reader.ReadVector3());

                case PhysicsShapeType.Sphere:
                    return PhysicsShapeRecord.Sphere(shapeKey, localPosition, localRotation, reader.ReadSingle());

                case PhysicsShapeType.Capsule:
                {
                    float radius = reader.ReadSingle();
                    float length = reader.ReadSingle();
                    return PhysicsShapeRecord.Capsule(shapeKey, localPosition, localRotation, radius, length);
                }

                case PhysicsShapeType.Mesh:
                {
                    int vertexCount = reader.ReadCount("Vertex", PhysicsArtifactLimits.MaxVerticesPerMesh);
                    totalVertices += vertexCount;
                    if (totalVertices > PhysicsArtifactLimits.MaxVertices)
                    {
                        throw new CanonicalBinaryException(
                            PhysicsArtifactErrorCode.LimitExceeded,
                            $"Level exceeds the total vertex limit of {PhysicsArtifactLimits.MaxVertices}.");
                    }

                    var vertices = new PhysicsVector3[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                    {
                        vertices[i] = reader.ReadVector3();
                    }

                    int indexCount = reader.ReadCount("Index", PhysicsArtifactLimits.MaxIndicesPerMesh);
                    totalIndices += indexCount;
                    if (totalIndices > PhysicsArtifactLimits.MaxIndices)
                    {
                        throw new CanonicalBinaryException(
                            PhysicsArtifactErrorCode.LimitExceeded,
                            $"Level exceeds the total index limit of {PhysicsArtifactLimits.MaxIndices}.");
                    }

                    var indices = new int[indexCount];
                    for (int i = 0; i < indexCount; i++)
                    {
                        indices[i] = reader.ReadInt32();
                    }

                    return PhysicsShapeRecord.Mesh(shapeKey, localPosition, localRotation, vertices, indices);
                }

                default:
                    throw new CanonicalBinaryException(
                        PhysicsArtifactErrorCode.InvalidValue,
                        $"Shape '{shapeKey}' has unsupported type {(byte)shapeType}.");
            }
        }

        private static PhysicsArtifactError CrossCheckManifest(
            PhysicsArtifact artifact,
            PhysicsArtifactManifest manifest,
            string actualHash)
        {
            if (!string.Equals(manifest.LevelId, artifact.LevelId, StringComparison.Ordinal))
            {
                return Mismatch($"level id '{manifest.LevelId}' vs '{artifact.LevelId}'", artifact, actualHash);
            }

            if (!JitterPhysicsHash.HexEquals(manifest.RuntimeCompatibilityId, artifact.RuntimeCompatibilityId))
            {
                return Mismatch(
                    $"runtime id {Short(manifest.RuntimeCompatibilityId)} vs {Short(artifact.RuntimeCompatibilityId)}",
                    artifact,
                    actualHash);
            }

            if (manifest.BodyCount != artifact.Bodies.Count)
            {
                return Mismatch($"body count {manifest.BodyCount} vs {artifact.Bodies.Count}", artifact, actualHash);
            }

            if (manifest.ShapeCount != artifact.ShapeCount)
            {
                return Mismatch($"shape count {manifest.ShapeCount} vs {artifact.ShapeCount}", artifact, actualHash);
            }

            if (manifest.VertexCount != artifact.VertexCount)
            {
                return Mismatch($"vertex count {manifest.VertexCount} vs {artifact.VertexCount}", artifact, actualHash);
            }

            if (manifest.TriangleCount != artifact.TriangleCount)
            {
                return Mismatch(
                    $"triangle count {manifest.TriangleCount} vs {artifact.TriangleCount}",
                    artifact,
                    actualHash);
            }

            if (manifest.TickRate != artifact.WorldSettings.TickRate)
            {
                return Mismatch(
                    $"tick rate {manifest.TickRate} vs {artifact.WorldSettings.TickRate}",
                    artifact,
                    actualHash);
            }

            return default;
        }

        private static PhysicsArtifactError Mismatch(string detail, PhysicsArtifact artifact, string hash)
        {
            return new PhysicsArtifactError(
                PhysicsArtifactErrorCode.ManifestMismatch,
                "Manifest disagrees with the payload: " + detail + ".",
                artifact.LevelId,
                hash);
        }

        private static string Short(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "<none>";
            }

            return hash.Length >= JitterPhysicsArtifactNaming.ShortHashLength
                ? hash.Substring(0, JitterPhysicsArtifactNaming.ShortHashLength)
                : hash;
        }
    }
}
