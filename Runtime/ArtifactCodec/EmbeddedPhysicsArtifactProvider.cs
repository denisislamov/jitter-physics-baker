using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// Serves an artifact that was compiled into the server binary as generated source.
    /// <para>
    /// This exists for one situation: a consumer whose build and deploy files must not change.
    /// An SDK-style project compiles every <c>.cs</c> under its folder, so dropping a generated
    /// file in is the only delivery that needs no csproj edit, no content rule and no volume.
    /// It is a proof-of-concept and small-level strategy, not a production one for large maps —
    /// hence the size cap on the generator side.
    /// </para>
    /// <para>
    /// The embedded bytes are treated exactly as suspiciously as a file on disk: they are
    /// re-hashed and cross-checked against the embedded manifest on load. Being inside the
    /// binary proves the compiler copied them, not that they are the bytes that were baked —
    /// a bad merge in a generated file is just as possible as a bad copy on a mount.
    /// </para>
    /// </summary>
    public sealed class EmbeddedPhysicsArtifactProvider : IPhysicsArtifactProvider
    {
        private readonly IReadOnlyList<string> _chunks;
        private readonly string _manifestJson;
        private readonly string _description;

        private byte[] _payload;

        /// <summary>
        /// Wraps the chunks and the manifest emitted by
        /// <see cref="EmbeddedArtifactSourceGenerator"/>. Generated code calls this; hand-written
        /// code normally has a file and should use <see cref="FilePhysicsArtifactProvider"/>.
        /// </summary>
        public EmbeddedPhysicsArtifactProvider(
            IReadOnlyList<string> chunks,
            string manifestJson,
            string description = null)
        {
            _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
            _manifestJson = manifestJson ?? throw new ArgumentNullException(nameof(manifestJson));
            _description = description;
        }

        /// <inheritdoc/>
        public string Description => _description ?? "embedded";

        /// <inheritdoc/>
        public PhysicsArtifactLoadResult Load(string expectedRuntimeCompatibilityId)
        {
            PhysicsArtifactManifest manifest = PhysicsArtifactManifestCodec.Read(
                _manifestJson, out string manifestError);

            if (manifest == null)
            {
                return PhysicsArtifactLoadResult.Failure(
                    PhysicsArtifactErrorCode.ManifestMismatch,
                    "Embedded manifest is not a manifest this build understands: " + manifestError,
                    Description);
            }

            byte[] payload;
            try
            {
                // Restored once and kept: the payload is immutable for the life of the process,
                // and decoding several megabytes of base64 per call would be a startup cost paid
                // repeatedly for no reason.
                payload = _payload ??= Restore(_chunks);
            }
            catch (FormatException exception)
            {
                return PhysicsArtifactLoadResult.Failure(
                    PhysicsArtifactErrorCode.SourceUnavailable,
                    "Embedded payload is not valid base64 and was probably edited by hand: "
                    + exception.Message,
                    Description,
                    manifest.LevelId);
            }

            PhysicsArtifactResult result = PhysicsArtifactReader.Read(payload, manifest.ArtifactHash, manifest);
            if (!result.Succeeded)
            {
                return PhysicsArtifactLoadResult.Failure(result.Error, Description);
            }

            if (!string.IsNullOrEmpty(expectedRuntimeCompatibilityId))
            {
                PhysicsArtifactError compatibilityError = PhysicsArtifactReader.CheckRuntimeCompatibility(
                    result.Artifact, expectedRuntimeCompatibilityId);

                if (compatibilityError.IsError)
                {
                    return PhysicsArtifactLoadResult.Failure(compatibilityError, Description);
                }
            }

            return PhysicsArtifactLoadResult.Success(
                result.Artifact, manifest, JitterPhysicsHash.Sha256Hex(payload), Description);
        }

        /// <summary>
        /// Concatenates the chunks and decodes them. Chunk boundaries carry no meaning — they
        /// exist only because a single multi-megabyte string literal is hostile to a compiler.
        /// </summary>
        public static byte[] Restore(IReadOnlyList<string> chunks)
        {
            if (chunks == null)
            {
                throw new ArgumentNullException(nameof(chunks));
            }

            int length = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                length += chunks[i]?.Length ?? 0;
            }

            var builder = new System.Text.StringBuilder(length);
            for (int i = 0; i < chunks.Count; i++)
            {
                builder.Append(chunks[i]);
            }

            return Convert.FromBase64String(builder.ToString());
        }
    }
}

