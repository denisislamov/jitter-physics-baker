using System;
using System.Collections.Generic;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// One static body of the baked level: a pose, its material constants and its shapes.
    /// <para>
    /// The record describes what the loader must create through Jitter's public API. It
    /// deliberately holds nothing that belongs to a live simulation — no handles, no
    /// broadphase entry, no contacts — because those are Jitter's internal state and
    /// serializing them would tie the artifact to a specific build of the engine.
    /// </para>
    /// </summary>
    public sealed class PhysicsBodyRecord
    {
        /// <summary>
        /// Stable authoring id of the body. Bodies are ordered by this id, so it also decides
        /// the creation order in the rebuilt world.
        /// </summary>
        public string SourceId { get; }

        /// <summary>World position of the body.</summary>
        public PhysicsVector3 Position { get; }

        /// <summary>World orientation of the body, canonical and normalized.</summary>
        public PhysicsQuaternion Orientation { get; }

        /// <summary>Friction applied to the created body.</summary>
        public float Friction { get; }

        /// <summary>Restitution applied to the created body.</summary>
        public float Restitution { get; }

        /// <summary>Shapes of the body, ordered by shape key.</summary>
        public IReadOnlyList<PhysicsShapeRecord> Shapes { get; }

        public PhysicsBodyRecord(
            string sourceId,
            PhysicsVector3 position,
            PhysicsQuaternion orientation,
            float friction,
            float restitution,
            IReadOnlyList<PhysicsShapeRecord> shapes)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            Position = position;
            Orientation = orientation;
            Friction = friction;
            Restitution = restitution;
            Shapes = shapes ?? throw new ArgumentNullException(nameof(shapes));
        }
    }
}
