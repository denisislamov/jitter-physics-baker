using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.UnityArtifact
{
    /// <summary>
    /// Project-relative locations the baker writes to. Kept out of the portable assemblies
    /// because a dedicated server has no <c>Assets/</c> folder.
    /// </summary>
    public static class JitterPhysicsArtifactPaths
    {
        /// <summary>Default folder for baked client artifacts.</summary>
        public const string DefaultGeneratedFolder = "Assets/Generated/JitterPhysics";

        /// <summary>Folder the installer owns; a receipt next to it records what it wrote.</summary>
        public const string DefaultInstallFolder = "Assets/DataSakura/JitterPhysics";

        /// <summary>Default destination of the fallback Jitter2 copy for projects without one.</summary>
        public const string DefaultJitterFolder = "Assets/DataSakura/ThirdParty/Jitter2";

        /// <summary>Default destination of the installed Jitter integration assembly.</summary>
        public const string DefaultIntegrationFolder = "Assets/DataSakura/JitterPhysics/Integration";

        /// <summary>Installation receipt used by update and uninstall.</summary>
        public const string InstallationReceiptPath =
            "Assets/DataSakura/JitterPhysics/InstallationReceipt.json";

        /// <summary>Asset path of the artifact ScriptableObject for a level.</summary>
        public static string ArtifactAssetPath(string generatedFolder, string levelId)
        {
            return Combine(generatedFolder, levelId + ".artifact.asset");
        }

        /// <summary>Asset path of the binary payload for a level and hash.</summary>
        public static string BinaryAssetPath(string generatedFolder, string levelId, string artifactHash)
        {
            return Combine(
                generatedFolder,
                JitterPhysicsArtifactNaming.BinaryFileName(levelId, artifactHash));
        }

        /// <summary>Asset path of the manifest for a level and hash.</summary>
        public static string ManifestAssetPath(string generatedFolder, string levelId, string artifactHash)
        {
            return Combine(
                generatedFolder,
                JitterPhysicsArtifactNaming.ManifestFileName(levelId, artifactHash));
        }

        private static string Combine(string folder, string fileName)
        {
            string trimmed = string.IsNullOrEmpty(folder)
                ? DefaultGeneratedFolder
                : folder.TrimEnd('/', '\\');

            // Unity asset paths are always '/' separated, on every platform.
            return trimmed + "/" + fileName;
        }
    }
}
