using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>
    /// Reads and writes the artifact manifest as deterministic JSON.
    /// <para>
    /// The package writes and parses this by hand instead of using a JSON library, for two
    /// reasons. The portable assemblies must compile both inside Unity and against a plain
    /// .NET SDK, and the two ecosystems do not share one serializer; and the output has to be
    /// byte-stable — fixed key order, invariant culture, LF line endings — which general
    /// purpose serializers do not promise.
    /// </para>
    /// <para>
    /// The format is deliberately flat: only string and integer values, no nesting. That
    /// keeps the parser small enough to be obviously correct and strict enough to reject
    /// anything unexpected.
    /// </para>
    /// </summary>
    public static class PhysicsArtifactManifestCodec
    {
        private const string SchemaVersionKey = "schemaVersion";
        private const string RuntimeCompatibilityIdKey = "runtimeCompatibilityId";
        private const string GeneratorVersionKey = "generatorVersion";
        private const string LevelIdKey = "levelId";
        private const string ArtifactHashKey = "artifactHash";
        private const string BodyCountKey = "bodyCount";
        private const string ShapeCountKey = "shapeCount";
        private const string VertexCountKey = "vertexCount";
        private const string TriangleCountKey = "triangleCount";
        private const string TickRateKey = "tickRate";
        private const string FileNameKey = "fileName";

        /// <summary>Largest manifest the parser will look at, as a denial-of-service guard.</summary>
        public const int MaxManifestBytes = 8 * 1024;

        /// <summary>Serializes a manifest to canonical JSON with LF line endings.</summary>
        public static string Write(PhysicsArtifactManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var builder = new StringBuilder(512);
            builder.Append("{\n");
            AppendString(builder, SchemaVersionKey, manifest.SchemaVersion, true);
            AppendString(builder, RuntimeCompatibilityIdKey, manifest.RuntimeCompatibilityId, true);
            AppendString(builder, GeneratorVersionKey, manifest.GeneratorVersion, true);
            AppendString(builder, LevelIdKey, manifest.LevelId, true);
            AppendString(builder, ArtifactHashKey, manifest.ArtifactHash, true);
            AppendNumber(builder, BodyCountKey, manifest.BodyCount, true);
            AppendNumber(builder, ShapeCountKey, manifest.ShapeCount, true);
            AppendNumber(builder, VertexCountKey, manifest.VertexCount, true);
            AppendNumber(builder, TriangleCountKey, manifest.TriangleCount, true);
            AppendNumber(builder, TickRateKey, manifest.TickRate, true);
            AppendString(builder, FileNameKey, manifest.FileName, false);
            builder.Append("}\n");
            return builder.ToString();
        }

        /// <summary>
        /// Parses a manifest. Returns <c>null</c> and a reason when the text is not a manifest
        /// this build understands; a malformed manifest is expected input, not an exception.
        /// </summary>
        public static PhysicsArtifactManifest Read(string json, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "Manifest is empty.";
                return null;
            }

            if (json.Length > MaxManifestBytes)
            {
                error = $"Manifest is {json.Length} characters, over the limit of {MaxManifestBytes}.";
                return null;
            }

            Dictionary<string, string> fields;
            try
            {
                fields = ParseFlatObject(json);
            }
            catch (FormatException exception)
            {
                error = exception.Message;
                return null;
            }

            if (!TryGet(fields, SchemaVersionKey, out string schemaVersion, ref error)
                || !TryGet(fields, RuntimeCompatibilityIdKey, out string runtimeId, ref error)
                || !TryGet(fields, GeneratorVersionKey, out string generatorVersion, ref error)
                || !TryGet(fields, LevelIdKey, out string levelId, ref error)
                || !TryGet(fields, ArtifactHashKey, out string artifactHash, ref error)
                || !TryGet(fields, FileNameKey, out string fileName, ref error)
                || !TryGetInt(fields, BodyCountKey, out int bodyCount, ref error)
                || !TryGetInt(fields, ShapeCountKey, out int shapeCount, ref error)
                || !TryGetInt(fields, VertexCountKey, out int vertexCount, ref error)
                || !TryGetInt(fields, TriangleCountKey, out int triangleCount, ref error)
                || !TryGetInt(fields, TickRateKey, out int tickRate, ref error))
            {
                return null;
            }

            return new PhysicsArtifactManifest(
                schemaVersion,
                runtimeId,
                generatorVersion,
                levelId,
                artifactHash,
                bodyCount,
                shapeCount,
                vertexCount,
                triangleCount,
                tickRate,
                fileName);
        }

        private static void AppendString(StringBuilder builder, string key, string value, bool comma)
        {
            builder.Append("  \"").Append(key).Append("\": \"").Append(Escape(value)).Append('"');
            builder.Append(comma ? ",\n" : "\n");
        }

        private static void AppendNumber(StringBuilder builder, string key, int value, bool comma)
        {
            builder.Append("  \"").Append(key).Append("\": ")
                .Append(value.ToString(CultureInfo.InvariantCulture));
            builder.Append(comma ? ",\n" : "\n");
        }

        private static string Escape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
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
                        if (character < 0x20)
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        private static Dictionary<string, string> ParseFlatObject(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            int index = SkipWhitespace(json, 0);
            Expect(json, ref index, '{');

            index = SkipWhitespace(json, index);
            if (index < json.Length && json[index] == '}')
            {
                return result;
            }

            while (true)
            {
                index = SkipWhitespace(json, index);
                string key = ParseString(json, ref index);
                index = SkipWhitespace(json, index);
                Expect(json, ref index, ':');
                index = SkipWhitespace(json, index);

                string value = index < json.Length && json[index] == '"'
                    ? ParseString(json, ref index)
                    : ParseNumber(json, ref index);

                if (result.ContainsKey(key))
                {
                    // A duplicate key means two different values claim the same field; picking
                    // either one silently is how a manifest ends up describing another file.
                    throw new FormatException($"Manifest declares '{key}' more than once.");
                }

                result[key] = value;

                index = SkipWhitespace(json, index);
                if (index >= json.Length)
                {
                    throw new FormatException("Manifest ended before the closing brace.");
                }

                if (json[index] == ',')
                {
                    index++;
                    continue;
                }

                Expect(json, ref index, '}');
                index = SkipWhitespace(json, index);
                if (index != json.Length)
                {
                    throw new FormatException("Manifest has trailing content after the closing brace.");
                }

                return result;
            }
        }

        private static string ParseString(string json, ref int index)
        {
            Expect(json, ref index, '"');
            var builder = new StringBuilder();
            while (true)
            {
                if (index >= json.Length)
                {
                    throw new FormatException("Manifest ended inside a string.");
                }

                char character = json[index++];
                if (character == '"')
                {
                    return builder.ToString();
                }

                if (character != '\\')
                {
                    builder.Append(character);
                    continue;
                }

                if (index >= json.Length)
                {
                    throw new FormatException("Manifest ended inside an escape sequence.");
                }

                char escape = json[index++];
                switch (escape)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (index + 4 > json.Length
                            || !ushort.TryParse(
                                json.Substring(index, 4),
                                NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture,
                                out ushort code))
                        {
                            throw new FormatException("Manifest contains a malformed \\u escape.");
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new FormatException($"Manifest contains an unknown escape '\\{escape}'.");
                }
            }
        }

        private static string ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '-'))
            {
                index++;
            }

            if (index == start)
            {
                throw new FormatException($"Manifest has an unexpected value at offset {start}.");
            }

            return json.Substring(start, index - start);
        }

        private static int SkipWhitespace(string json, int index)
        {
            while (index < json.Length
                   && (json[index] == ' ' || json[index] == '\t' || json[index] == '\n' || json[index] == '\r'))
            {
                index++;
            }

            return index;
        }

        private static void Expect(string json, ref int index, char expected)
        {
            if (index >= json.Length || json[index] != expected)
            {
                throw new FormatException(
                    $"Manifest expected '{expected}' at offset {index}.");
            }

            index++;
        }

        private static bool TryGet(
            Dictionary<string, string> fields,
            string key,
            out string value,
            ref string error)
        {
            if (fields.TryGetValue(key, out value))
            {
                return true;
            }

            error = $"Manifest is missing '{key}'.";
            return false;
        }

        private static bool TryGetInt(
            Dictionary<string, string> fields,
            string key,
            out int value,
            ref string error)
        {
            value = 0;
            if (!fields.TryGetValue(key, out string text))
            {
                error = $"Manifest is missing '{key}'.";
                return false;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = $"Manifest field '{key}' is not an integer.";
                return false;
            }

            return true;
        }
    }
}
