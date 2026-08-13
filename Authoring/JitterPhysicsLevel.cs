using System.Collections.Generic;
using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace DataSakura.JitterPhysics.Authoring
{
    /// <summary>
    /// The level being baked: its identity, where its geometry lives and which world
    /// settings it is baked for.
    /// <para>
    /// Exactly one of these is expected per scene. Zero means nothing can be baked; more
    /// than one means the artifact identity is ambiguous, and an ambiguous <c>levelId</c> is
    /// what makes a client load a different map than the server built.
    /// </para>
    /// </summary>
    [AddComponentMenu(
        JitterPhysicsAuthoringConstants.ComponentMenuRoot + "Jitter Physics Level",
        JitterPhysicsAuthoringConstants.LevelMenuOrder)]
    [DisallowMultipleComponent]
    public sealed class JitterPhysicsLevel : MonoBehaviour
    {
        [SerializeField]
        [Tooltip(
            "Identifier of this level. It names the artifact and is compared during the "
            + "client/server handshake, so it must not change casually.")]
        private string levelId = string.Empty;

        [SerializeField]
        [Tooltip(
            "Root of the static geometry. Only sources under this object are collected; "
            + "leave empty to search the whole scene for explicitly marked sources.")]
        private Transform geometryRoot;

        [SerializeField]
        [Tooltip("World settings this level is baked for. Required.")]
        private JitterPhysicsWorldProfile worldProfile;

        [SerializeField]
        [Tooltip("Folder the baked artifact is written to.")]
        private string generatedFolder = UnityArtifact.JitterPhysicsArtifactPaths.DefaultGeneratedFolder;

        [SerializeField]
        [HideInInspector]
        [Tooltip("Hash of the artifact this level was last baked into, for diagnostics only.")]
        private string lastArtifactHash = string.Empty;

        /// <summary>Identifier of this level.</summary>
        public string LevelId => levelId;

        /// <summary>Root of the static geometry, or <c>null</c> to search the whole scene.</summary>
        public Transform GeometryRoot => geometryRoot;

        /// <summary>World settings this level is baked for.</summary>
        public JitterPhysicsWorldProfile WorldProfile => worldProfile;

        /// <summary>Folder the baked artifact is written to.</summary>
        public string GeneratedFolder =>
            string.IsNullOrEmpty(generatedFolder)
                ? UnityArtifact.JitterPhysicsArtifactPaths.DefaultGeneratedFolder
                : generatedFolder;

        /// <summary>Hash of the artifact this level was last baked into; diagnostics only.</summary>
        public string LastArtifactHash => lastArtifactHash;

        /// <summary>True when the identifier is usable by the baker as it stands.</summary>
        public bool HasCanonicalLevelId => JitterPhysicsIdUtility.IsCanonical(levelId);

        /// <summary>Assigns a canonical identifier derived from the object name, keeping any existing one.</summary>
        public string EnsureLevelId()
        {
            if (HasCanonicalLevelId)
            {
                return levelId;
            }

            levelId = JitterPhysicsIdUtility.Sanitize(
                string.IsNullOrEmpty(levelId) ? gameObject.scene.name : levelId,
                "level");
            return levelId;
        }

        /// <summary>Records the hash of the artifact produced by the last successful bake.</summary>
        public void SetLastArtifactHash(string value)
        {
            lastArtifactHash = value ?? string.Empty;
        }

        /// <summary>
        /// Collects the static body sources of this level, without ordering them. Ordering
        /// is the baker's responsibility, because it has to be canonical rather than
        /// whatever order the scene hierarchy happens to produce.
        /// </summary>
        public IReadOnlyList<JitterStaticBodySource> CollectSources()
        {
            var result = new List<JitterStaticBodySource>();

            if (geometryRoot != null)
            {
                geometryRoot.GetComponentsInChildren(
                    JitterStaticBodySource.IncludeInactiveChildren, result);
                return result;
            }

            // Without a geometry root the whole scene is searched, but still only for
            // explicitly marked sources: an unmarked collider is never baked by accident.
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                var inRoot = new List<JitterStaticBodySource>();
                root.GetComponentsInChildren(JitterStaticBodySource.IncludeInactiveChildren, inRoot);
                result.AddRange(inRoot);
            }

            return result;
        }

        private void Reset()
        {
            levelId = JitterPhysicsIdUtility.Sanitize(gameObject.scene.name, "level");
            geometryRoot = transform;
        }
    }
}

