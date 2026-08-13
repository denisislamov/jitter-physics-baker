using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.Editor.Bootstrap;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Install
{
    /// <summary>
    /// Copies the engine-independent half of the package into a consumer's server project.
    /// <para>
    /// The package delivers server code as <em>sources</em>, not as a DLL, and the reason is
    /// concrete: Unity compiles Jitter2 into one assembly identity and a consumer's server may
    /// compile the same sources into another. A single precompiled Jitter-dependent binary
    /// cannot satisfy both without editing the server project, which is exactly what this
    /// delivery is supposed to avoid. An SDK-style project compiles whatever <c>.cs</c> is under
    /// its folder, so a copied projection needs no csproj change at all.
    /// </para>
    /// <para>
    /// The projection is generated and package-owned: it carries a manifest of file hashes, is
    /// updated in place, refuses to overwrite files somebody edited, and can be verified by the
    /// consumer's CI against the installed package version. A server that quietly runs last
    /// month's loader against this month's artifact is the failure this prevents.
    /// </para>
    /// </summary>
    public static class JitterPhysicsServerProjection
    {
        /// <summary>Name of the manifest written next to the projected sources.</summary>
        public const string ManifestFileName = "JitterPhysics.projection.json";

        private static readonly (string Source, string Target)[] Parts =
        {
            (Path.Combine("Runtime", "Contracts"), "Contracts"),
            (Path.Combine("Runtime", "ArtifactCodec"), "ArtifactCodec"),
            (Path.Combine("JitterIntegration~", "Runtime"), "Integration"),
        };

        /// <summary>Installs or updates the projection in <paramref name="targetFolder"/>.</summary>
        public static JitterPhysicsInstallResult Install(string targetFolder)
        {
            var issues = new JitterPhysicsIssueLog();

            if (string.IsNullOrEmpty(targetFolder))
            {
                issues.Error("No target folder was chosen.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            if (!TryCollect(out List<ProjectedFile> files, issues))
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            JitterPhysicsInstallReceipt receipt = JitterPhysicsInstallReceipt.Load(
                JitterPhysicsInstallReceipt.DefaultPath, out string receiptError);

            if (!string.IsNullOrEmpty(receiptError))
            {
                issues.Error(receiptError);
                return new JitterPhysicsInstallResult(null, issues);
            }

            JitterPhysicsInstalledComponent existing = receipt.Component(JitterPhysicsComponentIds.ServerRuntime);
            if (existing != null
                && string.Equals(Normalize(existing.Root), Normalize(targetFolder), StringComparison.Ordinal)
                && !VerifyUnmodified(existing, issues))
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            var written = new List<string>(files.Count + 1);
            var recorded = new List<JitterPhysicsInstalledFile>(files.Count + 1);

            try
            {
                if (existing != null)
                {
                    RemoveStale(existing, files, targetFolder, issues);
                }

                for (int i = 0; i < files.Count; i++)
                {
                    string path = Path.Combine(targetFolder, files[i].RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    WriteAtomic(path, files[i].Content);

                    written.Add(path);
                    recorded.Add(new JitterPhysicsInstalledFile(files[i].RelativePath, files[i].Hash));
                }

                string manifest = BuildManifest(files);
                string manifestPath = Path.Combine(targetFolder, ManifestFileName);
                WriteAtomic(manifestPath, Encoding.UTF8.GetBytes(manifest));

                written.Add(manifestPath);
                recorded.Add(new JitterPhysicsInstalledFile(
                    ManifestFileName, JitterPhysicsHash.Sha256HexUtf8(manifest)));
            }
            catch (Exception exception)
            {
                issues.Error("Projection failed: " + exception.Message);
                return new JitterPhysicsInstallResult(null, issues);
            }

            receipt
                .With(new JitterPhysicsInstalledComponent(
                    JitterPhysicsComponentIds.ServerRuntime,
                    JitterPhysicsOwnership.Package,
                    Normalize(targetFolder),
                    JitterPhysicsPackage.PackageVersion,
                    JitterPhysicsCompatibilityReport.Create().RuntimeCompatibilityId,
                    recorded))
                .Save(JitterPhysicsInstallReceipt.DefaultPath);

            AssetDatabase.Refresh();

            issues.Warning(
                "The projection is generated code. Edit the package and re-run this command; a local "
                + "edit here is invisible to everyone who installs the package.");

            return new JitterPhysicsInstallResult(written, issues);
        }

        /// <summary>
        /// Compares an installed projection with this package. Meant for a consumer's CI: it
        /// answers "is the server built from the same sources the client bakes with" without
        /// starting Unity's editor UI.
        /// </summary>
        public static JitterPhysicsInstallResult Verify(string targetFolder)
        {
            var issues = new JitterPhysicsIssueLog();

            if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
            {
                issues.Error($"No projection at '{targetFolder}'.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            if (!TryCollect(out List<ProjectedFile> files, issues))
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            var checkedFiles = new List<string>(files.Count);

            for (int i = 0; i < files.Count; i++)
            {
                string path = Path.Combine(targetFolder, files[i].RelativePath);
                checkedFiles.Add(path);

                if (!File.Exists(path))
                {
                    issues.Error($"'{files[i].RelativePath}' is missing from the projection.");
                    continue;
                }

                if (!JitterPhysicsHash.HexEquals(
                    JitterPhysicsHash.Sha256Hex(File.ReadAllBytes(path)), files[i].Hash))
                {
                    issues.Error(
                        $"'{files[i].RelativePath}' differs from the package. The server would build a "
                        + "different loader than the one this project bakes for.");
                }
            }

            string manifestPath = Path.Combine(targetFolder, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                issues.Error("The projection manifest is missing; this folder was not written by the package.");
            }

            if (!issues.HasErrors)
            {
                issues.Warning(
                    $"Projection matches package {JitterPhysicsPackage.PackageVersion}: {files.Count} files.");
            }

            return new JitterPhysicsInstallResult(checkedFiles, issues);
        }

        private static bool TryCollect(out List<ProjectedFile> files, JitterPhysicsIssueLog issues)
        {
            files = new List<ProjectedFile>();

            string packageRoot = JitterPhysicsCompatibilityReport.ResolvePackageRootPath();
            if (string.IsNullOrEmpty(packageRoot))
            {
                issues.Error("The package root could not be resolved.");
                return false;
            }

            for (int p = 0; p < Parts.Length; p++)
            {
                string sourceFolder = Path.Combine(packageRoot, Parts[p].Source);
                if (!Directory.Exists(sourceFolder))
                {
                    issues.Error($"'{Parts[p].Source}' is missing from the package.");
                    return false;
                }

                foreach (string file in Directory.GetFiles(sourceFolder, "*.cs", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(sourceFolder.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');

                    byte[] content = File.ReadAllBytes(file);
                    string text = Encoding.UTF8.GetString(content);

                    // The invariant this delivery depends on, checked instead of assumed: a
                    // UnityEngine reference here would not fail in Unity, only later, in a
                    // consumer's server build that has no engine at all.
                    if (text.Contains("using UnityEngine") || text.Contains("using UnityEditor"))
                    {
                        issues.Error(
                            $"'{Parts[p].Source}/{relative}' references Unity and cannot be projected "
                            + "into a server. This is a bug in the package layout.");
                        return false;
                    }

                    files.Add(new ProjectedFile(
                        Parts[p].Target + "/" + relative,
                        content,
                        JitterPhysicsHash.Sha256Hex(content)));
                }
            }

            files.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
            return files.Count > 0;
        }

        private static string BuildManifest(List<ProjectedFile> files)
        {
            var builder = new StringBuilder(files.Count * 96 + 256);
            builder.Append("{\n");
            builder.Append("  \"schemaVersion\": 1,\n");
            builder.Append("  \"package\": \"").Append(JitterPhysicsPackage.PackageName).Append("\",\n");
            builder.Append("  \"packageVersion\": \"").Append(JitterPhysicsPackage.PackageVersion).Append("\",\n");
            builder.Append("  \"artifactSchemaVersion\": ")
                .Append(JitterPhysicsPackage.ArtifactSchemaVersion.ToString(CultureInfo.InvariantCulture))
                .Append(",\n");
            builder.Append("  \"files\": [\n");

            for (int i = 0; i < files.Count; i++)
            {
                builder.Append("    { \"path\": \"").Append(files[i].RelativePath)
                    .Append("\", \"hash\": \"").Append(files[i].Hash).Append("\" }")
                    .Append(i == files.Count - 1 ? "\n" : ",\n");
            }

            builder.Append("  ]\n");
            builder.Append("}\n");
            return builder.ToString();
        }

        private static bool VerifyUnmodified(
            JitterPhysicsInstalledComponent component,
            JitterPhysicsIssueLog issues)
        {
            var modified = new List<string>();

            for (int i = 0; i < component.Files.Count; i++)
            {
                string path = Path.Combine(component.Root, component.Files[i].RelativePath);
                if (File.Exists(path)
                    && !JitterPhysicsHash.HexEquals(
                        JitterPhysicsHash.Sha256Hex(File.ReadAllBytes(path)), component.Files[i].Hash))
                {
                    modified.Add(path);
                }
            }

            if (modified.Count == 0)
            {
                return true;
            }

            issues.Error(
                "These projected files were modified after installation, so the update was refused:\n"
                + string.Join("\n", modified));

            return false;
        }

        private static void RemoveStale(
            JitterPhysicsInstalledComponent existing,
            List<ProjectedFile> incoming,
            string targetFolder,
            JitterPhysicsIssueLog issues)
        {
            var current = new HashSet<string>(StringComparer.Ordinal) { ManifestFileName };
            for (int i = 0; i < incoming.Count; i++)
            {
                current.Add(incoming[i].RelativePath);
            }

            for (int i = 0; i < existing.Files.Count; i++)
            {
                JitterPhysicsInstalledFile file = existing.Files[i];
                if (current.Contains(file.RelativePath))
                {
                    continue;
                }

                string path = Path.Combine(targetFolder, file.RelativePath);
                if (!File.Exists(path))
                {
                    continue;
                }

                if (JitterPhysicsHash.HexEquals(
                    JitterPhysicsHash.Sha256Hex(File.ReadAllBytes(path)), file.Hash))
                {
                    File.Delete(path);
                }
                else
                {
                    issues.Warning($"'{path}' is no longer part of the package but was modified, so it was kept.");
                }
            }
        }

        private static void WriteAtomic(string path, byte[] content)
        {
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, content);

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        private static string Normalize(string folder)
        {
            return folder.Replace('\\', '/').TrimEnd('/');
        }

        private readonly struct ProjectedFile
        {
            internal ProjectedFile(string relativePath, byte[] content, string hash)
            {
                RelativePath = relativePath;
                Content = content;
                Hash = hash;
            }

            internal string RelativePath { get; }

            internal byte[] Content { get; }

            internal string Hash { get; }
        }
    }

    /// <summary>Menu entries for the server projection.</summary>
    internal static class JitterPhysicsServerProjectionMenu
    {
        private const string Root = Authoring.JitterPhysicsAuthoringConstants.EditorMenuRoot;

        [MenuItem(Root + "Install/Install Server Runtime Sources...", false, 123)]
        private static void Install()
        {
            string folder = EditorUtility.SaveFolderPanel(
                "Install server runtime sources into", string.Empty, "JitterPhysics");

            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            JitterPhysicsInstallMenu.Report(JitterPhysicsServerProjection.Install(folder));
        }

        [MenuItem(Root + "Install/Verify Server Runtime Sources...", false, 124)]
        private static void Verify()
        {
            string folder = EditorUtility.OpenFolderPanel(
                "Verify server runtime sources in", string.Empty, string.Empty);

            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            JitterPhysicsInstallMenu.Report(JitterPhysicsServerProjection.Verify(folder));
        }
    }
}

