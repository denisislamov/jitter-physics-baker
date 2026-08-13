using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Baking
{
    /// <summary>
    /// Builds the stable key that identifies one collider inside its static body.
    /// <para>
    /// The key decides shape order in the artifact, so it has to be derived from something
    /// that does not change between two bakes of an unchanged scene. Instance ids, hash map
    /// enumeration order and <c>FindObjectsByType</c> ordering all fail that requirement:
    /// they are stable within a session and arbitrary across sessions, which produces an
    /// artifact whose hash changes for no visible reason.
    /// </para>
    /// <para>
    /// What is used instead is the collider's structural position: the path from the body
    /// root, the sibling index of every step, the index of the component on its object and
    /// the collider type. Two colliders can only collide in this key if they occupy the same
    /// place in the hierarchy, which is impossible.
    /// </para>
    /// </summary>
    public static class JitterPhysicsColliderKey
    {
        /// <summary>Separator between path steps; never valid inside a sanitized name.</summary>
        private const char StepSeparator = '/';

        /// <summary>
        /// Returns the canonical key of <paramref name="collider"/> relative to
        /// <paramref name="bodyRoot"/>.
        /// </summary>
        public static string Build(Transform bodyRoot, Collider collider)
        {
            var builder = new StringBuilder(64);
            AppendPath(builder, bodyRoot, collider.transform);

            builder.Append(StepSeparator)
                .Append(TypeTag(collider))
                .Append('#')
                .Append(ComponentIndex(collider).ToString(CultureInfo.InvariantCulture));

            return builder.ToString();
        }

        /// <summary>
        /// Appends the path from the body root to the collider's transform. The sibling index
        /// is part of every step, so two children with the same name stay distinguishable.
        /// </summary>
        private static void AppendPath(StringBuilder builder, Transform bodyRoot, Transform target)
        {
            var steps = new List<Transform>();
            for (Transform current = target; current != null && current != bodyRoot; current = current.parent)
            {
                steps.Add(current);
            }

            // Collected child-to-root, emitted root-to-child, so the key reads in the same
            // direction as the hierarchy.
            for (int i = steps.Count - 1; i >= 0; i--)
            {
                Transform step = steps[i];
                builder.Append(StepSeparator)
                    .Append(step.GetSiblingIndex().ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(Sanitize(step.name));
            }
        }

        /// <summary>
        /// Index of the collider among the colliders of its own game object. A single object
        /// may carry several colliders of the same type, and they must not share a key.
        /// </summary>
        private static int ComponentIndex(Collider collider)
        {
            Collider[] onObject = collider.gameObject.GetComponents<Collider>();
            for (int i = 0; i < onObject.Length; i++)
            {
                if (ReferenceEquals(onObject[i], collider))
                {
                    return i;
                }
            }

            return 0;
        }

        private static string TypeTag(Collider collider)
        {
            switch (collider)
            {
                case BoxCollider _:
                    return "box";
                case SphereCollider _:
                    return "sphere";
                case CapsuleCollider _:
                    return "capsule";
                case MeshCollider _:
                    return "mesh";
                default:
                    return "unsupported";
            }
        }

        /// <summary>
        /// Reduces a name to characters that survive the artifact's bounded UTF-8 strings and
        /// cannot be confused with the key's own separators.
        /// </summary>
        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "unnamed";
            }

            var builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length && builder.Length < 48; i++)
            {
                char character = char.ToLowerInvariant(name[i]);
                bool allowed = (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9')
                    || character == '_';
                builder.Append(allowed ? character : '_');
            }

            return builder.Length == 0 ? "unnamed" : builder.ToString();
        }
    }
}

