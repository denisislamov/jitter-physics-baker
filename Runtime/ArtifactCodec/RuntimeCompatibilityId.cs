using System;
using System.Globalization;
using System.Text;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// The inputs that decide whether two builds can share an artifact.
    /// <para>
    /// The artifact schema answers "can I parse this file". This answers the harder question,
    /// "will this file build the same world here as it did there" — which also depends on the
    /// Jitter sources in use, their compile profile and the conversion semantics of the
    /// package. Those cannot be inferred from the file, so they are folded into one id.
    /// </para>
    /// </summary>
    public readonly struct RuntimeCompatibilityInputs
    {
        /// <summary>Binary layout version.</summary>
        public int ArtifactSchemaVersion { get; }

        /// <summary>Canonical source hash of the Jitter build, taken from <c>jitter2.lock.json</c>.</summary>
        public string JitterSourceContentHash { get; }

        /// <summary>Floating point precision mode of that Jitter build.</summary>
        public string PrecisionMode { get; }

        /// <summary>Identifier of the compile profile (defines, unsafe code, intrinsics profile).</summary>
        public string CompileProfileId { get; }

        /// <summary>Version of the collider to descriptor conversion.</summary>
        public int ColliderConversionVersion { get; }

        /// <summary>Version of the descriptor to shape construction.</summary>
        public int ShapeConstructionVersion { get; }

        /// <summary>Version of the static world builder.</summary>
        public int WorldBuilderVersion { get; }

        /// <summary>Version of the world-affecting defaults.</summary>
        public int WorldDefaultsVersion { get; }

        public RuntimeCompatibilityInputs(
            int artifactSchemaVersion,
            string jitterSourceContentHash,
            string precisionMode,
            string compileProfileId,
            int colliderConversionVersion,
            int shapeConstructionVersion,
            int worldBuilderVersion,
            int worldDefaultsVersion)
        {
            ArtifactSchemaVersion = artifactSchemaVersion;
            JitterSourceContentHash = jitterSourceContentHash
                ?? throw new ArgumentNullException(nameof(jitterSourceContentHash));
            PrecisionMode = precisionMode ?? throw new ArgumentNullException(nameof(precisionMode));
            CompileProfileId = compileProfileId ?? throw new ArgumentNullException(nameof(compileProfileId));
            ColliderConversionVersion = colliderConversionVersion;
            ShapeConstructionVersion = shapeConstructionVersion;
            WorldBuilderVersion = worldBuilderVersion;
            WorldDefaultsVersion = worldDefaultsVersion;
        }

        /// <summary>
        /// Inputs for the current build, given the Jitter source hash and compile profile the
        /// project actually uses. The semantics versions come from the package itself, so a
        /// caller cannot accidentally claim compatibility it does not have.
        /// </summary>
        public static RuntimeCompatibilityInputs ForCurrentBuild(
            string jitterSourceContentHash,
            string compileProfileId)
        {
            return new RuntimeCompatibilityInputs(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                jitterSourceContentHash,
                JitterPhysicsSemantics.PrecisionMode,
                compileProfileId,
                JitterPhysicsSemantics.ColliderConversionVersion,
                JitterPhysicsSemantics.ShapeConstructionVersion,
                JitterPhysicsSemantics.WorldBuilderVersion,
                JitterPhysicsSemantics.WorldDefaultsVersion);
        }
    }

    /// <summary>
    /// Computes the runtime compatibility id. It is always derived, never typed by hand: a
    /// hand-maintained id is a number somebody forgets to change, and the failure it hides is
    /// a client and a server silently simulating different worlds.
    /// </summary>
    public static class RuntimeCompatibilityId
    {
        /// <summary>Returns the lowercase hex SHA-256 of the canonical encoding of the inputs.</summary>
        public static string Compute(RuntimeCompatibilityInputs inputs)
        {
            var builder = new StringBuilder(256);

            // Every field is written with its name and an explicit length, so that two
            // different input sets cannot produce the same concatenated text.
            AppendField(builder, "schema", inputs.ArtifactSchemaVersion.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "jitterSource", inputs.JitterSourceContentHash);
            AppendField(builder, "precision", inputs.PrecisionMode);
            AppendField(builder, "compileProfile", inputs.CompileProfileId);
            AppendField(builder, "colliderConversion", inputs.ColliderConversionVersion.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "shapeConstruction", inputs.ShapeConstructionVersion.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "worldBuilder", inputs.WorldBuilderVersion.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "worldDefaults", inputs.WorldDefaultsVersion.ToString(CultureInfo.InvariantCulture));

            return JitterPhysicsHash.Sha256HexUtf8(builder.ToString());
        }

        private static void AppendField(StringBuilder builder, string name, string value)
        {
            string text = value ?? string.Empty;
            builder.Append(name)
                .Append('=')
                .Append(text.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(text)
                .Append('\n');
        }
    }
}
