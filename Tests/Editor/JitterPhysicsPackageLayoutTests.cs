using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DataSakura.JitterPhysics.Contracts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
// UnityEditor also declares a legacy PackageInfo; the alias keeps the intent explicit.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>
    /// Structural invariants of the package. They are cheap to check and expensive to lose:
    /// a single reference to Jitter2 added to an always-compiled assembly breaks a clean
    /// import for every consumer, and review does not reliably catch it.
    /// </summary>
    public sealed class JitterPhysicsPackageLayoutTests
    {
        /// <summary>Assemblies that must compile in a project with no Jitter2 at all.</summary>
        private static readonly string[] AlwaysCompiledAssemblies =
        {
            "DataSakura.JitterPhysics.Contracts",
            "DataSakura.JitterPhysics.ArtifactCodec",
            "DataSakura.JitterPhysics.UnityArtifact",
            "DataSakura.JitterPhysics.Authoring",
            "DataSakura.JitterPhysics.Editor",
        };

        /// <summary>Names no package assembly may reference, directly or through an alias.</summary>
        private static readonly string[] ForbiddenReferences =
        {
            "Jitter2.Core",
            "Netick",
            "EFT.Runtime",
            "EFT.Shared",
        };

        [Test]
        public void PackageIsResolvedByPackageManager()
        {
            PackageInfo package = FindPackage();
            Assert.That(package, Is.Not.Null,
                $"'{JitterPhysicsPackage.PackageName}' is not resolved by the Package Manager.");
        }

        [Test]
        public void PackageVersionConstantMatchesManifest()
        {
            PackageInfo package = FindPackage();
            Assert.That(package, Is.Not.Null);

            // The portable assemblies also compile outside Unity, where package.json cannot be
            // read, so the constant is the source of truth there and must not drift.
            Assert.That(
                JitterPhysicsPackage.PackageVersion,
                Is.EqualTo(package.version),
                "JitterPhysicsPackage.PackageVersion is out of sync with package.json.");
        }

        [Test]
        public void PackageManifestExposesImportableSamples()
        {
            PackageInfo package = FindPackage();
            Assert.That(package, Is.Not.Null);

            string manifestPath = Path.Combine(package.resolvedPath, "package.json");
            PackageManifest manifest = JsonUtility.FromJson<PackageManifest>(File.ReadAllText(manifestPath));

            Assert.That(manifest.samples, Is.Not.Null);
            Assert.That(manifest.samples.Length, Is.EqualTo(1));
            Assert.That(manifest.samples[0].displayName, Is.EqualTo("Physics Baking Demos"));
            Assert.That(manifest.samples[0].path, Is.EqualTo("Samples~/Demos"));
            Assert.That(
                Directory.Exists(Path.Combine(package.resolvedPath, manifest.samples[0].path)),
                Is.True,
                "The Package Manager sample path must resolve inside the published package.");

            AssertSampleAssemblyMatchesInstallerTemplate(
                package.resolvedPath,
                "DataSakura.JitterPhysics.Samples.asmdef",
                "DataSakura.JitterPhysics.Samples.asmdef.template.json",
                "Runtime");
            AssertSampleAssemblyMatchesInstallerTemplate(
                package.resolvedPath,
                "DataSakura.JitterPhysics.Samples.Editor.asmdef",
                "DataSakura.JitterPhysics.Samples.Editor.asmdef.template.json",
                "Editor");
        }

        [Test]
        public void AlwaysCompiledAssembliesExist()
        {
            IReadOnlyDictionary<string, AssemblyDefinition> definitions = LoadPackageAssemblyDefinitions();
            foreach (string assemblyName in AlwaysCompiledAssemblies)
            {
                Assert.That(definitions.ContainsKey(assemblyName), Is.True,
                    $"Assembly definition '{assemblyName}' is missing from the package.");
            }
        }

        [Test]
        public void NoPackageAssemblyReferencesJitterOrConsumerAssemblies()
        {
            IReadOnlyDictionary<string, AssemblyDefinition> definitions = LoadPackageAssemblyDefinitions();
            foreach (KeyValuePair<string, AssemblyDefinition> entry in definitions)
            {
                string[] references = entry.Value.references ?? Array.Empty<string>();
                foreach (string forbidden in ForbiddenReferences)
                {
                    Assert.That(
                        references.Any(reference => reference.Contains(forbidden, StringComparison.Ordinal)),
                        Is.False,
                        $"Assembly '{entry.Key}' references '{forbidden}'. Jitter-dependent code "
                        + "lives in JitterIntegration~ and is installed explicitly, so that the "
                        + "package imports cleanly into a project without Jitter2.");
                }
            }
        }

        [Test]
        public void EditorAssemblyIsEditorOnly()
        {
            IReadOnlyDictionary<string, AssemblyDefinition> definitions = LoadPackageAssemblyDefinitions();
            AssemblyDefinition editor = definitions["DataSakura.JitterPhysics.Editor"];

            Assert.That(editor.includePlatforms, Is.EquivalentTo(new[] { "Editor" }),
                "The editor assembly must never be part of a player build.");
        }

        [Test]
        public void PortableAssembliesDeclareNoEngineReferences()
        {
            IReadOnlyDictionary<string, AssemblyDefinition> definitions = LoadPackageAssemblyDefinitions();

            // Contracts and the codec are compiled by a plain .NET SDK for the dedicated
            // server, so they must not acquire a UnityEngine dependency by accident.
            Assert.That(definitions["DataSakura.JitterPhysics.Contracts"].noEngineReferences, Is.True);
            Assert.That(definitions["DataSakura.JitterPhysics.ArtifactCodec"].noEngineReferences, Is.True);
        }

        [Test]
        public void HiddenFoldersAreNotImportedByUnity()
        {
            PackageInfo package = FindPackage();
            Assert.That(package, Is.Not.Null);

            // Folders ending with '~' are invisible to Unity. The dormant Jitter snapshot and
            // the integration sources rely on that: importing them would create a second
            // Jitter2.Core and break the project.
            string[] hidden = Directory
                .GetDirectories(package.resolvedPath, "*~", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToArray();

            foreach (string folder in hidden)
            {
                string assetPath = package.assetPath + "/" + folder;
                Assert.That(AssetDatabase.IsValidFolder(assetPath), Is.False,
                    $"Unity imported '{assetPath}', which must stay dormant.");
            }
        }

        [Test]
        public void AuthoringWindowKeepsTheSharedDataSakuraWorkflow()
        {
            FieldInfo tabsField = typeof(JitterPhysicsBakerWindow).GetField(
                "TabNames",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(tabsField, Is.Not.Null);
            Assert.That(
                (string[])tabsField.GetValue(null),
                Is.EqualTo(new[]
                {
                    "Overview",
                    "Sources",
                    "Bake",
                    "Tools",
                    "Setup",
                    "Artifacts",
                }),
                "The main workflow intentionally mirrors the other DataSakura authoring packages.");

            Assert.That(
                typeof(JitterPhysicsBakerWindow).GetMethod(nameof(JitterPhysicsBakerWindow.OpenSetupTab)),
                Is.Not.Null,
                "Setup menu actions must be able to route into the shared main window.");
            Assert.That(
                typeof(JitterPhysicsBakerWindow).GetMethod(nameof(JitterPhysicsBakerWindow.OpenArtifactsTab)),
                Is.Not.Null,
                "Artifact commands must be able to route into the shared main window.");
        }

        private static PackageInfo FindPackage()
        {
            return PackageInfo.FindForAssembly(typeof(JitterPhysicsAboutWindow).Assembly);
        }

        private static IReadOnlyDictionary<string, AssemblyDefinition> LoadPackageAssemblyDefinitions()
        {
            PackageInfo package = FindPackage();
            Assert.That(package, Is.Not.Null);

            var result = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(
                         package.resolvedPath, "*.asmdef", SearchOption.AllDirectories))
            {
                if (IsUnderHiddenPackageFolder(package.resolvedPath, path))
                {
                    continue;
                }

                var definition = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path));
                if (definition != null && !string.IsNullOrEmpty(definition.name))
                {
                    result[definition.name] = definition;
                }
            }

            return result;
        }

        private static bool IsUnderHiddenPackageFolder(string packageRoot, string path)
        {
            string relative = path.Substring(packageRoot.Length);
            return relative
                .Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment.EndsWith("~", StringComparison.Ordinal));
        }

        private static void AssertSampleAssemblyMatchesInstallerTemplate(
            string packageRoot,
            string assemblyFile,
            string templateFile,
            string assemblyFolder)
        {
            string sampleAssembly = Path.Combine(
                packageRoot, "Samples~", "Demos", assemblyFolder, assemblyFile);
            string installerTemplate = Path.Combine(
                packageRoot, "Samples~", "UnityAssemblyTemplate", templateFile);

            Assert.That(File.Exists(sampleAssembly), Is.True);
            Assert.That(File.Exists(installerTemplate), Is.True);
            Assert.That(
                File.ReadAllText(sampleAssembly).Replace("\r\n", "\n"),
                Is.EqualTo(File.ReadAllText(installerTemplate).Replace("\r\n", "\n")),
                "Package Manager import and the guarded installer must create the same assembly.");
        }

        [Serializable]
        private sealed class PackageManifest
        {
            public PackageSample[] samples;
        }

        [Serializable]
        private sealed class PackageSample
        {
            public string displayName;
            public string path;
        }

        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string name;
            public string[] references;
            public string[] includePlatforms;
            public bool noEngineReferences;
        }
    }
}
