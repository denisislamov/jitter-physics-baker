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

        /// <summary>Where the runnable samples go.</summary>
        public const string DefaultSamplesFolder = "Assets/DataSakura/JitterPhysics/Samples";

        private const string JitterAsmdefName = "Jitter2.Core.asmdef";
        private const string IntegrationAsmdefName = "DataSakura.JitterPhysics.JitterIntegration.asmdef";

        /// <summary>
        /// Shipped next to the Jitter2 plugin: it is not part of .NET Standard 2.1 and Unity does
        /// not deliver it to players, so the assembly cannot load without it.
        /// </summary>
        private const string UnsafeAssemblyFileName = "System.Runtime.CompilerServices.Unsafe.dll";

        /// <summary>
        /// Scripting define set while the integration adapter is installed. Assemblies that use the
        /// adapter — the demo runtime, a game's own Jitter code — gate on it so they compile only
        /// when the adapter exists and never break a project that has not installed it yet.
        /// </summary>
        public const string IntegrationDefine = "DATASAKURA_JITTER_INTEGRATION";

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

            string sourceFolder = Path.Combine(packageRoot, "Jitter2~", "Prebuilt");

            if (!Directory.Exists(sourceFolder)
                || !File.Exists(Path.Combine(sourceFolder, "Jitter2.Core.dll")))
            {
                issues.Error(
                    "The prebuilt Jitter2 assembly is missing from the package; run "
                    + "tools~/build-jitter2-unity.sh.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            // Refused before anything is written. Unity fixes game assemblies at C# 9, so a
            // snapshot written in a later language cannot be delivered as sources at all; the
            // package ships it compiled instead. A release whose compile profile still describes
            // the raw upstream form has no such assembly, and installing it would fill the
            // project with errors in a folder the user never chose to edit.
            JitterPhysicsLock lockFile;
            try
            {
                lockFile = JitterPhysicsLock.Load(packageRoot);
            }
            catch (Exception exception)
            {
                issues.Error($"'{JitterPhysicsLock.FileName}' could not be read: {exception.Message}");
                return new JitterPhysicsInstallResult(null, issues);
            }

            if (!lockFile.SupportsUnity)
            {
                issues.Error(
                    "This package release ships an unpatched upstream Jitter2 snapshot, which "
                    + "Unity cannot use, so it will not be installed. Add a Unity-compatible "
                    + "Jitter2 to the project yourself — the package bakes against whatever copy "
                    + "it finds. See Jitter2~/PATCHES.md.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            // Shipped alongside because netstandard2.1 does not define it and Unity does not
            // deliver it to players. A project that already has one keeps it: two copies of the
            // same assembly is a conflict Unity reports far from its cause.
            var skipped = new List<string>();
            if (ProjectContainsFile(UnsafeAssemblyFileName))
            {
                skipped.Add(UnsafeAssemblyFileName);
                issues.Warning(
                    "The project already provides System.Runtime.CompilerServices.Unsafe, so the "
                    + "package copy was not installed.");
            }

            JitterPhysicsInstallResult result = Install(
                JitterPhysicsComponentIds.Jitter,
                sourceFolder,
                targetFolder,
                null,
                null,
                compatibility.ExpectedSourceHash,
                receipt,
                issues,
                "*.dll",
                skipped);

            // Checked after the fact rather than trusted. The editor resolves this assembly from
            // its own toolchain, so a project missing it compiles and plays perfectly well and
            // then fails when a player build is made - far from here, and long after anyone
            // connects the two.
            if (result.Succeeded && !ProjectContainsFile(UnsafeAssemblyFileName))
            {
                issues.Warning(
                    $"'{UnsafeAssemblyFileName}' is not in the project, but the installed Jitter2 "
                    + "assembly references it. The editor will still run, because it resolves that "
                    + "assembly from its own toolchain, but a player build will fail to load "
                    + "Jitter2. Re-run the install to place it.");
            }

            return result;
        }

        /// <summary>Reports whether an asset with the given file name already exists in the project.</summary>
        private static bool ProjectContainsFile(string fileName)
        {
            string[] matches = Directory.GetFiles("Assets", fileName, SearchOption.AllDirectories);
            return matches.Length > 0;
        }

        /// <summary>
        /// Adds or removes a scripting define symbol for the build targets that matter here — the
        /// active one, which is what the editor compiles against, and Standalone, which is where the
        /// dedicated server and desktop players are built.
        /// </summary>
        /// <remarks>
        /// A define constraint is evaluated with the symbols of the target being compiled, so a
        /// symbol set only for a target the project never builds would leave the gated assembly
        /// invisible. Editing the set is idempotent: the symbol is added once and removed once.
        /// </remarks>
        private static void SetScriptingDefine(string symbol, bool enabled)
        {
            var targets = new HashSet<UnityEditor.Build.NamedBuildTarget>
            {
                UnityEditor.Build.NamedBuildTarget.Standalone,
                UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup),
            };

            foreach (UnityEditor.Build.NamedBuildTarget target in targets)
            {
                if (target == UnityEditor.Build.NamedBuildTarget.Unknown)
                {
                    continue;
                }

                PlayerSettings.GetScriptingDefineSymbols(target, out string[] current);
                var symbols = new List<string>(current);

                bool present = symbols.Contains(symbol);
                if (enabled && !present)
                {
                    symbols.Add(symbol);
                }
                else if (!enabled && present)
                {
                    symbols.RemoveAll(existing => string.Equals(existing, symbol, StringComparison.Ordinal));
                }
                else
                {
                    continue;
                }

                PlayerSettings.SetScriptingDefineSymbols(target, symbols.ToArray());
            }
        }

        /// <summary>Ordinal name lookup, spelled out to avoid the span-based Contains overload.</summary>
        private static bool ContainsName(IReadOnlyList<string> names, string candidate)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

            JitterPhysicsInstallResult result = Install(
                JitterPhysicsComponentIds.Integration,
                sourceFolder,
                targetFolder,
                IntegrationAsmdefName,
                templatePath,
                compatibility.ActualSourceHash,
                receipt,
                issues);

            // Only when the adapter is actually present. A demo or game assembly that references
            // it is gated behind this symbol so it compiles the moment the adapter exists and stays
            // out of the build until then, rather than turning a clean project into CS0246.
            if (result.Succeeded)
            {
                SetScriptingDefine(IntegrationDefine, enabled: true);
            }

            return result;
        }

        /// <summary>
        /// Adjusts the adapter's assembly definition to the form Jitter2 takes in this project.
        /// </summary>
        /// <remarks>
        /// An asmdef's <c>references</c> list names other assembly definitions. When Jitter2 is a
        /// precompiled plugin there is no assembly definition to name, and leaving the entry in
        /// makes Unity report a missing reference; auto-referenced plugins are visible to the
        /// adapter without being listed. A project with its own source-based Jitter2 does have an
        /// asmdef, and there the entry is exactly what is needed.
        /// </remarks>
        private static byte[] TailorIntegrationAsmdef(byte[] template)
        {
            if (ProjectContainsFile(JitterAsmdefName))
            {
                return template;
            }

            string text = Encoding.UTF8.GetString(template);
            string trimmed = text
                .Replace("\n    \"DataSakura.JitterPhysics.ArtifactCodec\",\n    \"Jitter2.Core\"\n",
                    "\n    \"DataSakura.JitterPhysics.ArtifactCodec\"\n");

            return Encoding.UTF8.GetBytes(trimmed);
        }

        /// <summary>
        /// Installs the runnable samples: two scenes built from code, a bouncing-ball drop, a
        /// first-person shooter and a runtime artifact check.
        /// </summary>
        /// <remarks>
        /// Refused without the adapter, because the sample assembly references it by name and
        /// Unity would answer with a missing-assembly error that says nothing about the cause.
        /// </remarks>
        /// <param name="targetFolder">Where to put them; <see cref="DefaultSamplesFolder"/> by default.</param>
        public static JitterPhysicsInstallResult InstallSamples(string targetFolder = null)
        {
            var issues = new JitterPhysicsIssueLog();
            targetFolder = Normalize(targetFolder ?? DefaultSamplesFolder);

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

            if (receipt.Component(JitterPhysicsComponentIds.Integration) == null)
            {
                issues.Error(
                    "The samples run against the Jitter integration adapter, which this project "
                    + "does not have. Install Jitter2 and the integration first.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            string sourceFolder = Path.Combine(packageRoot, "Samples~");
            string templateFolder = Path.Combine(sourceFolder, "UnityAssemblyTemplate");

            string runtimeTemplate = Path.Combine(
                templateFolder, "DataSakura.JitterPhysics.Samples.asmdef.template.json");
            string editorTemplate = Path.Combine(
                templateFolder, "DataSakura.JitterPhysics.Samples.Editor.asmdef.template.json");

            if (!Directory.Exists(sourceFolder) || !File.Exists(runtimeTemplate) || !File.Exists(editorTemplate))
            {
                issues.Error("The samples are missing from the package.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            // Two assemblies rather than one: the scene builders use UnityEditor, and an editor
            // type in a runtime assembly fails the player build rather than the import, which is
            // the worst moment to find out.
            var extraFiles = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(
                    "Runtime/DataSakura.JitterPhysics.Samples.asmdef", runtimeTemplate),
                new KeyValuePair<string, string>(
                    "Editor/DataSakura.JitterPhysics.Samples.Editor.asmdef", editorTemplate),
            };

            string readme = Path.Combine(sourceFolder, "README.md");
            if (File.Exists(readme))
            {
                extraFiles.Add(new KeyValuePair<string, string>("README.md", readme));
            }

            return Install(
                JitterPhysicsComponentIds.Samples,
                sourceFolder,
                targetFolder,
                null,
                null,
                JitterPhysicsPackage.PackageVersion,
                receipt,
                issues,
                "*.cs",
                null,
                extraFiles);
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

            // The gate symbol lives and dies with the adapter it guards, so assemblies that depend
            // on it stop compiling into the build the moment the adapter is gone.
            if (string.Equals(componentId, JitterPhysicsComponentIds.Integration, StringComparison.Ordinal))
            {
                SetScriptingDefine(IntegrationDefine, enabled: false);
            }

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
            string searchPattern = "*.cs",
            IReadOnlyList<string> skipFileNames = null,
            IReadOnlyList<KeyValuePair<string, string>> extraFiles = null)
        {
            JitterPhysicsInstalledComponent existing = receipt.Component(componentId);
            if (existing != null && !VerifyUnmodified(existing, issues))
            {
                return new JitterPhysicsInstallResult(null, issues);
            }

            var staged = new List<(string RelativePath, byte[] Content)>();

            foreach (string file in Directory.GetFiles(sourceFolder, searchPattern, SearchOption.AllDirectories))
            {
                if (skipFileNames != null && ContainsName(skipFileNames, Path.GetFileName(file)))
                {
                    continue;
                }

                string relative = file.Substring(sourceFolder.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');

                staged.Add((relative, File.ReadAllBytes(file)));
            }

            if (staged.Count == 0)
            {
                issues.Error($"No files matching '{searchPattern}' found under '{sourceFolder}'.");
                return new JitterPhysicsInstallResult(null, issues);
            }

            // The assembly definition is written from a template rather than copied from a folder
            // Unity compiles, because the package itself must never contain an asmdef that
            // references Jitter2 - that is the whole reason a clean import works.
            //
            // A precompiled plugin has none: Unity derives the assembly name from the file, and an
            // asmdef next to a .dll would describe an assembly with no sources in it.
            if (asmdefName != null)
            {
                byte[] asmdef = File.ReadAllBytes(asmdefTemplatePath);
                if (string.Equals(asmdefName, IntegrationAsmdefName, StringComparison.Ordinal))
                {
                    asmdef = TailorIntegrationAsmdef(asmdef);
                }

                staged.Add((asmdefName, asmdef));
            }

            // Written from templates for the same reason as the asmdef above: the package must not
            // contain an assembly definition that references Jitter2, or a clean import - the one
            // that happens before anything is installed - would stop compiling.
            if (extraFiles != null)
            {
                for (int i = 0; i < extraFiles.Count; i++)
                {
                    staged.Add((extraFiles[i].Key, File.ReadAllBytes(extraFiles[i].Value)));
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

















