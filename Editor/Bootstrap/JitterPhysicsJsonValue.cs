using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DataSakura.JitterPhysics.Editor.Bootstrap
{
    /// <summary>
    /// Kind of a parsed JSON value. The package only needs the subset that appears in
    /// <c>jitter2.lock.json</c>, so anything richer is intentionally absent.
    /// </summary>
    public enum JitterPhysicsJsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// A minimal, strict JSON reader used to parse <c>jitter2.lock.json</c>.
    /// <para>
    /// <c>JsonUtility</c> is not used here for one specific reason: the lock hash must be
    /// reproduced byte-for-byte by the Python tooling in <c>tools~</c>, which means the
    /// editor has to see the compile profile as an ordered set of raw key/value pairs rather
    /// than as a fixed C# class. A new field added to the profile must change the hash on
    /// both sides automatically, not only on the side whose model was updated.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsJsonValue
    {
        private readonly List<JitterPhysicsJsonValue> array;
        private readonly List<KeyValuePair<string, JitterPhysicsJsonValue>> members;

        private JitterPhysicsJsonValue(
            JitterPhysicsJsonKind kind,
            string text,
            bool boolean,
            List<JitterPhysicsJsonValue> array,
            List<KeyValuePair<string, JitterPhysicsJsonValue>> members)
        {
            Kind = kind;
            Text = text;
            Boolean = boolean;
            this.array = array;
            this.members = members;
        }

        /// <summary>Kind of this value.</summary>
        public JitterPhysicsJsonKind Kind { get; }

        /// <summary>String content, or the literal text of a number.</summary>
        public string Text { get; }

        /// <summary>Boolean content; meaningful only for <see cref="JitterPhysicsJsonKind.Bool"/>.</summary>
        public bool Boolean { get; }

        /// <summary>Array items in document order.</summary>
        public IReadOnlyList<JitterPhysicsJsonValue> Items =>
            array ?? (IReadOnlyList<JitterPhysicsJsonValue>)Array.Empty<JitterPhysicsJsonValue>();

        /// <summary>Object members in document order.</summary>
        public IReadOnlyList<KeyValuePair<string, JitterPhysicsJsonValue>> Members =>
            members
            ?? (IReadOnlyList<KeyValuePair<string, JitterPhysicsJsonValue>>)
            Array.Empty<KeyValuePair<string, JitterPhysicsJsonValue>>();

        /// <summary>Returns the member with the given name, or <c>null</c> when absent.</summary>
        public JitterPhysicsJsonValue Member(string name)
        {
            if (members == null)
            {
                return null;
            }

            for (int i = 0; i < members.Count; i++)
            {
                if (string.Equals(members[i].Key, name, StringComparison.Ordinal))
                {
                    return members[i].Value;
                }
            }

            return null;
        }

        /// <summary>Returns a string member, or <paramref name="fallback"/> when it is missing.</summary>
        public string StringMember(string name, string fallback = null)
        {
            JitterPhysicsJsonValue value = Member(name);
            return value != null && value.Kind == JitterPhysicsJsonKind.String ? value.Text : fallback;
        }

        /// <summary>Returns the items of a string array member; never <c>null</c>.</summary>
        public IReadOnlyList<string> StringArrayMember(string name)
        {
            JitterPhysicsJsonValue value = Member(name);
            if (value == null || value.Kind != JitterPhysicsJsonKind.Array)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(value.Items.Count);
            for (int i = 0; i < value.Items.Count; i++)
            {
                JitterPhysicsJsonValue item = value.Items[i];
                if (item.Kind == JitterPhysicsJsonKind.String)
                {
                    result.Add(item.Text);
                }
            }

            return result;
        }

        /// <summary>Parses a JSON document. Throws <see cref="FormatException"/> on malformed input.</summary>
        public static JitterPhysicsJsonValue Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            int index = 0;
            JitterPhysicsJsonValue value = ParseValue(json, ref index);
            index = SkipWhitespace(json, index);
            if (index != json.Length)
            {
                throw new FormatException($"Unexpected content at offset {index}.");
            }

            return value;
        }

        private static JitterPhysicsJsonValue ParseValue(string json, ref int index)
        {
            index = SkipWhitespace(json, index);
            if (index >= json.Length)
            {
                throw new FormatException("Document ended while a value was expected.");
            }

            char character = json[index];
            switch (character)
            {
                case '{':
                    return ParseObject(json, ref index);
                case '[':
                    return ParseArray(json, ref index);
                case '"':
                    return new JitterPhysicsJsonValue(
                        JitterPhysicsJsonKind.String, ParseString(json, ref index), false, null, null);
                case 't':
                    Expect(json, ref index, "true");
                    return new JitterPhysicsJsonValue(JitterPhysicsJsonKind.Bool, "true", true, null, null);
                case 'f':
                    Expect(json, ref index, "false");
                    return new JitterPhysicsJsonValue(JitterPhysicsJsonKind.Bool, "false", false, null, null);
                case 'n':
                    Expect(json, ref index, "null");
                    return new JitterPhysicsJsonValue(JitterPhysicsJsonKind.Null, null, false, null, null);
                default:
                    return new JitterPhysicsJsonValue(
                        JitterPhysicsJsonKind.Number, ParseNumber(json, ref index), false, null, null);
            }
        }

        private static JitterPhysicsJsonValue ParseObject(string json, ref int index)
        {
            var members = new List<KeyValuePair<string, JitterPhysicsJsonValue>>();
            index++;
            index = SkipWhitespace(json, index);

            if (index < json.Length && json[index] == '}')
            {
                index++;
                return new JitterPhysicsJsonValue(JitterPhysicsJsonKind.Object, null, false, null, members);
            }

            while (true)
            {
                index = SkipWhitespace(json, index);
                string key = ParseString(json, ref index);
                index = SkipWhitespace(json, index);
                Expect(json, ref index, ":");
                JitterPhysicsJsonValue value = ParseValue(json, ref index);

                for (int i = 0; i < members.Count; i++)
                {
                    if (string.Equals(members[i].Key, key, StringComparison.Ordinal))
                    {
                        throw new FormatException($"Object declares '{key}' more than once.");
                    }
                }

                members.Add(new KeyValuePair<string, JitterPhysicsJsonValue>(key, value));

                index = SkipWhitespace(json, index);
                if (index >= json.Length)
                {
                    throw new FormatException("Document ended before the closing brace.");
                }

                if (json[index] == ',')
                {
                    index++;
                    continue;
                }

                Expect(json, ref index, "}");
                return new JitterPhysicsJsonValue(JitterPhysicsJsonKind.Object, null, false, null, members);
            }
        }

        private static JitterPhysicsJsonValue ParseArray(string json, ref int index)
        {
            var items = new List<JitterPhysicsJsonValue>();
            index++;
            index = SkipWhitespace(json, index);

            if (index < json.Length && json[index] == ']')
            {
                index++;
                return new JitterPhysicsJsonValue(JitterPhysicsJsonKind.Array, null, false, items, null);
            }

            while (true)
            {
                items.Add(ParseValue(json, ref index));
                index = SkipWhitespace(json, index);
                if (index >= json.Length)
                {
                    throw new FormatException("Document ended before the closing bracket.");
                }

                if (json[index] == ',')
                {
                    index++;
                    continue;
                }

                Expect(json, ref index, "]");
                return new JitterPhysicsJsonValue(JitterPhysicsJsonKind.Array, null, false, items, null);
            }
        }

        private static string ParseString(string json, ref int index)
        {
            Expect(json, ref index, "\"");
            var builder = new StringBuilder();
            while (true)
            {
                if (index >= json.Length)
                {
                    throw new FormatException("Document ended inside a string.");
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
                    throw new FormatException("Document ended inside an escape sequence.");
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
                            throw new FormatException("Malformed \\u escape.");
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new FormatException($"Unknown escape '\\{escape}'.");
                }
            }
        }

        private static string ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length && IsNumberCharacter(json[index]))
            {
                index++;
            }

            if (index == start)
            {
                throw new FormatException($"Unexpected value at offset {start}.");
            }

            return json.Substring(start, index - start);
        }

        private static bool IsNumberCharacter(char value)
        {
            return (value >= '0' && value <= '9')
                || value == '-'
                || value == '+'
                || value == '.'
                || value == 'e'
                || value == 'E';
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

        private static void Expect(string json, ref int index, string expected)
        {
            index = SkipWhitespace(json, index);
            if (index + expected.Length > json.Length
                || string.CompareOrdinal(json, index, expected, 0, expected.Length) != 0)
            {
                throw new FormatException($"Expected '{expected}' at offset {index}.");
            }

            index += expected.Length;
        }
    }
}

