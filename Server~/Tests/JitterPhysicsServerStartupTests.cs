using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using Jitter2;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>
    /// The server startup path end to end: files on disk, a provider, a world, and the one
    /// flag a match server is allowed to gate connection approval on.
    /// </summary>
    public sealed class JitterPhysicsServerStartupTests
    {
        private const string RuntimeId = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "jphys-startup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (_directory != null && Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void BuildsTheWorldAndReportsReady()
        {
            IPhysicsArtifactProvider provider = Deliver();
            var world = new World();
            int before = world.RigidBodies.Count;

            JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(
                world, provider, new JitterPhysicsServerOptions(RuntimeId, "arena", 30));

            Assert.That(state.IsReady, Is.True, state.Error.ToString());
            Assert.That(state.LevelId, Is.EqualTo("arena"));
            Assert.That(state.BodyCount, Is.EqualTo(2));
            Assert.That(state.TickRate, Is.EqualTo(30));
            Assert.That(state.TopologyFingerprint, Has.Length.EqualTo(64));
            Assert.That(world.RigidBodies.Count - before, Is.EqualTo(2));
            Assert.That(state.RequireReady(), Is.SameAs(state));
        }

        [Test]
        public void SelfCheckLineCarriesWhatASmokeTestGrepsFor()
        {
            JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(
                new World(), Deliver(), new JitterPhysicsServerOptions(RuntimeId));

            string line = state.SelfCheck;

            Assert.That(line, Does.Contain("physics self-check OK"));
            Assert.That(line, Does.Contain("level=arena"));
            Assert.That(line, Does.Contain("artifact=" + state.ArtifactHash.Substring(0, 12)));
            Assert.That(line, Does.Contain("tickRate=30"));

            // Short hashes only: a log line is read by people, and the full value is never the
            // thing that makes a mismatch understandable.
            Assert.That(line, Does.Not.Contain(state.ArtifactHash));
            Assert.That(line, Does.Not.Contain(state.TopologyFingerprint));
        }

        [Test]
        public void LeavesTheWorldEmptyWhenTheArtifactWasNotDelivered()
        {
            var missing = new FilePhysicsArtifactProvider(Path.Combine(_directory, "absent.manifest.json"));
            var world = new World();
            int before = world.RigidBodies.Count;

            JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(
                world, missing, new JitterPhysicsServerOptions(RuntimeId));

            Assert.That(state.IsReady, Is.False);
            Assert.That(state.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.SourceUnavailable));
            Assert.That(state.Artifact, Is.Null);
            Assert.That(world.RigidBodies.Count - before, Is.Zero, "a refusal must leave no geometry");
            Assert.That(state.SelfCheck, Does.Contain("FAILED"));
            Assert.That(() => state.RequireReady(), Throws.InvalidOperationException);
        }

        [Test]
        public void RefusesAnArtifactForAnotherLevelThanTheOneItWasLaunchedFor()
        {
            var world = new World();
            int before = world.RigidBodies.Count;

            JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(
                world, Deliver(), new JitterPhysicsServerOptions(RuntimeId, "other_level"));

            Assert.That(state.IsReady, Is.False);
            Assert.That(state.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.InvalidValue));
            Assert.That(state.Error.Message, Does.Contain("other_level"));
            Assert.That(world.RigidBodies.Count - before, Is.Zero);
        }

        [Test]
        public void RefusesAnArtifactBakedForAnotherTickRate()
        {
            var world = new World();
            int before = world.RigidBodies.Count;

            JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(
                world, Deliver(), new JitterPhysicsServerOptions(RuntimeId, null, 60));

            Assert.That(state.IsReady, Is.False);
            Assert.That(state.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.InvalidValue));
            Assert.That(state.Error.Message, Does.Contain("60"));
            Assert.That(world.RigidBodies.Count - before, Is.Zero);
        }

        [Test]
        public void RefusesAnArtifactBakedForOtherRuntimeSemantics()
        {
            var world = new World();
            int before = world.RigidBodies.Count;

            JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(
                world,
                Deliver(),
                new JitterPhysicsServerOptions(JitterPhysicsHash.Sha256HexUtf8("some-other-build")));

            Assert.That(state.IsReady, Is.False);
            Assert.That(state.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.IncompatibleRuntime));
            Assert.That(world.RigidBodies.Count - before, Is.Zero);
        }

        [Test]
        public void RefusesToStartTwiceIntoTheSameWorld()
        {
            var world = new World();
            int before = world.RigidBodies.Count;
            var options = new JitterPhysicsServerOptions(RuntimeId);

            Assert.That(JitterPhysicsServerStartup.Start(world, Deliver(), options).IsReady, Is.True);
            JitterPhysicsServerState second = JitterPhysicsServerStartup.Start(world, Deliver(), options);

            Assert.That(second.IsReady, Is.False);
            Assert.That(
                world.RigidBodies.Count - before,
                Is.EqualTo(2),
                "the first world must survive the refusal");
        }

        [Test]
        public void ARequiredRuntimeIdIsNotOptional()
        {
            Assert.That(() => new JitterPhysicsServerOptions(null), Throws.ArgumentException);
        }

        private IPhysicsArtifactProvider Deliver()
        {
            PhysicsArtifactPayload baked = PhysicsArtifactWriter.WriteWithManifest(
                CreateArtifact(), JitterPhysicsPackage.PackageVersion);

            File.WriteAllBytes(Path.Combine(_directory, baked.Manifest.FileName), baked.Bytes);

            string manifestPath = Path.Combine(
                _directory,
                JitterPhysicsArtifactNaming.ManifestFileName("arena", baked.ArtifactHash));

            File.WriteAllText(
                manifestPath,
                PhysicsArtifactManifestCodec.Write(baked.Manifest),
                new UTF8Encoding(false));

            return new FilePhysicsArtifactProvider(manifestPath);
        }

        private static PhysicsArtifact CreateArtifact()
        {
            var ground = new PhysicsBodyRecord(
                "ground",
                PhysicsVector3.Zero,
                PhysicsQuaternion.Identity,
                0.2f,
                0f,
                new List<PhysicsShapeRecord>
                {
                    PhysicsShapeRecord.Box(
                        "shape_00",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(40f, 1f, 40f)),
                });

            var cover = new PhysicsBodyRecord(
                "cover",
                new PhysicsVector3(0f, 1f, 4f),
                PhysicsQuaternion.Identity,
                0.2f,
                0f,
                new List<PhysicsShapeRecord>
                {
                    PhysicsShapeRecord.Box(
                        "shape_00",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(2f, 1f, 0.5f)),
                });

            return new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "arena",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord> { cover, ground });
        }
    }
}
