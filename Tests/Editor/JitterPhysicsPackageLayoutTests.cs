using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                var definition = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path));
                if (definition != null && !string.IsNullOrEmpty(definition.name))
                {
                    result[definition.name] = definition;
                }
            }

            return result;
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
