using System;
using System.IO;
using System.Text;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// Loads an artifact from a manifest and payload that already exist on disk.
    /// <para>
    /// This is the delivery path for consumers that ship the artifact as content: publish it
    /// with the build, mount a volume, pull it from an artifact registry — the package does not
    /// care which, and deliberately knows nothing about any particular game's deploy layout.
    /// It is given one path, usually from a command line such as
    /// <c>--physics-manifest /srv/levels/arena.9f2c1b40e7ad.manifest.json</c>, and everything
    /// else follows from the manifest.
    /// </para>
    /// <para>
    /// The manifest is the entry point rather than the payload, because the payload alone
    /// cannot be cross-checked: counts, tick rate and the expected hash all live in the
    /// manifest, and reading the binary without them would mean trusting whatever bytes
    /// happened to be at that path.
    /// </para>
    /// </summary>
    public sealed class FilePhysicsArtifactProvider : IPhysicsArtifactProvider
    {
        private readonly string _manifestPath;
        private readonly string _payloadPath;

        /// <summary>
        /// Reads <paramref name="manifestPath"/> and, unless <paramref name="payloadPath"/>
        /// says otherwise, the payload named by that manifest in the same folder.
        /// <para>
        /// The override exists for delivery systems that rename files in transit. It has to be
        /// explicit: resolving the payload from the manifest by default is what keeps a manifest
        /// from being paired with a binary nobody meant to load.
        /// </para>
        /// </summary>
        public FilePhysicsArtifactProvider(string manifestPath, string payloadPath = null)
        {
            if (string.IsNullOrEmpty(manifestPath))
            {
                throw new ArgumentException("A manifest path is required.", nameof(manifestPath));
            }

            _manifestPath = manifestPath;
            _payloadPath = string.IsNullOrEmpty(payloadPath) ? null : payloadPath;
        }

        /// <summary>Path of the manifest this provider was configured with.</summary>
        public string ManifestPath => _manifestPath;

        /// <inheritdoc/>
        public string Description => "file:" + _manifestPath;

        /// <inheritdoc/>
        public PhysicsArtifactLoadResult Load(string expectedRuntimeCompatibilityId)
        {
            PhysicsArtifactManifest manifest = ReadManifest(out PhysicsArtifactError manifestError);
            if (manifestError.IsError)
            {
                return PhysicsArtifactLoadResult.Failure(manifestError, Description);
            }

            string payloadPath = ResolvePayloadPath(manifest, out PhysicsArtifactError pathError);
            if (pathError.IsError)
            {
                return PhysicsArtifactLoadResult.Failure(pathError, Description);
            }

            byte[] payload = ReadPayload(payloadPath, manifest, out PhysicsArtifactError payloadError);
            if (payloadError.IsError)
            {
                return PhysicsArtifactLoadResult.Failure(payloadError, Description);
            }

            // The reader re-hashes the bytes and enforces both the expected hash and the whole
            // manifest cross-check, so nothing here has to trust the file names or the counts.
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
                result.Artifact,
                manifest,
                JitterPhysicsHash.Sha256Hex(payload),
                Description);
        }

        private PhysicsArtifactManifest ReadManifest(out PhysicsArtifactError error)
        {
            error = default;

            FileInfo file;
            try
            {
                file = new FileInfo(_manifestPath);
            }
            catch (Exception exception) when (IsPathProblem(exception))
            {
                error = Unavailable($"Manifest path '{_manifestPath}' is not usable: {exception.Message}");
                return null;
            }

            if (!file.Exists)
            {
                error = Unavailable($"No artifact manifest at '{_manifestPath}'.");
                return null;
            }

            // Checked before reading: an oversized "manifest" is either the wrong file or an
            // attempt to make the parser allocate, and neither is worth pulling into memory.
            if (file.Length > PhysicsArtifactManifestCodec.MaxManifestBytes)
            {
                error = new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"Manifest '{_manifestPath}' is {file.Length} bytes, over the limit of "
                    + PhysicsArtifactManifestCodec.MaxManifestBytes + ".");
                return null;
            }

            string json;
            try
            {
                json = File.ReadAllText(_manifestPath, new UTF8Encoding(false));
            }
            catch (Exception exception) when (IsIoProblem(exception))
            {
                error = Unavailable($"Manifest '{_manifestPath}' could not be read: {exception.Message}");
                return null;
            }

            PhysicsArtifactManifest manifest = PhysicsArtifactManifestCodec.Read(json, out string parseError);
            if (manifest == null)
            {
                error = new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.ManifestMismatch,
                    $"Manifest '{_manifestPath}' is not a manifest this build understands: {parseError}");
                return null;
            }

            return manifest;
        }

        private string ResolvePayloadPath(PhysicsArtifactManifest manifest, out PhysicsArtifactError error)
        {
            error = default;

            if (_payloadPath != null)
            {
                return _payloadPath;
            }

            string fileName = manifest.FileName;

            // The payload name comes out of a file this process did not write, so it is treated
            // as untrusted input: a manifest that says "../../etc/passwd" or an absolute path
            // must not make the server read outside the folder it was pointed at.
            if (string.IsNullOrEmpty(fileName)
                || fileName.IndexOf('/') >= 0
                || fileName.IndexOf('\\') >= 0
                || fileName.IndexOf(Path.DirectorySeparatorChar) >= 0
                || fileName.IndexOf(Path.AltDirectorySeparatorChar) >= 0
                || fileName == "."
                || fileName == ".."
                || Path.IsPathRooted(fileName))
            {
                error = new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Manifest '{_manifestPath}' names its payload as '{fileName}', which is not a plain "
                    + "file name; the payload is always read from the manifest's own folder.",
                    manifest.LevelId,
                    manifest.ArtifactHash);
                return null;
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(_manifestPath));
            return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
        }

        private byte[] ReadPayload(
            string payloadPath,
            PhysicsArtifactManifest manifest,
            out PhysicsArtifactError error)
        {
            error = default;

            FileInfo file;
            try
            {
                file = new FileInfo(payloadPath);
            }
            catch (Exception exception) when (IsPathProblem(exception))
            {
                error = Unavailable(
                    $"Payload path '{payloadPath}' is not usable: {exception.Message}", manifest);
                return null;
            }

            if (!file.Exists)
            {
                error = Unavailable(
                    $"Manifest '{_manifestPath}' describes payload '{payloadPath}', which does not exist.",
                    manifest);
                return null;
            }

            // The cap is enforced on the file length rather than after reading, so a wrong or
            // hostile file cannot cost 500 MB of server memory before it is rejected.
            if (file.Length > PhysicsArtifactLimits.MaxArtifactBytes)
            {
                error = new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.LimitExceeded,
                    $"Payload '{payloadPath}' is {file.Length} bytes, over the limit of "
                    + PhysicsArtifactLimits.MaxArtifactBytes + ".",
                    manifest.LevelId,
                    manifest.ArtifactHash);
                return null;
            }

            try
            {
                return File.ReadAllBytes(payloadPath);
            }
            catch (Exception exception) when (IsIoProblem(exception))
            {
                error = Unavailable($"Payload '{payloadPath}' could not be read: {exception.Message}", manifest);
                return null;
            }
        }

        private static PhysicsArtifactError Unavailable(string message, PhysicsArtifactManifest manifest = null)
        {
            return new PhysicsArtifactError(
                PhysicsArtifactErrorCode.SourceUnavailable,
                message,
                manifest?.LevelId,
                manifest?.ArtifactHash);
        }

        private static bool IsPathProblem(Exception exception)
        {
            return exception is ArgumentException
                || exception is NotSupportedException
                || exception is PathTooLongException;
        }

        private static bool IsIoProblem(Exception exception)
        {
            return exception is IOException
                || exception is UnauthorizedAccessException
                || exception is NotSupportedException;
        }
    }
}

