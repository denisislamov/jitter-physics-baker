using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Diagnostics
{
    /// <summary>
    /// Exact geometry comparison used by the Scene View bake overlay.
    /// <para>
    /// Exact means artifact-exact rather than visually close: moving a collider by one
    /// representable float changes the bytes produced by the next bake, so the overlay must
    /// call it changed as well. Material properties are intentionally excluded because the
    /// overlay answers which geometry is stale, not whether friction needs a re-bake.
    /// </para>
    /// </summary>
    public static class JitterPhysicsGeometryComparer
    {
        /// <summary>
        /// Returns whether the current body transform has the exact canonical pose stored in
        /// <paramref name="bakedBody"/>.
        /// </summary>
        public static bool BodyPoseMatches(PhysicsBodyRecord bakedBody, Transform currentTransform)
        {
            if (bakedBody == null || currentTransform == null)
            {
                return false;
            }

            Vector3 position = currentTransform.position;
            Quaternion orientation = currentTransform.rotation;

            var currentPosition = new PhysicsVector3(position.x, position.y, position.z).Canonical();
            var currentOrientation = new PhysicsQuaternion(
                orientation.x, orientation.y, orientation.z, orientation.w).Canonical();

            return currentPosition.Equals(bakedBody.Position)
                && currentOrientation.Equals(bakedBody.Orientation);
        }

        /// <summary>
        /// Returns whether two shape records describe the exact same artifact geometry and
        /// carry the same stable shape key.
        /// </summary>
        public static bool ShapesMatch(PhysicsShapeRecord baked, PhysicsShapeRecord current)
        {
            if (baked == null || current == null
                || !string.Equals(baked.ShapeKey, current.ShapeKey, System.StringComparison.Ordinal)
                || baked.ShapeType != current.ShapeType
                || !baked.LocalPosition.Equals(current.LocalPosition)
                || !baked.LocalRotation.Equals(current.LocalRotation)
                || !baked.Size.Equals(current.Size)
                || !baked.Radius.Equals(current.Radius)
                || !baked.Length.Equals(current.Length)
                || baked.Vertices.Length != current.Vertices.Length
                || baked.Indices.Length != current.Indices.Length)
            {
                return false;
            }

            for (int i = 0; i < baked.Vertices.Length; i++)
            {
                if (!baked.Vertices[i].Equals(current.Vertices[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < baked.Indices.Length; i++)
            {
                if (baked.Indices[i] != current.Indices[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
