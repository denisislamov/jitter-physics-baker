using System;
using System.Collections.Generic;
using System.Text;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// Little-endian binary emitter for the artifact format.
    /// <para>
    /// Byte order and float encoding are written by hand instead of through
    /// <c>BinaryWriter</c> so that the produced bytes are defined by this file rather than by
    /// the behaviour of whichever BCL implementation the current runtime ships. A baked
    /// artifact is compared byte-for-byte across machines, so "probably the same" is not
    /// good enough.
    /// </para>
    /// </summary>
    internal sealed class CanonicalBinaryWriter
    {
        private readonly List<byte> buffer;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, true);

        internal CanonicalBinaryWriter(int capacity = 1024)
        {
            buffer = new List<byte>(capacity);
        }

        internal int Length => buffer.Count;

        internal void WriteByte(byte value)
        {
            buffer.Add(value);
        }

        internal void WriteBytes(byte[] value)
        {
            buffer.AddRange(value);
        }

        internal void WriteUInt16(ushort value)
        {
            buffer.Add((byte)(value & 0xFF));
            buffer.Add((byte)((value >> 8) & 0xFF));
        }

        internal void WriteInt32(int value)
        {
            uint unsigned = unchecked((uint)value);
            buffer.Add((byte)(unsigned & 0xFF));
            buffer.Add((byte)((unsigned >> 8) & 0xFF));
            buffer.Add((byte)((unsigned >> 16) & 0xFF));
            buffer.Add((byte)((unsigned >> 24) & 0xFF));
        }

        /// <summary>
        /// Writes an IEEE-754 single. The value is canonicalized first, so a <c>-0.0f</c>
        /// coming from an authoring transform cannot change the artifact hash.
        /// </summary>
        internal void WriteSingle(float value)
        {
            WriteInt32(BitConverter.SingleToInt32Bits(PhysicsCanonicalization.CanonicalFloat(value)));
        }

        internal void WriteVector3(PhysicsVector3 value)
        {
            WriteSingle(value.X);
            WriteSingle(value.Y);
            WriteSingle(value.Z);
        }

        internal void WriteQuaternion(PhysicsQuaternion value)
        {
            WriteSingle(value.X);
            WriteSingle(value.Y);
            WriteSingle(value.Z);
            WriteSingle(value.W);
        }

        /// <summary>
        /// Writes a length-prefixed UTF-8 string without a BOM. The length is in bytes, not in
        /// characters, so a reader never has to guess how far to advance.
        /// </summary>
        internal void WriteString(string value)
        {
            byte[] encoded = Utf8NoBom.GetBytes(value ?? string.Empty);
            if (encoded.Length > PhysicsArtifactLimits.MaxStringBytes)
            {
                throw new ArgumentException(
                    $"String of {encoded.Length} bytes exceeds the {PhysicsArtifactLimits.MaxStringBytes} byte limit.",
                    nameof(value));
            }

            WriteUInt16((ushort)encoded.Length);
            buffer.AddRange(encoded);
        }

        internal byte[] ToArray()
        {
            return buffer.ToArray();
        }
    }
}
