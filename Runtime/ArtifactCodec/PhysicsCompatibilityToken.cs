using System;
using System.Text;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// The compatibility claim a client sends when it connects, and the server checks before
    /// it accepts.
    /// <para>
    /// It is transport-agnostic on purpose: the package does not reference Netick or any
    /// other networking library, it only defines the bytes. A consumer puts them into
    /// whatever connection payload its transport offers.
    /// </para>
    /// <para>
    /// Both the artifact hash <em>and</em> the runtime compatibility id are carried. Checking
    /// only the artifact hash would accept a client that has the right map but builds it with
    /// different semantics, which is precisely the case that is hardest to diagnose later.
    /// This is a correctness check between honest peers, not an anti-cheat measure.
    /// </para>
    /// </summary>
    public readonly struct PhysicsCompatibilityToken
    {
        /// <summary>Token magic: <c>J P C T</c>.</summary>
        public static readonly byte[] Magic = { 0x4A, 0x50, 0x43, 0x54 };

        /// <summary>Version of the token layout itself.</summary>
        public const byte ProtocolVersion = 1;

        /// <summary>Raw digest length of both ids.</summary>
        private const int DigestBytes = 32;

        /// <summary>Largest token, used to size buffers and to reject oversized payloads early.</summary>
        public const int MaxTokenBytes = 4 + 1 + 1 + JitterPhysicsIdUtility.MaxLength + DigestBytes + DigestBytes;

        /// <summary>Level the peer intends to play.</summary>
        public string LevelId { get; }

        /// <summary>Hex SHA-256 of the artifact the peer has loaded.</summary>
        public string ArtifactHash { get; }

        /// <summary>Hex runtime compatibility id of the peer's build.</summary>
        public string RuntimeCompatibilityId { get; }

        public PhysicsCompatibilityToken(string levelId, string artifactHash, string runtimeCompatibilityId)
        {
            LevelId = levelId ?? throw new ArgumentNullException(nameof(levelId));
            ArtifactHash = artifactHash ?? throw new ArgumentNullException(nameof(artifactHash));
            RuntimeCompatibilityId = runtimeCompatibilityId
                ?? throw new ArgumentNullException(nameof(runtimeCompatibilityId));
        }

        /// <summary>Builds the token that describes a validated artifact.</summary>
        public static PhysicsCompatibilityToken ForArtifact(PhysicsArtifact artifact, string artifactHash)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            return new PhysicsCompatibilityToken(
                artifact.LevelId,
                artifactHash,
                artifact.RuntimeCompatibilityId);
        }

        /// <summary>Encodes the token. Throws when a field is not canonical — that is a local bug.</summary>
        public byte[] Encode()
        {
            if (!JitterPhysicsIdUtility.IsCanonical(LevelId))
            {
                throw new InvalidOperationException($"Level id '{LevelId}' is not canonical.");
            }

            byte[] levelId = new UTF8Encoding(false).GetBytes(LevelId);
            byte[] artifactDigest = HexToDigest(ArtifactHash, nameof(ArtifactHash));
            byte[] runtimeDigest = HexToDigest(RuntimeCompatibilityId, nameof(RuntimeCompatibilityId));

            var buffer = new byte[Magic.Length + 2 + levelId.Length + (DigestBytes * 2)];
            int offset = 0;
            Array.Copy(Magic, 0, buffer, offset, Magic.Length);
            offset += Magic.Length;
            buffer[offset++] = ProtocolVersion;
            buffer[offset++] = (byte)levelId.Length;
            Array.Copy(levelId, 0, buffer, offset, levelId.Length);
            offset += levelId.Length;
            Array.Copy(artifactDigest, 0, buffer, offset, DigestBytes);
            offset += DigestBytes;
            Array.Copy(runtimeDigest, 0, buffer, offset, DigestBytes);

            return buffer;
        }

        /// <summary>
        /// Decodes a token received from a peer. Returns false with a reason instead of
        /// throwing: this parses untrusted bytes, and the caller's job is to refuse the
        /// connection, not to handle an exception.
        /// </summary>
        public static bool TryDecode(byte[] payload, out PhysicsCompatibilityToken token, out string error)
        {
            token = default;

            if (payload == null || payload.Length == 0)
            {
                error = "Compatibility token is empty.";
                return false;
            }

            if (payload.Length > MaxTokenBytes)
            {
                error = $"Compatibility token is {payload.Length} bytes, over the limit of {MaxTokenBytes}.";
                return false;
            }

            int minimum = Magic.Length + 2 + (DigestBytes * 2);
            if (payload.Length < minimum)
            {
                error = $"Compatibility token is {payload.Length} bytes, shorter than the minimum {minimum}.";
                return false;
            }

            for (int i = 0; i < Magic.Length; i++)
            {
                if (payload[i] != Magic[i])
                {
                    error = "Compatibility token has the wrong magic.";
                    return false;
                }
            }

            int offset = Magic.Length;
            byte protocolVersion = payload[offset++];
            if (protocolVersion != ProtocolVersion)
            {
                error = $"Compatibility token protocol {protocolVersion} is not supported (expected {ProtocolVersion}).";
                return false;
            }

            int levelIdLength = payload[offset++];
            if (levelIdLength == 0 || levelIdLength > JitterPhysicsIdUtility.MaxLength)
            {
                error = $"Compatibility token declares a level id of {levelIdLength} bytes.";
                return false;
            }

            if (payload.Length != offset + levelIdLength + (DigestBytes * 2))
            {
                error = "Compatibility token length does not match its declared level id length.";
                return false;
            }

            string levelId;
            try
            {
                levelId = new UTF8Encoding(false, true).GetString(payload, offset, levelIdLength);
            }
            catch (DecoderFallbackException)
            {
                error = "Compatibility token level id is not valid UTF-8.";
                return false;
            }

            if (!JitterPhysicsIdUtility.IsCanonical(levelId))
            {
                error = "Compatibility token level id is not canonical.";
                return false;
            }

            offset += levelIdLength;
            string artifactHash = JitterPhysicsHash.ToHex(Slice(payload, offset, DigestBytes));
            offset += DigestBytes;
            string runtimeId = JitterPhysicsHash.ToHex(Slice(payload, offset, DigestBytes));

            token = new PhysicsCompatibilityToken(levelId, artifactHash, runtimeId);
            error = null;
            return true;
        }

        /// <summary>
        /// Compares a peer's token with the expectation. Only an exact match on all three
        /// fields is accepted; there is no "close enough" that is safe here.
        /// </summary>
        public bool Matches(PhysicsCompatibilityToken expected, out string reason)
        {
            if (!string.Equals(LevelId, expected.LevelId, StringComparison.Ordinal))
            {
                reason = $"level id '{LevelId}' does not match '{expected.LevelId}'";
                return false;
            }

            if (!JitterPhysicsHash.HexEquals(ArtifactHash, expected.ArtifactHash))
            {
                reason = $"artifact {Short(ArtifactHash)} does not match {Short(expected.ArtifactHash)}";
                return false;
            }

            if (!JitterPhysicsHash.HexEquals(RuntimeCompatibilityId, expected.RuntimeCompatibilityId))
            {
                reason = $"runtime {Short(RuntimeCompatibilityId)} does not match {Short(expected.RuntimeCompatibilityId)}";
                return false;
            }

            reason = null;
            return true;
        }

        private static byte[] Slice(byte[] source, int offset, int count)
        {
            var result = new byte[count];
            Array.Copy(source, offset, result, 0, count);
            return result;
        }

        private static byte[] HexToDigest(string hex, string what)
        {
            if (hex == null || hex.Length != DigestBytes * 2)
            {
                throw new InvalidOperationException(
                    $"{what} must be {DigestBytes * 2} hex characters.");
            }

            var digest = new byte[DigestBytes];
            for (int i = 0; i < DigestBytes; i++)
            {
                digest[i] = (byte)((HexValue(hex[i * 2], what) << 4) | HexValue(hex[(i * 2) + 1], what));
            }

            return digest;
        }

        private static int HexValue(char value, string what)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }

            throw new InvalidOperationException($"{what} contains a non-hexadecimal character '{value}'.");
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
