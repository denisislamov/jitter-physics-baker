/*
 * Compatibility shim for the Unity build of Jitter2.Core.
 * SPDX-License-Identifier: MIT
 *
 * Not part of upstream Jitter2. Compiled only into the netstandard2.1 assembly.
 */

// System.Runtime.Intrinsics arrived in .NET 5 and is absent from netstandard2.1, which is the
// surface Unity compiles and runs against. Jitter2 reaches for Vector128 in four files, so the
// assembly cannot bind without it.
//
// The shim declares the types in their original namespace and implements them in software. That
// choice is deliberate and has one consequence worth stating plainly: IsHardwareAccelerated
// returns false, so Jitter2 takes its own scalar paths in Contact and VertexSupportMap, which
// upstream wrote precisely for this case. TreeBox has no scalar twin and runs on the shim, but it
// only ever compares and subtracts lane-wise, and IEEE-754 makes those operations identical
// whether a CPU does four at once or one at a time.
//
// The same assembly is used by the Unity client and the dedicated server, so neither can drift
// onto a different code path. A build where one side had SIMD and the other did not would produce
// two different simulations from one artifact, which is the exact failure this package exists to
// prevent.

#if !NET5_0_OR_GREATER

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Runtime.Intrinsics
{
    /// <summary>
    /// A 128-bit vector of four 32-bit lanes, laid out exactly like the runtime type it replaces
    /// so that <see cref="Unsafe.As{TFrom,TTo}"/> reinterpretation over adjacent floats stays valid.
    /// </summary>
    /// <typeparam name="T">Lane type. Only <see cref="float"/> and <see cref="int"/> are supported.</typeparam>
    [StructLayout(LayoutKind.Sequential, Size = 16)]
    internal readonly struct Vector128<T> : IEquatable<Vector128<T>>
        where T : struct
    {
        private readonly uint lane0;
        private readonly uint lane1;
        private readonly uint lane2;
        private readonly uint lane3;

        internal Vector128(uint lane0, uint lane1, uint lane2, uint lane3)
        {
            this.lane0 = lane0;
            this.lane1 = lane1;
            this.lane2 = lane2;
            this.lane3 = lane3;
        }

        /// <summary>The number of lanes, fixed at four for the 32-bit lane types in use.</summary>
        public static int Count => 4;

        /// <summary>An all-zero vector.</summary>
        public static Vector128<T> Zero => default;

        internal uint Bits(int index)
        {
            switch (index)
            {
                case 0: return lane0;
                case 1: return lane1;
                case 2: return lane2;
                default: return index == 3 ? lane3 : throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        /// <inheritdoc />
        public bool Equals(Vector128<T> other) =>
            lane0 == other.lane0 && lane1 == other.lane1 && lane2 == other.lane2 && lane3 == other.lane3;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Vector128<T> other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)lane0;
                hash = (hash * 397) ^ (int)lane1;
                hash = (hash * 397) ^ (int)lane2;
                hash = (hash * 397) ^ (int)lane3;
                return hash;
            }
        }

        /// <inheritdoc />
        public override string ToString() => $"<{lane0:X8}, {lane1:X8}, {lane2:X8}, {lane3:X8}>";
    }

    /// <summary>
    /// Software implementations of the <c>Vector128</c> operations Jitter2 uses.
    /// </summary>
    internal static class Vector128
    {
        // False on purpose. It is not a claim about the CPU, it is a claim about this assembly:
        // there is no SIMD here, and callers that branch on it must take their scalar path.
        /// <summary>Always <see langword="false"/>; this shim is a software implementation.</summary>
        public static bool IsHardwareAccelerated => false;

        // All-bits-set is the comparison result the real type produces, and callers rely on it:
        // masks are combined with bitwise operators and then tested against Create(-1).
        private const uint True = 0xFFFFFFFFu;
        private const uint False = 0u;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe uint BitsOf(float value) => *(uint*)&value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float FloatOf(uint bits) => *(float*)&bits;

        /// <summary>Creates a vector with every lane set to <paramref name="value"/>.</summary>
        public static Vector128<float> Create(float value)
        {
            uint bits = BitsOf(value);
            return new Vector128<float>(bits, bits, bits, bits);
        }

        /// <summary>Creates a vector with every lane set to <paramref name="value"/>.</summary>
        public static Vector128<int> Create(int value)
        {
            uint bits = unchecked((uint)value);
            return new Vector128<int>(bits, bits, bits, bits);
        }

        /// <summary>Creates a vector from four lane values.</summary>
        public static Vector128<float> Create(float e0, float e1, float e2, float e3) =>
            new Vector128<float>(BitsOf(e0), BitsOf(e1), BitsOf(e2), BitsOf(e3));

        /// <summary>Creates a vector from four lane values.</summary>
        public static Vector128<int> Create(int e0, int e1, int e2, int e3) =>
            new Vector128<int>(unchecked((uint)e0), unchecked((uint)e1), unchecked((uint)e2), unchecked((uint)e3));

        /// <summary>Reads four consecutive values starting at <paramref name="source"/>.</summary>
        public static Vector128<float> LoadUnsafe(ref float source)
        {
            ref float head = ref source;
            return new Vector128<float>(
                BitsOf(head),
                BitsOf(Unsafe.Add(ref head, 1)),
                BitsOf(Unsafe.Add(ref head, 2)),
                BitsOf(Unsafe.Add(ref head, 3)));
        }

        /// <summary>Writes the vector to four consecutive values starting at <paramref name="destination"/>.</summary>
        public static void StoreUnsafe(this Vector128<float> vector, ref float destination)
        {
            ref float head = ref destination;
            head = vector.GetElement(0);
            Unsafe.Add(ref head, 1) = vector.GetElement(1);
            Unsafe.Add(ref head, 2) = vector.GetElement(2);
            Unsafe.Add(ref head, 3) = vector.GetElement(3);
        }

        /// <summary>Returns the lane at <paramref name="index"/>.</summary>
        public static float GetElement(this Vector128<float> vector, int index) => FloatOf(vector.Bits(index));

        /// <summary>Returns the lane at <paramref name="index"/>.</summary>
        public static int GetElement(this Vector128<int> vector, int index) => unchecked((int)vector.Bits(index));

        /// <summary>Reinterprets the lanes as 32-bit integers without converting them.</summary>
        public static Vector128<int> AsInt32<T>(this Vector128<T> vector)
            where T : struct =>
            new Vector128<int>(vector.Bits(0), vector.Bits(1), vector.Bits(2), vector.Bits(3));

        /// <summary>Reinterprets the lanes as single-precision floats without converting them.</summary>
        public static Vector128<float> AsSingle<T>(this Vector128<T> vector)
            where T : struct =>
            new Vector128<float>(vector.Bits(0), vector.Bits(1), vector.Bits(2), vector.Bits(3));

        private static Vector128<float> Apply(
            Vector128<float> left, Vector128<float> right, Func<float, float, float> op) =>
            Create(
                op(left.GetElement(0), right.GetElement(0)),
                op(left.GetElement(1), right.GetElement(1)),
                op(left.GetElement(2), right.GetElement(2)),
                op(left.GetElement(3), right.GetElement(3)));

        /// <summary>Adds corresponding lanes.</summary>
        public static Vector128<float> Add(Vector128<float> left, Vector128<float> right) =>
            Create(
                left.GetElement(0) + right.GetElement(0),
                left.GetElement(1) + right.GetElement(1),
                left.GetElement(2) + right.GetElement(2),
                left.GetElement(3) + right.GetElement(3));

        /// <summary>Subtracts corresponding lanes.</summary>
        public static Vector128<float> Subtract(Vector128<float> left, Vector128<float> right) =>
            Create(
                left.GetElement(0) - right.GetElement(0),
                left.GetElement(1) - right.GetElement(1),
                left.GetElement(2) - right.GetElement(2),
                left.GetElement(3) - right.GetElement(3));

        /// <summary>Multiplies corresponding lanes.</summary>
        public static Vector128<float> Multiply(Vector128<float> left, Vector128<float> right) =>
            Create(
                left.GetElement(0) * right.GetElement(0),
                left.GetElement(1) * right.GetElement(1),
                left.GetElement(2) * right.GetElement(2),
                left.GetElement(3) * right.GetElement(3));

        /// <summary>Divides corresponding lanes.</summary>
        public static Vector128<float> Divide(Vector128<float> left, Vector128<float> right) =>
            Create(
                left.GetElement(0) / right.GetElement(0),
                left.GetElement(1) / right.GetElement(1),
                left.GetElement(2) / right.GetElement(2),
                left.GetElement(3) / right.GetElement(3));

        // Min and Max follow the hardware instruction, not MathF: when the lanes are unordered the
        // second operand wins, and negative zero is not preferred over positive zero. Matching
        // MathF here would quietly change broadphase results on NaN input.
        /// <summary>Lane-wise minimum, following <c>minps</c> semantics.</summary>
        public static Vector128<float> Min(Vector128<float> left, Vector128<float> right) =>
            Apply(left, right, static (a, b) => a < b ? a : b);

        /// <summary>Lane-wise maximum, following <c>maxps</c> semantics.</summary>
        public static Vector128<float> Max(Vector128<float> left, Vector128<float> right) =>
            Apply(left, right, static (a, b) => a > b ? a : b);

        /// <summary>Lane-wise <c>&lt;</c>, producing an all-bits-set mask per lane.</summary>
        public static Vector128<float> LessThan(Vector128<float> left, Vector128<float> right) =>
            Mask(left, right, static (a, b) => a < b);

        /// <summary>Lane-wise <c>&lt;=</c>, producing an all-bits-set mask per lane.</summary>
        public static Vector128<float> LessThanOrEqual(Vector128<float> left, Vector128<float> right) =>
            Mask(left, right, static (a, b) => a <= b);

        /// <summary>Lane-wise <c>&gt;</c>, producing an all-bits-set mask per lane.</summary>
        public static Vector128<float> GreaterThan(Vector128<float> left, Vector128<float> right) =>
            Mask(left, right, static (a, b) => a > b);

        /// <summary>Lane-wise <c>&gt;=</c>, producing an all-bits-set mask per lane.</summary>
        public static Vector128<float> GreaterThanOrEqual(Vector128<float> left, Vector128<float> right) =>
            Mask(left, right, static (a, b) => a >= b);

        private static Vector128<float> Mask(
            Vector128<float> left, Vector128<float> right, Func<float, float, bool> predicate) =>
            new Vector128<float>(
                predicate(left.GetElement(0), right.GetElement(0)) ? True : False,
                predicate(left.GetElement(1), right.GetElement(1)) ? True : False,
                predicate(left.GetElement(2), right.GetElement(2)) ? True : False,
                predicate(left.GetElement(3), right.GetElement(3)) ? True : False);

        /// <summary>Lane-wise bitwise AND.</summary>
        public static Vector128<T> BitwiseAnd<T>(Vector128<T> left, Vector128<T> right)
            where T : struct =>
            new Vector128<T>(
                left.Bits(0) & right.Bits(0),
                left.Bits(1) & right.Bits(1),
                left.Bits(2) & right.Bits(2),
                left.Bits(3) & right.Bits(3));

        /// <summary>Lane-wise bitwise OR.</summary>
        public static Vector128<T> BitwiseOr<T>(Vector128<T> left, Vector128<T> right)
            where T : struct =>
            new Vector128<T>(
                left.Bits(0) | right.Bits(0),
                left.Bits(1) | right.Bits(1),
                left.Bits(2) | right.Bits(2),
                left.Bits(3) | right.Bits(3));

        /// <summary>True when every lane is bitwise equal.</summary>
        public static bool EqualsAll<T>(Vector128<T> left, Vector128<T> right)
            where T : struct =>
            left.Equals(right);

        /// <summary>True when at least one lane is bitwise equal.</summary>
        public static bool EqualsAny<T>(Vector128<T> left, Vector128<T> right)
            where T : struct =>
            left.Bits(0) == right.Bits(0)
            || left.Bits(1) == right.Bits(1)
            || left.Bits(2) == right.Bits(2)
            || left.Bits(3) == right.Bits(3);
    }
}

#endif

