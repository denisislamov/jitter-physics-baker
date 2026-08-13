using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Tests
{
    /// <summary>
    /// Covers the delivery path a dedicated server actually uses: two files on disk, produced
    /// by a bake somewhere else, with nothing but their own contents to prove what they are.
    /// </summary>
    public sealed class FilePhysicsArtifactProviderTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "jphys-provider-" + Guid.NewGuid().ToString("N"));
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
        public void LoadsTheArtifactTheManifestDescribes()
        {
            PhysicsArtifactPayload baked = Bake();
            string manifestPath = WriteBoth(baked);

            var provider = new FilePhysicsArtifactProvider(manifestPath);
            PhysicsArtifactLoadResult result = provider.Load(baked.Manifest.RuntimeCompatibilityId);

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());
            Assert.That(result.Artifact.LevelId, Is.EqualTo("arena"));
            Assert.That(result.Artifact.Bodies.Count, Is.EqualTo(baked.Manifest.BodyCount));
            Assert.That(result.ArtifactHash, Is.EqualTo(baked.ArtifactHash));
            Assert.That(result.Manifest.FileName, Is.EqualTo(baked.Manifest.FileName));
            Assert.That(result.Source, Does.Contain(manifestPath));
            Assert.That(result.ToString(), Does.Contain("level='arena'"));
        }

        [Test]
        public void ReportsAMissingManifestAsAnUnavailableSource()
        {
            var provider = new FilePhysicsArtifactProvider(Path.Combine(_directory, "nothing.manifest.json"));

            PhysicsArtifactLoadResult result = provider.Load(null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.SourceUnavailable));
        }

        [Test]
        public void ReportsAMissingPayloadAsAnUnavailableSource()
        {
            PhysicsArtifactPayload baked = Bake();
            string manifestPath = WriteBoth(baked);
            File.Delete(Path.Combine(_directory, baked.Manifest.FileName));

            PhysicsArtifactLoadResult result = new FilePhysicsArtifactProvider(manifestPath).Load(null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.SourceUnavailable));
            Assert.That(result.Error.LevelId, Is.EqualTo("arena"));
        }

        [Test]
        public void RejectsAPayloadThatWasModifiedAfterBaking()
        {
            PhysicsArtifactPayload baked = Bake();
            string manifestPath = WriteBoth(baked);

            string payloadPath = Path.Combine(_directory, baked.Manifest.FileName);
            byte[] bytes = File.ReadAllBytes(payloadPath);
            bytes[bytes.Length - 1] ^= 0xFF;
            File.WriteAllBytes(payloadPath, bytes);

            PhysicsArtifactLoadResult result = new FilePhysicsArtifactProvider(manifestPath).Load(null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.HashMismatch));
        }

        [Test]
        public void RejectsAManifestEditedToDescribeDifferentContent()
        {
            PhysicsArtifactPayload baked = Bake();
            string json = PhysicsArtifactManifestCodec.Write(baked.Manifest)
                .Replace("\"bodyCount\": 2", "\"bodyCount\": 3");
            string manifestPath = WriteBoth(baked, json);

            PhysicsArtifactLoadResult result = new FilePhysicsArtifactProvider(manifestPath).Load(null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.ManifestMismatch));
        }

        [Test]
        public void RejectsAManifestThatIsNotAManifest()
        {
            string manifestPath = Path.Combine(_directory, "broken.manifest.json");
            File.WriteAllText(manifestPath, "{ this is not json", new UTF8Encoding(false));

            PhysicsArtifactLoadResult result = new FilePhysicsArtifactProvider(manifestPath).Load(null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.ManifestMismatch));
        }

        [Test]
        public void RefusesAPayloadNameThatPointsOutsideTheManifestFolder()
        {
            PhysicsArtifactPayload baked = Bake();
            string json = PhysicsArtifactManifestCodec.Write(baked.Manifest)
                .Replace(baked.Manifest.FileName, "../" + baked.Manifest.FileName);
            string manifestPath = WriteBoth(baked, json);

            PhysicsArtifactLoadResult result = new FilePhysicsArtifactProvider(manifestPath).Load(null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.InvalidValue));
        }

        [Test]
        public void RefusesAnOversizedManifestBeforeParsingIt()
        {
            string manifestPath = Path.Combine(_directory, "huge.manifest.json");
            File.WriteAllText(
                manifestPath,
                new string('x', PhysicsArtifactManifestCodec.MaxManifestBytes + 1),
                new UTF8Encoding(false));

            PhysicsArtifactLoadResult result = new FilePhysicsArtifactProvider(manifestPath).Load(null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.LimitExceeded));
        }

        [Test]
        public void RefusesAnArtifactBakedForOtherRuntimeSemantics()
        {
            PhysicsArtifactPayload baked = Bake();
            string manifestPath = WriteBoth(baked);

            PhysicsArtifactLoadResult result = new FilePhysicsArtifactProvider(manifestPath)
                .Load(JitterPhysicsHash.Sha256HexUtf8("another-runtime"));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.IncompatibleRuntime));
        }

        [Test]
        public void UsesTheExplicitPayloadPathWhenDeliveryRenamedTheFile()
        {
            PhysicsArtifactPayload baked = Bake();
            string manifestPath = WriteBoth(baked);

            string renamed = Path.Combine(_directory, "delivered.bin");
            File.Move(Path.Combine(_directory, baked.Manifest.FileName), renamed);

            PhysicsArtifactLoadResult result = new FilePhysicsArtifactProvider(manifestPath, renamed).Load(null);

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());
            Assert.That(result.ArtifactHash, Is.EqualTo(baked.ArtifactHash));
        }

        private string WriteBoth(PhysicsArtifactPayload baked, string manifestJson = null)
        {
            File.WriteAllBytes(Path.Combine(_directory, baked.Manifest.FileName), baked.Bytes);

            string manifestPath = Path.Combine(
                _directory,
                JitterPhysicsArtifactNaming.ManifestFileName(baked.Manifest.LevelId, baked.ArtifactHash));

            File.WriteAllText(
                manifestPath,
                manifestJson ?? PhysicsArtifactManifestCodec.Write(baked.Manifest),
                new UTF8Encoding(false));

            return manifestPath;
        }

        private static PhysicsArtifactPayload Bake()
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
                        new PhysicsVector3(20f, 1f, 20f)),
                });

            var wall = new PhysicsBodyRecord(
                "wall",
                new PhysicsVector3(0f, 1.5f, 5f),
                PhysicsQuaternion.Identity,
                0.2f,
                0f,
                new List<PhysicsShapeRecord>
                {
                    PhysicsShapeRecord.Box(
                        "shape_00",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(10f, 3f, 0.5f)),
                });

            var artifact = new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                JitterPhysicsHash.Sha256HexUtf8("provider-runtime"),
                "arena",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord> { ground, wall });

            return PhysicsArtifactWriter.WriteWithManifest(artifact, JitterPhysicsPackage.PackageVersion);
        }
    }
}


