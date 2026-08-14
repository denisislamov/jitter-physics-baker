using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DataSakura.JitterPhysics.Editor.Bootstrap
{
    /// <summary>
    /// The parsed contents of <c>jitter2.lock.json</c>: which Jitter2 source set this
    /// package release supports, and how that source set is identified.
    /// <para>
    /// The lock describes a <em>semantic version of the sources</em>, not a location. A
    /// consumer may keep its Jitter2 copy anywhere; what has to match is the canonical
    /// content hash of the compiled sources together with the compile profile.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsLock
    {
        /// <summary>File name of the lock inside the package root.</summary>
        public const string FileName = "jitter2.lock.json";

        /// <summary>Default location of the dormant snapshot, relative to the package root.</summary>
        public const string DefaultSnapshotRoot = "Jitter2~/Runtime";

        private JitterPhysicsLock(
            int schemaVersion,
            string assemblyName,
            string upstreamRepository,
            string upstreamCommit,
            string patchSetId,
            string sourceContentHash,
            string compileProfileText,
            string compileProfileId,
            string precision,
            string intrinsicsProfile,
            string polyfillProfile,
            IReadOnlyList<string> includedFiles,
            IReadOnlyList<string> excludedFiles)
        {
            SchemaVersion = schemaVersion;
            AssemblyName = assemblyName;
            UpstreamRepository = upstreamRepository;
            UpstreamCommit = upstreamCommit;
            PatchSetId = patchSetId;
            SourceContentHash = sourceContentHash;
            CompileProfileText = compileProfileText;
            CompileProfileId = compileProfileId;
            Precision = precision;
            IntrinsicsProfile = intrinsicsProfile;
            PolyfillProfile = polyfillProfile;
            IncludedFiles = includedFiles;
            ExcludedFiles = excludedFiles;
        }

        /// <summary>Version of the lock format.</summary>
        public int SchemaVersion { get; }

        /// <summary>Assembly name the package integrates with, normally <c>Jitter2.Core</c>.</summary>
        public string AssemblyName { get; }

        /// <summary>Upstream repository the snapshot originates from.</summary>
        public string UpstreamRepository { get; }

        /// <summary>Upstream commit the snapshot was taken at.</summary>
        public string UpstreamCommit { get; }

        /// <summary>Identifier of the applied patch set.</summary>
        public string PatchSetId { get; }

        /// <summary>Expected canonical source hash, in <c>sha256:&lt;hex&gt;</c> form.</summary>
        public string SourceContentHash { get; }

        /// <summary>
        /// Canonical serialization of the compile profile, byte-identical to what the Python
        /// tooling hashes: keys sorted ordinally, no whitespace, no trailing newline.
        /// </summary>
        public string CompileProfileText { get; }

        /// <summary>
        /// Stable identifier of the compile profile, fed into the runtime compatibility id.
        /// It is a hash of <see cref="CompileProfileText"/> rather than a hand-written name,
        /// so a changed profile cannot keep an old identifier.
        /// </summary>
        public string CompileProfileId { get; }

        /// <summary>Floating point precision declared by the compile profile.</summary>
        public string Precision { get; }

        /// <summary>
        /// How the snapshot obtains SIMD: <c>hardware</c> means it uses
        /// <c>System.Runtime.Intrinsics</c> directly.
        /// </summary>
        public string IntrinsicsProfile { get; }

        /// <summary>
        /// Which polyfill set the snapshot carries for runtimes that lack parts of the BCL it
        /// uses; <c>none</c> means it carries none.
        /// </summary>
        public string PolyfillProfile { get; }

        /// <summary>
        /// Whether this snapshot can be compiled by Unity at all.
        /// <para>
        /// Two facts of the engine decide it, and neither is negotiable from the package side.
        /// Unity compiles game assemblies at C# 9 and ignores <c>-langversion</c> in an
        /// assembly's <c>csc.rsp</c>, so a snapshot written with file-scoped namespaces or
        /// collection expressions cannot parse. And Unity's script reference assemblies are
        /// .NET Standard 2.1, which does not contain <c>System.Runtime.Intrinsics</c>, so a
        /// snapshot compiled against hardware intrinsics cannot bind. A snapshot is only
        /// installable into <c>Assets/</c> once a patch set has resolved both, which the
        /// profile records as a non-hardware intrinsics profile and a polyfill set.
        /// </para>
        /// </summary>
        public bool SupportsUnity =>
            !string.Equals(IntrinsicsProfile, "hardware", StringComparison.Ordinal)
            && !string.Equals(PolyfillProfile, "none", StringComparison.Ordinal);

        /// <summary>Glob patterns selecting the hashed sources.</summary>
        public IReadOnlyList<string> IncludedFiles { get; }

        /// <summary>Glob patterns removing files from the hashed set.</summary>
        public IReadOnlyList<string> ExcludedFiles { get; }

        /// <summary>True when the snapshot has not been synced yet and the lock is a placeholder.</summary>
        public bool IsPlaceholder =>
            SourceContentHash != null && SourceContentHash.EndsWith("UNSET", StringComparison.Ordinal);

        /// <summary>Loads and parses the lock from a package root folder.</summary>
        public static JitterPhysicsLock Load(string packageRootPath)
        {
            if (string.IsNullOrEmpty(packageRootPath))
            {
                throw new ArgumentException("Package root path is required.", nameof(packageRootPath));
            }

            string path = Path.Combine(packageRootPath, FileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"'{FileName}' is missing from the package.", path);
            }

            return Parse(File.ReadAllText(path));
        }

        /// <summary>Parses lock JSON text.</summary>
        public static JitterPhysicsLock Parse(string json)
        {
            JitterPhysicsJsonValue root = JitterPhysicsJsonValue.Parse(json);
            if (root.Kind != JitterPhysicsJsonKind.Object)
            {
                throw new FormatException("The lock file must contain a JSON object.");
            }

            JitterPhysicsJsonValue profile = root.Member("compileProfile");
            if (profile == null || profile.Kind != JitterPhysicsJsonKind.Object)
            {
                throw new FormatException("The lock file must declare a 'compileProfile' object.");
            }

            string profileText = CanonicalCompileProfileText(profile);
            JitterPhysicsJsonValue schema = root.Member("schemaVersion");

            return new JitterPhysicsLock(
                schema != null && int.TryParse(schema.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int schemaVersion)
                    ? schemaVersion
                    : 0,
                root.StringMember("assemblyName", string.Empty),
                root.StringMember("upstreamRepository", string.Empty),
                root.StringMember("upstreamCommit", string.Empty),
                root.StringMember("patchSetId", string.Empty),
                root.StringMember("sourceContentHash", string.Empty),
                profileText,
                ArtifactCodec.JitterPhysicsHash.Sha256HexUtf8(profileText),
                profile.StringMember("precision", string.Empty),
                profile.StringMember("intrinsicsProfile", string.Empty),
                profile.StringMember("polyfillProfile", string.Empty),
                root.StringArrayMember("includedFiles"),
                root.StringArrayMember("excludedFiles"));
        }

        /// <summary>
        /// Serializes the compile profile the way <c>json.dumps(profile, sort_keys=True,
        /// separators=(",", ":"))</c> does in the Python tooling. The two must agree exactly,
        /// because the text is hashed as part of the canonical source hash.
        /// </summary>
        private static string CanonicalCompileProfileText(JitterPhysicsJsonValue profile)
        {
            var keys = new List<string>(profile.Members.Count);
            for (int i = 0; i < profile.Members.Count; i++)
            {
                keys.Add(profile.Members[i].Key);
            }

            keys.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder(256);
            builder.Append('{');
            for (int i = 0; i < keys.Count; i++)
            {
                if (i != 0)
                {
                    builder.Append(',');
                }

                AppendJsonString(builder, keys[i]);
                builder.Append(':');
                AppendCanonicalValue(builder, profile.Member(keys[i]));
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendCanonicalValue(StringBuilder builder, JitterPhysicsJsonValue value)
        {
            switch (value.Kind)
            {
                case JitterPhysicsJsonKind.String:
                    AppendJsonString(builder, value.Text);
                    break;
                case JitterPhysicsJsonKind.Bool:
                    builder.Append(value.Boolean ? "true" : "false");
                    break;
                case JitterPhysicsJsonKind.Number:
                    builder.Append(value.Text);
                    break;
                case JitterPhysicsJsonKind.Null:
                    builder.Append("null");
                    break;
                default:
                    // Nested containers inside the compile profile would need an agreed
                    // canonical form on both sides; until one exists, they are refused
                    // instead of hashed differently by the two implementations.
                    throw new FormatException(
                        "The compile profile may only contain scalar values.");
            }
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        // Python's json.dumps defaults to ensure_ascii=True, so every
                        // non-ASCII character is escaped; the same is done here.
                        if (character < 0x20 || character > 0x7E)
                        {
                            builder.Append("\\u")
                                .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}




