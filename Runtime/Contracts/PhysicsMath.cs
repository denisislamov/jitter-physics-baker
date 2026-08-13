using System;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Engine-independent 3-component vector. The package cannot use
    /// <c>UnityEngine.Vector3</c> here because the same records are decoded by a dedicated
    /// server that never loads the engine.
    /// </summary>
    [Serializable]
    public readonly struct PhysicsVector3 : IEquatable<PhysicsVector3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public PhysicsVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static PhysicsVector3 Zero => new PhysicsVector3(0f, 0f, 0f);

        /// <summary>True when no component is NaN or infinite.</summary>
        public bool IsFinite =>
            PhysicsCanonicalization.IsFinite(X)
            && PhysicsCanonicalization.IsFinite(Y)
            && PhysicsCanonicalization.IsFinite(Z);

        /// <summary>Component-wise canonical form; see <see cref="PhysicsCanonicalization"/>.</summary>
        public PhysicsVector3 Canonical() => new PhysicsVector3(
            PhysicsCanonicalization.CanonicalFloat(X),
            PhysicsCanonicalization.CanonicalFloat(Y),
            PhysicsCanonicalization.CanonicalFloat(Z));

        public bool Equals(PhysicsVector3 other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is PhysicsVector3 other && Equals(other);

        public override int GetHashCode() =>
            (X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode();

        public override string ToString() =>
            PhysicsCanonicalization.Format(X) + ", "
            + PhysicsCanonicalization.Format(Y) + ", "
            + PhysicsCanonicalization.Format(Z);
    }

    /// <summary>
    /// Engine-independent quaternion in <c>(x, y, z, w)</c> order, matching the order the
    /// binary format writes.
    /// </summary>
    [Serializable]
    public readonly struct PhysicsQuaternion : IEquatable<PhysicsQuaternion>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;

        public PhysicsQuaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static PhysicsQuaternion Identity => new PhysicsQuaternion(0f, 0f, 0f, 1f);

        public bool IsFinite =>
            PhysicsCanonicalization.IsFinite(X)
            && PhysicsCanonicalization.IsFinite(Y)
            && PhysicsCanonicalization.IsFinite(Z)
            && PhysicsCanonicalization.IsFinite(W);

        /// <summary>Squared length; used to check normalization without a square root.</summary>
        public float LengthSquared => X * X + Y * Y + Z * Z + W * W;

        /// <summary>
        /// Normalized, sign-canonical form. <c>q</c> and <c>-q</c> describe the same rotation
        /// but are different bytes, so exactly one of them is allowed in an artifact.
        /// </summary>
        public PhysicsQuaternion Canonical() => PhysicsCanonicalization.CanonicalQuaternion(this);

        public bool Equals(PhysicsQuaternion other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

        public override bool Equals(object obj) => obj is PhysicsQuaternion other && Equals(other);

        public override int GetHashCode() =>
            ((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode()) * 397 ^ W.GetHashCode();

        public override string ToString() =>
            PhysicsCanonicalization.Format(X) + ", "
            + PhysicsCanonicalization.Format(Y) + ", "
            + PhysicsCanonicalization.Format(Z) + ", "
            + PhysicsCanonicalization.Format(W);
    }
}
