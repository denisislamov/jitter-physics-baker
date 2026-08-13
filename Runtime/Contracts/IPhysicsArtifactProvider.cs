using System;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Where a process gets its artifact from.
    /// <para>
    /// A dedicated server may read the payload from a mounted file, from bytes embedded into
    /// its own binary, or from whatever a consumer's content system provides. The startup
    /// sequence must not care: it resolves one provider, asks it for the artifact, and either
    /// builds the world or refuses to accept players. Keeping that behind an interface is what
    /// stops delivery details — a path, a mount, a registry — from leaking into the loader.
    /// </para>
    /// <para>
    /// A provider never returns a partially checked artifact. Whatever it hands back has
    /// already been hashed, decoded, validated and cross-checked against its manifest, because
    /// a caller that receives an artifact object has no way to tell how much of that happened.
    /// </para>
    /// </summary>
    public interface IPhysicsArtifactProvider
    {
        /// <summary>
        /// Where this provider reads from, in a form that is safe to log: a path, or a
        /// description of the embedded payload. Startup logs need it to answer "which artifact
        /// did this process actually load".
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Obtains and fully validates the artifact.
        /// <para>
        /// <paramref name="expectedRuntimeCompatibilityId"/> is the id of the build asking for
        /// the artifact; when it is supplied, an artifact baked for other runtime semantics is
        /// rejected here rather than silently rebuilt into a world that differs from the
        /// client's. Pass <c>null</c> only from tools that inspect artifacts they cannot run.
        /// </para>
        /// <para>
        /// Failure is returned, not thrown: a missing or corrupt artifact is expected input for
        /// a server that must log the reason and stop cleanly.
        /// </para>
        /// </summary>
        PhysicsArtifactLoadResult Load(string expectedRuntimeCompatibilityId);
    }

    /// <summary>
    /// What a provider returns: the artifact, the manifest it came with and the hash of the
    /// bytes it was decoded from.
    /// <para>
    /// The hash is part of the result rather than something the caller recomputes, because the
    /// handshake sends it and re-hashing a 64 MB payload to obtain a value the provider already
    /// had is both wasteful and an opportunity to hash different bytes than were decoded.
    /// </para>
    /// </summary>
    public readonly struct PhysicsArtifactLoadResult
    {
        /// <summary>The decoded artifact, or <c>null</c> when the load failed.</summary>
        public PhysicsArtifact Artifact { get; }

        /// <summary>The manifest that described the payload, when one was available.</summary>
        public PhysicsArtifactManifest Manifest { get; }

        /// <summary>Lowercase hex SHA-256 of the bytes the artifact was decoded from.</summary>
        public string ArtifactHash { get; }

        /// <summary>Loggable description of the source, copied from the provider.</summary>
        public string Source { get; }

        /// <summary>Why the load failed; meaningful only when <see cref="Succeeded"/> is false.</summary>
        public PhysicsArtifactError Error { get; }

        private PhysicsArtifactLoadResult(
            PhysicsArtifact artifact,
            PhysicsArtifactManifest manifest,
            string artifactHash,
            string source,
            PhysicsArtifactError error)
        {
            Artifact = artifact;
            Manifest = manifest;
            ArtifactHash = artifactHash;
            Source = source;
            Error = error;
        }

        /// <summary>True when the artifact was obtained and passed every check.</summary>
        public bool Succeeded => !Error.IsError;

        /// <summary>Creates a successful result.</summary>
        public static PhysicsArtifactLoadResult Success(
            PhysicsArtifact artifact,
            PhysicsArtifactManifest manifest,
            string artifactHash,
            string source)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            if (string.IsNullOrEmpty(artifactHash))
            {
                throw new ArgumentException("A successful load must carry the payload hash.", nameof(artifactHash));
            }

            return new PhysicsArtifactLoadResult(artifact, manifest, artifactHash, source, default);
        }

        /// <summary>Creates a failed result.</summary>
        public static PhysicsArtifactLoadResult Failure(PhysicsArtifactError error, string source)
        {
            if (!error.IsError)
            {
                throw new ArgumentException("A failed load must carry a reason.", nameof(error));
            }

            return new PhysicsArtifactLoadResult(null, null, error.ArtifactHash, source, error);
        }

        /// <summary>Creates a failed result from a code and a message.</summary>
        public static PhysicsArtifactLoadResult Failure(
            PhysicsArtifactErrorCode code,
            string message,
            string source,
            string levelId = null,
            string artifactHash = null)
        {
            return Failure(new PhysicsArtifactError(code, message, levelId, artifactHash), source);
        }

        /// <summary>
        /// One line for the server's startup self-check: which level, which artifact, how big.
        /// Logs carry the short hash so that a mismatch stays readable.
        /// </summary>
        public override string ToString()
        {
            if (!Succeeded)
            {
                return "load failed from " + (Source ?? "<unknown source>") + ": " + Error;
            }

            return "level='" + Artifact.LevelId + "' hash=" + ShortHash(ArtifactHash)
                + " bodies=" + Artifact.Bodies.Count + " shapes=" + Artifact.ShapeCount
                + " tickRate=" + Artifact.WorldSettings.TickRate
                + " source=" + (Source ?? "<unknown source>");
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "<none>";
            }

            return hash.Length >= JitterPhysicsArtifactNaming.ShortHashLength
                ? hash.Substring(0, JitterPhysicsArtifactNaming.ShortHashLength)
                : hash;
        }
    }
}

