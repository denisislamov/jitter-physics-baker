using System;
using System.Collections.Generic;
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
    /// <summary>Outcome of an install, update or removal.</summary>
    public sealed class JitterPhysicsInstallResult
    {
        /// <summary>Project-relative paths that were written or removed.</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>Everything the operation has to say; an error means nothing was changed.</summary>
        public JitterPhysicsIssueLog Issues { get; }

        internal JitterPhysicsInstallResult(IReadOnlyList<string> files, JitterPhysicsIssueLog issues)
        {
            Files = files ?? Array.Empty<string>();
            Issues = issues;
        }

        /// <summary>True when the operation completed.</summary>
        public bool Succeeded => !Issues.HasErrors;
    }

    /// <summary>
    /// Copies package-owned sources into the project, and takes them back out again.
    /// <para>
    /// Two rules shape all of it. First, an external Jitter2 always wins: if the project already
    /// has one, the package references it by assembly name and never copies, moves or edits it.
    /// A tool that "helpfully" replaces a consumer's physics engine has destroyed months of
    /// local changes. Second, nothing is overwritten unless the receipt says the package wrote
    /// it and it has not been touched since; a modified file stops the operation and is reported
    /// by path.
    /// </para>
    /// <para>
    /// Every write goes through a staging folder and is moved into place, so an interrupted
    /// install leaves either the old state or the new one, never half of each.
    /// </para>
    /// </summary>
    public static class JitterPhysicsInstaller
    {
        /// <summary>Where the fallback Jitter2 copy goes.</summary>
        public const string DefaultJitterFolder = "Assets/DataSakura/ThirdParty/Jitter2";

        /// <summary>Where the Jitter-dependent adapter goes.</summary>
        public const string DefaultIntegrationFolder = "Assets/DataSakura/JitterPhysics/Integration";

        private const string JitterAsmdefName = "Jitter2.Core.asmdef";
        private const string IntegrationAsmdefName = "DataSakura.JitterPhysics.JitterIntegration.asmdef";

        /// <summary>
        /// Installs or updates the dormant Jitter2 snapshot. Refused when the project already has
        /// a Jitter2 the package does not own.
        /// </summary>
        public static JitterPhysicsInstallResult InstallJitter(string targetFolder = null)
        {
            var issues = new JitterPhysicsIssueLog();
            targetFolder = Normalize(targetFolder ?? DefaultJitterFolder);

            if (RefuseInPlayMode(issues))
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            string packageRoot = JitterPhysicsCompatibilityReport.ResolvePackageRootPath();
            if (string.IsNullOrEmpty(packageRoot))
            {
                issues.Error("The package root could not be resolved, so there is nothing to copy from.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            JitterPhysicsInstallReceipt receipt = LoadReceipt(issues);
            if (issues.HasErrors)
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            JitterPhysicsInstalledComponent existing = receipt.Component(JitterPhysicsComponentIds.Jitter);
            JitterPhysicsCompatibilityReport compatibility = JitterPhysicsCompatibilityReport.Create();

            if (existing == null && compatibility.Status != JitterPhysicsCompatibilityStatus.Missing)
            {
                // Not an error the user can be talked out of: the project has its own copy, and
                // that copy is the one the package is supposed to bake against.
                issues.Error(
                    "This project already has a Jitter2.Core that the package did not install, so "
                    + "the fallback copy is not needed and will not be added. " + compatibility.Message);
                return new JitterPhysicsInstallResult(null, issues);
            }

            string sourceFolder = Path.Combine(packageRoot, "Jitter2~", "Runtime");
            string templatePath = Path.Combine(
                packageRoot, "Jitter2~", "StandaloneUnity", "Jitter2.Core.asmdef.template.json");

            if (!Directory.Exists(sourceFolder) || !File.Exists(templatePath))
            {
                issues.Error(
                    "The dormant Jitter2 snapshot is missing from the package; run tools~/sync-jitter2.py.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            issues.Warning(
                "The snapshot in this package release is unpatched upstream Jitter2: it has no "
                + "JITTER_UNITY define and uses hardware intrinsics, so it is verified under .NET "
                + "but not yet validated as a Unity fallback. See Jitter2~/PATCHES.md.");

            // Upstream Jitter2 uses C# 10+ syntax (file-scoped namespaces, collection
            // expressions), while Unity compiles an asmdef assembly at its default language
            // version. An assembly-scoped csc.rsp raises it just for this folder, which is the
            // minimum that lets the snapshot compile at all; it changes syntax acceptance, not
            // semantics, so it does not affect the artifact bytes or the source hash.
            var extraFiles = new[]
            {
                ("csc.rsp", System.Text.Encoding.UTF8.GetBytes("-langversion:latest\n")),
            };

            return Install(
                JitterPhysicsComponentIds.Jitter,
                sourceFolder,
                targetFolder,
                JitterAsmdefName,
                templatePath,
                compatibility.ExpectedSourceHash,
                receipt,
                issues,
                extraFiles);
        }

        /// <summary>Installs or updates the Jitter-dependent adapter assembly.</summary>
        public static JitterPhysicsInstallResult InstallIntegration(string targetFolder = null)
        {
            var issues = new JitterPhysicsIssueLog();
            targetFolder = Normalize(targetFolder ?? DefaultIntegrationFolder);

            if (RefuseInPlayMode(issues))
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            string packageRoot = JitterPhysicsCompatibilityReport.ResolvePackageRootPath();
            if (string.IsNullOrEmpty(packageRoot))
            {
                issues.Error("The package root could not be resolved, so there is nothing to copy from.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            JitterPhysicsInstallReceipt receipt = LoadReceipt(issues);
            if (issues.HasErrors)
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            JitterPhysicsCompatibilityReport compatibility = JitterPhysicsCompatibilityReport.Create();
            if (compatibility.Status == JitterPhysicsCompatibilityStatus.Missing)
            {
                // The adapter references Jitter2.Core by name; installing it into a project with
                // no Jitter2 turns a clean import into a wall of CS0246.
                issues.Error(
                    "The adapter references Jitter2.Core, which this project does not have. Install "
                    + "Jitter2 first, or point the project at its own copy.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            string sourceFolder = Path.Combine(packageRoot, "JitterIntegration~", "Runtime");
            string templatePath = Path.Combine(
                packageRoot,
                "JitterIntegration~",
                "UnityAssemblyTemplate",
                "DataSakura.JitterPhysics.JitterIntegration.asmdef.template.json");

            if (!Directory.Exists(sourceFolder) || !File.Exists(templatePath))
            {
                issues.Error("The integration sources are missing from the package.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            return Install(
                JitterPhysicsComponentIds.Integration,
                sourceFolder,
                targetFolder,
                IntegrationAsmdefName,
                templatePath,
                compatibility.ActualSourceHash,
                receipt,
                issues);
        }

        /// <summary>
        /// Removes several components in one operation and reports them together. The menu and
        /// the Setup window use this rather than calling <see cref="Uninstall"/> twice: two
        /// separate reports mean the second one — the one with nothing to warn about — is the
        /// one the user is left looking at, and the warning that a modified file was kept
        /// silently scrolls away.
        /// </summary>
        public static JitterPhysicsInstallResult UninstallAll(params string[] componentIds)
        {
            var issues = new JitterPhysicsIssueLog();
            var removed = new List<string>();

            for (int i = 0; i < componentIds.Length; i++)
            {
                JitterPhysicsInstallResult result = Uninstall(componentIds[i]);

                for (int f = 0; f < result.Files.Count; f++)
                {
                    removed.Add(result.Files[f]);
                }

                for (int n = 0; n < result.Issues.Issues.Count; n++)
                {
                    JitterPhysicsIssue issue = result.Issues.Issues[n];
                    if (issue.IsError)
                    {
                        issues.Error(issue.Message, issue.Context);
                    }
                    else
                    {
                        issues.Warning(issue.Message, issue.Context);
                    }
                }
            }

            return new JitterPhysicsInstallResult(removed, issues);
        }

        /// <summary>
        /// Removes a component the package installed. Files that were modified since installation
        /// are kept and reported: the package wrote them, but somebody has since made them theirs.
        /// </summary>
        public static JitterPhysicsInstallResult Uninstall(string componentId)
        {
            var issues = new JitterPhysicsIssueLog();

            if (RefuseInPlayMode(issues))
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            JitterPhysicsInstallReceipt receipt = LoadReceipt(issues);
            if (issues.HasErrors)
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            JitterPhysicsInstalledComponent component = receipt.Component(componentId);
            if (component == null)
            {
                issues.Warning($"'{componentId}' is not recorded as installed by this package; nothing to remove.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            var removed = new List<string>();
            var kept = new List<string>();

            for (int i = 0; i < component.Files.Count; i++)
            {
                JitterPhysicsInstalledFile file = component.Files[i];
                string path = Path.Combine(component.Root, file.RelativePath).Replace('\\', '/');

                if (!File.Exists(path))
                {
                    continue;
                }

                if (!JitterPhysicsHash.HexEquals(HashFile(path), file.Hash))
                {
                    kept.Add(path);
                    continue;
                }

                AssetDatabase.DeleteAsset(path);
                removed.Add(path);
            }

            if (kept.Count > 0)
            {
                issues.Warning(
                    "Kept files that were modified after installation:\n" + string.Join("\n", kept));
            }

            DeleteEmptyFolders(component.Root);

            receipt.Without(componentId).Save(JitterPhysicsInstallReceipt.DefaultPath);
            AssetDatabase.Refresh();

            return new JitterPhysicsInstallResult(removed, issues);
        }

        /// <summary>
        /// Compares what the receipt claims with what is on disk. This is what a consumer's CI
        /// runs to catch "the package was updated but the installed copy was not".
        /// </summary>
        public static JitterPhysicsInstallResult Validate()
        {
            var issues = new JitterPhysicsIssueLog();
            JitterPhysicsInstallReceipt receipt = LoadReceipt(issues);
            var checkedFiles = new List<string>();

            if (issues.HasErrors)
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            if (receipt.Components.Count == 0)
            {
                issues.Warning("Nothing is installed by this package in this project.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            for (int c = 0; c < receipt.Components.Count; c++)
            {
                JitterPhysicsInstalledComponent component = receipt.Components[c];

                if (!string.Equals(component.PackageVersion, JitterPhysicsPackage.PackageVersion, StringComparison.Ordinal))
                {
                    issues.Warning(
                        $"'{component.Id}' was installed by package {component.PackageVersion}, this is "
                        + $"{JitterPhysicsPackage.PackageVersion}. Update the installation so the project "
                        + "and the package agree about runtime semantics.");
                }

                for (int i = 0; i < component.Files.Count; i++)
                {
                    JitterPhysicsInstalledFile file = component.Files[i];
                    string path = Path.Combine(component.Root, file.RelativePath).Replace('\\', '/');
                    checkedFiles.Add(path);

                    if (!File.Exists(path))
                    {
                        issues.Error($"'{path}' is recorded as installed but is missing.");
                        continue;
                    }

                    if (!JitterPhysicsHash.HexEquals(HashFile(path), file.Hash))
                    {
                        issues.Error(
                            $"'{path}' was modified after installation. Package-owned files are "
                            + "generated; edit the package instead, or take ownership of the copy "
                            + "and remove it from the receipt.");
                    }
                }
            }

            return new JitterPhysicsInstallResult(checkedFiles, issues);
        }

        private static JitterPhysicsInstallResult Install(
            string componentId,
            string sourceFolder,
            string targetFolder,
            string asmdefName,
            string asmdefTemplatePath,
            string sourceHash,
            JitterPhysicsInstallReceipt receipt,
            JitterPhysicsIssueLog issues,
            IReadOnlyList<(string RelativePath, byte[] Content)> extraFiles = null)
        {
            JitterPhysicsInstalledComponent existing = receipt.Component(componentId);
            if (existing != null && !VerifyUnmodified(existing, issues))
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            var staged = new List<(string RelativePath, byte[] Content)>();

            foreach (string file in Directory.GetFiles(sourceFolder, "*.cs", SearchOption.AllDirectories))
            {
                string relative = file.Substring(sourceFolder.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');

                staged.Add((relative, File.ReadAllBytes(file)));
            }

            if (staged.Count == 0)
            {
                issues.Error($"No sources found under '{sourceFolder}'.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            // The assembly definition is written from a template rather than copied from a folder
            // Unity compiles, because the package itself must never contain an asmdef that
            // references Jitter2 - that is the whole reason a clean import works.
            staged.Add((asmdefName, File.ReadAllBytes(asmdefTemplatePath)));

            if (extraFiles != null)
            {
                for (int i = 0; i < extraFiles.Count; i++)
                {
                    staged.Add(extraFiles[i]);
                }
            }

            staged.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));

            var written = new List<string>(staged.Count);
            var recorded = new List<JitterPhysicsInstalledFile>(staged.Count);
            string staging = FileUtil.GetUniqueTempPathInProject();

            try
            {
                Directory.CreateDirectory(staging);

                for (int i = 0; i < staged.Count; i++)
                {
                    string stagedPath = Path.Combine(staging, staged[i].RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(stagedPath));
                    File.WriteAllBytes(stagedPath, staged[i].Content);
                }

                RemoveStaleFiles(existing, staged, issues);

                for (int i = 0; i < staged.Count; i++)
                {
                    string targetPath = Path.Combine(targetFolder, staged[i].RelativePath).Replace('\\', '/');
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }

                    File.Move(Path.Combine(staging, staged[i].RelativePath), targetPath);

                    written.Add(targetPath);
                    recorded.Add(new JitterPhysicsInstalledFile(
                        staged[i].RelativePath, JitterPhysicsHash.Sha256Hex(staged[i].Content)));
                }
            }
            catch (Exception exception)
            {
                issues.Error("Installation failed: " + exception.Message);
                return new JitterPhysicsInstallResult(null, issues);
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }

            receipt
                .With(new JitterPhysicsInstalledComponent(
                    componentId,
                    JitterPhysicsOwnership.Package,
                    targetFolder,
                    JitterPhysicsPackage.PackageVersion,
                    sourceHash,
                    recorded))
                .Save(JitterPhysicsInstallReceipt.DefaultPath);

            AssetDatabase.Refresh();

            return new JitterPhysicsInstallResult(written, issues);
        }

        private static bool VerifyUnmodified(
            JitterPhysicsInstalledComponent component,
            JitterPhysicsIssueLog issues)
        {
            var modified = new List<string>();

            for (int i = 0; i < component.Files.Count; i++)
            {
                JitterPhysicsInstalledFile file = component.Files[i];
                string path = Path.Combine(component.Root, file.RelativePath).Replace('\\', '/');

                if (File.Exists(path) && !JitterPhysicsHash.HexEquals(HashFile(path), file.Hash))
                {
                    modified.Add(path);
                }
            }

            if (modified.Count == 0)
            {
                return true;
            }

            issues.Error(
                "These installed files were modified after installation, so the update was refused:\n"
                + string.Join("\n", modified)
                + "\n\nA local fix that gets overwritten by an update is the worst possible outcome: "
                + "it works until it silently does not. Move the change into the package, or remove "
                + "the installation and reinstall.");

            return false;
        }

        private static void RemoveStaleFiles(
            JitterPhysicsInstalledComponent existing,
            List<(string RelativePath, byte[] Content)> staged,
            JitterPhysicsIssueLog issues)
        {
            if (existing == null)
            {
                return;
            }

            var incoming = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < staged.Count; i++)
            {
                incoming.Add(staged[i].RelativePath);
            }

            for (int i = 0; i < existing.Files.Count; i++)
            {
                JitterPhysicsInstalledFile file = existing.Files[i];
                if (incoming.Contains(file.RelativePath))
                {
                    continue;
                }

                string path = Path.Combine(existing.Root, file.RelativePath).Replace('\\', '/');
                if (!File.Exists(path))
                {
                    continue;
                }

                // A file the new version no longer has. Leaving it behind would keep compiling
                // against an API that is gone, which fails in a much more confusing place.
                if (JitterPhysicsHash.HexEquals(HashFile(path), file.Hash))
                {
                    AssetDatabase.DeleteAsset(path);
                }
                else
                {
                    issues.Warning(
                        $"'{path}' is no longer part of the package but was modified locally, so it was kept.");
                }
            }
        }

        private static JitterPhysicsInstallReceipt LoadReceipt(JitterPhysicsIssueLog issues)
        {
            JitterPhysicsInstallReceipt receipt = JitterPhysicsInstallReceipt.Load(
                JitterPhysicsInstallReceipt.DefaultPath, out string error);

            if (!string.IsNullOrEmpty(error))
            {
                issues.Error(
                    error + " Refusing to continue: without a readable receipt the installer cannot "
                    + "tell its own files from the project's.");
            }

            return receipt;
        }

        private static bool RefuseInPlayMode(JitterPhysicsIssueLog issues)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return false;
            }

            issues.Error(
                "Installing while in Play Mode would reload assemblies under a running simulation. "
                + "Exit Play Mode first.");

            return true;
        }

        private static void DeleteEmptyFolders(string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (string directory in Directory.GetDirectories(root))
            {
                DeleteEmptyFolders(directory);
            }

            if (Directory.GetFiles(root).Length == 0 && Directory.GetDirectories(root).Length == 0)
            {
                AssetDatabase.DeleteAsset(root.Replace('\\', '/'));
            }
        }

        private static string HashFile(string path)
        {
            return JitterPhysicsHash.Sha256Hex(File.ReadAllBytes(path));
        }

        private static string Normalize(string folder)
        {
            return folder.Replace('\\', '/').TrimEnd('/');
        }
    }

    /// <summary>Menu entries for the installation actions.</summary>
    internal static class JitterPhysicsInstallMenu
    {
        private const string Root = Authoring.JitterPhysicsAuthoringConstants.EditorMenuRoot;

        [MenuItem(Root + "Install/Install Jitter2 into Project", false, 120)]
        private static void InstallJitter() => Report(JitterPhysicsInstaller.InstallJitter());

        [MenuItem(Root + "Install/Install or Update Jitter Integration", false, 121)]
        private static void InstallIntegration() => Report(JitterPhysicsInstaller.InstallIntegration());

        [MenuItem(Root + "Install/Validate Installation", false, 122)]
        private static void Validate() => Report(JitterPhysicsInstaller.Validate());

        [MenuItem(Root + "Install/Remove Package-Owned Installation", false, 140)]
        private static void Remove()
        {
            if (!EditorUtility.DisplayDialog(
                "Remove installation",
                "Files this package installed and that have not been modified since will be deleted. "
                + "Anything you changed is kept.",
                "Remove",
                "Cancel"))
            {
                return;
            }

            Report(JitterPhysicsInstaller.UninstallAll(
                JitterPhysicsComponentIds.Integration, JitterPhysicsComponentIds.Jitter));
        }

        internal static void Report(JitterPhysicsInstallResult result)
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

            if (result.Succeeded && result.Files.Count > 0)
            {
                Debug.Log(
                    JitterPhysicsPackage.LogPrefix + $"{result.Files.Count} file(s): "
                    + string.Join(", ", result.Files));
            }
        }
    }
}





