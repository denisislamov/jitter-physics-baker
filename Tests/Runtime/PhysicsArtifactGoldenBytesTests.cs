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
    /// The binary layout of artifact schema 1, and what happens to payloads that do not
    /// follow it.
    /// <para>
    /// The expected bytes are assembled here field by field instead of being compared against
    /// whatever the writer currently produces. That is the point of a golden test: it is an
    /// independent statement of the format, so a change to the writer fails loudly rather
    /// than quietly redefining what an artifact is. A client and a server built from
    /// different commits must never disagree about how to read the same file.
    /// </para>
    /// </summary>
    public sealed class PhysicsArtifactGoldenBytesTests
    {
        private const string RuntimeId =
            "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

        [Test]
        public void MinimalBoxArtifactMatchesTheGoldenLayout()
        {
            byte[] actual = PhysicsArtifactWriter.Write(CreateMinimalBoxArtifact());
            byte[] expected = BuildExpectedMinimalBoxBytes();

            Assert.That(
                BitConverter.ToString(actual),
                Is.EqualTo(BitConverter.ToString(expected)),
                "The artifact layout changed. This is only allowed together with a schema "
                + "version bump, because existing artifacts would otherwise be reinterpreted.");
        }

        [Test]
        public void GoldenArtifactHashIsStableAcrossWrites()
        {
            PhysicsArtifact artifact = CreateMinimalBoxArtifact();

            string first = JitterPhysicsHash.Sha256Hex(PhysicsArtifactWriter.Write(artifact));
            string second = JitterPhysicsHash.Sha256Hex(PhysicsArtifactWriter.Write(artifact));

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.EqualTo(JitterPhysicsHash.Sha256Hex(BuildExpectedMinimalBoxBytes())));
        }

        [Test]
        public void NegativeZeroAndFlippedQuaternionDoNotChangeTheArtifact()
        {
            byte[] canonical = PhysicsArtifactWriter.Write(CreateMinimalBoxArtifact());

            // -0.0f has different bits than +0.0f, and -q describes the same rotation as q.
            // Both must collapse before the bytes are produced, otherwise two authoring
            // sessions of an unchanged scene would ship different artifacts.
            var body = new PhysicsBodyRecord(
                "b",
                new PhysicsVector3(-0f, -0f, -0f).Canonical(),
                new PhysicsQuaternion(0f, 0f, 0f, -1f).Canonical(),
                0.2f,
                0f,
                new List<PhysicsShapeRecord>
                {
                    PhysicsShapeRecord.Box(
                        "s",
                        new PhysicsVector3(-0f, -0f, -0f).Canonical(),
                        new PhysicsQuaternion(-0f, -0f, -0f, -1f).Canonical(),
                        new PhysicsVector3(1f, 2f, 3f)),
                });

            byte[] fromNoisyInput = PhysicsArtifactWriter.Write(new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "l",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord> { body }));

            Assert.That(BitConverter.ToString(fromNoisyInput), Is.EqualTo(BitConverter.ToString(canonical)));
        }

        [Test]
        public void OneFieldChangeChangesTheHash()
        {
            string baseline = JitterPhysicsHash.Sha256Hex(PhysicsArtifactWriter.Write(CreateMinimalBoxArtifact()));

            var body = new PhysicsBodyRecord(
                "b",
                PhysicsVector3.Zero,
                PhysicsQuaternion.Identity,
                0.2f,
                0f,
                new List<PhysicsShapeRecord>
                {
                    PhysicsShapeRecord.Box(
                        "s",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(1f, 2f, 3.0001f)),
                });

            string changed = JitterPhysicsHash.Sha256Hex(PhysicsArtifactWriter.Write(new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "l",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord> { body })));

            Assert.That(changed, Is.Not.EqualTo(baseline));
        }

        [Test]
        public void WriterRefusesRecordsThatAreNotInCanonicalOrder()
        {
            var shapes = new List<PhysicsShapeRecord>
            {
                PhysicsShapeRecord.Sphere("s_b", PhysicsVector3.Zero, PhysicsQuaternion.Identity, 1f),
                PhysicsShapeRecord.Sphere("s_a", PhysicsVector3.Zero, PhysicsQuaternion.Identity, 1f),
            };

            var artifact = new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "l",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord>
                {
                    new PhysicsBodyRecord("b", PhysicsVector3.Zero, PhysicsQuaternion.Identity, 0.2f, 0f, shapes),
                });

            // Reordering silently would hide a nondeterministic baker, which is exactly the
            // failure the canonical order exists to make visible.
            Assert.Throws<ArgumentException>(() => PhysicsArtifactWriter.Write(artifact));
        }

        [TestCase(PhysicsArtifactErrorCode.BadMagic, TestName = "CorruptMagicIsRejected")]
        [TestCase(PhysicsArtifactErrorCode.UnsupportedSchema, TestName = "UnknownSchemaIsRejected")]
        [TestCase(PhysicsArtifactErrorCode.TruncatedPayload, TestName = "TruncatedPayloadIsRejected")]
        [TestCase(PhysicsArtifactErrorCode.TrailingBytes, TestName = "TrailingBytesAreRejected")]
        [TestCase(PhysicsArtifactErrorCode.EmptyPayload, TestName = "EmptyPayloadIsRejected")]
        [TestCase(PhysicsArtifactErrorCode.HashMismatch, TestName = "HashMismatchIsRejected")]
        public void CorruptPayloadsAreRejectedWithTheMatchingCode(PhysicsArtifactErrorCode expected)
        {
            byte[] payload = PhysicsArtifactWriter.Write(CreateMinimalBoxArtifact());
            byte[] corrupted;
            string expectedHash = null;

            switch (expected)
            {
                case PhysicsArtifactErrorCode.BadMagic:
                    corrupted = (byte[])payload.Clone();
                    corrupted[0] ^= 0xFF;
                    break;

                case PhysicsArtifactErrorCode.UnsupportedSchema:
                    corrupted = (byte[])payload.Clone();
                    corrupted[4] = 0x7F;
                    break;

                case PhysicsArtifactErrorCode.TruncatedPayload:
                    corrupted = new byte[payload.Length - 4];
                    Array.Copy(payload, corrupted, corrupted.Length);
                    break;

                case PhysicsArtifactErrorCode.TrailingBytes:
                    corrupted = new byte[payload.Length + 2];
                    Array.Copy(payload, corrupted, payload.Length);
                    break;

                case PhysicsArtifactErrorCode.EmptyPayload:
                    corrupted = Array.Empty<byte>();
                    break;

                default:
                    corrupted = payload;
                    expectedHash = new string('a', 64);
                    break;
            }

            PhysicsArtifactResult result = PhysicsArtifactReader.Read(corrupted, expectedHash);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(expected), result.Error.ToString());
            Assert.That(result.Artifact, Is.Null, "A rejected payload must not produce an artifact.");
        }

        [Test]
        public void ManifestThatDisagreesWithThePayloadIsRejected()
        {
            PhysicsArtifact artifact = CreateMinimalBoxArtifact();
            PhysicsArtifactPayload payload = PhysicsArtifactWriter.WriteWithManifest(artifact, "0.0.1");

            PhysicsArtifactManifest tampered = new PhysicsArtifactManifest(
                payload.Manifest.SchemaVersion,
                payload.Manifest.RuntimeCompatibilityId,
                payload.Manifest.GeneratorVersion,
                payload.Manifest.LevelId,
                payload.Manifest.ArtifactHash,
                payload.Manifest.BodyCount + 1,
                payload.Manifest.ShapeCount,
                payload.Manifest.VertexCount,
                payload.Manifest.TriangleCount,
                payload.Manifest.TickRate,
                payload.Manifest.FileName);

            PhysicsArtifactResult result = PhysicsArtifactReader.Read(payload.Bytes, null, tampered);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.ManifestMismatch));
        }

        [Test]
        public void MatchingManifestIsAccepted()
        {
            PhysicsArtifactPayload payload =
                PhysicsArtifactWriter.WriteWithManifest(CreateMinimalBoxArtifact(), "0.0.1");

            PhysicsArtifactResult result = PhysicsArtifactReader.Read(
                payload.Bytes, payload.ArtifactHash, payload.Manifest);

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());
            Assert.That(payload.Manifest.FileName, Does.EndWith(".jphys.bytes"));
        }

        [Test]
        public void ManifestRoundTripsThroughItsCanonicalJson()
        {
            PhysicsArtifactPayload payload =
                PhysicsArtifactWriter.WriteWithManifest(CreateMinimalBoxArtifact(), "0.0.1");

            string json = PhysicsArtifactManifestCodec.Write(payload.Manifest);
            PhysicsArtifactManifest parsed = PhysicsArtifactManifestCodec.Read(json, out string error);

            Assert.That(error, Is.Null);
            Assert.That(parsed, Is.Not.Null);
            Assert.That(PhysicsArtifactManifestCodec.Write(parsed), Is.EqualTo(json));
            Assert.That(parsed.ArtifactHash, Is.EqualTo(payload.ArtifactHash));
        }

        [Test]
        public void ArtifactBakedForAnotherRuntimeIsRefused()
        {
            PhysicsArtifactResult result = PhysicsArtifactReader.Read(
                PhysicsArtifactWriter.Write(CreateMinimalBoxArtifact()));
            Assert.That(result.Succeeded, Is.True, result.Error.ToString());

            PhysicsArtifactError error = PhysicsArtifactReader.CheckRuntimeCompatibility(
                result.Artifact, new string('b', 64));

            Assert.That(error.IsError, Is.True);
            Assert.That(error.Code, Is.EqualTo(PhysicsArtifactErrorCode.IncompatibleRuntime));
        }

        private static PhysicsArtifact CreateMinimalBoxArtifact()
        {
            var body = new PhysicsBodyRecord(
                "b",
                PhysicsVector3.Zero,
                PhysicsQuaternion.Identity,
                0.2f,
                0f,
                new List<PhysicsShapeRecord>
                {
                    PhysicsShapeRecord.Box(
                        "s",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(1f, 2f, 3f)),
                });

            return new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "l",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord> { body });
        }

        /// <summary>
        /// Spells out schema 1 for the minimal fixture: magic, header, world settings, one
        /// body and one box shape, little-endian throughout.
        /// </summary>
        private static byte[] BuildExpectedMinimalBoxBytes()
        {
            Assert.That(
                BitConverter.IsLittleEndian,
                Is.True,
                "The fixture builds little-endian bytes directly; a big-endian host would need "
                + "the same byte swapping the writer performs.");

            using (var stream = new MemoryStream())
            {
                // Header.
                stream.Write(new byte[] { 0x4A, 0x50, 0x48, 0x59 }, 0, 4); // "JPHY"
                WriteUInt16(stream, 1);                                    // schemaVersion
                WriteUInt16(stream, 0);                                    // reserved
                WriteHex(stream, RuntimeId);                               // runtimeCompatibilityId
                WriteString(stream, "l");                                  // levelId

                // World settings.
                WriteSingle(stream, 0f);
                WriteSingle(stream, -9.81f);
                WriteSingle(stream, 0f);
                WriteInt32(stream, 30); // tickRate
                WriteInt32(stream, 1);  // substepCount
                WriteInt32(stream, 6);  // solverIterations
                WriteInt32(stream, 4);  // relaxationIterations
                stream.WriteByte(1);    // allowDeactivation
                stream.WriteByte(1);    // solveMode: deterministic
                stream.WriteByte(0);    // multiThreaded: never

                // Bodies.
                WriteInt32(stream, 1);
                WriteString(stream, "b");
                WriteSingle(stream, 0f);
                WriteSingle(stream, 0f);
                WriteSingle(stream, 0f);
                WriteSingle(stream, 0f); // orientation x
                WriteSingle(stream, 0f); // orientation y
                WriteSingle(stream, 0f); // orientation z
                WriteSingle(stream, 1f); // orientation w
                WriteSingle(stream, 0.2f); // friction
                WriteSingle(stream, 0f);   // restitution

                // Shapes.
                WriteInt32(stream, 1);
                WriteString(stream, "s");
                stream.WriteByte(1); // PhysicsShapeType.Box
                WriteSingle(stream, 0f);
                WriteSingle(stream, 0f);
                WriteSingle(stream, 0f);
                WriteSingle(stream, 0f);
                WriteSingle(stream, 0f);
                WriteSingle(stream, 0f);
                WriteSingle(stream, 1f);
                WriteSingle(stream, 1f); // size x
                WriteSingle(stream, 2f); // size y
                WriteSingle(stream, 3f); // size z

                return stream.ToArray();
            }
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private static void WriteInt32(Stream stream, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteSingle(Stream stream, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteString(Stream stream, string value)
        {
            byte[] encoded = new UTF8Encoding(false).GetBytes(value);
            WriteUInt16(stream, (ushort)encoded.Length);
            stream.Write(encoded, 0, encoded.Length);
        }

        private static void WriteHex(Stream stream, string hex)
        {
            for (int i = 0; i < hex.Length; i += 2)
            {
                stream.WriteByte(Convert.ToByte(hex.Substring(i, 2), 16));
            }
        }
    }
}

