using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace DataSakura.JitterPhysics.Integration
{
    /// <summary>Outcome of applying an artifact to a world.</summary>
    public sealed class PhysicsWorldBuildResult
    {
        /// <summary>Failure description; only meaningful when <see cref="Succeeded"/> is false.</summary>
        public PhysicsArtifactError Error { get; }

        /// <summary>Number of static bodies created.</summary>
        public int BodyCount { get; }

        /// <summary>Number of collision shapes created, counting one per mesh triangle.</summary>
        public int ShapeCount { get; }

        /// <summary>Milliseconds spent building the world.</summary>
        public double ElapsedMilliseconds { get; }

        /// <summary>
        /// Hash over the created topology in creation order. Two runtimes that build the same
        /// world from the same artifact produce the same value; it is the practical way to
        /// prove the client and the server agree about static geometry, which is a stronger
        /// statement than "both loaded the same file".
        /// </summary>
        public string TopologyFingerprint { get; }

        internal PhysicsWorldBuildResult(
            PhysicsArtifactError error,
            int bodyCount,
            int shapeCount,
            double elapsedMilliseconds,
            string topologyFingerprint)
        {
            Error = error;
            BodyCount = bodyCount;
            ShapeCount = shapeCount;
            ElapsedMilliseconds = elapsedMilliseconds;
            TopologyFingerprint = topologyFingerprint;
        }

        /// <summary>True when the world was built.</summary>
        public bool Succeeded => !Error.IsError;
    }

    /// <summary>
    /// Rebuilds the static half of a Jitter world from a baked artifact.
    /// <para>
    /// This is the one loader. The Unity client and the dedicated server both call it, which
    /// is the entire point: two implementations of "turn these records into shapes" would
    /// drift, and the drift would appear as a player walking through a wall on one side only.
    /// </para>
    /// <para>
    /// The builder creates bodies through Jitter's public API and never restores engine
    /// internals. It also does not own the simulation: <c>World.Step</c> stays with the
    /// consumer, because the tick loop belongs to the game, not to the level format.
    /// </para>
    /// </summary>
    public static class JitterPhysicsWorldBuilder
    {
        /// <summary>
        /// Worlds that already carry a static artifact. Applying a second one would silently
        /// double the level's geometry, so it is refused rather than merged.
        /// </summary>
        private static readonly ConditionalWeakTable<World, AppliedArtifact> Applied =
            new ConditionalWeakTable<World, AppliedArtifact>();

        private sealed class AppliedArtifact
        {
            internal AppliedArtifact(string levelId)
            {
                LevelId = levelId;
            }

            internal string LevelId { get; }
        }

        /// <summary>
        /// Applies world settings and static geometry to <paramref name="world"/>.
        /// <para>
        /// On failure nothing is left behind: every body created during the attempt is
        /// removed again. A partially built level is worse than none, because it looks like
        /// it worked.
        /// </para>
        /// </summary>
        public static PhysicsWorldBuildResult Apply(World world, PhysicsArtifact artifact)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            if (Applied.TryGetValue(world, out AppliedArtifact existing))
            {
                return Failure(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"This world already has the static artifact of level '{existing.LevelId}' applied. "
                    + "A new artifact needs a new world; hot reloading a running match world is not supported.",
                    artifact);
            }

            PhysicsArtifactError validationError = PhysicsArtifactValidator.Validate(artifact);
            if (validationError.IsError)
            {
                return new PhysicsWorldBuildResult(validationError, 0, 0, 0d, null);
            }

            var stopwatch = Stopwatch.StartNew();
            var created = new List<RigidBody>(artifact.Bodies.Count);
            var fingerprint = new FingerprintBuilder();
            int shapeCount = 0;

            try
            {
                ApplyWorldSettings(world, artifact.WorldSettings);

                for (int i = 0; i < artifact.Bodies.Count; i++)
                {
                    PhysicsBodyRecord record = artifact.Bodies[i];
                    RigidBody body = world.CreateRigidBody();
                    created.Add(body);

                    shapeCount += AddShapes(body, record, fingerprint);

                    body.Position = ToJVector(record.Position);
                    body.Orientation = ToJQuaternion(record.Orientation);
                    body.Friction = record.Friction;
                    body.Restitution = record.Restitution;

                    // Set last: switching to static zeroes velocities and deactivates the
                    // body, and Jitter expects the pose to be in place by then.
                    body.MotionType = MotionType.Static;

                    fingerprint.Body(record);
                }
            }
            catch (Exception exception)
            {
                Rollback(world, created);
                return Failure(
                    PhysicsArtifactErrorCode.InvalidValue,
                    "Building the world failed and was rolled back: " + exception.Message,
                    artifact);
            }

            stopwatch.Stop();
            Applied.Add(world, new AppliedArtifact(artifact.LevelId));

            return new PhysicsWorldBuildResult(
                default,
                created.Count,
                shapeCount,
                stopwatch.Elapsed.TotalMilliseconds,
                fingerprint.Build());
        }

        /// <summary>
        /// True when a static artifact has already been applied to <paramref name="world"/>.
        /// </summary>
        public static bool HasArtifact(World world)
        {
            return world != null && Applied.TryGetValue(world, out _);
        }

        private static void ApplyWorldSettings(World world, PhysicsWorldSettings settings)
        {
            world.Gravity = ToJVector(settings.Gravity);

            // Both are invariants of prediction rather than preferences: a client that solves
            // differently from the server diverges in a way no reconciliation can explain.
            world.SolveMode = SolveMode.Deterministic;
            world.SolverIterations = (settings.SolverIterations, settings.RelaxationIterations);
            world.AllowDeactivation = settings.AllowDeactivation;
        }

        private static int AddShapes(
            RigidBody body,
            PhysicsBodyRecord record,
            FingerprintBuilder fingerprint)
        {
            int count = 0;

            for (int i = 0; i < record.Shapes.Count; i++)
            {
                PhysicsShapeRecord shape = record.Shapes[i];
                fingerprint.Shape(shape);

                if (shape.ShapeType == PhysicsShapeType.Mesh)
                {
                    count += AddMeshShapes(body, shape);
                    continue;
                }

                RigidBodyShape primitive = CreatePrimitive(shape);

                // Static bodies never need a mass tensor, and computing one for a large
                // level is pure cost, so the existing values are preserved instead.
                body.AddShape(Transform(primitive, shape), MassInertiaUpdateMode.Preserve);
                count++;
            }

            return count;
        }

        private static RigidBodyShape CreatePrimitive(PhysicsShapeRecord shape)
        {
            switch (shape.ShapeType)
            {
                case PhysicsShapeType.Box:
                    return new BoxShape(ToJVector(shape.Size));

                case PhysicsShapeType.Sphere:
                    return new SphereShape(shape.Radius);

                case PhysicsShapeType.Capsule:
                    return new CapsuleShape(shape.Radius, shape.Length);

                default:
                    throw new InvalidOperationException(
                        $"Shape '{shape.ShapeKey}' has unsupported type {shape.ShapeType}.");
            }
        }

        private static RigidBodyShape Transform(RigidBodyShape shape, PhysicsShapeRecord record)
        {
            bool hasTranslation = record.LocalPosition.X != 0f
                || record.LocalPosition.Y != 0f
                || record.LocalPosition.Z != 0f;
            bool hasRotation = record.LocalRotation.X != 0f
                || record.LocalRotation.Y != 0f
                || record.LocalRotation.Z != 0f
                || record.LocalRotation.W != 1f;

            if (!hasTranslation && !hasRotation)
            {
                // An identity pose is the common case; wrapping it would add an indirection
                // to every collision query for nothing.
                return shape;
            }

            JMatrix rotation = JMatrix.CreateFromQuaternion(ToJQuaternion(record.LocalRotation));
            return new TransformedShape(shape, ToJVector(record.LocalPosition), rotation);
        }

        private static int AddMeshShapes(RigidBody body, PhysicsShapeRecord record)
        {
            var vertices = new JVector[record.Vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = ToJVector(record.Vertices[i]);
            }

            // Vertices are already expressed in the body's local space by the baker, so no
            // transform is applied here: the artifact is the single description of where the
            // geometry is.
            var mesh = new TriangleMesh(vertices, record.Indices);

            var shapes = new List<RigidBodyShape>(mesh.Indices.Length);
            for (int i = 0; i < mesh.Indices.Length; i++)
            {
                shapes.Add(new TriangleShape(mesh, i));
            }

            body.AddShapes(shapes, MassInertiaUpdateMode.Preserve);
            return shapes.Count;
        }

        private static void Rollback(World world, List<RigidBody> created)
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                try
                {
                    world.Remove(created[i]);
                }
                catch (Exception)
                {
                    // Rollback runs while another failure is already being reported; the
                    // original cause is more useful than a secondary cleanup error.
                }
            }
        }

        private static PhysicsWorldBuildResult Failure(
            PhysicsArtifactErrorCode code,
            string message,
            PhysicsArtifact artifact)
        {
            return new PhysicsWorldBuildResult(
                new PhysicsArtifactError(code, message, artifact.LevelId), 0, 0, 0d, null);
        }

        private static JVector ToJVector(PhysicsVector3 value)
        {
            return new JVector(value.X, value.Y, value.Z);
        }

        private static JQuaternion ToJQuaternion(PhysicsQuaternion value)
        {
            return new JQuaternion(value.X, value.Y, value.Z, value.W);
        }

        /// <summary>
        /// Accumulates a deterministic description of what was created, in creation order.
        /// </summary>
        private sealed class FingerprintBuilder
        {
            private readonly System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);

            internal void Body(PhysicsBodyRecord record)
            {
                builder.Append("b:").Append(record.SourceId)
                    .Append('|').Append(Format(record.Position))
                    .Append('|').Append(Format(record.Orientation))
                    .Append('\n');
            }

            internal void Shape(PhysicsShapeRecord record)
            {
                builder.Append("s:").Append(record.ShapeKey)
                    .Append('|').Append((int)record.ShapeType)
                    .Append('|').Append(Format(record.LocalPosition))
                    .Append('|').Append(Format(record.LocalRotation))
                    .Append('|').Append(Format(record.Size))
                    .Append('|').Append(PhysicsCanonicalization.Format(record.Radius))
                    .Append('|').Append(PhysicsCanonicalization.Format(record.Length))
                    .Append('|').Append(record.Vertices.Length)
                    .Append('|').Append(record.Indices.Length)
                    .Append('\n');
            }

            internal string Build()
            {
                return JitterPhysicsHash.Sha256HexUtf8(builder.ToString());
            }

            private static string Format(PhysicsVector3 value)
            {
                return value.ToString();
            }

            private static string Format(PhysicsQuaternion value)
            {
                return value.ToString();
            }
        }
    }
}



