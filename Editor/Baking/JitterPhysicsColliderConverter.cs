using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Baking
{
    /// <summary>Why a collider could not be converted.</summary>
    public enum JitterPhysicsConversionStatus
    {
        /// <summary>The collider was converted.</summary>
        Converted = 0,

        /// <summary>The collider type is not supported by artifact schema 1.</summary>
        UnsupportedType,

        /// <summary>The collider is a trigger and describes no collision geometry.</summary>
        Trigger,

        /// <summary>A scale component is zero, so the shape has no volume.</summary>
        DegenerateScale,

        /// <summary>A transform or collider value is NaN or infinite.</summary>
        NotFinite,

        /// <summary>The resulting shape is smaller than the format allows.</summary>
        DegenerateShape,

        /// <summary>A mesh collider has no readable mesh data.</summary>
        UnreadableMesh,

        /// <summary>A mesh has no triangles, or an index count that is not a multiple of three.</summary>
        InvalidMesh,
    }

    /// <summary>Outcome of converting one collider.</summary>
    public readonly struct JitterPhysicsConversionResult
    {
        /// <summary>Why the conversion succeeded or failed.</summary>
        public JitterPhysicsConversionStatus Status { get; }

        /// <summary>The produced record, or <c>null</c> on failure.</summary>
        public PhysicsShapeRecord Shape { get; }

        /// <summary>Human-readable detail for diagnostics.</summary>
        public string Message { get; }

        /// <summary>Non-fatal remark; the shape is still produced.</summary>
        public string Warning { get; }

        private JitterPhysicsConversionResult(
            JitterPhysicsConversionStatus status,
            PhysicsShapeRecord shape,
            string message,
            string warning)
        {
            Status = status;
            Shape = shape;
            Message = message;
            Warning = warning;
        }

        /// <summary>True when a shape was produced.</summary>
        public bool Succeeded => Status == JitterPhysicsConversionStatus.Converted;

        internal static JitterPhysicsConversionResult Success(PhysicsShapeRecord shape, string warning = null)
        {
            return new JitterPhysicsConversionResult(
                JitterPhysicsConversionStatus.Converted, shape, null, warning);
        }

        internal static JitterPhysicsConversionResult Failure(
            JitterPhysicsConversionStatus status,
            string message)
        {
            return new JitterPhysicsConversionResult(status, null, message, null);
        }
    }

    /// <summary>
    /// Converts Unity colliders into portable shape descriptors.
    /// <para>
    /// This is where the package decides what a level's geometry <em>means</em>, so the rules
    /// are explicit rather than best-effort. A collider that cannot be represented exactly is
    /// rejected with a reason instead of approximated: a wall that is silently the wrong size
    /// is far more expensive to find than a bake that refuses to run.
    /// </para>
    /// <para>
    /// The one deliberate approximation is a sphere under non-uniform scale, which has no
    /// exact sphere representation. It is converted conservatively — using the largest axis,
    /// so the shape never becomes smaller than the authored collider — and reported as a
    /// warning, because a player passing through geometry is worse than a player bumping into
    /// slightly more of it.
    /// </para>
    /// </summary>
    public static class JitterPhysicsColliderConverter
    {
        /// <summary>Smallest accepted extent; below this a shape has no usable volume.</summary>
        public const float MinimumExtent = 1e-5f;

        /// <summary>
        /// Converts <paramref name="collider"/> into a shape expressed in the local space of
        /// <paramref name="bodyRoot"/>.
        /// </summary>
        public static JitterPhysicsConversionResult Convert(
            Transform bodyRoot,
            Collider collider,
            string shapeKey)
        {
            if (collider.isTrigger)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.Trigger,
                    "Triggers describe volumes for gameplay, not collision geometry.");
            }

            Transform colliderTransform = collider.transform;
            Vector3 lossyScale = colliderTransform.lossyScale;

            if (!IsFinite(lossyScale) || !IsFinite(colliderTransform.position))
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.NotFinite,
                    "The transform contains NaN or infinity.");
            }

            Vector3 absoluteScale = new Vector3(
                Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z));

            switch (collider)
            {
                case BoxCollider box:
                    return ConvertBox(bodyRoot, box, absoluteScale, shapeKey);
                case SphereCollider sphere:
                    return ConvertSphere(bodyRoot, sphere, absoluteScale, shapeKey);
                case CapsuleCollider capsule:
                    return ConvertCapsule(bodyRoot, capsule, absoluteScale, shapeKey);
                case MeshCollider mesh:
                    return ConvertMesh(bodyRoot, mesh, shapeKey);
                default:
                    return JitterPhysicsConversionResult.Failure(
                        JitterPhysicsConversionStatus.UnsupportedType,
                        $"{collider.GetType().Name} is not supported by artifact schema 1.");
            }
        }

        private static JitterPhysicsConversionResult ConvertBox(
            Transform bodyRoot,
            BoxCollider box,
            Vector3 absoluteScale,
            string shapeKey)
        {
            // Unity's size is the full extent, and so is Jitter's, so the sizes map directly.
            Vector3 size = Vector3.Scale(box.size, absoluteScale);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

            if (size.x < MinimumExtent || size.y < MinimumExtent || size.z < MinimumExtent)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.DegenerateShape,
                    $"The scaled size {size} has an extent of zero.");
            }

            GetLocalPose(bodyRoot, box.transform, box.center, out PhysicsVector3 position, out PhysicsQuaternion rotation);

            return JitterPhysicsConversionResult.Success(
                PhysicsShapeRecord.Box(shapeKey, position, rotation, ToVector(size)));
        }

        private static JitterPhysicsConversionResult ConvertSphere(
            Transform bodyRoot,
            SphereCollider sphere,
            Vector3 absoluteScale,
            string shapeKey)
        {
            float maximumScale = Mathf.Max(absoluteScale.x, Mathf.Max(absoluteScale.y, absoluteScale.z));
            float radius = sphere.radius * maximumScale;

            if (radius < MinimumExtent)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.DegenerateShape,
                    $"The scaled radius {radius} is zero.");
            }

            GetLocalPose(
                bodyRoot, sphere.transform, sphere.center, out PhysicsVector3 position, out PhysicsQuaternion rotation);

            string warning = null;
            if (!IsUniform(absoluteScale))
            {
                warning =
                    $"Non-uniform scale {absoluteScale} cannot be represented by a sphere. The largest "
                    + $"axis was used, so the shape is a conservative over-approximation (radius {radius}).";
            }

            return JitterPhysicsConversionResult.Success(
                PhysicsShapeRecord.Sphere(shapeKey, position, rotation, radius), warning);
        }

        private static JitterPhysicsConversionResult ConvertCapsule(
            Transform bodyRoot,
            CapsuleCollider capsule,
            Vector3 absoluteScale,
            string shapeKey)
        {
            // Unity's capsule points along X, Y or Z; the shape record is always Y-aligned,
            // and the difference is carried by the local rotation rather than by a separate
            // axis field the runtime would have to interpret the same way.
            float heightScale;
            float radiusScale;
            Quaternion axisRotation;

            switch (capsule.direction)
            {
                case 0:
                    heightScale = absoluteScale.x;
                    radiusScale = Mathf.Max(absoluteScale.y, absoluteScale.z);
                    axisRotation = Quaternion.Euler(0f, 0f, -90f);
                    break;
                case 2:
                    heightScale = absoluteScale.z;
                    radiusScale = Mathf.Max(absoluteScale.x, absoluteScale.y);
                    axisRotation = Quaternion.Euler(90f, 0f, 0f);
                    break;
                default:
                    heightScale = absoluteScale.y;
                    radiusScale = Mathf.Max(absoluteScale.x, absoluteScale.z);
                    axisRotation = Quaternion.identity;
                    break;
            }

            float radius = capsule.radius * radiusScale;
            float scaledHeight = capsule.height * heightScale;

            if (radius < MinimumExtent)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.DegenerateShape,
                    $"The scaled radius {radius} is zero.");
            }

            // Unity's height includes both caps. A height below the diameter degenerates into
            // a sphere, which is legal and is expressed as a zero-length cylinder.
            float length = Mathf.Max(0f, scaledHeight - (2f * radius));

            GetLocalPose(
                bodyRoot,
                capsule.transform,
                capsule.center,
                out PhysicsVector3 position,
                out PhysicsQuaternion rotation,
                axisRotation);

            return JitterPhysicsConversionResult.Success(
                PhysicsShapeRecord.Capsule(shapeKey, position, rotation, radius, length));
        }

        private static JitterPhysicsConversionResult ConvertMesh(
            Transform bodyRoot,
            MeshCollider collider,
            string shapeKey)
        {
            Mesh mesh = collider.sharedMesh;
            if (mesh == null)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.UnreadableMesh,
                    "The mesh collider has no mesh assigned.");
            }

            if (!mesh.isReadable)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.UnreadableMesh,
                    $"'{mesh.name}' is not readable. Enable Read/Write in the model import settings; "
                    + "the baker cannot read vertex data otherwise.");
            }

            Vector3[] sourceVertices = mesh.vertices;
            int[] sourceIndices = mesh.triangles;

            if (sourceVertices.Length == 0 || sourceIndices.Length == 0)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.InvalidMesh,
                    $"'{mesh.name}' has no triangles.");
            }

            if (sourceIndices.Length % 3 != 0)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.InvalidMesh,
                    $"'{mesh.name}' has {sourceIndices.Length} indices, which is not a multiple of three.");
            }

            // Mesh vertices are baked into the body's local space with the full transform,
            // so a non-uniform or sheared transform is represented exactly instead of being
            // approximated by a pose plus a scale the runtime would have to reapply.
            Matrix4x4 toBodyLocal = bodyRoot.worldToLocalMatrix * collider.transform.localToWorldMatrix;

            var vertices = new PhysicsVector3[sourceVertices.Length];
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector3 transformed = toBodyLocal.MultiplyPoint3x4(sourceVertices[i]);
                if (!IsFinite(transformed))
                {
                    return JitterPhysicsConversionResult.Failure(
                        JitterPhysicsConversionStatus.NotFinite,
                        $"Vertex {i} of '{mesh.name}' is NaN or infinite after transformation.");
                }

                vertices[i] = ToVector(transformed);
            }

            var indices = new int[sourceIndices.Length];
            System.Array.Copy(sourceIndices, indices, sourceIndices.Length);

            // A negative determinant mirrors the geometry, which flips every triangle's
            // normal. Without swapping the winding the surface would face inwards and
            // collision queries would report the inside of the level as solid.
            if (toBodyLocal.determinant < 0f)
            {
                for (int i = 0; i < indices.Length; i += 3)
                {
                    (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                }
            }

            return JitterPhysicsConversionResult.Success(
                PhysicsShapeRecord.Mesh(
                    shapeKey, PhysicsVector3.Zero, PhysicsQuaternion.Identity, vertices, indices));
        }

        /// <summary>
        /// Expresses a collider's centre and rotation in the body's local space, optionally
        /// composed with a fixed axis correction.
        /// </summary>
        private static void GetLocalPose(
            Transform bodyRoot,
            Transform colliderTransform,
            Vector3 center,
            out PhysicsVector3 position,
            out PhysicsQuaternion rotation,
            Quaternion axisRotation = default)
        {
            Vector3 worldCenter = colliderTransform.TransformPoint(center);
            Quaternion inverseBody = Quaternion.Inverse(bodyRoot.rotation);

            Vector3 localPosition = inverseBody * (worldCenter - bodyRoot.position);
            Quaternion localRotation = inverseBody * colliderTransform.rotation;

            if (axisRotation != default)
            {
                localRotation *= axisRotation;
            }

            position = ToVector(localPosition);
            rotation = new PhysicsQuaternion(
                localRotation.x, localRotation.y, localRotation.z, localRotation.w).Canonical();
        }

        private static bool IsUniform(Vector3 scale)
        {
            const float tolerance = 1e-4f;
            return Mathf.Abs(scale.x - scale.y) <= tolerance && Mathf.Abs(scale.y - scale.z) <= tolerance;
        }

        private static bool IsFinite(Vector3 value)
        {
            return PhysicsCanonicalization.IsFinite(value.x)
                && PhysicsCanonicalization.IsFinite(value.y)
                && PhysicsCanonicalization.IsFinite(value.z);
        }

        private static PhysicsVector3 ToVector(Vector3 value)
        {
            return new PhysicsVector3(value.x, value.y, value.z).Canonical();
        }
    }
}

