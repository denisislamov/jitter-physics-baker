using System;
using System.IO;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Export
{
    /// <summary>What an export produced.</summary>
    public sealed class JitterPhysicsExportResult
    {
        /// <summary>Files written, in the order they were written.</summary>
        public string[] Files { get; }

        /// <summary>Everything the export wants to say; errors mean nothing was written.</summary>
        public JitterPhysicsIssueLog Issues { get; }

        internal JitterPhysicsExportResult(string[] files, JitterPhysicsIssueLog issues)
        {
            Files = files ?? Array.Empty<string>();
            Issues = issues;
        }

        /// <summary>True when the export completed.</summary>
        public bool Succeeded => Files.Length > 0 && !Issues.HasErrors;
    }

    /// <summary>
    /// Copies a baked artifact out of the project, for a server that is not this Unity project.
    /// <para>
    /// Nothing here bakes. Both exports read the artifact that already exists, verify it, and
    /// then write it out unchanged. That is the whole point: the server has to run the exact
    /// bytes the client has, and a convenience "re-bake while exporting" would silently replace
    /// them with bytes nobody verified — identical in the good case, and undetectably different
    /// in the case that matters.
    /// </para>
    /// </summary>
    public static class JitterPhysicsArtifactExporter
    {
        /// <summary>
        /// Writes the payload and its manifest into <paramref name="targetFolder"/> under their
        /// canonical names. This is the delivery form <c>FilePhysicsArtifactProvider</c> reads.
        /// </summary>
        public static JitterPhysicsExportResult ExportBinary(
            JitterPhysicsArtifactAsset asset,
            string targetFolder)
        {
            var issues = new JitterPhysicsIssueLog();

            if (!TryRead(asset, issues, out byte[] payload, out PhysicsArtifactManifest manifest)
                || !TryPrepareFolder(targetFolder, issues))
            {
                return new JitterPhysicsExportResult(null, issues);
            }

            string payloadPath = Path.Combine(
                targetFolder,
                JitterPhysicsArtifactNaming.BinaryFileName(manifest.LevelId, manifest.ArtifactHash));

            string manifestPath = Path.Combine(
                targetFolder,
                JitterPhysicsArtifactNaming.ManifestFileName(manifest.LevelId, manifest.ArtifactHash));

            try
            {
                WriteAtomic(payloadPath, payload);
                WriteAtomic(manifestPath, PhysicsArtifactManifestCodec.Write(manifest));
            }
            catch (Exception exception)
            {
                issues.Error("Export failed: " + exception.Message, asset);
                return new JitterPhysicsExportResult(null, issues);
            }

            return new JitterPhysicsExportResult(new[] { payloadPath, manifestPath }, issues);
        }

        /// <summary>
        /// Writes a generated C# provider carrying the exact payload into
        /// <paramref name="targetFolder"/>. For consumers whose build files must not change: an
        /// SDK-style project compiles the file simply because it is there.
        /// </summary>
        public static JitterPhysicsExportResult ExportEmbedded(
            JitterPhysicsArtifactAsset asset,
            string targetFolder,
            EmbeddedArtifactSourceOptions options)
        {
            var issues = new JitterPhysicsIssueLog();

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!TryRead(asset, issues, out byte[] payload, out PhysicsArtifactManifest manifest)
                || !TryPrepareFolder(targetFolder, issues))
            {
                return new JitterPhysicsExportResult(null, issues);
            }

            EmbeddedArtifactSource source;
            try
            {
                source = EmbeddedArtifactSourceGenerator.Generate(payload, manifest, options);
            }
            catch (ArgumentException exception)
            {
                issues.Error(exception.Message, asset);
                return new JitterPhysicsExportResult(null, issues);
            }

            // Prove the generated chunks restore the exact bytes before anybody compiles them.
            // A provider that fails at server startup is found hours later; this is found now.
            byte[] restored = EmbeddedPhysicsArtifactProvider.Restore(source.Chunks);
            if (!JitterPhysicsHash.HexEquals(JitterPhysicsHash.Sha256Hex(restored), manifest.ArtifactHash))
            {
                issues.Error(
                    "The generated source does not restore the artifact it was made from. "
                    + "Nothing was written; this is a bug in the exporter.",
                    asset);
                return new JitterPhysicsExportResult(null, issues);
            }

            string path = Path.Combine(targetFolder, source.FileName);

            try
            {
                WriteAtomic(path, source.Code);
            }
            catch (Exception exception)
            {
                issues.Error("Export failed: " + exception.Message, asset);
                return new JitterPhysicsExportResult(null, issues);
            }

            issues.Warning(
                $"Embedded {payload.Length} bytes of level '{manifest.LevelId}' into generated source. "
                + "Embedding is for proof-of-concept and small levels: every level change is a server "
                + "recompile.",
                asset);

            return new JitterPhysicsExportResult(new[] { path }, issues);
        }

        private static bool TryRead(
            JitterPhysicsArtifactAsset asset,
            JitterPhysicsIssueLog issues,
            out byte[] payload,
            out PhysicsArtifactManifest manifest)
        {
            payload = null;
            manifest = null;

            if (asset == null)
            {
                issues.Error("No artifact was selected.");
                return false;
            }

            if (!asset.HasPayload)
            {
                issues.Error($"Artifact '{asset.name}' has no payload; re-bake the level.", asset);
                return false;
            }

            payload = asset.GetPayloadBytes();

            string payloadPath = AssetDatabase.GetAssetPath(asset.Payload);
            string folder = Path.GetDirectoryName(payloadPath);
            string manifestPath = string.IsNullOrEmpty(folder)
                ? null
                : Path.Combine(
                    folder,
                    JitterPhysicsArtifactNaming.ManifestFileName(asset.LevelId, asset.ArtifactHash));

            if (manifestPath == null || !File.Exists(manifestPath))
            {
                // The manifest is not reconstructed from the asset's fields on purpose: those
                // are copies, and exporting a manifest this project invented rather than the one
                // the bake wrote would break the cross-check it exists for.
                issues.Error(
                    $"The manifest of '{asset.LevelId}' is missing next to its payload; re-bake the level.",
                    asset);
                return false;
            }

            manifest = PhysicsArtifactManifestCodec.Read(File.ReadAllText(manifestPath), out string manifestError);
            if (manifest == null)
            {
                issues.Error($"Manifest '{manifestPath}' could not be read: {manifestError}", asset);
                return false;
            }

            PhysicsArtifactResult verification = PhysicsArtifactReader.Read(
                payload, manifest.ArtifactHash, manifest);

            if (!verification.Succeeded)
            {
                issues.Error(
                    "Refusing to export an artifact that does not decode: " + verification.Error, asset);
                return false;
            }

            return true;
        }

        private static bool TryPrepareFolder(string targetFolder, JitterPhysicsIssueLog issues)
        {
            if (string.IsNullOrEmpty(targetFolder))
            {
                issues.Error("No export folder was chosen.");
                return false;
            }

            try
            {
                Directory.CreateDirectory(targetFolder);
                return true;
            }
            catch (Exception exception)
            {
                issues.Error($"Export folder '{targetFolder}' is not usable: {exception.Message}");
                return false;
            }
        }

        private static void WriteAtomic(string path, byte[] content)
        {
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, content);
            Replace(temporary, path);
        }

        private static void WriteAtomic(string path, string content)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
            Replace(temporary, path);
        }

        private static void Replace(string temporary, string path)
        {
            // Written aside and moved into place, so an interrupted export cannot leave half a
            // file where a server expects a whole one.
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }
    }

    /// <summary>Menu entries for the exports.</summary>
    internal static class JitterPhysicsExportMenu
    {
        private const string ExportBinaryPath =
            Authoring.JitterPhysicsAuthoringConstants.EditorMenuRoot + "Export Selected Artifact...";

        private const string ExportEmbeddedPath =
            Authoring.JitterPhysicsAuthoringConstants.EditorMenuRoot + "Export Embedded Server Artifact...";

        [MenuItem(ExportBinaryPath, false, 30)]
        private static void ExportBinary()
        {
            JitterPhysicsArtifactAsset asset = Selection.activeObject as JitterPhysicsArtifactAsset;
            if (asset == null)
            {
                Debug.LogError(
                    JitterPhysicsPackage.LogPrefix + "Select a baked artifact asset first.");
                return;
            }

            string folder = EditorUtility.SaveFolderPanel("Export artifact to", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            Report(JitterPhysicsArtifactExporter.ExportBinary(asset, folder));
        }

        [MenuItem(ExportEmbeddedPath, false, 31)]
        private static void ExportEmbedded()
        {
            JitterPhysicsArtifactAsset asset = Selection.activeObject as JitterPhysicsArtifactAsset;
            if (asset == null)
            {
                Debug.LogError(
                    JitterPhysicsPackage.LogPrefix + "Select a baked artifact asset first.");
                return;
            }

            string folder = EditorUtility.SaveFolderPanel(
                "Export generated provider to", string.Empty, string.Empty);

            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            var options = new EmbeddedArtifactSourceOptions(
                JitterPhysicsExportDefaults.GeneratedNamespace,
                JitterPhysicsExportDefaults.ClassNameFor(asset.LevelId));

            Report(JitterPhysicsArtifactExporter.ExportEmbedded(asset, folder, options));
        }

        private static void Report(JitterPhysicsExportResult result)
        {
            for (int i = 0; i < result.Issues.Issues.Count; i++)
            {
                JitterPhysicsIssue issue = result.Issues.Issues[i];
                if (issue.IsError)
                {
                    Debug.LogError(JitterPhysicsPackage.LogPrefix + issue.Message, issue.Context);
                }
                else
                {
                    Debug.LogWarning(JitterPhysicsPackage.LogPrefix + issue.Message, issue.Context);
                }
            }

            if (result.Succeeded)
            {
                Debug.Log(
                    JitterPhysicsPackage.LogPrefix + "Exported: " + string.Join(", ", result.Files));
            }
        }
    }

    /// <summary>Defaults the export UI starts from; every one of them is overridable.</summary>
    public static class JitterPhysicsExportDefaults
    {
        /// <summary>Namespace of a generated provider.</summary>
        public const string GeneratedNamespace = "DataSakura.JitterPhysics.Generated";

        /// <summary>Turns a canonical level id into a class name.</summary>
        public static string ClassNameFor(string levelId)
        {
            if (string.IsNullOrEmpty(levelId))
            {
                return "PhysicsArtifact";
            }

            var builder = new System.Text.StringBuilder(levelId.Length + 8);
            bool capitalize = true;

            for (int i = 0; i < levelId.Length; i++)
            {
                char character = levelId[i];
                if (!char.IsLetterOrDigit(character))
                {
                    capitalize = true;
                    continue;
                }

                builder.Append(capitalize ? char.ToUpperInvariant(character) : character);
                capitalize = char.IsDigit(character);
            }

            if (builder.Length == 0 || char.IsDigit(builder[0]))
            {
                builder.Insert(0, "Level");
            }

            return builder.Append("Artifact").ToString();
        }
    }
}
