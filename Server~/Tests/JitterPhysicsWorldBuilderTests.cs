using System.Collections.Generic;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using Jitter2;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>
    /// The shared loader: artifact records in, a Jitter world out.
    /// <para>
    /// These tests run under plain .NET because that is where the dedicated server lives.
    /// The same code is compiled by Unity for the client, so what is asserted here — record
    /// order becomes creation order, the topology fingerprint is reproducible, a failed
    /// build leaves nothing behind — is what makes it safe for both sides to trust one file.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsWorldBuilderTests
    {
        [Test]
        public void ArtifactBecomesStaticGeometry()
        {
            var world = new World();
            PhysicsArtifact artifact = CreateArenaArtifact();

            PhysicsWorldBuildResult result = JitterPhysicsWorldBuilder.Apply(world, artifact);

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());
            Assert.That(result.BodyCount, Is.EqualTo(artifact.Bodies.Count));
            Assert.That(result.ShapeCount, Is.GreaterThanOrEqualTo(artifact.ShapeCount));
            Assert.That(result.TopologyFingerprint, Has.Length.EqualTo(64));

            foreach (RigidBody body in world.RigidBodies)
            {
                Assert.That(body.MotionType, Is.EqualTo(MotionType.Static));
            }
        }

        [Test]
        public void TopologyFingerprintIsReproducible()
        {
            PhysicsArtifact artifact = CreateArenaArtifact();

            string first = Build(artifact).TopologyFingerprint;
            string second = Build(artifact).TopologyFingerprint;

            // Two worlds built from one artifact must be indistinguishable. This is the check
            // a client and a server compare in practice, so it has to be exact.
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void DecodedArtifactBuildsTheSameTopologyAsTheOriginal()
        {
            PhysicsArtifact original = CreateArenaArtifact();
            byte[] payload = PhysicsArtifactWriter.Write(original);

            PhysicsArtifactResult decoded = PhysicsArtifactReader.Read(payload);
            Assert.That(decoded.Succeeded, Is.True, decoded.Error.ToString());

            // A round trip through the binary format must not change the world that comes out
            // of it, otherwise the file would mean something different on the receiving side.
            Assert.That(
                Build(decoded.Artifact).TopologyFingerprint,
                Is.EqualTo(Build(original).TopologyFingerprint));
        }

        [Test]
        public void WorldSettingsFromTheArtifactAreApplied()
        {
            var world = new World();
            PhysicsArtifact artifact = CreateArenaArtifact();

            JitterPhysicsWorldBuilder.Apply(world, artifact);

            Assert.That(world.SolveMode, Is.EqualTo(SolveMode.Deterministic));
            Assert.That(world.Gravity.Y, Is.EqualTo(artifact.WorldSettings.Gravity.Y).Within(1e-5f));
            Assert.That(world.SolverIterations.solver, Is.EqualTo(artifact.WorldSettings.SolverIterations));
        }

        [Test]
        public void ApplyingASecondArtifactToTheSameWorldIsRefused()
        {
            var world = new World();
            PhysicsArtifact artifact = CreateArenaArtifact();

            Assert.That(JitterPhysicsWorldBuilder.Apply(world, artifact).Succeeded, Is.True);
            Assert.That(JitterPhysicsWorldBuilder.HasArtifact(world), Is.True);

            PhysicsWorldBuildResult second = JitterPhysicsWorldBuilder.Apply(world, artifact);

            // Merging would silently double every wall in the level.
            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.InvalidValue));
        }

        [Test]
        public void BakedGeometryActuallyCollides()
        {
            var world = new World();
            Assert.That(JitterPhysicsWorldBuilder.Apply(world, CreateArenaArtifact()).Succeeded, Is.True);

            RigidBody falling = world.CreateRigidBody();
            falling.AddShape(new Jitter2.Collision.Shapes.BoxShape(new JVector(1f, 1f, 1f)));
            falling.Position = new JVector(0f, 5f, 0f);

            for (int i = 0; i < 240; i++)
            {
                world.Step(1f / 30f, multiThread: false);
            }

            // The ground of the fixture spans y in [-1, 0], so a unit box rests at y = 0.5.
            // Without this the tests would only prove that objects were created, not that the
            // level they describe can be stood on.
            Assert.That(falling.Position.Y, Is.GreaterThan(0f), "The body fell through the baked ground.");
            Assert.That(falling.Position.Y, Is.EqualTo(0.5f).Within(0.15f));
        }

        [Test]
        public void MeshGeometryBecomesTriangles()
        {
            PhysicsArtifact artifact = CreateMeshArtifact();

            PhysicsWorldBuildResult result = Build(artifact);

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());

            // One Jitter shape per triangle, which is how Jitter represents a mesh.
            Assert.That(result.ShapeCount, Is.EqualTo(artifact.TriangleCount));
        }

        [Test]
        public void LocalShapePosesArePreserved()
        {
            var world = new World();

            var shapes = new List<PhysicsShapeRecord>
            {
                PhysicsShapeRecord.Box(
                    "offset",
                    new PhysicsVector3(0f, 2f, 0f),
                    PhysicsQuaternion.Identity,
                    new PhysicsVector3(1f, 1f, 1f)),
            };

            var artifact = new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "poses",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord>
                {
                    new PhysicsBodyRecord(
                        "body",
                        new PhysicsVector3(10f, 0f, 0f),
                        PhysicsQuaternion.Identity,
                        0.2f,
                        0f,
                        shapes),
                });

            Assert.That(JitterPhysicsWorldBuilder.Apply(world, artifact).Succeeded, Is.True);

            RigidBody body = null;
            foreach (RigidBody candidate in world.RigidBodies)
            {
                body = candidate;
            }

            Assert.That(body, Is.Not.Null);

            // The shape sits two units above a body that stands ten units along X, so the
            // world-space bounds of the shape have to reflect both the body pose and the
            // local one. Checking the shape rather than the body is what proves the local
            // pose survived: a body pose alone would place the geometry at y = 0.
            Assert.That(body.Position.X, Is.EqualTo(10f).Within(1e-4f));

            Jitter2.Collision.Shapes.RigidBodyShape shape = body.Shapes[0];
            JVector center = (shape.WorldBoundingBox.Min + shape.WorldBoundingBox.Max) * (Real)0.5;

            Assert.That(center.Y, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(center.X, Is.EqualTo(10f).Within(1e-3f));
        }

        private const string RuntimeId =
            "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

        private static PhysicsWorldBuildResult Build(PhysicsArtifact artifact)
        {
            return JitterPhysicsWorldBuilder.Apply(new World(), artifact);
        }

        /// <summary>Ground plus two covers, the shape of the smallest realistic level.</summary>
        private static PhysicsArtifact CreateArenaArtifact()
        {
            var bodies = new List<PhysicsBodyRecord>
            {
                new PhysicsBodyRecord(
                    "cover_a",
                    new PhysicsVector3(-3f, 0.5f, 2f),
                    PhysicsQuaternion.Identity,
                    0.2f,
                    0f,
                    new List<PhysicsShapeRecord>
                    {
                        PhysicsShapeRecord.Box(
                            "s_box",
                            PhysicsVector3.Zero,
                            PhysicsQuaternion.Identity,
                            new PhysicsVector3(1f, 1f, 1f)),
                    }),
                new PhysicsBodyRecord(
                    "cover_b",
                    new PhysicsVector3(3f, 0.5f, 2f),
                    PhysicsQuaternion.Identity,
                    0.2f,
                    0f,
                    new List<PhysicsShapeRecord>
                    {
                        PhysicsShapeRecord.Capsule(
                            "s_capsule",
                            PhysicsVector3.Zero,
                            PhysicsQuaternion.Identity,
                            0.5f,
                            1f),
                    }),
                new PhysicsBodyRecord(
                    "ground",
                    new PhysicsVector3(0f, -0.5f, 0f),
                    PhysicsQuaternion.Identity,
                    0.2f,
                    0f,
                    new List<PhysicsShapeRecord>
                    {
                        PhysicsShapeRecord.Box(
                            "s_ground",
                            PhysicsVector3.Zero,
                            PhysicsQuaternion.Identity,
                            new PhysicsVector3(40f, 1f, 40f)),
                    }),
            };

            return new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "arena",
                PhysicsWorldSettings.Default,
                bodies);
        }

        private static PhysicsArtifact CreateMeshArtifact()
        {
            var vertices = new[]
            {
                new PhysicsVector3(-5f, 0f, -5f),
                new PhysicsVector3(5f, 0f, -5f),
                new PhysicsVector3(5f, 0f, 5f),
                new PhysicsVector3(-5f, 0f, 5f),
            };

            var indices = new[] { 0, 1, 2, 0, 2, 3 };

            return new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "mesh_level",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord>
                {
                    new PhysicsBodyRecord(
                        "terrain",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        0.2f,
                        0f,
                        new List<PhysicsShapeRecord>
                        {
                            PhysicsShapeRecord.Mesh(
                                "s_mesh",
                                PhysicsVector3.Zero,
                                PhysicsQuaternion.Identity,
                                vertices,
                                indices),
                        }),
                });
        }
    }
}


