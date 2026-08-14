using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataSakura.JitterPhysics.Contracts;
using UnityEditor;
using UnityEditor.Compilation;

namespace DataSakura.JitterPhysics.Editor.Bootstrap
{
    /// <summary>
    /// Describes one assembly the package cares about, as the compilation pipeline sees it.
    /// </summary>
    public readonly struct JitterPhysicsAssemblyInfo
    {
        /// <summary>Assembly name without extension, e.g. <c>Jitter2.Core</c>.</summary>
        public string Name { get; }

        /// <summary>Whether the compilation pipeline knows an assembly with this name.</summary>
        public bool Exists { get; }

        /// <summary>Project-relative paths of the assembly definition files found for the name.</summary>
        public IReadOnlyList<string> DefinitionPaths { get; }

        internal JitterPhysicsAssemblyInfo(string name, bool exists, IReadOnlyList<string> definitionPaths)
        {
            Name = name;
            Exists = exists;
            DefinitionPaths = definitionPaths ?? Array.Empty<string>();
        }

        /// <summary>
        /// True when more than one assembly definition claims the name. Two compiled copies
        /// of Jitter2 cannot coexist, so the installer must refuse to act in that state.
        /// </summary>
        public bool IsDuplicated => DefinitionPaths.Count > 1;
    }

    /// <summary>
    /// Read-only probe over the compilation pipeline.
    /// <para>
    /// Discovery deliberately goes through assembly metadata instead of a hard-coded folder:
    /// a consumer may keep its Jitter2 copy anywhere, and the package must find it there.
    /// Nothing here mutates the project — installation is always an explicit user command.
    /// </para>
    /// </summary>
    public static class JitterPhysicsAssemblyProbe
    {
        /// <summary>Assemblies that must compile before any Jitter2 is present in the project.</summary>
        public static readonly string[] BootstrapAssemblyNames =
        {
            "DataSakura.JitterPhysics.Contracts",
            "DataSakura.JitterPhysics.ArtifactCodec",
            "DataSakura.JitterPhysics.UnityArtifact",
            "DataSakura.JitterPhysics.Authoring",
            "DataSakura.JitterPhysics.Editor",
        };

        /// <summary>Looks up a single assembly by name.</summary>
        public static JitterPhysicsAssemblyInfo Probe(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                throw new ArgumentException("Assembly name is required.", nameof(assemblyName));
            }

            bool compiledFromSources = CompilationPipeline
                .GetAssemblies(AssembliesType.Editor)
                .Any(assembly => string.Equals(assembly.name, assemblyName, StringComparison.Ordinal));

            // GetAssemblies only returns assemblies Unity compiles from sources. The fallback
            // installer deliberately delivers Jitter2 as a precompiled DLL (its upstream sources
            // require a newer language version than Unity supports), so ignoring this list makes
            // a successful install look Missing forever and blocks installation of the adapter.
            bool precompiled = CompilationPipeline
                .GetPrecompiledAssemblyPaths(
                    CompilationPipeline.PrecompiledAssemblySources.UserAssembly)
                .Any(path => string.Equals(
                    Path.GetFileNameWithoutExtension(path), assemblyName, StringComparison.Ordinal));

            bool exists = compiledFromSources || precompiled;

            // An .asmdef may exist while the assembly failed to compile, and a precompiled
            // plugin has no .asmdef at all; both cases are reported rather than collapsed.
            string[] definitionPaths = AssetDatabase
                .FindAssets("t:AssemblyDefinitionAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .Where(path => string.Equals(ReadAssemblyDefinitionName(path), assemblyName, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return new JitterPhysicsAssemblyInfo(assemblyName, exists, definitionPaths);
        }

        /// <summary>
        /// Reads the <c>name</c> field of an assembly definition file. The file name and the
        /// declared assembly name are allowed to differ, so the declared name is the only
        /// reliable way to detect a duplicate <c>Jitter2.Core</c>.
        /// </summary>
        private static string ReadAssemblyDefinitionName(string assemblyDefinitionPath)
        {
            try
            {
                var parsed = UnityEngine.JsonUtility.FromJson<AssemblyDefinitionName>(
                    System.IO.File.ReadAllText(assemblyDefinitionPath));
                return parsed != null ? parsed.name : null;
            }
            catch (Exception exception) when (exception is System.IO.IOException
                                              || exception is ArgumentException)
            {
                // A malformed or unreadable .asmdef is Unity's problem to report; the probe
                // must not throw and break the editor window that calls it.
                return null;
            }
        }

        [Serializable]
        private sealed class AssemblyDefinitionName
        {
            public string name;
        }

        /// <summary>Probes the Jitter2 core assembly the package integrates with.</summary>
        public static JitterPhysicsAssemblyInfo ProbeJitter()
        {
            return Probe(JitterPhysicsPackage.JitterAssemblyName);
        }

        /// <summary>Probes every always-compiled package assembly, in declaration order.</summary>
        public static IReadOnlyList<JitterPhysicsAssemblyInfo> ProbeBootstrapAssemblies()
        {
            var result = new List<JitterPhysicsAssemblyInfo>(BootstrapAssemblyNames.Length);
            for (int i = 0; i < BootstrapAssemblyNames.Length; i++)
            {
                result.Add(Probe(BootstrapAssemblyNames[i]));
            }

            return result;
        }
    }
}
