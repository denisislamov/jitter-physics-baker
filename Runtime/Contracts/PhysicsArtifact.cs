using System;
using System.Collections.Generic;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// The decoded content of one baked level: world settings plus ordered static bodies.
    /// <para>
    /// This is the boundary between the portable half of the package and the Jitter-dependent
    /// half. Everything up to here — bake, encode, decode, validate — happens without Jitter;
    /// the world builder then turns these records into shapes and bodies.
    /// </para>
    /// </summary>
    public sealed class PhysicsArtifact
    {
        /// <summary>Binary layout version this artifact was written with.</summary>
        public int SchemaVersion { get; }

        /// <summary>
        /// Hex SHA-256 identifying the runtime semantics the artifact was baked for. A client
        /// and a server with different ids must not share an artifact even when the binary
        /// layout matches, because the same records would build a different world.
        /// </summary>
        public string RuntimeCompatibilityId { get; }

        /// <summary>Canonical level id.</summary>
        public string LevelId { get; }

        /// <summary>World settings that affect the simulation.</summary>
        public PhysicsWorldSettings WorldSettings { get; }

        /// <summary>Static bodies, ordered by <see cref="PhysicsBodyRecord.SourceId"/>.</summary>
        public IReadOnlyList<PhysicsBodyRecord> Bodies { get; }

        public PhysicsArtifact(
            int schemaVersion,
            string runtimeCompatibilityId,
            string levelId,
            PhysicsWorldSettings worldSettings,
            IReadOnlyList<PhysicsBodyRecord> bodies)
        {
            SchemaVersion = schemaVersion;
            RuntimeCompatibilityId = runtimeCompatibilityId
                ?? throw new ArgumentNullException(nameof(runtimeCompatibilityId));
            LevelId = levelId ?? throw new ArgumentNullException(nameof(levelId));
            WorldSettings = worldSettings ?? throw new ArgumentNullException(nameof(worldSettings));
            Bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
        }

        /// <summary>Total number of shapes across all bodies.</summary>
        public int ShapeCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Bodies.Count; i++)
                {
                    count += Bodies[i].Shapes.Count;
                }

                return count;
            }
        }

        /// <summary>Total number of mesh vertices across all shapes.</summary>
        public int VertexCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Bodies.Count; i++)
                {
                    IReadOnlyList<PhysicsShapeRecord> shapes = Bodies[i].Shapes;
                    for (int shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
                    {
                        count += shapes[shapeIndex].Vertices.Length;
                    }
                }

                return count;
            }
        }

        /// <summary>Total number of mesh triangles across all shapes.</summary>
        public int TriangleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Bodies.Count; i++)
                {
                    IReadOnlyList<PhysicsShapeRecord> shapes = Bodies[i].Shapes;
                    for (int shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
                    {
                        count += shapes[shapeIndex].TriangleCount;
                    }
                }

                return count;
            }
        }
    }
}
