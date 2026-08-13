using System;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Shape kinds supported by artifact schema v1. Values are part of the binary format and
    /// must never be renumbered; a new kind gets a new value and a schema version bump.
    /// </summary>
    public enum PhysicsShapeType : byte
    {
        /// <summary>Reserved so that a zero byte in a corrupt file is never a valid shape.</summary>
        None = 0,

        /// <summary>Box with a full-size extent, converted from a <c>BoxCollider</c>.</summary>
        Box = 1,

        /// <summary>Sphere with a single radius, converted from a <c>SphereCollider</c>.</summary>
        Sphere = 2,

        /// <summary>
        /// Capsule described by a radius and the length of its cylindrical part, converted
        /// from a <c>CapsuleCollider</c>.
        /// </summary>
        Capsule = 3,

        /// <summary>
        /// Triangle mesh stored as vertices and indices. The artifact never stores a built
        /// acceleration structure: that is Jitter's internal state and is rebuilt on load.
        /// </summary>
        Mesh = 4,
    }

    /// <summary>
    /// One collision shape of a static body, in the body's local space.
    /// <para>
    /// A record is immutable and is created through the factory method of its kind, so an
    /// inconsistent combination — a sphere carrying a box size, a mesh with no vertices —
    /// cannot be constructed in the first place.
    /// </para>
    /// </summary>
    public sealed class PhysicsShapeRecord
    {
        /// <summary>
        /// Stable key derived from the collider's place in the hierarchy. It orders shapes
        /// deterministically and identifies them in diagnostics; it is never an instance id.
        /// </summary>
        public string ShapeKey { get; }

        /// <summary>Shape kind, deciding how <see cref="Size"/> and the mesh arrays are read.</summary>
        public PhysicsShapeType ShapeType { get; }

        /// <summary>Shape origin in the body's local space.</summary>
        public PhysicsVector3 LocalPosition { get; }

        /// <summary>Shape rotation in the body's local space, canonical and normalized.</summary>
        public PhysicsQuaternion LocalRotation { get; }

        /// <summary>Full box size. Only meaningful for <see cref="PhysicsShapeType.Box"/>.</summary>
        public PhysicsVector3 Size { get; }

        /// <summary>Radius of a sphere or capsule.</summary>
        public float Radius { get; }

        /// <summary>
        /// Length of the cylindrical part of a capsule, excluding the two caps. Zero is legal
        /// and describes a capsule degenerated into a sphere.
        /// </summary>
        public float Length { get; }

        /// <summary>Mesh vertices in the shape's local space; empty for primitives.</summary>
        public PhysicsVector3[] Vertices { get; }

        /// <summary>Mesh triangle indices, three per triangle; empty for primitives.</summary>
        public int[] Indices { get; }

        /// <summary>Number of triangles of a mesh shape.</summary>
        public int TriangleCount => Indices.Length / 3;

        private PhysicsShapeRecord(
            string shapeKey,
            PhysicsShapeType shapeType,
            PhysicsVector3 localPosition,
            PhysicsQuaternion localRotation,
            PhysicsVector3 size,
            float radius,
            float length,
            PhysicsVector3[] vertices,
            int[] indices)
        {
            ShapeKey = shapeKey ?? throw new ArgumentNullException(nameof(shapeKey));
            ShapeType = shapeType;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            Size = size;
            Radius = radius;
            Length = length;
            Vertices = vertices ?? Array.Empty<PhysicsVector3>();
            Indices = indices ?? Array.Empty<int>();
        }

        /// <summary>Creates a box shape from its full size.</summary>
        public static PhysicsShapeRecord Box(
            string shapeKey,
            PhysicsVector3 localPosition,
            PhysicsQuaternion localRotation,
            PhysicsVector3 size)
        {
            return new PhysicsShapeRecord(
                shapeKey,
                PhysicsShapeType.Box,
                localPosition,
                localRotation,
                size,
                0f,
                0f,
                null,
                null);
        }

        /// <summary>Creates a sphere shape.</summary>
        public static PhysicsShapeRecord Sphere(
            string shapeKey,
            PhysicsVector3 localPosition,
            PhysicsQuaternion localRotation,
            float radius)
        {
            return new PhysicsShapeRecord(
                shapeKey,
                PhysicsShapeType.Sphere,
                localPosition,
                localRotation,
                PhysicsVector3.Zero,
                radius,
                0f,
                null,
                null);
        }

        /// <summary>Creates a capsule shape from a radius and a cylinder length.</summary>
        public static PhysicsShapeRecord Capsule(
            string shapeKey,
            PhysicsVector3 localPosition,
            PhysicsQuaternion localRotation,
            float radius,
            float length)
        {
            return new PhysicsShapeRecord(
                shapeKey,
                PhysicsShapeType.Capsule,
                localPosition,
                localRotation,
                PhysicsVector3.Zero,
                radius,
                length,
                null,
                null);
        }

        /// <summary>Creates a triangle mesh shape. The arrays are taken by reference and never mutated.</summary>
        public static PhysicsShapeRecord Mesh(
            string shapeKey,
            PhysicsVector3 localPosition,
            PhysicsQuaternion localRotation,
            PhysicsVector3[] vertices,
            int[] indices)
        {
            return new PhysicsShapeRecord(
                shapeKey,
                PhysicsShapeType.Mesh,
                localPosition,
                localRotation,
                PhysicsVector3.Zero,
                0f,
                0f,
                vertices ?? throw new ArgumentNullException(nameof(vertices)),
                indices ?? throw new ArgumentNullException(nameof(indices)));
        }
    }
}
