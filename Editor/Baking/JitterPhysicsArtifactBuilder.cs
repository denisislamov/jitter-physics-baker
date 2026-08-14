using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Baking
{
    /// <summary>Result of building an artifact from a scene.</summary>
    public sealed class JitterPhysicsBuildResult
    {
        /// <summary>The built artifact, or <c>null</c> when the build was refused.</summary>
        public PhysicsArtifact Artifact { get; }

        /// <summary>Everything found while validating and converting.</summary>
        public JitterPhysicsIssueLog Issues { get; }

        internal JitterPhysicsBuildResult(PhysicsArtifact artifact, JitterPhysicsIssueLog issues)
        {
            Artifact = artifact;
            Issues = issues;
        }

        /// <summary>True when an artifact was produced.</summary>
        public bool Succeeded => Artifact != null;
    }

    /// <summary>
    /// Turns an authored scene into a canonical artifact.
    /// <para>
    /// The pipeline is collect, convert, canonicalize, then hand over to the codec. Its
    /// central obligation is that two runs over an unchanged scene produce identical bytes,
    /// which is why nothing here depends on scene traversal order, instance ids or hash map
    /// enumeration: records are sorted by their stable authored identifiers before they are
    /// written.
    /// </para>
    /// <para>
    /// The build is all-or-nothing. A level that is partially convertible is not written as
    /// a partially correct artifact, because the missing geometry would show up as a hole in
    /// a wall at runtime rather than as a message at bake time.
    /// </para>
    /// </summary>
    public static class JitterPhysicsArtifactBuilder
    {
        /// <summary>
        /// Builds the artifact for <paramref name="level"/>. Nothing is written to disk;
        /// producing the records and persisting them are separate steps so that validation
        /// can run without touching the project.
        /// </summary>
        public static JitterPhysicsBuildResult Build(
            JitterPhysicsLevel level,
            string runtimeCompatibilityId)
        {
            var issues = new JitterPhysicsIssueLog();

            if (level == null)
            {
                issues.Error("No JitterPhysicsLevel was supplied.");
                return new JitterPhysicsBuildResult(null, issues);
            }

            if (string.IsNullOrEmpty(runtimeCompatibilityId) || runtimeCompatibilityId.Length != 64)
            {
                issues.Error(
                    "The runtime compatibility id is unavailable. Baking requires a Jitter2 copy "
                    + "that matches jitter2.lock.json; see the Setup window.",
                    level);
                return new JitterPhysicsBuildResult(null, issues);
            }

            string levelId = level.EnsureLevelId();
            if (!JitterPhysicsIdUtility.IsCanonical(levelId))
            {
                issues.Error($"The level id '{levelId}' is not canonical.", level);
                return new JitterPhysicsBuildResult(null, issues);
            }

            if (level.WorldProfile == null)
            {
                issues.Error(
                    "No world profile is assigned. The world settings are part of the artifact, "
                    + "so there is no safe default to fall back on.",
                    level);
                return new JitterPhysicsBuildResult(null, issues);
            }

            IReadOnlyList<PhysicsBodyRecord> bodies = BuildBodies(level, issues);

            if (issues.HasErrors)
            {
                return new JitterPhysicsBuildResult(null, issues);
            }

            if (bodies.Count == 0)
            {
                issues.Error(
                    "The level contains no static bodies. Mark the geometry with "
                    + "JitterStaticBodySource before baking.",
                    level);
                return new JitterPhysicsBuildResult(null, issues);
            }

            var artifact = new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                runtimeCompatibilityId,
                levelId,
                level.WorldProfile.ToWorldSettings(),
                bodies);

            // The codec validates as well, but failing here names the authoring object that
            // caused the problem instead of a record index nobody can map back to a scene.
            PhysicsArtifactError error = PhysicsArtifactValidator.Validate(artifact);
            if (error.IsError)
            {
                issues.Error("The built artifact is not canonical: " + error, level);
                return new JitterPhysicsBuildResult(null, issues);
            }

            return new JitterPhysicsBuildResult(artifact, issues);
        }

        private static IReadOnlyList<PhysicsBodyRecord> BuildBodies(
            JitterPhysicsLevel level,
            JitterPhysicsIssueLog issues)
        {
            IReadOnlyList<JitterStaticBodySource> sources = level.CollectSources();
            var bodies = new List<PhysicsBodyRecord>(sources.Count);
            var seenSourceIds = new Dictionary<string, JitterStaticBodySource>(StringComparer.Ordinal);

            for (int i = 0; i < sources.Count; i++)
            {
                JitterStaticBodySource source = sources[i];
                string sourceId = source.EnsureSourceId();

                if (!JitterPhysicsIdUtility.IsCanonical(sourceId))
                {
                    issues.Error($"The source id '{sourceId}' is not canonical.", source);
                    continue;
                }

                if (seenSourceIds.TryGetValue(sourceId, out JitterStaticBodySource previous))
                {
                    // Duplicates are usually a copy-pasted object. Left alone they would make
                    // record order ambiguous and the bake nondeterministic.
                    issues.Error(
                        $"Duplicate Source Id '{sourceId}': '{source.name}' and "
                        + $"'{previous.name}' both use it. Duplicating a GameObject copies its "
                        + "Source Id. Click this error to select the offending object, then set "
                        + "Jitter Static Body Source > Source Id to a unique value before baking.",
                        source);
                    continue;
                }

                seenSourceIds.Add(sourceId, source);

                PhysicsBodyRecord body = BuildBody(source, sourceId, issues);
                if (body != null)
                {
                    bodies.Add(body);
                }
            }

            // Ordinal order by the authored id: stable across machines, sessions and scene
            // reorganisations, unlike anything derived from the hierarchy itself.
            bodies.Sort(static (left, right) => string.CompareOrdinal(left.SourceId, right.SourceId));
            return bodies;
        }

        private static PhysicsBodyRecord BuildBody(
            JitterStaticBodySource source,
            string sourceId,
            JitterPhysicsIssueLog issues)
        {
            Transform bodyRoot = source.transform;
            var colliders = new List<Collider>();

            if (source.IncludeChildren)
            {
                bodyRoot.GetComponentsInChildren(JitterStaticBodySource.IncludeInactiveChildren, colliders);
            }
            else
            {
                colliders.AddRange(bodyRoot.GetComponents<Collider>());
            }

            var shapes = new List<PhysicsShapeRecord>(colliders.Count);
            var seenKeys = new Dictionary<string, Collider>(StringComparer.Ordinal);

            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];

                if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string shapeKey = JitterPhysicsColliderKey.Build(bodyRoot, collider);
                if (seenKeys.ContainsKey(shapeKey))
                {
                    issues.Error(
                        $"Two colliders of '{sourceId}' produced the same key '{shapeKey}'. "
                        + "This is a bug in the baker; please report the hierarchy.",
                        collider);
                    continue;
                }

                seenKeys.Add(shapeKey, collider);

                JitterPhysicsConversionResult result;
                try
                {
                    result = JitterPhysicsColliderConverter.Convert(bodyRoot, collider, shapeKey);
                }
                catch (Exception exception)
                {
                    // A converter bug must not escape as an unhandled exception through the
                    // editor window's OnGUI: that hides which collider caused it and takes the
                    // whole panel down. Turn it into an issue pointing at the object instead.
                    issues.Error(
                        $"Converting collider '{shapeKey}' failed unexpectedly: {exception.Message}. "
                        + "This is a bug in the baker; please report the collider.",
                        collider);
                    continue;
                }

                if (!result.Succeeded)
                {
                    issues.Error(result.Message, collider);
                    continue;
                }

                if (!string.IsNullOrEmpty(result.Warning))
                {
                    issues.Warning(result.Warning, collider);
                }

                shapes.Add(result.Shape);
            }

            if (shapes.Count == 0)
            {
                issues.Error(
                    $"'{sourceId}' has no convertible colliders. A static body without collision "
                    + "geometry is never intended; remove the source or add a collider.",
                    source);
                return null;
            }

            shapes.Sort(static (left, right) => string.CompareOrdinal(left.ShapeKey, right.ShapeKey));

            Vector3 position = bodyRoot.position;
            Quaternion rotation = bodyRoot.rotation;

            return new PhysicsBodyRecord(
                sourceId,
                new PhysicsVector3(position.x, position.y, position.z).Canonical(),
                new PhysicsQuaternion(rotation.x, rotation.y, rotation.z, rotation.w).Canonical(),
                source.Friction,
                source.Restitution,
                shapes);
        }
    }
}

