using System;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// File naming rules for a baked artifact. Client, server projection and editor export
    /// all derive names here so that a file copied between them stays recognisable.
    /// </summary>
    public static class JitterPhysicsArtifactNaming
    {
        /// <summary>Extension of the deterministic binary payload.</summary>
        public const string BinaryExtension = ".jphys.bytes";

        /// <summary>Extension of the JSON manifest that travels next to the payload.</summary>
        public const string ManifestExtension = ".manifest.json";

        /// <summary>Number of leading hash characters embedded into file names.</summary>
        public const int ShortHashLength = 12;

        /// <summary>Length of a full lowercase hex SHA-256.</summary>
        public const int FullHashLength = 64;

        /// <summary><c>&lt;levelId&gt;.&lt;hash12&gt;.jphys.bytes</c></summary>
        public static string BinaryFileName(string levelId, string artifactHash)
        {
            return Prefix(levelId, artifactHash) + BinaryExtension;
        }

        /// <summary><c>&lt;levelId&gt;.&lt;hash12&gt;.manifest.json</c></summary>
        public static string ManifestFileName(string levelId, string artifactHash)
        {
            return Prefix(levelId, artifactHash) + ManifestExtension;
        }

        /// <summary>
        /// First <see cref="ShortHashLength"/> characters of the hash. Runtime logs use the
        /// short form; the editor prints the full hash so that mismatches stay diagnosable.
        /// </summary>
        public static string ShortHash(string artifactHash)
        {
            if (artifactHash == null)
            {
                throw new ArgumentNullException(nameof(artifactHash));
            }

            if (artifactHash.Length != FullHashLength)
            {
                throw new ArgumentException(
                    $"Artifact hash must be {FullHashLength} lowercase hex characters, got {artifactHash.Length}.",
                    nameof(artifactHash));
            }

            return artifactHash.Substring(0, ShortHashLength);
        }

        private static string Prefix(string levelId, string artifactHash)
        {
            if (!JitterPhysicsIdUtility.IsCanonical(levelId))
            {
                throw new ArgumentException(
                    $"Level id '{levelId}' is not canonical; artifact names must not depend on authoring spelling.",
                    nameof(levelId));
            }

            return levelId + "." + ShortHash(artifactHash);
        }
    }
}
