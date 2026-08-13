using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Tests
{
    public sealed class JitterPhysicsArtifactCodecTests
    {
        [Test]
        public void WriteReadRoundTripSupportsAllShapeKinds()
        {
            PhysicsArtifact artifact = CreateAllShapesArtifact();

            byte[] payload = PhysicsArtifactWriter.Write(artifact);
            PhysicsArtifactResult result = PhysicsArtifactReader.Read(payload);

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());
            Assert.That(result.Artifact.Bodies.Count, Is.EqualTo(artifact.Bodies.Count));
            Assert.That(result.Artifact.ShapeCount, Is.EqualTo(4));
            Assert.That(result.Artifact.VertexCount, Is.EqualTo(3));
            Assert.That(result.Artifact.TriangleCount, Is.EqualTo(1));
        }

        [Test]
        public void WriteProducesDeterministicBytesForSameArtifact()
        {
            PhysicsArtifact artifact = CreateAllShapesArtifact();

            byte[] first = PhysicsArtifactWriter.Write(artifact);
            byte[] second = PhysicsArtifactWriter.Write(artifact);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(JitterPhysicsHash.Sha256Hex(first), Is.EqualTo(JitterPhysicsHash.Sha256Hex(second)));
        }

        [Test]
        public void ReadRejectsTrailingBytes()
        {
            PhysicsArtifact artifact = CreateMinimalArtifact();
            byte[] payload = PhysicsArtifactWriter.Write(artifact);
            var withTrailing = new byte[payload.Length + 1];
            Array.Copy(payload, withTrailing, payload.Length);
            withTrailing[withTrailing.Length - 1] = 0xFF;

            PhysicsArtifactResult result = PhysicsArtifactReader.Read(withTrailing);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.TrailingBytes));
        }

        [Test]
        public void ReadRejectsNegativeZeroFloat()
        {
            PhysicsArtifact artifact = CreateMinimalArtifact();
            byte[] payload = PhysicsArtifactWriter.Write(artifact);

            // Header fields are fixed-length except level id: make its size one to keep offsets stable.
            int gravityXOffset = 4 + 2 + 2 + 32 + 2 + 1;
            payload[gravityXOffset + 0] = 0x00;
            payload[gravityXOffset + 1] = 0x00;
            payload[gravityXOffset + 2] = 0x00;
            payload[gravityXOffset + 3] = 0x80;

            PhysicsArtifactResult result = PhysicsArtifactReader.Read(payload);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.InvalidValue));
            Assert.That(result.Error.Message, Does.Contain("negative zero"));
        }

        [Test]
        public void RuntimeCompatibilityIdChangesWhenAnySemanticInputChanges()
        {
            var baseline = new RuntimeCompatibilityInputs(
                artifactSchemaVersion: 1,
                jitterSourceContentHash: "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                precisionMode: "f32",
                compileProfileId: "unity-jitter",
                colliderConversionVersion: 1,
                shapeConstructionVersion: 1,
                worldBuilderVersion: 1,
                worldDefaultsVersion: 1);

            string id1 = RuntimeCompatibilityId.Compute(baseline);
            string id2 = RuntimeCompatibilityId.Compute(new RuntimeCompatibilityInputs(
                1,
                baseline.JitterSourceContentHash,
                baseline.PrecisionMode,
                baseline.CompileProfileId,
                2,
                baseline.ShapeConstructionVersion,
                baseline.WorldBuilderVersion,
                baseline.WorldDefaultsVersion));

            Assert.That(id1, Has.Length.EqualTo(64));
            Assert.That(id2, Has.Length.EqualTo(64));
            Assert.That(id2, Is.Not.EqualTo(id1));
        }

        private static PhysicsArtifact CreateMinimalArtifact()
        {
            var body = new PhysicsBodyRecord(
                "body",
                new PhysicsVector3(0f, 0f, 0f),
                PhysicsQuaternion.Identity,
                0.2f,
                0f,
                new List<PhysicsShapeRecord>
                {
                    PhysicsShapeRecord.Box(
                        "shape_00",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(1f, 2f, 3f)),
                });

            return new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                JitterPhysicsHash.Sha256HexUtf8("runtime"),
                "l",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord> { body });
        }

        private static PhysicsArtifact CreateAllShapesArtifact()
        {
            var body = new PhysicsBodyRecord(
                "body_01",
                new PhysicsVector3(1f, 2f, 3f),
                PhysicsQuaternion.Identity,
                0.2f,
                0.1f,
                new List<PhysicsShapeRecord>
                {
                    PhysicsShapeRecord.Box("shape_00", PhysicsVector3.Zero, PhysicsQuaternion.Identity, new PhysicsVector3(2f, 3f, 4f)),
                    PhysicsShapeRecord.Capsule("shape_01", new PhysicsVector3(0f, 1f, 0f), PhysicsQuaternion.Identity, 0.5f, 2f),
                    PhysicsShapeRecord.Mesh(
                        "shape_02",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new[]
                        {
                            new PhysicsVector3(0f, 0f, 0f),
                            new PhysicsVector3(1f, 0f, 0f),
                            new PhysicsVector3(0f, 1f, 0f),
                        },
                        new[] { 0, 1, 2 }),
                    PhysicsShapeRecord.Sphere("shape_03", PhysicsVector3.Zero, PhysicsQuaternion.Identity, 0.25f),
                });

            return new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                JitterPhysicsHash.Sha256HexUtf8("runtime-all"),
                "arena",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord> { body });
        }
    }
}

