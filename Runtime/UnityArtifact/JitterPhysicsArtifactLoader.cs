using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace DataSakura.JitterPhysics.UnityArtifact
{
    /// <summary>
    /// Turns a <see cref="JitterPhysicsArtifactAsset"/> into a validated
    /// <see cref="PhysicsArtifact"/>.
    /// <para>
    /// The asset's serialized fields are treated as untrusted input. They are convenient for
    /// the inspector, but they live in a Unity asset that a merge, a manual edit or a botched
    /// import can change without touching the payload — so the payload is re-hashed and
    /// re-decoded here, and the asset's own claims are checked against the result rather than
    /// believed.
    /// </para>
    /// <para>
    /// Nothing in this type touches Jitter. Loading an artifact and building a world are
    /// separate steps on purpose: a project that only wants to inspect a level must not need
    /// a physics engine to do it.
    /// </para>
    /// </summary>
    public static class JitterPhysicsArtifactLoader
    {
        /// <summary>
        /// Decodes and validates the artifact referenced by <paramref name="asset"/>.
        /// </summary>
        /// <param name="asset">The asset to load.</param>
        /// <param name="expectedRuntimeCompatibilityId">
        /// The runtime id of the build doing the loading. When supplied, an artifact baked for
        /// different runtime semantics is rejected here instead of quietly building a world
        /// that behaves differently from its peers.
        /// </param>
        public static PhysicsArtifactResult Load(
            JitterPhysicsArtifactAsset asset,
            string expectedRuntimeCompatibilityId = null)
        {
            if (asset == null)
            {
                return PhysicsArtifactResult.Failure(
                    PhysicsArtifactErrorCode.EmptyPayload,
                    "No artifact asset was supplied.");
            }

            if (!asset.HasPayload)
            {
                return PhysicsArtifactResult.Failure(
                    PhysicsArtifactErrorCode.EmptyPayload,
                    $"Artifact asset '{asset.name}' has no payload assigned. The .bytes file was "
                    + "probably deleted or excluded from the build.",
                    asset.LevelId);
            }

            // The expected hash comes from the asset, so this re-check catches a payload that
            // was replaced without updating the asset, and vice versa.
            PhysicsArtifactResult result = PhysicsArtifactReader.Read(
                asset.GetPayloadBytes(), asset.ArtifactHash);

            if (!result.Succeeded)
            {
                return result;
            }

            PhysicsArtifactError metadataError = CheckMetadata(asset, result.Artifact);
            if (metadataError.IsError)
            {
                return PhysicsArtifactResult.Failure(
                    metadataError.Code, metadataError.Message, result.Artifact.LevelId);
            }

            if (!string.IsNullOrEmpty(expectedRuntimeCompatibilityId))
            {
                PhysicsArtifactError runtimeError = PhysicsArtifactReader.CheckRuntimeCompatibility(
                    result.Artifact, expectedRuntimeCompatibilityId);

                if (runtimeError.IsError)
                {
                    return PhysicsArtifactResult.Failure(
                        runtimeError.Code, runtimeError.Message, result.Artifact.LevelId);
                }
            }

            return result;
        }

        /// <summary>
        /// Compares the asset's serialized description with the decoded payload.
        /// <para>
        /// A mismatch is reported rather than corrected. The two disagreeing is evidence that
        /// something edited one of them out of band, and silently preferring the payload would
        /// hide that the project's view of the level is wrong.
        /// </para>
        /// </summary>
        private static PhysicsArtifactError CheckMetadata(
            JitterPhysicsArtifactAsset asset,
            PhysicsArtifact artifact)
        {
            if (!string.IsNullOrEmpty(asset.LevelId)
                && !string.Equals(asset.LevelId, artifact.LevelId, System.StringComparison.Ordinal))
            {
                return new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.ManifestMismatch,
                    $"Artifact asset '{asset.name}' claims level '{asset.LevelId}', but the payload "
                    + $"describes '{artifact.LevelId}'. Re-bake the level.",
                    artifact.LevelId);
            }

            if (!string.IsNullOrEmpty(asset.RuntimeCompatibilityId)
                && !JitterPhysicsHash.HexEquals(asset.RuntimeCompatibilityId, artifact.RuntimeCompatibilityId))
            {
                return new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.ManifestMismatch,
                    $"Artifact asset '{asset.name}' records a different runtime compatibility id "
                    + "than its payload. Re-bake the level.",
                    artifact.LevelId);
            }

            if (asset.BodyCount != 0 && asset.BodyCount != artifact.Bodies.Count)
            {
                return new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.ManifestMismatch,
                    $"Artifact asset '{asset.name}' records {asset.BodyCount} bodies, but the payload "
                    + $"contains {artifact.Bodies.Count}. Re-bake the level.",
                    artifact.LevelId);
            }

            return default;
        }
    }
}

