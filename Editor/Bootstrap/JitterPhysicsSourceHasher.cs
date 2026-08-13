using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DataSakura.JitterPhysics.Editor.Bootstrap
{
    /// <summary>
    /// Computes the canonical Jitter2 source content hash.
    /// <para>
    /// This is the editor half of a two-implementation contract: <c>tools~/hash-jitter2.py</c>
    /// computes the same value in CI and when the snapshot is synced. They must agree
    /// byte-for-byte, otherwise the editor would report a project as incompatible that CI
    /// considers fine, or the other way round. Everything that could differ between the two
    /// — file selection, ordering, line endings, the serialized compile profile — is pinned
    /// explicitly rather than inherited from a platform default.
    /// </para>
    /// </summary>
    public static class JitterPhysicsSourceHasher
    {
        /// <summary>Prefix of a computed hash, matching the value stored in the lock.</summary>
        public const string HashPrefix = "sha256:";

        /// <summary>File extensions whose line endings are normalized before hashing.</summary>
        private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".rsp",
            ".json",
            ".txt",
            ".md",
            ".asmdef",
        };

        private static readonly Dictionary<string, Regex> GlobCache = new Dictionary<string, Regex>(StringComparer.Ordinal);

        /// <summary>One hashed file: its canonical relative path and its normalized bytes.</summary>
        public readonly struct SourceInput
        {
            /// <summary>Path relative to the source root, always using <c>/</c>.</summary>
            public string RelativePath { get; }

            /// <summary>Content after line-ending normalization.</summary>
            public byte[] Content { get; }

            internal SourceInput(string relativePath, byte[] content)
            {
                RelativePath = relativePath;
                Content = content;
            }
        }

        /// <summary>
        /// Selects the files that participate in the hash, in canonical order. A path must
        /// match an include pattern and must not match an exclude pattern.
        /// </summary>
        public static IReadOnlyList<SourceInput> CollectInputs(
            string sourceRootPath,
            IReadOnlyList<string> includePatterns,
            IReadOnlyList<string> excludePatterns)
        {
            if (string.IsNullOrEmpty(sourceRootPath) || !Directory.Exists(sourceRootPath))
            {
                return Array.Empty<SourceInput>();
            }

            var selected = new List<string>();
            foreach (string path in Directory.EnumerateFiles(sourceRootPath, "*", SearchOption.AllDirectories))
            {
                string relative = CanonicalRelativePath(sourceRootPath, path);
                if (includePatterns != null && includePatterns.Count > 0
                    && !MatchesAny(relative, includePatterns))
                {
                    continue;
                }

                if (excludePatterns != null && MatchesAny(relative, excludePatterns))
                {
                    continue;
                }

                selected.Add(relative);
            }

            // Ordinal order on the relative path: the digest must not depend on how the file
            // system happened to enumerate the folder, nor on where the package is checked out.
            selected.Sort(StringComparer.Ordinal);

            var inputs = new List<SourceInput>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                string absolute = Path.Combine(sourceRootPath, selected[i].Replace('/', Path.DirectorySeparatorChar));
                inputs.Add(new SourceInput(selected[i], NormalizeContent(selected[i], File.ReadAllBytes(absolute))));
            }

            return inputs;
        }

        /// <summary>
        /// Hashes the selected inputs together with the canonical compile profile text.
        /// Each element is length-prefixed so that two different file sets cannot produce the
        /// same digest by concatenating differently.
        /// </summary>
        public static string ComputeSourceContentHash(
            IReadOnlyList<SourceInput> inputs,
            string compileProfileText)
        {
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            if (compileProfileText == null)
            {
                throw new ArgumentNullException(nameof(compileProfileText));
            }

            var utf8 = new UTF8Encoding(false);
            using (var stream = new MemoryStream())
            {
                byte[] profileBytes = utf8.GetBytes(compileProfileText);
                WriteAscii(stream, "compileProfile\n");
                WriteAscii(stream, profileBytes.Length.ToString(CultureInfo.InvariantCulture));
                WriteAscii(stream, "\n");
                stream.Write(profileBytes, 0, profileBytes.Length);
                WriteAscii(stream, "\n");

                for (int i = 0; i < inputs.Count; i++)
                {
                    byte[] pathBytes = utf8.GetBytes(inputs[i].RelativePath);
                    byte[] content = inputs[i].Content;

                    stream.Write(pathBytes, 0, pathBytes.Length);
                    WriteAscii(stream, "\n");
                    WriteAscii(stream, content.Length.ToString(CultureInfo.InvariantCulture));
                    WriteAscii(stream, "\n");
                    stream.Write(content, 0, content.Length);
                    WriteAscii(stream, "\n");
                }

                using (var sha = SHA256.Create())
                {
                    return HashPrefix + ArtifactCodec.JitterPhysicsHash.ToHex(sha.ComputeHash(stream.ToArray()));
                }
            }
        }

        /// <summary>Collects and hashes in one call, for a source root and a parsed lock.</summary>
        public static string ComputeSourceContentHash(string sourceRootPath, JitterPhysicsLock lockFile)
        {
            if (lockFile == null)
            {
                throw new ArgumentNullException(nameof(lockFile));
            }

            IReadOnlyList<SourceInput> inputs = CollectInputs(
                sourceRootPath, lockFile.IncludedFiles, lockFile.ExcludedFiles);
            return ComputeSourceContentHash(inputs, lockFile.CompileProfileText);
        }

        /// <summary>
        /// Glob matching with rules fixed by this package rather than by a platform helper:
        /// <c>**/</c> matches zero or more leading directories, <c>**</c> matches anything,
        /// <c>*</c> matches anything except a separator, <c>?</c> matches one such character.
        /// </summary>
        public static bool GlobMatches(string relativePath, string pattern)
        {
            if (relativePath == null || pattern == null)
            {
                return false;
            }

            return CompileGlob(pattern).IsMatch(relativePath);
        }

        private static bool MatchesAny(string relativePath, IReadOnlyList<string> patterns)
        {
            for (int i = 0; i < patterns.Count; i++)
            {
                if (GlobMatches(relativePath, patterns[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static Regex CompileGlob(string pattern)
        {
            if (GlobCache.TryGetValue(pattern, out Regex cached))
            {
                return cached;
            }

            var builder = new StringBuilder(pattern.Length * 4);
            builder.Append('^');

            int index = 0;
            while (index < pattern.Length)
            {
                if (string.CompareOrdinal(pattern, index, "**/", 0, 3) == 0)
                {
                    builder.Append("(?:[^/]+/)*");
                    index += 3;
                }
                else if (string.CompareOrdinal(pattern, index, "**", 0, 2) == 0)
                {
                    builder.Append(".*");
                    index += 2;
                }
                else if (pattern[index] == '*')
                {
                    builder.Append("[^/]*");
                    index++;
                }
                else if (pattern[index] == '?')
                {
                    builder.Append("[^/]");
                    index++;
                }
                else
                {
                    builder.Append(Regex.Escape(pattern[index].ToString()));
                    index++;
                }
            }

            builder.Append('$');
            var regex = new Regex(builder.ToString(), RegexOptions.CultureInvariant);
            GlobCache[pattern] = regex;
            return regex;
        }

        private static string CanonicalRelativePath(string rootPath, string filePath)
        {
            string relative = filePath.Substring(rootPath.Length);
            relative = relative.Replace('\\', '/');
            return relative.TrimStart('/');
        }

        private static byte[] NormalizeContent(string relativePath, byte[] content)
        {
            string extension = Path.GetExtension(relativePath);
            if (!TextExtensions.Contains(extension))
            {
                return content;
            }

            // Line endings are normalized rather than trusted: a checkout with CRLF must
            // produce the same hash as a checkout with LF, otherwise the lock would fail on
            // Windows for reasons that have nothing to do with the sources.
            var utf8 = new UTF8Encoding(false);
            string text = utf8.GetString(content);
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return utf8.GetBytes(text);
        }

        private static void WriteAscii(Stream stream, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                stream.WriteByte((byte)value[i]);
            }
        }
    }
}

