using System;
using System.Globalization;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// The canonicalization rules that make a bake byte-exact.
    /// <para>
    /// Two bakes of an unchanged scene must produce the same file and the same SHA-256, on
    /// any machine. Floating point gives two ways to break that even when the numbers are
    /// mathematically equal: <c>-0.0f</c> has different bits than <c>+0.0f</c>, and a
    /// quaternion <c>q</c> describes the same rotation as <c>-q</c>. Both are collapsed to
    /// one representation here, in one place, so the writer and the validator cannot
    /// disagree about what "canonical" means.
    /// </para>
    /// </summary>
    public static class PhysicsCanonicalization
    {
        /// <summary>
        /// Tolerance for the "is this quaternion normalized" check. Authoring data comes from
        /// float transforms, so exact unit length is not achievable; the artifact stores the
        /// normalized value and the reader re-checks it within this window.
        /// </summary>
        public const float QuaternionLengthTolerance = 1e-4f;

        /// <summary>True when the value is neither NaN nor infinite.</summary>
        public static bool IsFinite(float value)
        {
            // float.IsFinite exists in netstandard2.1 but not in every runtime the portable
            // assemblies target, so the check is written explicitly.
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Canonical form of a single float: negative zero becomes positive zero. Every other
        /// value is returned unchanged — the package never rounds authoring data, because
        /// rounding would silently move geometry.
        /// </summary>
        public static float CanonicalFloat(float value)
        {
            return value == 0f ? 0f : value;
        }

        /// <summary>
        /// Normalizes a quaternion and picks the canonical sign. The first non-zero component
        /// in <c>w, x, y, z</c> order is made positive, so <c>q</c> and <c>-q</c> always
        /// serialize to the same bytes.
        /// </summary>
        public static PhysicsQuaternion CanonicalQuaternion(PhysicsQuaternion value)
        {
            if (!value.IsFinite)
            {
                throw new ArgumentException(
                    "Quaternion contains NaN or infinity and cannot be canonicalized.",
                    nameof(value));
            }

            double lengthSquared =
                (double)value.X * value.X
                + (double)value.Y * value.Y
                + (double)value.Z * value.Z
                + (double)value.W * value.W;
            if (lengthSquared <= 0d)
            {
                throw new ArgumentException(
                    "Quaternion has zero length and does not describe a rotation.",
                    nameof(value));
            }

            // Normalization runs in double and is rounded once to float, so that the result
            // does not depend on the order the runtime happens to evaluate the expression in.
            double inverseLength = 1d / Math.Sqrt(lengthSquared);
            float x = (float)(value.X * inverseLength);
            float y = (float)(value.Y * inverseLength);
            float z = (float)(value.Z * inverseLength);
            float w = (float)(value.W * inverseLength);

            if (ShouldNegate(w, x, y, z))
            {
                x = -x;
                y = -y;
                z = -z;
                w = -w;
            }

            return new PhysicsQuaternion(
                CanonicalFloat(x),
                CanonicalFloat(y),
                CanonicalFloat(z),
                CanonicalFloat(w));
        }

        /// <summary>
        /// True when the quaternion is normalized within <see cref="QuaternionLengthTolerance"/>
        /// and carries the canonical sign. The reader uses this instead of re-canonicalizing,
        /// because a file that is not canonical was not produced by this baker.
        /// </summary>
        public static bool IsCanonicalQuaternion(PhysicsQuaternion value)
        {
            if (!value.IsFinite)
            {
                return false;
            }

            float lengthSquared = value.LengthSquared;
            if (Math.Abs(lengthSquared - 1f) > QuaternionLengthTolerance)
            {
                return false;
            }

            return !ShouldNegate(value.W, value.X, value.Y, value.Z)
                && !IsNegativeZero(value.X)
                && !IsNegativeZero(value.Y)
                && !IsNegativeZero(value.Z)
                && !IsNegativeZero(value.W);
        }

        /// <summary>True when the value is exactly <c>-0.0f</c>.</summary>
        public static bool IsNegativeZero(float value)
        {
            return value == 0f && float.IsNegative(value);
        }

        /// <summary>
        /// Culture-independent round-trip formatting, used by diagnostics and error messages.
        /// A comma decimal separator in a log line makes two machines look different when
        /// they are not.
        /// </summary>
        public static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool ShouldNegate(float w, float x, float y, float z)
        {
            if (w != 0f)
            {
                return w < 0f;
            }

            if (x != 0f)
            {
                return x < 0f;
            }

            if (y != 0f)
            {
                return y < 0f;
            }

            return z < 0f;
        }
    }
}
