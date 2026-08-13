using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace DataSakura.JitterPhysics.Authoring
{
    /// <summary>
    /// Explicit marker for the root of one static body.
    /// <para>
    /// Only colliders under a marked source are baked. Collecting every collider in the
    /// scene would be more convenient and considerably worse: a designer could not tell what
    /// ends up in the artifact, and adding an unrelated trigger or a decorative collider
    /// would silently change the level's hash and break the client/server handshake.
    /// </para>
    /// </summary>
    [AddComponentMenu(
        JitterPhysicsAuthoringConstants.ComponentMenuRoot + "Jitter Static Body Source",
        JitterPhysicsAuthoringConstants.SourceMenuOrder)]
    [DisallowMultipleComponent]
    public sealed class JitterStaticBodySource : MonoBehaviour
    {
        [SerializeField]
        [Tooltip(
            "Stable identifier of this body. It orders bodies in the artifact and therefore "
            + "decides creation order in the rebuilt world. Generated once and then kept: "
            + "renaming the object must not change the artifact.")]
        private string sourceId = string.Empty;

        [SerializeField]
        [Tooltip("Include colliders on child objects, not only on this one.")]
        private bool includeChildren = true;

        [Header("Material")]
        [SerializeField]
        [Range(0f, 1f)]
        private float friction = 0.2f;

        [SerializeField]
        [Range(0f, 1f)]
        private float restitution;

        /// <summary>Stable identifier of this body inside its level.</summary>
        public string SourceId => sourceId;

        /// <summary>Whether colliders on child objects are part of this body.</summary>
        public bool IncludeChildren => includeChildren;

        /// <summary>
        /// Inactive objects are never baked. A designer disables an object to take it out of
        /// the level, and an invisible wall that still blocks movement is the exact bug this
        /// rule prevents.
        /// </summary>
        public const bool IncludeInactiveChildren = false;

        /// <summary>Friction applied to the created body.</summary>
        public float Friction => friction;

        /// <summary>Restitution applied to the created body.</summary>
        public float Restitution => restitution;

        /// <summary>True when the identifier is usable by the baker as it stands.</summary>
        public bool HasCanonicalSourceId => JitterPhysicsIdUtility.IsCanonical(sourceId);

        /// <summary>
        /// Assigns a canonical identifier derived from the object name, keeping any existing
        /// one. Used by the inspector and by the repair path of the validator.
        /// </summary>
        public string EnsureSourceId()
        {
            if (HasCanonicalSourceId)
            {
                return sourceId;
            }

            sourceId = JitterPhysicsIdUtility.Sanitize(
                string.IsNullOrEmpty(sourceId) ? name : sourceId,
                "static_body");
            return sourceId;
        }

        /// <summary>Overwrites the identifier with a canonical form of the given value.</summary>
        public void SetSourceId(string value)
        {
            sourceId = JitterPhysicsIdUtility.Sanitize(value, "static_body");
        }

        private void Reset()
        {
            // A brand new component gets an id immediately, so that a source is never baked
            // under a generated placeholder that changes on the next domain reload.
            sourceId = JitterPhysicsIdUtility.Sanitize(name, "static_body");
        }
    }
}

