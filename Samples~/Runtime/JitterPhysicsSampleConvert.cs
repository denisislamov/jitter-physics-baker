using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using UnityEngine;

namespace DataSakura.JitterPhysics.Samples
{
    /// <summary>
    /// Conversions between Unity and Jitter2 vector types, and helpers for spawning the dynamic
    /// bodies the samples throw at the baked level.
    /// </summary>
    /// <remarks>
    /// The conversion is component-for-component with no axis flip. The artifact stores the
    /// authoring transforms exactly as Unity reported them, so the baked level and anything the
    /// game spawns share one coordinate system; flipping here would misplace every bullet.
    /// </remarks>
    public static class JitterPhysicsSampleConvert
    {
        /// <summary>Unity position or direction to Jitter2.</summary>
        public static JVector ToJitter(this Vector3 value) => new JVector(value.x, value.y, value.z);

        /// <summary>Jitter2 position or direction to Unity.</summary>
        public static Vector3 ToUnity(this JVector value) => new Vector3(value.X, value.Y, value.Z);

        /// <summary>Unity rotation to Jitter2.</summary>
        public static JQuaternion ToJitter(this Quaternion value) =>
            new JQuaternion(value.x, value.y, value.z, value.w);

        /// <summary>Jitter2 rotation to Unity.</summary>
        public static Quaternion ToUnity(this JQuaternion value) =>
            new Quaternion(value.X, value.Y, value.Z, value.W);

        /// <summary>Creates a dynamic sphere.</summary>
        /// <param name="world">World to create it in.</param>
        /// <param name="position">World-space centre.</param>
        /// <param name="radius">Sphere radius in metres.</param>
        /// <param name="mass">Mass in kilograms.</param>
        /// <param name="restitution">0 for a dead landing, near 1 for a lively bounce.</param>
        /// <param name="friction">Surface friction.</param>
        public static RigidBody CreateSphere(
            World world,
            Vector3 position,
            float radius,
            float mass = 1f,
            float restitution = 0.4f,
            float friction = 0.4f)
        {
            RigidBody body = world.CreateRigidBody();
            body.AddShape(new SphereShape(radius));
            body.Position = position.ToJitter();
            body.SetMassInertia(mass);
            body.Restitution = restitution;
            body.Friction = friction;
            return body;
        }

        /// <summary>Creates a dynamic box.</summary>
        /// <param name="world">World to create it in.</param>
        /// <param name="position">World-space centre.</param>
        /// <param name="rotation">World-space rotation.</param>
        /// <param name="size">Full extents in metres, not half extents.</param>
        /// <param name="mass">Mass in kilograms.</param>
        public static RigidBody CreateBox(
            World world,
            Vector3 position,
            Quaternion rotation,
            Vector3 size,
            float mass = 1f)
        {
            RigidBody body = world.CreateRigidBody();
            body.AddShape(new BoxShape(size.ToJitter()));
            body.Position = position.ToJitter();
            body.Orientation = rotation.ToJitter();
            body.SetMassInertia(mass);
            return body;
        }

        /// <summary>
        /// Creates an upright capsule that is moved by code rather than by the solver.
        /// </summary>
        /// <remarks>
        /// Kinematic, because a character driven by input should not be pushed around by the
        /// bodies it walks into; it pushes them.
        /// </remarks>
        /// <param name="world">World to create it in.</param>
        /// <param name="position">World-space centre.</param>
        /// <param name="radius">Capsule radius in metres.</param>
        /// <param name="cylinderLength">Length of the straight section between the two caps.</param>
        public static RigidBody CreateKinematicCapsule(
            World world,
            Vector3 position,
            float radius,
            float cylinderLength)
        {
            RigidBody body = world.CreateRigidBody();
            body.AddShape(new CapsuleShape(radius, cylinderLength));
            body.Position = position.ToJitter();
            body.MotionType = MotionType.Kinematic;
            return body;
        }

        /// <summary>Returns the body a raycast proxy belongs to, or null when it is not a shape.</summary>
        public static RigidBody BodyOf(object proxy) =>
            proxy is RigidBodyShape shape ? shape.RigidBody : null;
    }
}

