using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// Semantic validation of a decoded artifact.
    /// <para>
    /// The writer runs it before encoding and the reader runs it after decoding, on purpose:
    /// the same rules then apply to data produced here and to data that arrived from
    /// somewhere else. A world is never built from a partially valid artifact — either every
    /// record passes, or nothing is created.
    /// </para>
    /// </summary>
    public static class PhysicsArtifactValidator
    {
        /// <summary>
        /// Returns the first violation found, or a default (non-error) value when the artifact
        /// is canonical and within every limit.
        /// </summary>
        public static PhysicsArtifactError Validate(PhysicsArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            if (artifact.SchemaVersion != JitterPhysicsPackage.ArtifactSchemaVersion)
            {
                return Error(
                    PhysicsArtifactErrorCode.UnsupportedSchema,
                    $"Artifact schema {artifact.SchemaVersion} is not supported; this build writes and reads "
                    + JitterPhysicsPackage.ArtifactSchemaVersion + ".",
                    artifact);
            }

            if (!IsHex64(artifact.RuntimeCompatibilityId))
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    "Runtime compatibility id must be 64 lowercase hex characters.",
                    artifact);
            }

            if (!JitterPhysicsIdUtility.IsCanonical(artifact.LevelId))
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Level id '{artifact.LevelId}' is not canonical.",
                    artifact);
            }

            PhysicsArtifactError settingsError = ValidateSettings(artifact);
            if (settingsError.IsError)
            {
                return settingsError;
            }

            if (artifact.Bodies.Count > PhysicsArtifactLimits.MaxBodies)
            {
                return Error(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"Level has {artifact.Bodies.Count} bodies, over the limit of {PhysicsArtifactLimits.MaxBodies}.",
                    artifact);
            }

            if (artifact.ShapeCount > PhysicsArtifactLimits.MaxShapes)
            {
                return Error(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"Level has {artifact.ShapeCount} shapes, over the limit of {PhysicsArtifactLimits.MaxShapes}.",
                    artifact);
            }

            if (artifact.VertexCount > PhysicsArtifactLimits.MaxVertices)
            {
                return Error(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"Level has {artifact.VertexCount} vertices, over the limit of {PhysicsArtifactLimits.MaxVertices}.",
                    artifact);
            }

            string previousSourceId = null;
            for (int i = 0; i < artifact.Bodies.Count; i++)
            {
                PhysicsBodyRecord body = artifact.Bodies[i];
                if (body == null)
                {
                    return Error(PhysicsArtifactErrorCode.InvalidValue, $"Body {i} is null.", artifact);
                }

                // Strictly ascending ordinal order both proves uniqueness and pins the creation
                // order of the rebuilt world, which must not depend on scene traversal.
                if (previousSourceId != null
                    && string.CompareOrdinal(previousSourceId, body.SourceId) >= 0)
                {
                    return Error(
                        PhysicsArtifactErrorCode.InvalidOrdering,
                        $"Body ids are not in strictly ascending ordinal order: '{previousSourceId}' then '{body.SourceId}'.",
                        artifact);
                }

                previousSourceId = body.SourceId;

                PhysicsArtifactError bodyError = ValidateBody(artifact, body);
                if (bodyError.IsError)
                {
                    return bodyError;
                }
            }

            return default;
        }

        private static PhysicsArtifactError ValidateSettings(PhysicsArtifact artifact)
        {
            PhysicsWorldSettings settings = artifact.WorldSettings;

            if (!settings.Gravity.IsFinite)
            {
                return Error(PhysicsArtifactErrorCode.InvalidValue, "Gravity is not finite.", artifact);
            }

            if (settings.TickRate < PhysicsArtifactLimits.MinTickRate
                || settings.TickRate > PhysicsArtifactLimits.MaxTickRate)
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Tick rate {settings.TickRate} is outside [{PhysicsArtifactLimits.MinTickRate}, "
                    + PhysicsArtifactLimits.MaxTickRate + "].",
                    artifact);
            }

            if (settings.SubstepCount < 1 || settings.SubstepCount > PhysicsArtifactLimits.MaxSubstepCount)
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Substep count {settings.SubstepCount} is outside [1, {PhysicsArtifactLimits.MaxSubstepCount}].",
                    artifact);
            }

            if (settings.SolverIterations < 1 || settings.SolverIterations > PhysicsArtifactLimits.MaxIterations)
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Solver iteration count {settings.SolverIterations} is outside [1, "
                    + PhysicsArtifactLimits.MaxIterations + "].",
                    artifact);
            }

            if (settings.RelaxationIterations < 0
                || settings.RelaxationIterations > PhysicsArtifactLimits.MaxIterations)
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Relaxation iteration count {settings.RelaxationIterations} is outside [0, "
                    + PhysicsArtifactLimits.MaxIterations + "].",
                    artifact);
            }

            return default;
        }

        private static PhysicsArtifactError ValidateBody(PhysicsArtifact artifact, PhysicsBodyRecord body)
        {
            if (!JitterPhysicsIdUtility.IsCanonical(body.SourceId))
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Body id '{body.SourceId}' is not canonical.",
                    artifact);
            }

            if (!IsFiniteCoordinate(body.Position))
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Body '{body.SourceId}' has a position outside the supported coordinate range.",
                    artifact);
            }

            if (!PhysicsCanonicalization.IsCanonicalQuaternion(body.Orientation))
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Body '{body.SourceId}' has a non-canonical orientation.",
                    artifact);
            }

            if (!IsInRange(body.Friction, 0f, 100f) || !IsInRange(body.Restitution, 0f, 1f))
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Body '{body.SourceId}' has friction {PhysicsCanonicalization.Format(body.Friction)} or "
                    + $"restitution {PhysicsCanonicalization.Format(body.Restitution)} out of range.",
                    artifact);
            }

            IReadOnlyList<PhysicsShapeRecord> shapes = body.Shapes;
            if (shapes.Count == 0)
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Body '{body.SourceId}' has no shapes; an empty static body cannot collide and is never intended.",
                    artifact);
            }

            if (shapes.Count > PhysicsArtifactLimits.MaxShapesPerBody)
            {
                return Error(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"Body '{body.SourceId}' has {shapes.Count} shapes, over the limit of "
                    + PhysicsArtifactLimits.MaxShapesPerBody + ".",
                    artifact);
            }

            string previousShapeKey = null;
            for (int i = 0; i < shapes.Count; i++)
            {
                PhysicsShapeRecord shape = shapes[i];
                if (shape == null)
                {
                    return Error(
                        PhysicsArtifactErrorCode.InvalidValue,
                        $"Body '{body.SourceId}' has a null shape at index {i}.",
                        artifact);
                }

                if (previousShapeKey != null
                    && string.CompareOrdinal(previousShapeKey, shape.ShapeKey) >= 0)
                {
                    return Error(
                        PhysicsArtifactErrorCode.InvalidOrdering,
                        $"Shapes of body '{body.SourceId}' are not in strictly ascending ordinal order: "
                        + $"'{previousShapeKey}' then '{shape.ShapeKey}'.",
                        artifact);
                }

                previousShapeKey = shape.ShapeKey;

                PhysicsArtifactError shapeError = ValidateShape(artifact, body, shape);
                if (shapeError.IsError)
                {
                    return shapeError;
                }
            }

            return default;
        }

        private static PhysicsArtifactError ValidateShape(
            PhysicsArtifact artifact,
            PhysicsBodyRecord body,
            PhysicsShapeRecord shape)
        {
            string where = $"Shape '{shape.ShapeKey}' of body '{body.SourceId}'";

            if (string.IsNullOrEmpty(shape.ShapeKey))
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"A shape of body '{body.SourceId}' has an empty key.",
                    artifact);
            }

            if (!IsFiniteCoordinate(shape.LocalPosition))
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    where + " has a local position outside the supported coordinate range.",
                    artifact);
            }

            if (!PhysicsCanonicalization.IsCanonicalQuaternion(shape.LocalRotation))
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidValue,
                    where + " has a non-canonical local rotation.",
                    artifact);
            }

            switch (shape.ShapeType)
            {
                case PhysicsShapeType.Box:
                    if (!IsPositiveExtent(shape.Size.X)
                        || !IsPositiveExtent(shape.Size.Y)
                        || !IsPositiveExtent(shape.Size.Z))
                    {
                        return Error(
                            PhysicsArtifactErrorCode.InvalidValue,
                            where + $" has an invalid size ({shape.Size}); a zero or negative extent is degenerate.",
                            artifact);
                    }

                    return default;

                case PhysicsShapeType.Sphere:
                    if (!IsPositiveExtent(shape.Radius))
                    {
                        return Error(
                            PhysicsArtifactErrorCode.InvalidValue,
                            where + $" has radius {PhysicsCanonicalization.Format(shape.Radius)}.",
                            artifact);
                    }

                    return default;

                case PhysicsShapeType.Capsule:
                    if (!IsPositiveExtent(shape.Radius))
                    {
                        return Error(
                            PhysicsArtifactErrorCode.InvalidValue,
                            where + $" has radius {PhysicsCanonicalization.Format(shape.Radius)}.",
                            artifact);
                    }

                    // A zero cylinder length is legal: a capsule whose height equals its
                    // diameter collapses into a sphere, and rejecting it would break authoring.
                    if (!IsInRange(shape.Length, 0f, PhysicsArtifactLimits.MaxShapeExtent))
                    {
                        return Error(
                            PhysicsArtifactErrorCode.InvalidValue,
                            where + $" has cylinder length {PhysicsCanonicalization.Format(shape.Length)}.",
                            artifact);
                    }

                    return default;

                case PhysicsShapeType.Mesh:
                    return ValidateMesh(artifact, shape, where);

                default:
                    return Error(
                        PhysicsArtifactErrorCode.InvalidValue,
                        where + $" has unsupported type {(byte)shape.ShapeType}.",
                        artifact);
            }
        }

        private static PhysicsArtifactError ValidateMesh(
            PhysicsArtifact artifact,
            PhysicsShapeRecord shape,
            string where)
        {
            PhysicsVector3[] vertices = shape.Vertices;
            int[] indices = shape.Indices;

            if (vertices.Length == 0 || indices.Length == 0)
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidMesh,
                    where + " is an empty mesh.",
                    artifact);
            }

            if (vertices.Length > PhysicsArtifactLimits.MaxVerticesPerMesh)
            {
                return Error(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    where + $" has {vertices.Length} vertices, over the limit of "
                    + PhysicsArtifactLimits.MaxVerticesPerMesh + ".",
                    artifact);
            }

            if (indices.Length > PhysicsArtifactLimits.MaxIndicesPerMesh)
            {
                return Error(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    where + $" has {indices.Length} indices, over the limit of "
                    + PhysicsArtifactLimits.MaxIndicesPerMesh + ".",
                    artifact);
            }

            if (indices.Length % 3 != 0)
            {
                return Error(
                    PhysicsArtifactErrorCode.InvalidMesh,
                    where + $" has {indices.Length} indices, which is not a multiple of three.",
                    artifact);
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IsFiniteCoordinate(vertices[i]))
                {
                    return Error(
                        PhysicsArtifactErrorCode.InvalidValue,
                        where + $" has vertex {i} outside the supported coordinate range.",
                        artifact);
                }
            }

            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i];
                int b = indices[i + 1];
                int c = indices[i + 2];

                if (!IsValidIndex(a, vertices.Length)
                    || !IsValidIndex(b, vertices.Length)
                    || !IsValidIndex(c, vertices.Length))
                {
                    return Error(
                        PhysicsArtifactErrorCode.InvalidMesh,
                        where + $" has triangle {i / 3} referencing a vertex outside [0, {vertices.Length - 1}].",
                        artifact);
                }

                if (a == b || b == c || a == c)
                {
                    // A collapsed triangle has no normal, so a collision query against it is
                    // undefined; the baker rejects it instead of shipping a hole in the level.
                    return Error(
                        PhysicsArtifactErrorCode.InvalidMesh,
                        where + $" has degenerate triangle {i / 3} with repeated vertex indices.",
                        artifact);
                }
            }

            return default;
        }

        private static bool IsValidIndex(int index, int vertexCount)
        {
            return index >= 0 && index < vertexCount;
        }

        private static bool IsFiniteCoordinate(PhysicsVector3 value)
        {
            return value.IsFinite
                && Math.Abs(value.X) <= PhysicsArtifactLimits.MaxCoordinateMagnitude
                && Math.Abs(value.Y) <= PhysicsArtifactLimits.MaxCoordinateMagnitude
                && Math.Abs(value.Z) <= PhysicsArtifactLimits.MaxCoordinateMagnitude;
        }

        private static bool IsPositiveExtent(float value)
        {
            return PhysicsCanonicalization.IsFinite(value)
                && value > 0f
                && value <= PhysicsArtifactLimits.MaxShapeExtent;
        }

        private static bool IsInRange(float value, float minimum, float maximum)
        {
            return PhysicsCanonicalization.IsFinite(value) && value >= minimum && value <= maximum;
        }

        private static bool IsHex64(string value)
        {
            if (value == null || value.Length != JitterPhysicsArtifactNaming.FullHashLength)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool isHex = (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static PhysicsArtifactError Error(
            PhysicsArtifactErrorCode code,
            string message,
            PhysicsArtifact artifact)
        {
            return new PhysicsArtifactError(code, message, artifact.LevelId);
        }
    }
}
