using System;
using System.Text;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// Thrown internally when a payload is malformed. It never escapes the codec: the reader
    /// converts it into a typed <see cref="PhysicsArtifactError"/>, because callers have to
    /// stop the process with a diagnosis rather than catch an exception across runtimes.
    /// </summary>
    internal sealed class CanonicalBinaryException : Exception
    {
        internal PhysicsArtifactErrorCode Code { get; }

        internal CanonicalBinaryException(PhysicsArtifactErrorCode code, string message)
            : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// Bounds-checked little-endian reader. Every read verifies that the requested bytes are
    /// actually present, so a truncated file fails with "truncated" instead of reading
    /// whatever happens to follow in memory.
    /// </summary>
    internal sealed class CanonicalBinaryReader
    {
        private readonly byte[] data;
        private int position;
        private static readonly UTF8Encoding Utf8Strict = new UTF8Encoding(false, true);

        internal CanonicalBinaryReader(byte[] data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
        }

        internal int Position => position;

        internal int Remaining => data.Length - position;

        internal byte ReadByte()
        {
            Require(1);
            return data[position++];
        }

        internal byte[] ReadBytes(int count)
        {
            Require(count);
            var result = new byte[count];
            Array.Copy(data, position, result, 0, count);
            position += count;
            return result;
        }

        internal ushort ReadUInt16()
        {
            Require(2);
            ushort value = (ushort)(data[position] | (data[position + 1] << 8));
            position += 2;
            return value;
        }

        internal int ReadInt32()
        {
            Require(4);
            uint value = (uint)data[position]
                | ((uint)data[position + 1] << 8)
                | ((uint)data[position + 2] << 16)
                | ((uint)data[position + 3] << 24);
            position += 4;
            return unchecked((int)value);
        }

        /// <summary>
        /// Reads a count and checks it against a hard cap before the caller allocates. This
        /// ordering is the whole point of the limits: a corrupt count must never reach
        /// <c>new T[count]</c>.
        /// </summary>
        internal int ReadCount(string what, int maximum)
        {
            int value = ReadInt32();
            if (value < 0)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"{what} count is negative ({value}).");
            }

            if (value > maximum)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"{what} count {value} exceeds the limit of {maximum}.");
            }

            return value;
        }

        internal float ReadSingle()
        {
            float value = BitConverter.Int32BitsToSingle(ReadInt32());
            if (!PhysicsCanonicalization.IsFinite(value))
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.InvalidValue,
                    "Payload contains a NaN or infinite float.");
            }

            if (PhysicsCanonicalization.IsNegativeZero(value))
            {
                // A canonical bake never emits -0.0f; seeing one means the file was produced
                // by something else or edited, and its hash can no longer be trusted.
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.InvalidValue,
                    "Payload contains negative zero, which a canonical bake never writes.");
            }

            return value;
        }

        internal PhysicsVector3 ReadVector3()
        {
            float x = ReadSingle();
            float y = ReadSingle();
            float z = ReadSingle();
            return new PhysicsVector3(x, y, z);
        }

        internal PhysicsQuaternion ReadQuaternion()
        {
            float x = ReadSingle();
            float y = ReadSingle();
            float z = ReadSingle();
            float w = ReadSingle();
            var value = new PhysicsQuaternion(x, y, z, w);
            if (!PhysicsCanonicalization.IsCanonicalQuaternion(value))
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.InvalidValue,
                    "Payload contains a quaternion that is not normalized or not sign-canonical.");
            }

            return value;
        }

        internal string ReadString(string what)
        {
            int length = ReadUInt16();
            if (length > PhysicsArtifactLimits.MaxStringBytes)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"{what} is {length} bytes long, over the {PhysicsArtifactLimits.MaxStringBytes} byte limit.");
            }

            byte[] encoded = ReadBytes(length);
            try
            {
                // Strict decoding: invalid UTF-8 is a corrupt payload, not a replacement char.
                return Utf8Strict.GetString(encoded);
            }
            catch (DecoderFallbackException)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"{what} is not valid UTF-8.");
            }
        }

        internal void RequireEndOfPayload()
        {
            if (Remaining != 0)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.TrailingBytes,
                    $"Payload has {Remaining} unread trailing bytes.");
            }
        }

        private void Require(int count)
        {
            if (count < 0 || Remaining < count)
            {
                throw new CanonicalBinaryException(
                    PhysicsArtifactErrorCode.TruncatedPayload,
                    $"Payload ended after {position} bytes while {count} more were required.");
            }
        }
    }
}
