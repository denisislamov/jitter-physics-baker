using System;
using System.Text;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Sanitizer for the identifiers that end up inside the deterministic artifact
    /// (<c>levelId</c>, <c>sourceId</c>) and in file names.
    /// <para>
    /// Identifiers must survive a byte-exact bake, so the result is restricted to ASCII
    /// lowercase letters, digits and <c>_</c>. Culture-dependent casing is avoided on
    /// purpose: <see cref="char.ToLowerInvariant(char)"/> keeps two machines with different
    /// locales in agreement.
    /// </para>
    /// </summary>
    public static class JitterPhysicsIdUtility
    {
        /// <summary>Maximum identifier length accepted by the codec and by file naming.</summary>
        public const int MaxLength = 64;

        /// <summary>
        /// Returns a canonical identifier, or <paramref name="fallback"/> when
        /// <paramref name="value"/> contains nothing usable.
        /// </summary>
        public static string Sanitize(string value, string fallback)
        {
            string sanitized = SanitizeCore(value);
            if (sanitized.Length != 0)
            {
                return sanitized;
            }

            string sanitizedFallback = SanitizeCore(fallback);
            return sanitizedFallback.Length != 0 ? sanitizedFallback : "unnamed";
        }

        /// <summary>
        /// Checks an identifier without rewriting it. The baker uses this to fail loudly
        /// instead of silently baking geometry under a different id than the author sees.
        /// </summary>
        public static bool IsCanonical(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (!IsAllowed(value[i]))
                {
                    return false;
                }
            }

            return value[0] != '_' && value[value.Length - 1] != '_';
        }

        private static string SanitizeCore(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Math.Min(value.Length, MaxLength));
            bool lastWasSeparator = false;
            for (int i = 0; i < value.Length && builder.Length < MaxLength; i++)
            {
                char lowered = char.ToLowerInvariant(value[i]);
                if (IsAllowed(lowered) && lowered != '_')
                {
                    builder.Append(lowered);
                    lastWasSeparator = false;
                    continue;
                }

                // Runs of separators collapse so that "Level 01 / Alpha" and
                // "Level_01__Alpha" cannot produce two different ids for one level.
                if (!lastWasSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                    lastWasSeparator = true;
                }
            }

            while (builder.Length > 0 && builder[builder.Length - 1] == '_')
            {
                builder.Length -= 1;
            }

            return builder.ToString();
        }

        private static bool IsAllowed(char value)
        {
            return (value >= 'a' && value <= 'z')
                || (value >= '0' && value <= '9')
                || value == '_';
        }
    }
}
