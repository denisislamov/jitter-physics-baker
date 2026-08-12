using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// The single SHA-256 implementation of the package. Artifact identity, the Jitter
    /// source lock and the runtime compatibility id all hash through here, so client,
    /// server and CI cannot drift by using slightly different hex formatting.
    /// </summary>
    public static class JitterPhysicsHash
    {
        /// <summary>Lowercase hex SHA-256 of the whole buffer.</summary>
        public static string Sha256Hex(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            return Sha256Hex(bytes, 0, bytes.Length);
        }

        /// <summary>Lowercase hex SHA-256 of a range of a buffer.</summary>
        public static string Sha256Hex(byte[] bytes, int offset, int count)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (offset < 0 || count < 0 || offset + count > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes, offset, count));
            }
        }

        /// <summary>
        /// Lowercase hex SHA-256 of a stream, read from its current position to the end.
        /// Used by the lock tool so that large source sets are not buffered in memory.
        /// </summary>
        public static string Sha256Hex(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        /// <summary>
        /// Lowercase hex SHA-256 of a string encoded as UTF-8 without a BOM. Text that
        /// participates in a hash is always encoded this way; a BOM would make the same
        /// logical content hash differently on different writers.
        /// </summary>
        public static string Sha256HexUtf8(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return Sha256Hex(new UTF8Encoding(false).GetBytes(value));
        }

        /// <summary>Converts a digest to the lowercase hex form used everywhere in the package.</summary>
        public static string ToHex(byte[] digest)
        {
            if (digest == null)
            {
                throw new ArgumentNullException(nameof(digest));
            }

            var builder = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
            {
                builder.Append(HexDigits[digest[i] >> 4]);
                builder.Append(HexDigits[digest[i] & 0x0F]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Ordinal, length-checked comparison of two hex hashes. Hash comparison must never
        /// be culture sensitive, and a wrong-length input is a corrupt input, not a mismatch.
        /// </summary>
        public static bool HexEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            // Constant-time-ish: the handshake compares hashes coming from the network.
            int difference = 0;
            for (int i = 0; i < left.Length; i++)
            {
                difference |= char.ToLowerInvariant(left[i]) ^ char.ToLowerInvariant(right[i]);
            }

            return difference == 0;
        }

        private const string HexDigits = "0123456789abcdef";
    }
}
