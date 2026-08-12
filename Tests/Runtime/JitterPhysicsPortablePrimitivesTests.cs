using System;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Tests
{
    /// <summary>
    /// Behaviour of the portable primitives every later stage depends on. They run as
    /// PlayMode tests as well, because the same code has to behave identically under Mono,
    /// IL2CPP and a plain .NET runtime.
    /// </summary>
    public sealed class JitterPhysicsPortablePrimitivesTests
    {
        [TestCase("Shooter Arena", "shooter_arena")]
        [TestCase("Level 01 / Alpha", "level_01_alpha")]
        [TestCase("__weird__id__", "weird_id")]
        [TestCase("ÜberLevel", "berlevel")]
        public void SanitizeProducesCanonicalIds(string input, string expected)
        {
            string sanitized = JitterPhysicsIdUtility.Sanitize(input, "fallback");

            Assert.That(sanitized, Is.EqualTo(expected));
            Assert.That(JitterPhysicsIdUtility.IsCanonical(sanitized), Is.True);
        }

        [Test]
        public void SanitizeFallsBackWhenNothingUsableRemains()
        {
            Assert.That(JitterPhysicsIdUtility.Sanitize("///", "new_level"), Is.EqualTo("new_level"));
            Assert.That(JitterPhysicsIdUtility.Sanitize(null, null), Is.EqualTo("unnamed"));
        }

        [Test]
        public void SanitizeIsIdempotent()
        {
            // A bake must not change the id just because it ran twice.
            string once = JitterPhysicsIdUtility.Sanitize("Shooter Arena", "level");
            Assert.That(JitterPhysicsIdUtility.Sanitize(once, "level"), Is.EqualTo(once));
        }

        [Test]
        public void ArtifactFileNamesEmbedLevelAndShortHash()
        {
            string hash = JitterPhysicsHash.Sha256HexUtf8("payload");

            Assert.That(
                JitterPhysicsArtifactNaming.BinaryFileName("shooter", hash),
                Is.EqualTo("shooter." + hash.Substring(0, 12) + ".jphys.bytes"));
            Assert.That(
                JitterPhysicsArtifactNaming.ManifestFileName("shooter", hash),
                Is.EqualTo("shooter." + hash.Substring(0, 12) + ".manifest.json"));
        }

        [Test]
        public void ArtifactNamingRejectsNonCanonicalLevelId()
        {
            string hash = JitterPhysicsHash.Sha256HexUtf8("payload");

            Assert.Throws<ArgumentException>(
                () => JitterPhysicsArtifactNaming.BinaryFileName("Shooter Arena", hash));
        }

        [Test]
        public void ArtifactNamingRejectsTruncatedHash()
        {
            Assert.Throws<ArgumentException>(
                () => JitterPhysicsArtifactNaming.ShortHash("deadbeef"));
        }

        [Test]
        public void Sha256MatchesKnownVector()
        {
            // Guards the hex formatting: everything the package identifies is compared as a
            // lowercase hex string, and a formatting change would silently invalidate artifacts.
            Assert.That(
                JitterPhysicsHash.Sha256HexUtf8(string.Empty),
                Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
            Assert.That(
                JitterPhysicsHash.Sha256HexUtf8("abc"),
                Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        }

        [Test]
        public void Sha256IgnoresByteOrderMark()
        {
            // A UTF-8 BOM written by an editor would otherwise change the hash of identical
            // logical content and break byte-exact bake comparisons across machines.
            byte[] withoutBom = System.Text.Encoding.UTF8.GetBytes("abc");
            Assert.That(JitterPhysicsHash.Sha256Hex(withoutBom),
                Is.EqualTo(JitterPhysicsHash.Sha256HexUtf8("abc")));
        }

        [Test]
        public void HexEqualsIsCaseInsensitiveAndLengthChecked()
        {
            string hash = JitterPhysicsHash.Sha256HexUtf8("abc");

            Assert.That(JitterPhysicsHash.HexEquals(hash, hash.ToUpperInvariant()), Is.True);
            Assert.That(JitterPhysicsHash.HexEquals(hash, hash.Substring(0, 63)), Is.False);
            Assert.That(JitterPhysicsHash.HexEquals(hash, null), Is.False);
        }
    }
}
