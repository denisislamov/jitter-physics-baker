using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>How the generated provider source should look.</summary>
    public sealed class EmbeddedArtifactSourceOptions
    {
        /// <summary>Default cap on the payload size a build is allowed to embed.</summary>
        public const int DefaultMaxEmbeddedBytes = 4 * 1024 * 1024;

        /// <summary>Default base64 characters per chunk; a multiple of four so chunks decode independently.</summary>
        public const int DefaultChunkLength = 4096;

        /// <summary>Namespace of the generated class.</summary>
        public string Namespace { get; }

        /// <summary>Name of the generated class; the file is named after it.</summary>
        public string ClassName { get; }

        /// <summary>Base64 characters per chunk.</summary>
        public int ChunkLength { get; }

        /// <summary>
        /// Largest payload this generator will embed. It is a policy, not a technical limit:
        /// embedding a production-sized map would inflate every server build and turn a level
        /// change into a recompile, so the wall is placed where somebody has to think.
        /// </summary>
        public int MaxEmbeddedBytes { get; }

        public EmbeddedArtifactSourceOptions(
            string @namespace,
            string className,
            int chunkLength = DefaultChunkLength,
            int maxEmbeddedBytes = DefaultMaxEmbeddedBytes)
        {
            if (!IsIdentifierPath(@namespace))
            {
                throw new ArgumentException(
                    $"'{@namespace}' is not a valid namespace; the generator writes it verbatim.",
                    nameof(@namespace));
            }

            if (!IsIdentifier(className))
            {
                throw new ArgumentException(
                    $"'{className}' is not a valid class name.", nameof(className));
            }

            if (chunkLength < 4 || chunkLength % 4 != 0)
            {
                throw new ArgumentException(
                    "Chunk length must be a positive multiple of four so that base64 chunks stay aligned.",
                    nameof(chunkLength));
            }

            if (maxEmbeddedBytes <= 0)
            {
                throw new ArgumentException("The size cap must be positive.", nameof(maxEmbeddedBytes));
            }

            Namespace = @namespace;
            ClassName = className;
            ChunkLength = chunkLength;
            MaxEmbeddedBytes = maxEmbeddedBytes;
        }

        private static bool IsIdentifierPath(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string[] parts = value.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!IsIdentifier(parts[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (!char.IsLetter(value[0]) && value[0] != '_')
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                if (!char.IsLetterOrDigit(value[i]) && value[i] != '_')
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The generated file: what to write, and what it describes.</summary>
    public sealed class EmbeddedArtifactSource
    {
        /// <summary>File name to write, <c>&lt;ClassName&gt;.g.cs</c>.</summary>
        public string FileName { get; }

        /// <summary>Generated C#, with LF line endings.</summary>
        public string Code { get; }

        /// <summary>The base64 chunks the code embeds, in order.</summary>
        public IReadOnlyList<string> Chunks { get; }

        /// <summary>Hash of the payload that was embedded.</summary>
        public string ArtifactHash { get; }

        internal EmbeddedArtifactSource(
            string fileName,
            string code,
            IReadOnlyList<string> chunks,
            string artifactHash)
        {
            FileName = fileName;
            Code = code;
            Chunks = chunks;
            ArtifactHash = artifactHash;
        }
    }

    /// <summary>
    /// Turns an already baked payload into a generated C# provider.
    /// <para>
    /// The generator never bakes. It is given the exact bytes that were written and the manifest
    /// that describes them, and it refuses if those two disagree — which is what keeps "the
    /// embedded artifact" and "the artifact the client has" from being two different things.
    /// Re-baking here would defeat the purpose: the point of the export is that the server runs
    /// the very bytes that were verified, not a fresh bake that ought to be identical.
    /// </para>
    /// <para>
    /// Output is deterministic: no timestamp, no machine name, no user. Two exports of the same
    /// artifact produce byte-identical source, so a regenerated file is either an empty diff or
    /// a real change.
    /// </para>
    /// </summary>
    public static class EmbeddedArtifactSourceGenerator
    {
        /// <summary>Generates the provider source for <paramref name="payload"/>.</summary>
        public static EmbeddedArtifactSource Generate(
            byte[] payload,
            PhysicsArtifactManifest manifest,
            EmbeddedArtifactSourceOptions options)
        {
            if (payload == null || payload.Length == 0)
            {
                throw new ArgumentException("There is nothing to embed.", nameof(payload));
            }

            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (payload.Length > options.MaxEmbeddedBytes)
            {
                throw new ArgumentException(
                    $"Artifact '{manifest.LevelId}' is {payload.Length} bytes, over the embedding cap of "
                    + $"{options.MaxEmbeddedBytes}. Embedding is for proof-of-concept and small levels; "
                    + "deliver a production level as content and load it with a file provider.",
                    nameof(payload));
            }

            string actualHash = JitterPhysicsHash.Sha256Hex(payload);
            if (!JitterPhysicsHash.HexEquals(actualHash, manifest.ArtifactHash))
            {
                // Refusing here rather than fixing the manifest: the two were produced together
                // by a bake, so a disagreement means one of them is not from that bake.
                throw new ArgumentException(
                    $"The manifest describes artifact {manifest.ArtifactHash}, the bytes hash to {actualHash}. "
                    + "Export the payload and the manifest that were baked together.",
                    nameof(manifest));
            }

            IReadOnlyList<string> chunks = Split(Convert.ToBase64String(payload), options.ChunkLength);
            string code = Emit(payload.Length, manifest, options, chunks);

            return new EmbeddedArtifactSource(options.ClassName + ".g.cs", code, chunks, actualHash);
        }

        private static IReadOnlyList<string> Split(string base64, int chunkLength)
        {
            var chunks = new List<string>((base64.Length / chunkLength) + 1);
            for (int offset = 0; offset < base64.Length; offset += chunkLength)
            {
                chunks.Add(base64.Substring(offset, Math.Min(chunkLength, base64.Length - offset)));
            }

            return chunks;
        }

        private static string Emit(
            int payloadSize,
            PhysicsArtifactManifest manifest,
            EmbeddedArtifactSourceOptions options,
            IReadOnlyList<string> chunks)
        {
            string manifestJson = PhysicsArtifactManifestCodec.Write(manifest);
            var builder = new StringBuilder(chunks.Count * (options.ChunkLength + 8) + 2048);

            builder.Append("// <auto-generated>\n");
            builder.Append("//     Generated by ").Append(JitterPhysicsPackage.PackageName)
                .Append(' ').Append(JitterPhysicsPackage.PackageVersion).Append(".\n");
            builder.Append("//     Level '").Append(manifest.LevelId).Append("', artifact ")
                .Append(manifest.ArtifactHash).Append(", ").Append(payloadSize.ToString(CultureInfo.InvariantCulture))
                .Append(" bytes.\n");
            builder.Append("//\n");
            builder.Append("//     Do not edit. These are the exact bytes a bake produced and hashed; editing them\n");
            builder.Append("//     changes the level the server simulates while every client still has the old one.\n");
            builder.Append("//     Regenerate instead: the export is deterministic, so an unchanged artifact is an\n");
            builder.Append("//     empty diff.\n");
            builder.Append("// </auto-generated>\n");
            builder.Append('\n');
            builder.Append("using DataSakura.JitterPhysics.ArtifactCodec;\n");
            builder.Append("using DataSakura.JitterPhysics.Contracts;\n");
            builder.Append('\n');
            builder.Append("namespace ").Append(options.Namespace).Append('\n');
            builder.Append("{\n");
            builder.Append("    /// <summary>The baked artifact of level '").Append(manifest.LevelId)
                .Append("', compiled into this assembly.</summary>\n");
            builder.Append("    public static class ").Append(options.ClassName).Append('\n');
            builder.Append("    {\n");
            builder.Append("        /// <summary>Canonical level id.</summary>\n");
            builder.Append("        public const string LevelId = \"").Append(manifest.LevelId).Append("\";\n");
            builder.Append('\n');
            builder.Append("        /// <summary>Lowercase hex SHA-256 of the embedded payload.</summary>\n");
            builder.Append("        public const string ArtifactHash = \"").Append(manifest.ArtifactHash).Append("\";\n");
            builder.Append('\n');
            builder.Append("        /// <summary>Runtime semantics the artifact was baked for.</summary>\n");
            builder.Append("        public const string RuntimeCompatibilityId = \"")
                .Append(manifest.RuntimeCompatibilityId).Append("\";\n");
            builder.Append('\n');
            builder.Append("        /// <summary>Size of the embedded payload in bytes.</summary>\n");
            builder.Append("        public const int PayloadSize = ")
                .Append(payloadSize.ToString(CultureInfo.InvariantCulture)).Append(";\n");
            builder.Append('\n');
            builder.Append("        /// <summary>\n");
            builder.Append("        /// The provider a server hands to <c>JitterPhysicsServerStartup.Start</c>. The\n");
            builder.Append("        /// payload is re-hashed and cross-checked against the manifest on load, exactly as\n");
            builder.Append("        /// a file would be: being inside the binary proves the compiler copied these bytes,\n");
            builder.Append("        /// not that they are the ones that were baked.\n");
            builder.Append("        /// </summary>\n");
            builder.Append("        public static IPhysicsArtifactProvider CreateProvider()\n");
            builder.Append("        {\n");
            builder.Append("            return new EmbeddedPhysicsArtifactProvider(Chunks, ManifestJson, \"embedded:")
                .Append(manifest.LevelId).Append('.')
                .Append(JitterPhysicsArtifactNaming.ShortHash(manifest.ArtifactHash)).Append("\");\n");
            builder.Append("        }\n");
            builder.Append('\n');
            builder.Append("        private const string ManifestJson =\n");
            builder.Append("            \"").Append(EscapeLiteral(manifestJson)).Append("\";\n");
            builder.Append('\n');
            builder.Append("        private static readonly string[] Chunks =\n");
            builder.Append("        {\n");

            for (int i = 0; i < chunks.Count; i++)
            {
                builder.Append("            \"").Append(chunks[i]).Append("\",\n");
            }

            builder.Append("        };\n");
            builder.Append("    }\n");
            builder.Append("}\n");

            return builder.ToString();
        }

        private static string EscapeLiteral(string value)
        {
            var builder = new StringBuilder(value.Length + 32);
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
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}

