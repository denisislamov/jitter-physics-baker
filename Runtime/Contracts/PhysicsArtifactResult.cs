using System;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Why an artifact was rejected. Codes exist so that callers can react without parsing
    /// English text: a client shows "you have the wrong map", a server refuses a connection,
    /// and a test asserts a specific failure instead of "some exception".
    /// </summary>
    public enum PhysicsArtifactErrorCode
    {
        /// <summary>No error.</summary>
        None = 0,

        /// <summary>The payload is null or empty.</summary>
        EmptyPayload,

        /// <summary>The payload does not start with the artifact magic.</summary>
        BadMagic,

        /// <summary>The schema version is not supported by this build.</summary>
        UnsupportedSchema,

        /// <summary>The payload ended in the middle of a record.</summary>
        TruncatedPayload,

        /// <summary>The payload contains bytes after the last record.</summary>
        TrailingBytes,

        /// <summary>A count or a size exceeds a hard safety cap.</summary>
        LimitExceeded,

        /// <summary>A value is out of range, not finite, or not canonical.</summary>
        InvalidValue,

        /// <summary>Two bodies or two shapes share an id, or the order is not canonical.</summary>
        InvalidOrdering,

        /// <summary>A mesh index points outside the vertex array, or the index count is not a multiple of three.</summary>
        InvalidMesh,

        /// <summary>The SHA-256 of the payload differs from the expected hash.</summary>
        HashMismatch,

        /// <summary>The manifest disagrees with the payload.</summary>
        ManifestMismatch,

        /// <summary>The artifact was baked for different runtime semantics.</summary>
        IncompatibleRuntime,

        /// <summary>
        /// The artifact could not be obtained at all: no file at the configured path, no
        /// embedded payload, no read permission. Kept apart from the corruption codes because
        /// the operator action is different — "deliver the artifact" rather than "re-bake it".
        /// </summary>
        SourceUnavailable,
    }

    /// <summary>
    /// A rejection, carrying enough context to diagnose it from a log line: what failed, for
    /// which level, and which hash was involved.
    /// </summary>
    public readonly struct PhysicsArtifactError
    {
        /// <summary>Machine-readable reason.</summary>
        public PhysicsArtifactErrorCode Code { get; }

        /// <summary>Human-readable reason, safe to log.</summary>
        public string Message { get; }

        /// <summary>Level the payload claimed to describe, when it was readable.</summary>
        public string LevelId { get; }

        /// <summary>Hash of the payload, when it was computed.</summary>
        public string ArtifactHash { get; }

        public PhysicsArtifactError(
            PhysicsArtifactErrorCode code,
            string message,
            string levelId = null,
            string artifactHash = null)
        {
            Code = code;
            Message = message ?? string.Empty;
            LevelId = levelId;
            ArtifactHash = artifactHash;
        }

        /// <summary>True when this value describes an actual failure.</summary>
        public bool IsError => Code != PhysicsArtifactErrorCode.None;

        public override string ToString()
        {
            string suffix = string.Empty;
            if (!string.IsNullOrEmpty(LevelId))
            {
                suffix += " level='" + LevelId + "'";
            }

            if (!string.IsNullOrEmpty(ArtifactHash))
            {
                // Logs carry the short hash; the editor prints the full one.
                suffix += " hash="
                    + (ArtifactHash.Length >= JitterPhysicsArtifactNaming.ShortHashLength
                        ? ArtifactHash.Substring(0, JitterPhysicsArtifactNaming.ShortHashLength)
                        : ArtifactHash);
            }

            return Code + ": " + Message + suffix;
        }
    }

    /// <summary>
    /// Result of decoding or validating an artifact. Corrupt input is an expected outcome
    /// rather than an exception: the loader has to report it and stop the process cleanly,
    /// and exceptions across an IL2CPP or server boundary are a poor way to do that.
    /// </summary>
    public readonly struct PhysicsArtifactResult
    {
        /// <summary>Decoded artifact, or <c>null</c> when the result is a failure.</summary>
        public PhysicsArtifact Artifact { get; }

        /// <summary>Failure description; meaningful only when <see cref="Succeeded"/> is false.</summary>
        public PhysicsArtifactError Error { get; }

        private PhysicsArtifactResult(PhysicsArtifact artifact, PhysicsArtifactError error)
        {
            Artifact = artifact;
            Error = error;
        }

        /// <summary>True when the artifact was decoded and passed every check.</summary>
        public bool Succeeded => !Error.IsError;

        /// <summary>Creates a successful result.</summary>
        public static PhysicsArtifactResult Success(PhysicsArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            return new PhysicsArtifactResult(artifact, default);
        }

        /// <summary>Creates a failed result.</summary>
        public static PhysicsArtifactResult Failure(
            PhysicsArtifactErrorCode code,
            string message,
            string levelId = null,
            string artifactHash = null)
        {
            return new PhysicsArtifactResult(
                null,
                new PhysicsArtifactError(code, message, levelId, artifactHash));
        }
    }
}
