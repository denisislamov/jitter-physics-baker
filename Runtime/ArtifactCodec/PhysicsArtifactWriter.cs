using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// Encodes an artifact into its canonical binary form.
    /// <para>
    /// The writer is strict rather than forgiving: it refuses records that are out of order,
    /// duplicated or out of range instead of fixing them. Silently reordering would hide a
    /// bug in the baker, and a baker that produces a different order on a different machine
    /// is exactly the failure the deterministic format exists to prevent.
    /// </para>
    /// </summary>
    public static class PhysicsArtifactWriter
    {
        /// <summary>
        /// Encodes the artifact. Throws <see cref="ArgumentException"/> when the artifact is
        /// not canonical — that is a programming error in the baker, not untrusted input.
        /// </summary>
        public static byte[] Write(PhysicsArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            PhysicsArtifactError error = PhysicsArtifactValidator.Validate(artifact);
            if (error.IsError)
            {
                throw new ArgumentException(
                    "Refusing to write a non-canonical artifact: " + error, nameof(artifact));
            }

            var writer = new CanonicalBinaryWriter(EstimateSize(artifact));
            writer.WriteBytes(PhysicsArtifactFormat.Magic);
            writer.WriteUInt16((ushort)artifact.SchemaVersion);
            writer.WriteUInt16(PhysicsArtifactFormat.Reserved);
            writer.WriteBytes(HexToBytes(artifact.RuntimeCompatibilityId));
            writer.WriteString(artifact.LevelId);

            PhysicsWorldSettings settings = artifact.WorldSettings;
            writer.WriteVector3(settings.Gravity);
            writer.WriteInt32(settings.TickRate);
            writer.WriteInt32(settings.SubstepCount);
            writer.WriteInt32(settings.SolverIterations);
            writer.WriteInt32(settings.RelaxationIterations);
            writer.WriteByte(settings.AllowDeactivation ? (byte)1 : (byte)0);
            writer.WriteByte(PhysicsWorldSettings.DeterministicSolveMode);
            writer.WriteByte(PhysicsArtifactFormat.SingleThreaded);

            IReadOnlyList<PhysicsBodyRecord> bodies = artifact.Bodies;
            writer.WriteInt32(bodies.Count);
            for (int i = 0; i < bodies.Count; i++)
            {
                WriteBody(writer, bodies[i]);
            }

            return writer.ToArray();
        }

        /// <summary>
        /// Encodes the artifact and returns its payload together with the manifest that
        /// describes it, so that the two can never be produced from different inputs.
        /// </summary>
        public static PhysicsArtifactPayload WriteWithManifest(
            PhysicsArtifact artifact,
            string generatorVersion)
        {
            byte[] bytes = Write(artifact);
            string hash = JitterPhysicsHash.Sha256Hex(bytes);
            return new PhysicsArtifactPayload(
                bytes,
                hash,
                PhysicsArtifactManifest.ForArtifact(artifact, hash, generatorVersion));
        }

        private static void WriteBody(CanonicalBinaryWriter writer, PhysicsBodyRecord body)
        {
            writer.WriteString(body.SourceId);
            writer.WriteVector3(body.Position);
            writer.WriteQuaternion(body.Orientation);
            writer.WriteSingle(body.Friction);
            writer.WriteSingle(body.Restitution);

            IReadOnlyList<PhysicsShapeRecord> shapes = body.Shapes;
            writer.WriteInt32(shapes.Count);
            for (int i = 0; i < shapes.Count; i++)
            {
                WriteShape(writer, shapes[i]);
            }
        }

        private static void WriteShape(CanonicalBinaryWriter writer, PhysicsShapeRecord shape)
        {
            writer.WriteString(shape.ShapeKey);
            writer.WriteByte((byte)shape.ShapeType);
            writer.WriteVector3(shape.LocalPosition);
            writer.WriteQuaternion(shape.LocalRotation);

            switch (shape.ShapeType)
            {
                case PhysicsShapeType.Box:
                    writer.WriteVector3(shape.Size);
                    break;

                case PhysicsShapeType.Sphere:
                    writer.WriteSingle(shape.Radius);
                    break;

                case PhysicsShapeType.Capsule:
                    writer.WriteSingle(shape.Radius);
                    writer.WriteSingle(shape.Length);
                    break;

                case PhysicsShapeType.Mesh:
                    PhysicsVector3[] vertices = shape.Vertices;
                    writer.WriteInt32(vertices.Length);
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        writer.WriteVector3(vertices[i]);
                    }

                    int[] indices = shape.Indices;
                    writer.WriteInt32(indices.Length);
                    for (int i = 0; i < indices.Length; i++)
                    {
                        writer.WriteInt32(indices[i]);
                    }

                    break;

                default:
                    throw new ArgumentException(
                        $"Shape '{shape.ShapeKey}' has unsupported type {shape.ShapeType}.",
                        nameof(shape));
            }
        }

        private static byte[] HexToBytes(string hex)
        {
            var bytes = new byte[PhysicsArtifactFormat.CompatibilityIdBytes];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)((HexValue(hex[i * 2]) << 4) | HexValue(hex[(i * 2) + 1]));
            }

            return bytes;
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            throw new ArgumentException(
                $"'{value}' is not a lowercase hexadecimal digit.", nameof(value));
        }

        private static int EstimateSize(PhysicsArtifact artifact)
        {
            // Header, bodies and shapes are small; meshes dominate. A rough estimate avoids
            // repeated growth of the backing buffer for large levels.
            return 128
                + (artifact.Bodies.Count * 96)
                + (artifact.ShapeCount * 64)
                + (artifact.VertexCount * 12)
                + (artifact.TriangleCount * 12);
        }
    }

    /// <summary>A written artifact: its exact bytes, their hash and the matching manifest.</summary>
    public sealed class PhysicsArtifactPayload
    {
        /// <summary>Canonical binary payload.</summary>
        public byte[] Bytes { get; }

        /// <summary>Lowercase hex SHA-256 of <see cref="Bytes"/>.</summary>
        public string ArtifactHash { get; }

        /// <summary>Manifest describing <see cref="Bytes"/>.</summary>
        public PhysicsArtifactManifest Manifest { get; }

        internal PhysicsArtifactPayload(byte[] bytes, string artifactHash, PhysicsArtifactManifest manifest)
        {
            Bytes = bytes;
            ArtifactHash = artifactHash;
            Manifest = manifest;
        }
    }
}
