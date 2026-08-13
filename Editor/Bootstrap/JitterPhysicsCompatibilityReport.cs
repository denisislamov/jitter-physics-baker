using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace DataSakura.JitterPhysics.Editor.Bootstrap
{
    /// <summary>
    /// How the Jitter2 copy of this project relates to the one the package supports.
    /// </summary>
    public enum JitterPhysicsCompatibilityStatus
    {
        /// <summary>
        /// No <c>Jitter2.Core</c> exists. This is a valid state: the package imports and
        /// compiles without Jitter, and baking becomes available after an explicit install.
        /// </summary>
        Missing = 0,

        /// <summary>Exactly one source copy exists and its canonical hash matches the lock.</summary>
        Compatible,

        /// <summary>Exactly one source copy exists but its canonical hash differs from the lock.</summary>
        Incompatible,

        /// <summary>More than one assembly definition declares the name; nothing may be installed.</summary>
        Duplicate,

        /// <summary>
        /// The assembly exists but has no assembly definition, so it is a precompiled plugin.
        /// Its sources cannot be hashed, so compatibility cannot be proven.
        /// </summary>
        UnsupportedPlugin,

        /// <summary>The report could not be produced; see the message.</summary>
        Unknown,
    }

    /// <summary>
    /// A read-only answer to "can this project bake, and against which Jitter2".
    /// <para>
    /// The report is deliberately separate from any install action. Discovery has to be safe
    /// to run at any time — from a window, from a test, from CI — and installation has to be
    /// an explicit, separate decision made by a human who has seen this result.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsCompatibilityReport
    {
        private JitterPhysicsCompatibilityReport(
            JitterPhysicsCompatibilityStatus status,
            string message,
            IReadOnlyList<string> jitterDefinitionPaths,
            string expectedSourceHash,
            string actualSourceHash,
            string compileProfileId,
            string runtimeCompatibilityId,
            int hashedFileCount,
            bool lockIsPlaceholder)
        {
            Status = status;
            Message = message;
            JitterDefinitionPaths = jitterDefinitionPaths ?? Array.Empty<string>();
            ExpectedSourceHash = expectedSourceHash;
            ActualSourceHash = actualSourceHash;
            CompileProfileId = compileProfileId;
            RuntimeCompatibilityId = runtimeCompatibilityId;
            HashedFileCount = hashedFileCount;
            LockIsPlaceholder = lockIsPlaceholder;
        }

        /// <summary>Classification of the project state.</summary>
        public JitterPhysicsCompatibilityStatus Status { get; }

        /// <summary>Actionable description, safe to show in a window or a log.</summary>
        public string Message { get; }

        /// <summary>Every assembly definition found for the Jitter assembly name.</summary>
        public IReadOnlyList<string> JitterDefinitionPaths { get; }

        /// <summary>Source hash the package release expects, from <c>jitter2.lock.json</c>.</summary>
        public string ExpectedSourceHash { get; }

        /// <summary>Source hash computed from the project's Jitter2 sources, when they exist.</summary>
        public string ActualSourceHash { get; }

        /// <summary>Identifier of the compile profile the lock declares.</summary>
        public string CompileProfileId { get; }

        /// <summary>
        /// Runtime compatibility id implied by the current state. It is only meaningful when
        /// <see cref="Status"/> is <see cref="JitterPhysicsCompatibilityStatus.Compatible"/>;
        /// otherwise it describes a build that must not bake.
        /// </summary>
        public string RuntimeCompatibilityId { get; }

        /// <summary>Number of files that participated in the computed hash.</summary>
        public int HashedFileCount { get; }

        /// <summary>True when the lock still carries a placeholder hash, before the first snapshot sync.</summary>
        public bool LockIsPlaceholder { get; }

        /// <summary>True when baking is allowed in the current state.</summary>
        public bool CanBake => Status == JitterPhysicsCompatibilityStatus.Compatible;

        /// <summary>Produces the report for the current project.</summary>
        public static JitterPhysicsCompatibilityReport Create()
        {
            string packageRoot = ResolvePackageRootPath();
            if (packageRoot == null)
            {
                return Failure("The package could not be resolved by the Package Manager.");
            }

            JitterPhysicsLock lockFile;
            try
            {
                lockFile = JitterPhysicsLock.Load(packageRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException)
            {
                return Failure($"'{JitterPhysicsLock.FileName}' could not be read: {exception.Message}");
            }

            JitterPhysicsAssemblyInfo jitter = JitterPhysicsAssemblyProbe.ProbeJitter();

            if (jitter.IsDuplicated)
            {
                return new JitterPhysicsCompatibilityReport(
                    JitterPhysicsCompatibilityStatus.Duplicate,
                    $"{jitter.DefinitionPaths.Count} assembly definitions declare '{jitter.Name}'. "
                    + "Two compiled copies of Jitter2 cannot coexist: remove all but one before "
                    + "installing or baking anything.",
                    jitter.DefinitionPaths,
                    lockFile.SourceContentHash,
                    null,
                    lockFile.CompileProfileId,
                    null,
                    0,
                    lockFile.IsPlaceholder);
            }

            if (!jitter.Exists && jitter.DefinitionPaths.Count == 0)
            {
                return new JitterPhysicsCompatibilityReport(
                    JitterPhysicsCompatibilityStatus.Missing,
                    $"This project has no '{jitter.Name}' assembly. The package still compiles; "
                    + "install the bundled fallback or add your own copy before baking.",
                    jitter.DefinitionPaths,
                    lockFile.SourceContentHash,
                    null,
                    lockFile.CompileProfileId,
                    null,
                    0,
                    lockFile.IsPlaceholder);
            }

            if (jitter.DefinitionPaths.Count == 0)
            {
                return new JitterPhysicsCompatibilityReport(
                    JitterPhysicsCompatibilityStatus.UnsupportedPlugin,
                    $"'{jitter.Name}' is compiled but has no assembly definition, so it is a "
                    + "precompiled plugin. Its sources cannot be hashed and compatibility "
                    + "cannot be proven; supply Jitter2 as source instead.",
                    jitter.DefinitionPaths,
                    lockFile.SourceContentHash,
                    null,
                    lockFile.CompileProfileId,
                    null,
                    0,
                    lockFile.IsPlaceholder);
            }

            string sourceRoot = ResolveSourceRoot(jitter.DefinitionPaths[0]);
            IReadOnlyList<JitterPhysicsSourceHasher.SourceInput> inputs =
                JitterPhysicsSourceHasher.CollectInputs(
                    sourceRoot, lockFile.IncludedFiles, lockFile.ExcludedFiles);
            string actualHash = JitterPhysicsSourceHasher.ComputeSourceContentHash(
                inputs, lockFile.CompileProfileText);

            bool matches = string.Equals(actualHash, lockFile.SourceContentHash, StringComparison.Ordinal);

            // Qualified on purpose: this class exposes a RuntimeCompatibilityId property,
            // which would otherwise shadow the type of the same name.
            string runtimeId = ArtifactCodec.RuntimeCompatibilityId.Compute(
                RuntimeCompatibilityInputs.ForCurrentBuild(actualHash, lockFile.CompileProfileId));

            if (!matches)
            {
                return new JitterPhysicsCompatibilityReport(
                    JitterPhysicsCompatibilityStatus.Incompatible,
                    $"The Jitter2 sources at '{sourceRoot}' hash to {actualHash}, but this package "
                    + $"release supports {lockFile.SourceContentHash}. Baking is blocked: the same "
                    + "artifact would build a different world on a peer built against the expected "
                    + "sources.",
                    jitter.DefinitionPaths,
                    lockFile.SourceContentHash,
                    actualHash,
                    lockFile.CompileProfileId,
                    runtimeId,
                    inputs.Count,
                    lockFile.IsPlaceholder);
            }

            return new JitterPhysicsCompatibilityReport(
                JitterPhysicsCompatibilityStatus.Compatible,
                $"'{jitter.Name}' at '{sourceRoot}' matches the source hash of this package release.",
                jitter.DefinitionPaths,
                lockFile.SourceContentHash,
                actualHash,
                lockFile.CompileProfileId,
                runtimeId,
                inputs.Count,
                lockFile.IsPlaceholder);
        }

        /// <summary>
        /// Serializes the report as deterministic JSON for CI. Machine-readable output exists
        /// so that a pipeline can fail on a specific status instead of grepping a log.
        /// </summary>
        public string ToJson()
        {
            var builder = new StringBuilder(512);
            builder.Append("{\n");
            AppendString(builder, "status", Status.ToString(), true);
            AppendString(builder, "package", JitterPhysicsPackage.PackageName, true);
            AppendString(builder, "packageVersion", JitterPhysicsPackage.PackageVersion, true);
            AppendString(builder, "expectedSourceHash", ExpectedSourceHash ?? string.Empty, true);
            AppendString(builder, "actualSourceHash", ActualSourceHash ?? string.Empty, true);
            AppendString(builder, "compileProfileId", CompileProfileId ?? string.Empty, true);
            AppendString(builder, "runtimeCompatibilityId", RuntimeCompatibilityId ?? string.Empty, true);
            AppendNumber(builder, "hashedFileCount", HashedFileCount, true);
            AppendBool(builder, "lockIsPlaceholder", LockIsPlaceholder, true);
            AppendBool(builder, "canBake", CanBake, true);

            builder.Append("  \"jitterDefinitionPaths\": [");
            for (int i = 0; i < JitterDefinitionPaths.Count; i++)
            {
                if (i != 0)
                {
                    builder.Append(", ");
                }

                builder.Append('"').Append(Escape(JitterDefinitionPaths[i])).Append('"');
            }

            builder.Append("],\n");
            AppendString(builder, "message", Message ?? string.Empty, false);
            builder.Append("}\n");
            return builder.ToString();
        }

        private static JitterPhysicsCompatibilityReport Failure(string message)
        {
            return new JitterPhysicsCompatibilityReport(
                JitterPhysicsCompatibilityStatus.Unknown,
                message,
                Array.Empty<string>(),
                null,
                null,
                null,
                null,
                0,
                false);
        }

        /// <summary>Absolute path of the package root, or <c>null</c> when it cannot be resolved.</summary>
        public static string ResolvePackageRootPath()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(JitterPhysicsCompatibilityReport).Assembly);
            return package?.resolvedPath;
        }

        /// <summary>
        /// Absolute folder that holds the Jitter2 sources: the folder of its assembly
        /// definition. The package never assumes a fixed location for a consumer's copy.
        /// </summary>
        private static string ResolveSourceRoot(string assemblyDefinitionAssetPath)
        {
            string directory = Path.GetDirectoryName(assemblyDefinitionAssetPath) ?? string.Empty;
            string projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, directory));
        }

        private static void AppendString(StringBuilder builder, string key, string value, bool comma)
        {
            builder.Append("  \"").Append(key).Append("\": \"").Append(Escape(value)).Append('"')
                .Append(comma ? ",\n" : "\n");
        }

        private static void AppendNumber(StringBuilder builder, string key, int value, bool comma)
        {
            builder.Append("  \"").Append(key).Append("\": ")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append(comma ? ",\n" : "\n");
        }

        private static void AppendBool(StringBuilder builder, string key, bool value, bool comma)
        {
            builder.Append("  \"").Append(key).Append("\": ").Append(value ? "true" : "false")
                .Append(comma ? ",\n" : "\n");
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}


